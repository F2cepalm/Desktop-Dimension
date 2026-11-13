using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace InfiniteSpace.Native;

/// <summary>
/// Whether anyone can see the screen at all: not while the session is locked (or switched away from),
/// nor while the displays are off. Windows tells a hidden window of its own; nothing is polled.
/// </summary>
internal sealed class ScreenWatch : IDisposable
{
    private readonly HwndSource _window;
    private readonly IntPtr _displayNotification;
    private readonly bool _sessionNotification;
    private bool _locked;
    private bool _displaysOff;

    public ScreenWatch()
    {
        _window = new HwndSource(new HwndSourceParameters("Desktop_Dimension screen watch")
        {
            WindowStyle = unchecked((int)Win32.WS_POPUP),
            ExtendedWindowStyle = (int)(Win32.WS_EX_TOOLWINDOW | Win32.WS_EX_NOACTIVATE),
            Width = 0,
            Height = 0,
        });
        _window.AddHook(WndProc);
        _sessionNotification = Win32.WTSRegisterSessionNotification(_window.Handle, 0); // this session only
        var display = Win32.GUID_CONSOLE_DISPLAY_STATE;
        _displayNotification = Win32.RegisterPowerSettingNotification(_window.Handle, ref display, 0); // the current state comes at once
    }

    /// <summary>Nobody can see the screen: the session is locked or the displays are off.</summary>
    public bool Dark => _locked || _displaysOff;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        switch (msg)
        {
            case Win32.WM_WTSSESSION_CHANGE:
                if (wParam.ToInt32() == Win32.WTS_SESSION_LOCK)
                    _locked = true;
                else if (wParam.ToInt32() == Win32.WTS_SESSION_UNLOCK)
                    _locked = false;
                break;
            case Win32.WM_POWERBROADCAST when wParam.ToInt32() == Win32.PBT_POWERSETTINGCHANGE && lParam != IntPtr.Zero:
                // POWERBROADCAST_SETTING { GUID PowerSetting; DWORD DataLength; UCHAR Data[] }
                var setting = Marshal.PtrToStructure<Guid>(lParam);
                if (setting == Win32.GUID_CONSOLE_DISPLAY_STATE && Marshal.ReadInt32(lParam, 16) >= 4)
                    _displaysOff = Marshal.ReadInt32(lParam, 20) == 0;
                break;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_displayNotification != IntPtr.Zero)
            Win32.UnregisterPowerSettingNotification(_displayNotification);
        if (_sessionNotification)
            Win32.WTSUnRegisterSessionNotification(_window.Handle);
        _window.RemoveHook(WndProc);
        _window.Dispose();
    }
}
