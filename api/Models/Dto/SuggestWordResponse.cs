namespace BubbleSplash.Api.Models.Dto;

/// <summary>
/// Response DTO returned after successfully submitting a word suggestion.
/// </summary>
public class SuggestWordResponse
{
    public int Id { get; set; }
    public required string Word { get; set; }
    public required string Status { get; set; }
    public required string Message { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
