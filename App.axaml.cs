using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Threading;
using ZaloShortcuts.Services;

namespace ZaloShortcuts;

public partial class App : Application
{
    private IClassicDesktopStyleApplicationLifetime? _desktop;
    private MainWindow? _mainWindow;
    private SuggestionWindow? _suggestionWindow;
    private OnboardingWindow? _onboardingWindow;
    private ShortcutEngine? _engine;
    private AppSettingsStore? _settings;
    private TrayIcon? _trayIcon;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            _desktop = desktop;
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;

            var store = new ShortcutStore();
            store.Load();
            _settings = new AppSettingsStore(store.DataDirectory);
            _settings.Load();

            _suggestionWindow = new SuggestionWindow();
            _engine = new ShortcutEngine(
                store.GetSnapshot,
                snapshot => Dispatcher.UIThread.Post(
                    () => _suggestionWindow?.ApplySnapshot(snapshot)),
                status => Dispatcher.UIThread.Post(
                    () => _mainWindow?.SetEngineStatus(status)));

            _mainWindow = new MainWindow(store, _engine, ShowOnboarding);
            _mainWindow.Opened += (_, _) =>
            {
                var needsSetup =
                    _settings?.HasCompletedOnboarding == false ||
                    _settings?.HasStartAtLoginPreference == false ||
                    OperatingSystem.IsMacOS() &&
                    !MacAccessibilityPermission.IsTrusted();

                if (Program.LaunchInBackground && !needsSetup)
                {
                    _mainWindow.Hide();
                    _mainWindow.ShowActivated = true;
                    _mainWindow.ShowInTaskbar = true;
                }
                else if (needsSetup)
                {
                    Dispatcher.UIThread.Post(ShowOnboarding);
                }
            };

            if (Program.LaunchInBackground &&
                _settings.HasCompletedOnboarding &&
                _settings.HasStartAtLoginPreference)
            {
                _mainWindow.ShowActivated = false;
                _mainWindow.ShowInTaskbar = false;
            }

            desktop.MainWindow = _mainWindow;
            SetupTrayIcon();

            if (desktop is IActivatableLifetime activatableLifetime)
            {
                activatableLifetime.Activated += (_, eventArgs) =>
                {
                    if (eventArgs.Kind == ActivationKind.Reopen)
                    {
                        Dispatcher.UIThread.Post(ShowMainWindow);
                    }
                };
            }

            desktop.Exit += (_, _) => DisposeServices();
            _engine.Start();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private void SetupTrayIcon()
    {
        if (_mainWindow is null || _engine is null)
        {
            return;
        }

        var showItem = new NativeMenuItem("Open shortcut manager");
        showItem.Click += (_, _) => ShowMainWindow();

        var toggleItem = new NativeMenuItem("Enable / disable shortcuts");
        toggleItem.Click += (_, _) =>
        {
            if (_engine is not null)
            {
                _engine.Enabled = !_engine.Enabled;
                _mainWindow?.SyncEnabledToggle();
            }
        };

        var quitItem = new NativeMenuItem("Quit");
        quitItem.Click += (_, _) => ExitApplication();

        var menu = new NativeMenu();
        menu.Add(showItem);
        menu.Add(toggleItem);
        menu.Add(new NativeMenuItemSeparator());
        menu.Add(quitItem);

        _trayIcon = new TrayIcon
        {
            ToolTipText = "Zalo Shortcuts",
            IsVisible = true,
            Menu = menu
        };

        try
        {
            using var iconStream = AssetLoader.Open(new Uri("avares://ZaloShortcuts/Assets/icon.png"));
            _trayIcon.Icon = new WindowIcon(iconStream);
        }
        catch
        {
            // The menu remains usable on platforms which can render a default tray icon.
        }

        _trayIcon.Clicked += (_, _) => ShowMainWindow();
        TrayIcon.SetIcons(this, new TrayIcons { _trayIcon });
    }

    private void ShowMainWindow()
    {
        if (_mainWindow is null)
        {
            return;
        }

        _mainWindow.Show();
        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Activate();
    }

    private void ShowOnboarding()
    {
        if (_mainWindow is null || _settings is null)
        {
            return;
        }

        if (_onboardingWindow is { IsVisible: true })
        {
            _onboardingWindow.Activate();
            return;
        }

        _onboardingWindow = new OnboardingWindow(
            _settings,
            new StartAtLoginManager(),
            () => _engine?.Restart());
        _onboardingWindow.Closed += (_, _) => _onboardingWindow = null;
        _ = _onboardingWindow.ShowDialog(_mainWindow);
    }

    private void ExitApplication()
    {
        _mainWindow?.AllowClose();
        DisposeServices();
        _desktop?.Shutdown();
    }

    private void DisposeServices()
    {
        _engine?.Dispose();
        _engine = null;
        _suggestionWindow?.Close();
        _suggestionWindow = null;
        _onboardingWindow?.Close();
        _onboardingWindow = null;
        _trayIcon?.Dispose();
        _trayIcon = null;
    }
}
