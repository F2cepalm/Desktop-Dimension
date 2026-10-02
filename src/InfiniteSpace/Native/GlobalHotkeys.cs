using System.Windows.Input;
using System.Windows.Interop;

namespace InfiniteSpace.Native;

/// <summary>
/// System-wide hotkeys via RegisterHotKey, received on a hidden message-only window.
/// Must be created and used on the UI thread.
/// </summary>
internal sealed class GlobalHotkeys : IDisposable
{
    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _handlers = [];
    private int _nextId = 0xB000;

    public GlobalHotkeys()
    {
        var parameters = new HwndSourceParameters("InfiniteSpace.Hotkeys")
        {
            WindowStyle = 0,
            ParentWindow = Win32.HWND_MESSAGE,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    /// <param name="repeat">Whether holding the keys fires repeatedly.</param>
    /// <returns>An id for <see cref="Unregister"/>, or null when another application owns the combination.</returns>
    public int? Register(ModifierKeys modifiers, Key key, Action handler, bool repeat = false)
    {
        uint mods = repeat ? 0 : Win32.MOD_NOREPEAT;
        if (modifiers.HasFlag(ModifierKeys.Alt)) mods |= Win32.MOD_ALT;
        if (modifiers.HasFlag(ModifierKeys.Control)) mods |= Win32.MOD_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) mods |= Win32.MOD_SHIFT;
        if (modifiers.HasFlag(ModifierKeys.Windows)) mods |= Win32.MOD_WIN;

        int id = _nextId++;
        uint vk = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!Win32.RegisterHotKey(_source.Handle, id, mods, vk))
            return null;

        _handlers[id] = handler;
        return id;
    }

    /// <summary>Gives the combination back to the rest of the system.</summary>
    public void Unregister(int id)
    {
        if (_handlers.Remove(id))
            Win32.UnregisterHotKey(_source.Handle, id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32.WM_HOTKEY && _handlers.TryGetValue(wParam.ToInt32(), out var handler))
        {
            handled = true;
            handler();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        foreach (int id in _handlers.Keys)
            Win32.UnregisterHotKey(_source.Handle, id);
        _handlers.Clear();
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }
}
