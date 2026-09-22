using SharpHook;
using SharpHook.Data;
using ZaloShortcuts.Models;

namespace ZaloShortcuts.Services;

public sealed class ShortcutEngine : IDisposable
{
    private readonly object _gate = new();
    private readonly Func<IReadOnlyList<ShortcutMessage>> _shortcutProvider;
    private readonly Action<SuggestionSnapshot> _suggestionsChanged;
    private readonly Action<EngineStatus> _statusChanged;
    private readonly EventSimulator _simulator = new()
    {
        TextSimulationDelayOnX11 = TimeSpan.FromMilliseconds(35)
    };

    private SimpleGlobalHook? _hook;
    private string _buffer = string.Empty;
    private bool _enabled = true;
    private bool _suspended;
    private bool _suppressTabRelease;
    private bool _disposed;

    public ShortcutEngine(
        Func<IReadOnlyList<ShortcutMessage>> shortcutProvider,
        Action<SuggestionSnapshot> suggestionsChanged,
        Action<EngineStatus> statusChanged)
    {
        _shortcutProvider = shortcutProvider;
        _suggestionsChanged = suggestionsChanged;
        _statusChanged = statusChanged;
    }

    public bool Enabled
    {
        get
        {
            lock (_gate)
            {
                return _enabled;
            }
        }
        set
        {
            lock (_gate)
            {
                _enabled = value;
                if (!value)
                {
                    _buffer = string.Empty;
                }
            }

            if (!value)
            {
                HideSuggestions();
            }

            PublishStatus(
                value ? "Shortcuts are on. Type /part and press Tab." : "Shortcuts are paused.",
                isError: false);
        }
    }

    public bool Suspended
    {
        get
        {
            lock (_gate)
            {
                return _suspended;
            }
        }
        set
        {
            lock (_gate)
            {
                _suspended = value;
                if (value)
                {
                    _buffer = string.Empty;
                }
            }

            if (value)
            {
                HideSuggestions();
            }
        }
    }

    public void Start()
    {
        if (_disposed || _hook is not null)
        {
            return;
        }

        if (PlatformSupport.IsWaylandOnly)
        {
            PublishStatus(
                "Global shortcuts need an X11 desktop session on Linux; Wayland blocks this keyboard hook.",
                isError: true);
            return;
        }

        try
        {
            _hook = new SimpleGlobalHook(GlobalHookType.All);
            _hook.HookEnabled += OnHookEnabled;
            _hook.HookDisabled += OnHookDisabled;
            _hook.KeyTyped += OnKeyTyped;
            _hook.KeyPressed += OnKeyPressed;
            _hook.KeyReleased += OnKeyReleased;
            _hook.MousePressed += OnMousePressed;

            var runningHook = _hook;
            _ = runningHook.RunAsync().ContinueWith(
                task =>
                {
                    if (task.Exception is not null &&
                        ReferenceEquals(_hook, runningHook))
                    {
                        PublishStatus(
                            $"Keyboard listener stopped: {task.Exception.GetBaseException().Message}",
                            isError: true);
                    }
                },
                TaskScheduler.Default);
        }
        catch (Exception exception)
        {
            PublishStatus($"Could not start keyboard listener: {exception.Message}", isError: true);
        }
    }

    public void Restart()
    {
        if (_disposed)
        {
            return;
        }

        StopHook();
        Start();
    }

    private void OnHookEnabled(object? sender, HookEventArgs e) =>
        PublishStatus("Shortcuts are on. Type /part and press Tab.", isError: false);

    private void OnHookDisabled(object? sender, HookEventArgs e) =>
        PublishStatus("Keyboard listener stopped.", isError: true);

    private void OnMousePressed(object? sender, MouseHookEventArgs e)
    {
        if (e.IsEventSimulated)
        {
            return;
        }

        ClearBuffer();
    }

    private void OnKeyTyped(object? sender, KeyboardHookEventArgs e)
    {
        if (e.IsEventSimulated || !CanTrack())
        {
            return;
        }

        var character = e.Data.KeyChar;
        SuggestionSnapshot? snapshot = null;

        lock (_gate)
        {
            if (character == '/')
            {
                _buffer = "/";
            }
            else if (_buffer.Length > 0 && IsKeywordCharacter(character))
            {
                if (_buffer.Length >= 41)
                {
                    _buffer = string.Empty;
                }
                else
                {
                    _buffer += char.ToLowerInvariant(character);
                }
            }
            else if (_buffer.Length > 0)
            {
                _buffer = string.Empty;
            }

            if (_buffer.Length > 0)
            {
                var matches = ShortcutMatcher.Find(_shortcutProvider(), _buffer);
                if (matches.Count == 0)
                {
                    _buffer = string.Empty;
                }
                else
                {
                    snapshot = new SuggestionSnapshot(true, _buffer, matches);
                }
            }
        }

        if (snapshot is null)
        {
            HideSuggestions();
        }
        else
        {
            _suggestionsChanged(snapshot);
        }
    }

    private void OnKeyPressed(object? sender, KeyboardHookEventArgs e)
    {
        if (e.IsEventSimulated || !CanTrack())
        {
            return;
        }

        switch (e.Data.KeyCode)
        {
            case KeyCode.VcTab:
                TryExpand(e);
                break;
            case KeyCode.VcBackspace:
                HandleBackspace();
                break;
            case KeyCode.VcEscape:
            case KeyCode.VcEnter:
            case KeyCode.VcSpace:
            case KeyCode.VcLeft:
            case KeyCode.VcRight:
            case KeyCode.VcUp:
            case KeyCode.VcDown:
            case KeyCode.VcHome:
            case KeyCode.VcEnd:
            case KeyCode.VcDelete:
                ClearBuffer();
                break;
        }
    }

    private void OnKeyReleased(object? sender, KeyboardHookEventArgs e)
    {
        if (e.IsEventSimulated)
        {
            return;
        }

        if (e.Data.KeyCode == KeyCode.VcTab && _suppressTabRelease && PlatformSupport.CanSuppressKeys)
        {
            e.SuppressEvent = true;
            _suppressTabRelease = false;
        }
    }

    private void TryExpand(KeyboardHookEventArgs e)
    {
        ShortcutMessage? selected;
        string trigger;

        lock (_gate)
        {
            if (_buffer.Length == 0)
            {
                return;
            }

            selected = ShortcutMatcher.Find(_shortcutProvider(), _buffer, limit: 1).FirstOrDefault();
            if (selected is null)
            {
                _buffer = string.Empty;
                return;
            }

            trigger = _buffer;
            _buffer = string.Empty;
            if (PlatformSupport.CanSuppressKeys)
            {
                e.SuppressEvent = true;
                _suppressTabRelease = true;
            }
        }

        HideSuggestions();
        _ = Task.Run(() => ExpandAsync(trigger, selected.Message));
    }

    private async Task ExpandAsync(string trigger, string message)
    {
        try
        {
            if (PlatformSupport.IsLinux)
            {
                // Linux/X11 cannot suppress the original Tab. Move focus back to the
                // previous control before deleting the typed trigger.
                await Task.Delay(80);
                SimulateShiftTab();
                await Task.Delay(45);
            }
            else
            {
                await Task.Delay(15);
            }

            for (var index = 0; index < trigger.Length; index++)
            {
                _simulator.SimulateKeyPress(KeyCode.VcBackspace);
                _simulator.SimulateKeyRelease(KeyCode.VcBackspace);
            }

            _simulator.SimulateTextEntry(message);
            PublishStatus("Message inserted. Press Enter normally to send.", isError: false);
        }
        catch (Exception exception)
        {
            PublishStatus($"Could not insert the message: {exception.Message}", isError: true);
        }
    }

    private void SimulateShiftTab()
    {
        _simulator.SimulateKeyPress(KeyCode.VcLeftShift);
        _simulator.SimulateKeyPress(KeyCode.VcTab);
        _simulator.SimulateKeyRelease(KeyCode.VcTab);
        _simulator.SimulateKeyRelease(KeyCode.VcLeftShift);
    }

    private void HandleBackspace()
    {
        SuggestionSnapshot? snapshot = null;

        lock (_gate)
        {
            if (_buffer.Length == 0)
            {
                return;
            }

            _buffer = _buffer[..^1];
            if (_buffer.Length > 0)
            {
                var matches = ShortcutMatcher.Find(_shortcutProvider(), _buffer);
                if (matches.Count > 0)
                {
                    snapshot = new SuggestionSnapshot(true, _buffer, matches);
                }
            }
        }

        if (snapshot is null)
        {
            HideSuggestions();
        }
        else
        {
            _suggestionsChanged(snapshot);
        }
    }

    private bool CanTrack()
    {
        lock (_gate)
        {
            return _enabled && !_suspended;
        }
    }

    private void ClearBuffer()
    {
        lock (_gate)
        {
            _buffer = string.Empty;
        }

        HideSuggestions();
    }

    private void HideSuggestions() =>
        _suggestionsChanged(new SuggestionSnapshot(false, string.Empty, []));

    private void PublishStatus(string message, bool isError) =>
        _statusChanged(new EngineStatus(message, isError));

    private static bool IsKeywordCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_';

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        StopHook();
    }

    private void StopHook()
    {
        var hook = _hook;
        _hook = null;
        if (hook is not null)
        {
            hook.HookEnabled -= OnHookEnabled;
            hook.HookDisabled -= OnHookDisabled;
            hook.KeyTyped -= OnKeyTyped;
            hook.KeyPressed -= OnKeyPressed;
            hook.KeyReleased -= OnKeyReleased;
            hook.MousePressed -= OnMousePressed;
            hook.Dispose();
        }
    }
}

public sealed record SuggestionSnapshot(
    bool IsVisible,
    string TypedToken,
    IReadOnlyList<ShortcutMessage> Matches);

public sealed record EngineStatus(string Message, bool IsError);
