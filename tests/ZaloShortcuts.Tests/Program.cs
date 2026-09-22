using ZaloShortcuts.Models;
using ZaloShortcuts.Services;

var tests = new (string Name, Action Run)[]
{
    ("prefix matches are ordered predictably", PrefixMatchesAreOrderedPredictably),
    ("exact match wins", ExactMatchWins),
    ("store validates and prevents duplicates", StoreValidatesAndPreventsDuplicates),
    ("store persists Unicode messages", StorePersistsUnicodeMessages),
    ("onboarding and startup preferences persist", OnboardingPreferencesPersist)
};

var failures = new List<string>();
foreach (var test in tests)
{
    try
    {
        test.Run();
        Console.WriteLine($"PASS  {test.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{test.Name}: {exception.Message}");
        Console.WriteLine($"FAIL  {test.Name}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine($"{tests.Length} shortcut tests passed.");
return 0;

static void PrefixMatchesAreOrderedPredictably()
{
    var shortcuts = new[]
    {
        NewMessage("hello"),
        NewMessage("help"),
        NewMessage("booking")
    };

    var result = ShortcutMatcher.Find(shortcuts, "/he");
    Equal(2, result.Count);
    Equal("help", result[0].Keyword);
    Equal("hello", result[1].Keyword);
}

static void ExactMatchWins()
{
    var shortcuts = new[]
    {
        NewMessage("hello"),
        NewMessage("he")
    };

    var result = ShortcutMatcher.Find(shortcuts, "/he");
    Equal("he", result[0].Keyword);
}

static void StoreValidatesAndPreventsDuplicates()
{
    WithTemporaryStore(store =>
    {
        var invalid = store.Upsert(null, "has spaces", "Invalid", "Message");
        False(invalid.Success, "A keyword containing spaces should be rejected.");

        var first = store.Upsert(null, "/welcome", "Welcome", "Hello");
        True(first.Success, "The valid shortcut should save.");

        var duplicate = store.Upsert(null, "WELCOME", "Duplicate", "Hello again");
        False(duplicate.Success, "Duplicate keywords should be rejected case-insensitively.");
    });
}

static void StorePersistsUnicodeMessages()
{
    var directory = Path.Combine(Path.GetTempPath(), $"zalo-shortcuts-tests-{Guid.NewGuid():N}");
    try
    {
        var original = new ShortcutStore(directory);
        original.Load();
        var saved = original.Upsert(
            null,
            "camon",
            "Cảm ơn",
            "Cảm ơn bạn đã nhắn tin 😊");
        True(saved.Success, "The Unicode message should save.");

        var reloaded = new ShortcutStore(directory);
        reloaded.Load();
        var item = reloaded.GetSnapshot().Single(candidate => candidate.Keyword == "camon");
        Equal("Cảm ơn bạn đã nhắn tin 😊", item.Message);
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void OnboardingPreferencesPersist()
{
    var directory = Path.Combine(Path.GetTempPath(), $"zalo-shortcuts-tests-{Guid.NewGuid():N}");
    try
    {
        var original = new AppSettingsStore(directory);
        original.Load();
        False(original.HasCompletedOnboarding, "Onboarding should start incomplete.");
        False(
            original.HasStartAtLoginPreference,
            "A new install should not have a startup preference yet.");

        original.SaveOnboardingPreferences(startAtLogin: true);

        var reloaded = new AppSettingsStore(directory);
        reloaded.Load();
        True(reloaded.HasCompletedOnboarding, "Onboarding completion should persist.");
        True(
            reloaded.HasStartAtLoginPreference,
            "The startup preference should be marked as chosen.");
        True(reloaded.StartAtLogin, "Automatic startup should persist.");

        reloaded.SaveOnboardingPreferences(startAtLogin: false);
        var disabled = new AppSettingsStore(directory);
        disabled.Load();
        False(disabled.StartAtLogin, "Turning automatic startup off should persist.");
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static ShortcutMessage NewMessage(string keyword) => new()
{
    Keyword = keyword,
    Title = keyword,
    Message = $"Message for {keyword}"
};

static void WithTemporaryStore(Action<ShortcutStore> action)
{
    var directory = Path.Combine(Path.GetTempPath(), $"zalo-shortcuts-tests-{Guid.NewGuid():N}");
    try
    {
        var store = new ShortcutStore(directory);
        store.Load();
        action(store);
    }
    finally
    {
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}

static void True(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void False(bool condition, string message) => True(!condition, message);

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}
