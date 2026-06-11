namespace BubbleSplash.Api.Models.Dto;

public class SuggestionAdminDto
{
    public int Id { get; set; }
    public required string Word { get; set; }
    public string? Reason { get; set; }
    public required string Status { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReviewedAtUtc { get; set; }
    public string? SubmitterIp { get; set; }
    /// <summary>
    /// Only set on approve responses. True = added, false = already present, null = file update failed.
    /// </summary>
    public bool? AddedToDictionary { get; set; }

    /// <summary>
    /// Only set on reject responses when undoing an approval. True = removed, false = not found, null = file update failed.
    /// </summary>
    public bool? RemovedFromDictionary { get; set; }
}
