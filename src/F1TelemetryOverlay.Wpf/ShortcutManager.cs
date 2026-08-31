using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Input;

namespace F1TelemetryOverlay.Wpf;

internal enum ShortcutAction
{
    ToggleVisibility,
    ToggleLock,
    ToggleDemo,
    ToggleSteering,
    Quit,
}

internal sealed class ShortcutManager : IDisposable
{
    private readonly HwndSource _source;
    private readonly IntPtr _handle;
    private readonly Dictionary<int, ShortcutAction> _actions = [];
    private readonly Dictionary<int, (uint Modifiers, uint Key, ShortcutAction Action)> _bindings = [];
    private readonly List<int> _registeredIds = [];
    private bool _disposed;

    internal ShortcutManager(System.Windows.Window window)
    {
        _handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(_handle)
            ?? throw new InvalidOperationException("The overlay window handle is unavailable.");
        _source.AddHook(WindowHook);
    }

    internal bool TryReplace(F1TelemetryOverlay.Core.ShortcutSettings shortcuts,
        Action<ShortcutAction> callback, out string error)
    {
        ArgumentNullException.ThrowIfNull(shortcuts);
        ArgumentNullException.ThrowIfNull(callback);
        error = string.Empty;

        (string Text, ShortcutAction Action)[] entries =
        [
            (shortcuts.ToggleVisibility, ShortcutAction.ToggleVisibility),
            (shortcuts.ToggleLock, ShortcutAction.ToggleLock),
            (shortcuts.ToggleDemo, ShortcutAction.ToggleDemo),
            (shortcuts.ToggleSteering, ShortcutAction.ToggleSteering),
            (shortcuts.Quit, ShortcutAction.Quit),
        ];

        List<(uint Modifiers, uint Key, ShortcutAction Action, string Text)> parsed = [];
        HashSet<string> normalized = new(StringComparer.OrdinalIgnoreCase);
        foreach ((string text, ShortcutAction action) in entries)
        {
            if (!TryParse(text, out uint modifiers, out uint key))
            {
                error = $"The shortcut {text} is invalid.";
                return false;
            }

            string identity = $"{modifiers}:{key}";
            if (!normalized.Add(identity))
            {
                error = "Every shortcut must be unique.";
                return false;
            }

            parsed.Add((modifiers, key, action, text));
        }

        Dictionary<int, (uint Modifiers, uint Key, ShortcutAction Action)> previousBindings = new(_bindings);
        UnregisterCurrent();
        _actions.Clear();
        _bindings.Clear();

        for (int index = 0; index < parsed.Count; index++)
        {
            (uint modifiers, uint key, ShortcutAction action, string text) = parsed[index];
            int id = 0x4F1 + index;
            if (!NativeMethods.RegisterHotKey(_handle, id, modifiers, key))
            {
                UnregisterCurrent();
                _actions.Clear();
                _bindings.Clear();
                if (!Restore(previousBindings))
                {
                    error = $"The shortcut {text} is unavailable, and Windows could not restore the previous shortcuts.";
                }
                else
                {
                    error = $"The shortcut {text} is already used by another application.";
                }

                return false;
            }

            _registeredIds.Add(id);
            _actions[id] = action;
            _bindings[id] = (modifiers, key, action);
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnregisterCurrent();
        _source.RemoveHook(WindowHook);
    }

    private bool Restore(Dictionary<int, (uint Modifiers, uint Key, ShortcutAction Action)> bindings)
    {
        foreach ((int id, (uint modifiers, uint key, ShortcutAction action)) in bindings)
        {
            if (!NativeMethods.RegisterHotKey(_handle, id, modifiers, key))
            {
                foreach (int registeredId in _registeredIds) NativeMethods.UnregisterHotKey(_handle, registeredId);
                _registeredIds.Clear();
                _actions.Clear();
                _bindings.Clear();
                return false;
            }

            _registeredIds.Add(id);
            _actions[id] = action;
            _bindings[id] = (modifiers, key, action);
        }

        return true;
    }

    private void UnregisterCurrent()
    {
        foreach (int id in _registeredIds) NativeMethods.UnregisterHotKey(_handle, id);
        _registeredIds.Clear();
        _bindings.Clear();
    }

    private IntPtr WindowHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotKey && _actions.TryGetValue(wParam.ToInt32(), out ShortcutAction action))
        {
            _source.Dispatcher.BeginInvoke(() => ShortcutPressed?.Invoke(action));
            handled = true;
        }

        return IntPtr.Zero;
    }

    internal event Action<ShortcutAction>? ShortcutPressed;

    /// <summary>
    /// Formats a key press using the same modifier names accepted by
    /// <see cref="TryParse"/>. This keeps the settings capture surface from
    /// having to synthesize a string that registration cannot consume.
    /// </summary>
    internal static bool TryFormat(Key key, ModifierKeys modifiers, out string text)
    {
        text = string.Empty;
        if (IsModifierKey(key) || modifiers == ModifierKeys.None) return false;

        int virtualKey = KeyInterop.VirtualKeyFromKey(key);
        if (virtualKey is <= 0 or > 0xFF) return false;

        List<string> tokens = [];
        if ((modifiers & ModifierKeys.Control) != 0) tokens.Add("Control");
        if ((modifiers & ModifierKeys.Alt) != 0) tokens.Add("Alt");
        if ((modifiers & ModifierKeys.Shift) != 0) tokens.Add("Shift");
        if ((modifiers & ModifierKeys.Windows) != 0) tokens.Add("Windows");
        tokens.Add(key.ToString());
        text = string.Join('+', tokens);
        return true;
    }

    private static bool TryParse(string? text, out uint modifiers, out uint key)
    {
        modifiers = 0;
        key = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string[] tokens = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (tokens.Length < 2) return false;

        HashSet<string> seenModifiers = new(StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < tokens.Length - 1; index++)
        {
            string token = tokens[index];
            if (!seenModifiers.Add(token)) return false;
            switch (token.ToLowerInvariant())
            {
                case "alt": modifiers |= NativeMethods.ModAlt; break;
                case "control":
                case "ctrl": modifiers |= NativeMethods.ModControl; break;
                case "shift": modifiers |= NativeMethods.ModShift; break;
                case "win":
                case "windows":
                case "meta": modifiers |= NativeMethods.ModWin; break;
                default: return false;
            }
        }

        if (modifiers == 0) return false;
        string keyToken = tokens[^1];
        if (!Enum.TryParse(keyToken, true, out Key parsedKey) || parsedKey == Key.None) return false;
        int virtualKey = KeyInterop.VirtualKeyFromKey(parsedKey);
        if (virtualKey is <= 0 or > 0xFF) return false;
        key = (uint)virtualKey;
        return true;
    }

    private static bool IsModifierKey(Key key) => key is
        Key.LeftAlt or Key.RightAlt or
        Key.LeftCtrl or Key.RightCtrl or
        Key.LeftShift or Key.RightShift or
        Key.LWin or Key.RWin;
}
