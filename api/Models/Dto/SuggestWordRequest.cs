using System.ComponentModel.DataAnnotations;

namespace BubbleSplash.Api.Models.Dto;

/// <summary>
/// Inbound DTO for submitting a word suggestion from the frontend.
/// </summary>
public class SuggestWordRequest
{
    /// <summary>
    /// The word being suggested. Must be 4-45 lowercase letters.
    /// </summary>
    [Required(ErrorMessage = "Word is required.")]
    [RegularExpression(@"^[a-zA-Z]{4,45}$", ErrorMessage = "Word must be 4-45 letters only.")]
    public required string Word { get; set; }

    /// <summary>
    /// Optional reason for suggesting this word.
    /// </summary>
    [MaxLength(250, ErrorMessage = "Reason must be 250 characters or fewer.")]
    public string? Reason { get; set; }
}
