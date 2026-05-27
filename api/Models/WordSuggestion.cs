namespace BubbleSplash.Api.Models;

/// <summary>
/// Represents a user-submitted word suggestion for the dictionary.
/// </summary>
public class WordSuggestion
{
    public int Id { get; set; }

    /// <summary>
    /// The suggested word (lowercase, letters only).
    /// </summary>
    public required string Word { get; set; }

    /// <summary>
    /// Optional reason or justification for why this word should be added.
    /// </summary>
    public string? Reason { get; set; }

    /// <summary>
    /// The review status of the suggestion.
    /// </summary>
    public SuggestionStatus Status { get; set; } = SuggestionStatus.Pending;

    /// <summary>
    /// When the suggestion was submitted (UTC).
    /// </summary>
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// When the suggestion was last reviewed or updated (UTC).
    /// </summary>
    public DateTime? ReviewedAtUtc { get; set; }

    /// <summary>
    /// IP address of the submitter (for basic spam/abuse tracking).
    /// </summary>
    public string? SubmitterIp { get; set; }
}

public enum SuggestionStatus
{
    Pending = 0,
    Approved = 1,
    Rejected = 2
}
