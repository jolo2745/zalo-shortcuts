using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ZaloShortcuts.Models;
using ZaloShortcuts.Services;

namespace ZaloShortcuts;

public partial class MainWindow : Window
{
    private static readonly IBrush HealthyBrush = new SolidColorBrush(Color.Parse("#2CA66F"));
    private static readonly IBrush ErrorBrush = new SolidColorBrush(Color.Parse("#D64545"));
    private readonly ShortcutStore _store;
    private readonly ShortcutEngine _engine;
    private readonly Action _showOnboarding;
    private Guid? _editingId;
    private bool _allowClose;
    private bool _updatingToggle;

    public MainWindow()
        : this(
            new ShortcutStore(),
            new ShortcutEngine(() => [], _ => { }, _ => { }),
            () => { })
    {
    }

    public MainWindow(ShortcutStore store, ShortcutEngine engine, Action showOnboarding)
    {
        _store = store;
        _engine = engine;
        _showOnboarding = showOnboarding;
        InitializeComponent();

        PlatformHelpText.Text = PlatformSupport.CurrentSummary;
        RefreshList();
        ClearForm();

        Activated += (_, _) => _engine.Suspended = true;
        Deactivated += (_, _) => _engine.Suspended = false;
        Opened += (_, _) => _engine.Suspended = true;
        Closing += OnClosing;
    }

    public void SetEngineStatus(EngineStatus status)
    {
        EngineStatusText.Text = status.Message;
        StatusDot.Fill = status.IsError ? ErrorBrush : HealthyBrush;
        SyncEnabledToggle();
    }

    public void SyncEnabledToggle()
    {
        _updatingToggle = true;
        EnabledToggle.IsChecked = _engine.Enabled;
        EnabledLabel.Text = _engine.Enabled ? "Shortcuts on" : "Shortcuts off";
        _updatingToggle = false;
    }

    public void AllowClose() => _allowClose = true;

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
        _engine.Suspended = false;
    }

    private void EnabledToggleChanged(object? sender, RoutedEventArgs e)
    {
        if (_updatingToggle)
        {
            return;
        }

        _engine.Enabled = EnabledToggle.IsChecked == true;
        EnabledLabel.Text = _engine.Enabled ? "Shortcuts on" : "Shortcuts off";
    }

    private void SetupGuideClicked(object? sender, RoutedEventArgs e) => _showOnboarding();

    private void NewClicked(object? sender, RoutedEventArgs e) => ClearForm();

    private void SaveClicked(object? sender, RoutedEventArgs e)
    {
        var result = _store.Upsert(
            _editingId,
            KeywordBox.Text ?? string.Empty,
            TitleBox.Text ?? string.Empty,
            MessageBox.Text ?? string.Empty);

        FormStatusText.Foreground = result.Success ? HealthyBrush : ErrorBrush;
        FormStatusText.Text = result.Message;

        if (!result.Success || result.Item is null)
        {
            return;
        }

        _editingId = result.Item.Id;
        RefreshList(result.Item.Id);
        DeleteButton.IsEnabled = true;
    }

    private void DeleteClicked(object? sender, RoutedEventArgs e)
    {
        if (_editingId is null)
        {
            return;
        }

        _store.Delete(_editingId.Value);
        RefreshList();
        ClearForm();
        FormStatusText.Foreground = HealthyBrush;
        FormStatusText.Text = "Message deleted.";
    }

    private void ShortcutSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (ShortcutList.SelectedItem is not ShortcutMessage item)
        {
            return;
        }

        _editingId = item.Id;
        KeywordBox.Text = item.Keyword;
        TitleBox.Text = item.Title;
        MessageBox.Text = item.Message;
        DeleteButton.IsEnabled = true;
        FormStatusText.Text = string.Empty;
    }

    private void SearchChanged(object? sender, TextChangedEventArgs e) => RefreshList(_editingId);

    private void RefreshList(Guid? selectId = null)
    {
        var query = SearchBox?.Text?.Trim() ?? string.Empty;
        var items = _store.GetSnapshot()
            .Where(item =>
                query.Length == 0 ||
                item.Keyword.Contains(query.TrimStart('/'), StringComparison.OrdinalIgnoreCase) ||
                item.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                item.Message.Contains(query, StringComparison.OrdinalIgnoreCase))
            .ToList();

        ShortcutList.ItemsSource = items;
        CountText.Text = $"{items.Count} saved message{(items.Count == 1 ? string.Empty : "s")}";

        if (selectId is not null)
        {
            ShortcutList.SelectedItem = items.FirstOrDefault(item => item.Id == selectId);
        }
    }

    private void ClearForm()
    {
        _editingId = null;
        if (KeywordBox is null)
        {
            return;
        }

        ShortcutList.SelectedItem = null;
        KeywordBox.Text = string.Empty;
        TitleBox.Text = string.Empty;
        MessageBox.Text = string.Empty;
        DeleteButton.IsEnabled = false;
        FormStatusText.Text = string.Empty;
        KeywordBox.Focus();
    }
}
