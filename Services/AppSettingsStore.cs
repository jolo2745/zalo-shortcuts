using System.Text.Json;

namespace ZaloShortcuts.Services;

public sealed class AppSettingsStore
{
    private readonly object _gate = new();
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public AppSettingsStore(string dataDirectory)
    {
        DataDirectory = dataDirectory;
        FilePath = Path.Combine(dataDirectory, "settings.json");
    }

    public string DataDirectory { get; }

    public string FilePath { get; }

    public bool HasCompletedOnboarding { get; private set; }

    public bool HasStartAtLoginPreference { get; private set; }

    public bool StartAtLogin { get; private set; }

    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(FilePath))
            {
                Reset();
                return;
            }

            try
            {
                var json = File.ReadAllText(FilePath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json, _jsonOptions);
                HasCompletedOnboarding = settings?.HasCompletedOnboarding == true;
                HasStartAtLoginPreference = settings?.StartAtLogin.HasValue == true;
                StartAtLogin = settings?.StartAtLogin == true;
            }
            catch
            {
                Reset();
            }
        }
    }

    public void SaveOnboardingPreferences(bool startAtLogin)
    {
        lock (_gate)
        {
            HasCompletedOnboarding = true;
            HasStartAtLoginPreference = true;
            StartAtLogin = startAtLogin;
            Directory.CreateDirectory(DataDirectory);
            var json = JsonSerializer.Serialize(
                new AppSettings
                {
                    HasCompletedOnboarding = true,
                    StartAtLogin = startAtLogin
                },
                _jsonOptions);
            var temporaryPath = $"{FilePath}.tmp";
            File.WriteAllText(temporaryPath, json);
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
    }

    private void Reset()
    {
        HasCompletedOnboarding = false;
        HasStartAtLoginPreference = false;
        StartAtLogin = false;
    }

    private sealed class AppSettings
    {
        public bool HasCompletedOnboarding { get; set; }

        public bool? StartAtLogin { get; set; }
    }
}
