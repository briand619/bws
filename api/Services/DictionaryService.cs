using System.Text.Json;

namespace BubbleSplash.Api.Services;

public class DictionaryService
{
    private readonly string _filePath;
    private readonly ILogger<DictionaryService> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private static readonly JsonSerializerOptions SerializeOptions = new() { WriteIndented = true };

    public DictionaryService(IConfiguration configuration, IWebHostEnvironment env, ILogger<DictionaryService> logger)
    {
        var configured = configuration["Dictionary:FilePath"]
            ?? throw new InvalidOperationException("Dictionary:FilePath is not configured.");

        _filePath = Path.GetFullPath(configured, env.ContentRootPath);
        _logger = logger;
    }

    /// <summary>
    /// Inserts <paramref name="word"/> into the dictionary file in alphabetical order.
    /// Returns true if added, false if already present.
    /// </summary>
    public async Task<bool> AddWordAsync(string word)
    {
        await _lock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            var words = JsonSerializer.Deserialize<List<string>>(json) ?? [];

            if (words.Any(w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase)))
                return false;

            // Binary search for the sorted insertion point
            int index = words.BinarySearch(word, StringComparer.Ordinal);
            if (index < 0) index = ~index;
            words.Insert(index, word);

            await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(words, SerializeOptions));
            _logger.LogInformation("Added '{Word}' to dictionary at index {Index}", word, index);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Removes <paramref name="word"/> from the dictionary file.
    /// Returns true if removed, false if not found.
    /// </summary>
    public async Task<bool> RemoveWordAsync(string word)
    {
        await _lock.WaitAsync();
        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            var words = JsonSerializer.Deserialize<List<string>>(json) ?? [];

            int removed = words.RemoveAll(w => string.Equals(w, word, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return false;

            await File.WriteAllTextAsync(_filePath, JsonSerializer.Serialize(words, SerializeOptions));
            _logger.LogInformation("Removed '{Word}' from dictionary", word);
            return true;
        }
        finally
        {
            _lock.Release();
        }
    }
}
