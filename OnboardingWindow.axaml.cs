using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Threading;
using ZaloShortcuts.Services;

namespace ZaloShortcuts;

public partial class OnboardingWindow : Window
{
    private readonly AppSettingsStore _settings;
    private readonly StartAtLoginManager _startAtLogin;
    private readonly Action _accessibilityGranted;
    private readonly DispatcherTimer _accessibilityTimer;
    private bool _hasNotifiedAccessibilityGranted;

    public OnboardingWindow()
        : this(new AppSettingsStore(
            Path.Combine(Path.GetTempPath(), "zalo-shortcuts-design")),
            new StartAtLoginManager(),
            () => { })
    {
    }

    public OnboardingWindow(AppSettingsStore settings)
        : this(settings, new StartAtLoginManager(), () => { })
    {
    }

    public OnboardingWindow(
        AppSettingsStore settings,
        StartAtLoginManager startAtLogin,
        Action accessibilityGranted)
    {
        _settings = settings;
        _startAtLogin = startAtLogin;
        _accessibilityGranted = accessibilityGranted;
        _accessibilityTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _accessibilityTimer.Tick += (_, _) => RefreshAccessibilityStatus();
        InitializeComponent();

        MacPermissionPanel.IsVisible = OperatingSystem.IsMacOS();
        WindowsPermissionPanel.IsVisible = !OperatingSystem.IsMacOS();
        StartAtLoginCheckBox.IsChecked = settings.HasStartAtLoginPreference
            ? settings.StartAtLogin
            : true;
        StartAtLoginCheckBox.IsEnabled = startAtLogin.IsSupported;

        if (!startAtLogin.IsSupported)
        {
            StartAtLoginCheckBox.IsChecked = false;
        }

        if (OperatingSystem.IsMacOS())
        {
            FinishButton.IsEnabled = false;
            Opened += (_, _) => BeginAccessibilitySetup();
            Closed += (_, _) => _accessibilityTimer.Stop();
        }
    }

    private void OpenAccessibilitySettingsClicked(object? sender, RoutedEventArgs e)
    {
        MacAccessibilityPermission.RequestAccess();
        OpenAccessibilitySettings();
        RefreshAccessibilityStatus();
        _accessibilityTimer.Start();
    }

    private void BeginAccessibilitySetup()
    {
        if (RefreshAccessibilityStatus())
        {
            return;
        }

        MacAccessibilityPermission.RequestAccess();
        OpenAccessibilitySettings();
        _accessibilityTimer.Start();
    }

    private bool RefreshAccessibilityStatus()
    {
        var trusted = MacAccessibilityPermission.IsTrusted();
        if (!OperatingSystem.IsMacOS())
        {
            return true;
        }

        FinishButton.IsEnabled = trusted;
        if (trusted)
        {
            AccessibilityStatusText.Foreground = Brushes.ForestGreen;
            AccessibilityStatusText.Text =
                "Accessibility permission is on. You can finish setup.";
            _accessibilityTimer.Stop();

            if (!_hasNotifiedAccessibilityGranted)
            {
                _hasNotifiedAccessibilityGranted = true;
                _accessibilityGranted();
            }
        }
        else
        {
            AccessibilityStatusText.Foreground =
                new SolidColorBrush(Color.Parse("#B42318"));
            AccessibilityStatusText.Text =
                "Waiting for permission — turn on Zalo Shortcuts in Accessibility.";
        }

        return trusted;
    }

    private void OpenAccessibilitySettings()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "x-apple.systempreferences:com.apple.preference.security?Privacy_Accessibility",
                UseShellExecute = true
            });
            SetupStatusText.Text =
                "Turn on Zalo Shortcuts in Accessibility. This guide will detect it automatically.";
        }
        catch (Exception exception)
        {
            SetupStatusText.Text =
                $"Could not open System Settings automatically. Open Privacy & Security → Accessibility manually. ({exception.Message})";
        }
    }

    private void FinishClicked(object? sender, RoutedEventArgs e)
    {
        if (OperatingSystem.IsMacOS() && !RefreshAccessibilityStatus())
        {
            SetupStatusText.Text =
                "Accessibility permission is still off. Turn it on before finishing.";
            OpenAccessibilitySettings();
            return;
        }

        var startAtLogin = StartAtLoginCheckBox.IsChecked == true;
        var result = _startAtLogin.SetEnabled(startAtLogin);
        if (!result.Success)
        {
            SetupStatusText.Text = result.Message;
            return;
        }

        _settings.SaveOnboardingPreferences(startAtLogin);
        SetupStatusText.Foreground = Brushes.ForestGreen;
        SetupStatusText.Text = result.Message;
        Close();
    }

    private void SkipClicked(object? sender, RoutedEventArgs e) => Close();
}
