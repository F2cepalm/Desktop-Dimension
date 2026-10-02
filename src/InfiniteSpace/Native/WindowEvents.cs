using System.Runtime.ExceptionServices;
using System.Windows.Threading;

namespace InfiniteSpace.Native;

/// <summary>
/// Hears, as it happens, when another app's window comes to the front, is minimised or restored, or
/// is shown, so spaces sitting on that app can follow within the same compositor frame instead of
/// blinking, and spaces kept for a closed window can come back when it opens again.
/// </summary>
/// <remarks>Events arrive on the thread that created this (the UI thread), through its message loop.</remarks>
internal sealed class WindowEvents : IDisposable
{
    private readonly Win32.WinEventProc _callback; // must stay alive while hooked
    private readonly IntPtr[] _hooks;

    /// <param name="changed">Called with the event type and the top-level window it is about.</param>
    public WindowEvents(Action<uint, IntPtr> changed)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        _callback = (_, eventType, hwnd, idObject, idChild, _, _) =>
        {
            if (idObject != 0 || idChild != 0 || hwnd == IntPtr.Zero) // a part of a window, not the window itself
                return;
            try
            {
                changed(eventType, hwnd);
            }
            catch (Exception ex)
            {
                // Thrown back into native code it would end the process: report it the usual way instead.
                dispatcher.BeginInvoke(() => ExceptionDispatchInfo.Throw(ex));
            }
        };

        const uint flags = Win32.WINEVENT_OUTOFCONTEXT | Win32.WINEVENT_SKIPOWNPROCESS;
        _hooks =
        [
            Win32.SetWinEventHook(Win32.EVENT_SYSTEM_FOREGROUND, Win32.EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _callback, 0, 0, flags),
            Win32.SetWinEventHook(Win32.EVENT_SYSTEM_MINIMIZESTART, Win32.EVENT_SYSTEM_MINIMIZEEND, IntPtr.Zero, _callback, 0, 0, flags),
            Win32.SetWinEventHook(Win32.EVENT_OBJECT_SHOW, Win32.EVENT_OBJECT_SHOW, IntPtr.Zero, _callback, 0, 0, flags),
        ];
    }

    public void Dispose()
    {
        foreach (var hook in _hooks)
        {
            if (hook != IntPtr.Zero)
                Win32.UnhookWinEvent(hook);
        }
    }
}
