using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BubbleSplash.Api.Data;
using BubbleSplash.Api.Filters;
using BubbleSplash.Api.Models;
using BubbleSplash.Api.Models.Dto;
using BubbleSplash.Api.Services;

namespace BubbleSplash.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SuggestionsController : ControllerBase
{
    private readonly BubbleSplashDbContext _db;
    private readonly ILogger<SuggestionsController> _logger;
    private readonly DictionaryService _dictionary;

    public SuggestionsController(BubbleSplashDbContext db, ILogger<SuggestionsController> logger, DictionaryService dictionary)
    {
        _db = db;
        _logger = logger;
        _dictionary = dictionary;
    }

    private static SuggestWordResponse ToResponse(WordSuggestion s, string message) => new()
    {
        Id = s.Id,
        Word = s.Word,
        Status = s.Status.ToString(),
        Message = message,
        CreatedAtUtc = s.CreatedAtUtc
    };

    private static SuggestionAdminDto ToAdminDto(WordSuggestion s) => new()
    {
        Id = s.Id,
        Word = s.Word,
        Reason = s.Reason,
        Status = s.Status.ToString(),
        CreatedAtUtc = s.CreatedAtUtc,
        ReviewedAtUtc = s.ReviewedAtUtc,
        SubmitterIp = s.SubmitterIp
    };

    /// <summary>
    /// Submit a new word suggestion for dictionary review.
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(SuggestWordResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitSuggestion([FromBody] SuggestWordRequest request)
    {
        var word = request.Word.Trim().ToLowerInvariant();

        // Check for duplicate pending/approved suggestions
        var alreadyExists = await _db.WordSuggestions
            .AnyAsync(s => s.Word == word && s.Status != SuggestionStatus.Rejected);

        if (alreadyExists)
        {
            return Conflict(new ProblemDetails
            {
                Title = "Duplicate suggestion",
                Detail = $"The word \"{word}\" has already been suggested.",
                Status = StatusCodes.Status409Conflict
            });
        }

        var suggestion = new WordSuggestion
        {
            Word = word,
            Reason = string.IsNullOrWhiteSpace(request.Reason) ? null : request.Reason.Trim(),
            SubmitterIp = HttpContext.Connection.RemoteIpAddress?.ToString()
        };

        _db.WordSuggestions.Add(suggestion);
        await _db.SaveChangesAsync();

        _logger.LogInformation("New word suggestion submitted: {Word} (ID: {Id})", suggestion.Word, suggestion.Id);

        return CreatedAtAction(
            nameof(GetSuggestion),
            new { id = suggestion.Id },
            ToResponse(suggestion, $"Thank you! \"{suggestion.Word}\" has been submitted for review."));
    }

    /// <summary>
    /// Get a specific suggestion by ID.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(SuggestWordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetSuggestion(int id)
    {
        var suggestion = await _db.WordSuggestions.FindAsync(id);

        if (suggestion is null)
            return NotFound();

        return Ok(ToResponse(suggestion, $"Suggestion for \"{suggestion.Word}\" is currently {suggestion.Status}."));
    }

    /// <summary>
    /// List all suggestions, optionally filtered by status.
    /// </summary>
    [TypeFilter(typeof(AdminKeyFilter))]
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<SuggestionAdminDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ListSuggestions([FromQuery] string? status = null)
    {
        var query = _db.WordSuggestions.AsQueryable();

        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<SuggestionStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(s => s.Status == parsed);
        }

        var suggestions = await query
            .OrderByDescending(s => s.CreatedAtUtc)
            .Select(s => new SuggestionAdminDto
            {
                Id = s.Id,
                Word = s.Word,
                Reason = s.Reason,
                Status = s.Status.ToString(),
                CreatedAtUtc = s.CreatedAtUtc,
                ReviewedAtUtc = s.ReviewedAtUtc,
                SubmitterIp = s.SubmitterIp
            })
            .ToListAsync();

        return Ok(suggestions);
    }

    /// <summary>
    /// Approve a pending suggestion.
    /// </summary>
    [TypeFilter(typeof(AdminKeyFilter))]
    [HttpPost("{id:int}/approve")]
    [ProducesResponseType(typeof(SuggestionAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> ApproveSuggestion(int id)
    {
        var suggestion = await _db.WordSuggestions.FindAsync(id);

        if (suggestion is null)
            return NotFound();

        if (suggestion.Status == SuggestionStatus.Approved)
            return Conflict(new ProblemDetails { Detail = "Suggestion is already approved." });

        suggestion.Status = SuggestionStatus.Approved;
        suggestion.ReviewedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Suggestion approved: {Word} (ID: {Id})", suggestion.Word, suggestion.Id);

        bool? addedToDictionary = null;
        try
        {
            addedToDictionary = await _dictionary.AddWordAsync(suggestion.Word);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to add '{Word}' to dictionary file after approval", suggestion.Word);
        }

        var dto = ToAdminDto(suggestion);
        dto.AddedToDictionary = addedToDictionary;
        return Ok(dto);
    }

    /// <summary>
    /// Reject a pending suggestion.
    /// </summary>
    [TypeFilter(typeof(AdminKeyFilter))]
    [HttpPost("{id:int}/reject")]
    [ProducesResponseType(typeof(SuggestionAdminDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RejectSuggestion(int id)
    {
        var suggestion = await _db.WordSuggestions.FindAsync(id);

        if (suggestion is null)
            return NotFound();

        if (suggestion.Status == SuggestionStatus.Rejected)
            return Conflict(new ProblemDetails { Detail = "Suggestion is already rejected." });

        bool wasApproved = suggestion.Status == SuggestionStatus.Approved;

        suggestion.Status = SuggestionStatus.Rejected;
        suggestion.ReviewedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _logger.LogInformation("Suggestion rejected: {Word} (ID: {Id})", suggestion.Word, suggestion.Id);

        bool? removedFromDictionary = null;
        if (wasApproved)
        {
            try
            {
                removedFromDictionary = await _dictionary.RemoveWordAsync(suggestion.Word);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to remove '{Word}' from dictionary file after rejection", suggestion.Word);
            }
        }

        var dto = ToAdminDto(suggestion);
        dto.RemovedFromDictionary = removedFromDictionary;
        return Ok(dto);
    }
}
