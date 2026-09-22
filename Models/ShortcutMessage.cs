namespace ZaloShortcuts.Models;

public sealed class ShortcutMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Keyword { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Message { get; set; } = string.Empty;

    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;

    public string TriggerDisplay => $"/{Keyword}";

    public string MessagePreview
    {
        get
        {
            var oneLine = Message.Replace("\r", " ").Replace("\n", " ").Trim();
            return oneLine.Length <= 72 ? oneLine : $"{oneLine[..69]}…";
        }
    }

    public ShortcutMessage Clone() => new()
    {
        Id = Id,
        Keyword = Keyword,
        Title = Title,
        Message = Message,
        UpdatedAt = UpdatedAt
    };
}
