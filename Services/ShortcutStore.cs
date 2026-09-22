using System.Text.Json;
using System.Text.RegularExpressions;
using ZaloShortcuts.Models;

namespace ZaloShortcuts.Services;

public sealed partial class ShortcutStore
{
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
    private List<ShortcutMessage> _items = [];

    public ShortcutStore(string? dataDirectory = null)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        DataDirectory = dataDirectory ?? Path.Combine(appData, "ZaloShortcuts");
        FilePath = Path.Combine(DataDirectory, "shortcuts.json");
    }

    public string DataDirectory { get; }

    public string FilePath { get; }

    public void Load()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(DataDirectory);

            if (!File.Exists(FilePath))
            {
                _items =
                [
                    new ShortcutMessage
                    {
                        Keyword = "hello",
                        Title = "Hello",
                        Message = "Hello! Thanks for your message."
                    }
                ];
                SaveLocked();
                return;
            }

            try
            {
                var json = File.ReadAllText(FilePath);
                var document = JsonSerializer.Deserialize<ShortcutDocument>(json, _jsonOptions);
                _items = (document?.Shortcuts ?? [])
                    .Where(IsValidStoredItem)
                    .GroupBy(item => item.Keyword, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderByDescending(item => item.UpdatedAt).First())
                    .OrderBy(item => item.Keyword, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch
            {
                var backupPath = $"{FilePath}.invalid-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
                File.Copy(FilePath, backupPath, overwrite: false);
                _items = [];
                SaveLocked();
            }
        }
    }

    public IReadOnlyList<ShortcutMessage> GetSnapshot()
    {
        lock (_gate)
        {
            return _items
                .OrderBy(item => item.Keyword, StringComparer.OrdinalIgnoreCase)
                .Select(item => item.Clone())
                .ToList();
        }
    }

    public StoreResult Upsert(Guid? id, string rawKeyword, string title, string message)
    {
        var keyword = NormalizeKeyword(rawKeyword);
        title = title.Trim();
        message = message.Trim();

        if (!KeywordPattern().IsMatch(keyword))
        {
            return StoreResult.Fail("Use 1–40 letters, numbers, hyphens, or underscores for the shortcut word.");
        }

        if (message.Length == 0)
        {
            return StoreResult.Fail("Write a message before saving.");
        }

        if (message.Length > 10_000)
        {
            return StoreResult.Fail("Messages can be up to 10,000 characters.");
        }

        lock (_gate)
        {
            var duplicate = _items.FirstOrDefault(item =>
                item.Keyword.Equals(keyword, StringComparison.OrdinalIgnoreCase) &&
                item.Id != id);
            if (duplicate is not null)
            {
                return StoreResult.Fail($"The shortcut /{keyword} already exists.");
            }

            var item = id is null ? null : _items.FirstOrDefault(candidate => candidate.Id == id);
            if (item is null)
            {
                item = new ShortcutMessage();
                _items.Add(item);
            }

            item.Keyword = keyword;
            item.Title = title.Length == 0 ? $"/{keyword}" : title;
            item.Message = message;
            item.UpdatedAt = DateTimeOffset.UtcNow;
            SaveLocked();
            return StoreResult.Ok(item.Clone(), $"Saved /{keyword}.");
        }
    }

    public void Delete(Guid id)
    {
        lock (_gate)
        {
            _items.RemoveAll(item => item.Id == id);
            SaveLocked();
        }
    }

    public static string NormalizeKeyword(string value) =>
        value.Trim().TrimStart('/').ToLowerInvariant();

    private static bool IsValidStoredItem(ShortcutMessage item) =>
        item.Id != Guid.Empty &&
        KeywordPattern().IsMatch(item.Keyword) &&
        item.Message.Length > 0;

    private void SaveLocked()
    {
        Directory.CreateDirectory(DataDirectory);
        var document = new ShortcutDocument
        {
            Version = 1,
            Shortcuts = _items
                .OrderBy(item => item.Keyword, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
        var json = JsonSerializer.Serialize(document, _jsonOptions);
        var temporaryPath = $"{FilePath}.tmp";
        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, FilePath, overwrite: true);
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,39}$", RegexOptions.CultureInvariant)]
    private static partial Regex KeywordPattern();

    private sealed class ShortcutDocument
    {
        public int Version { get; set; } = 1;

        public List<ShortcutMessage> Shortcuts { get; set; } = [];
    }
}

public sealed record StoreResult(bool Success, string Message, ShortcutMessage? Item)
{
    public static StoreResult Ok(ShortcutMessage item, string message) => new(true, message, item);

    public static StoreResult Fail(string message) => new(false, message, null);
}
