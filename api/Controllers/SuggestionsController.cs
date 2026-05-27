using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using BubbleSplash.Api.Data;
using BubbleSplash.Api.Models;
using BubbleSplash.Api.Models.Dto;

namespace BubbleSplash.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SuggestionsController : ControllerBase
{
    private readonly BubbleSplashDbContext _db;
    private readonly ILogger<SuggestionsController> _logger;

    public SuggestionsController(BubbleSplashDbContext db, ILogger<SuggestionsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>
    /// Submit a new word suggestion for dictionary review.
    /// </summary>
    /// <param name="request">The word suggestion payload.</param>
    /// <returns>The created suggestion with its assigned ID.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(SuggestWordResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> SubmitSuggestion([FromBody] SuggestWordRequest request)
    {
        var word = request.Word.Trim().ToLowerInvariant();

        // Check for duplicate pending/approved suggestions
        var existingSuggestion = await _db.WordSuggestions
            .FirstOrDefaultAsync(s => s.Word == word && s.Status != SuggestionStatus.Rejected);

        if (existingSuggestion is not null)
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

        var response = new SuggestWordResponse
        {
            Id = suggestion.Id,
            Word = suggestion.Word,
            Status = suggestion.Status.ToString(),
            Message = $"Thank you! \"{suggestion.Word}\" has been submitted for review.",
            CreatedAtUtc = suggestion.CreatedAtUtc
        };

        return CreatedAtAction(nameof(GetSuggestion), new { id = suggestion.Id }, response);
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
        {
            return NotFound();
        }

        return Ok(new SuggestWordResponse
        {
            Id = suggestion.Id,
            Word = suggestion.Word,
            Status = suggestion.Status.ToString(),
            Message = $"Suggestion for \"{suggestion.Word}\" is currently {suggestion.Status}.",
            CreatedAtUtc = suggestion.CreatedAtUtc
        });
    }
}
