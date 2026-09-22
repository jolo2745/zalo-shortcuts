using ZaloShortcuts.Models;

namespace ZaloShortcuts.Services;

public static class ShortcutMatcher
{
    public static IReadOnlyList<ShortcutMessage> Find(
        IEnumerable<ShortcutMessage> shortcuts,
        string typedToken,
        int limit = 5)
    {
        var query = typedToken.Trim().TrimStart('/').ToLowerInvariant();

        return shortcuts
            .Where(item => item.Keyword.StartsWith(query, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(item => item.Keyword.Equals(query, StringComparison.OrdinalIgnoreCase))
            .ThenBy(item => item.Keyword.Length)
            .ThenBy(item => item.Keyword, StringComparer.OrdinalIgnoreCase)
            .Take(Math.Max(1, limit))
            .Select(item => item.Clone())
            .ToList();
    }
}
