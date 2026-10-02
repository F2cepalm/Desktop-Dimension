using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using InfiniteSpace.Native;

namespace InfiniteSpace.Shell;

/// <summary>Fades a popup (the ink palette, a text box) out of the way once the user gets on with painting or erasing.</summary>
internal static class WindowFade
{
    /// <summary>From fully there to gone.</summary>
    public static readonly TimeSpan Duration = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Fades a window out, then calls <paramref name="close"/>. Meanwhile it takes no clicks: they reach
    /// whatever is under it, so painting carries on through it.
    /// </summary>
    /// <remarks>The window must allow transparency (be layered), as the popups here do.</remarks>
    public static void Out(Window window, Action close)
    {
        var hwnd = new WindowInteropHelper(window).Handle;
        if (hwnd != IntPtr.Zero)
            Win32.SetWindowLong(hwnd, Win32.GWL_EXSTYLE, Win32.GetWindowLong(hwnd, Win32.GWL_EXSTYLE) | Win32.WS_EX_TRANSPARENT | Win32.WS_EX_NOACTIVATE);
        window.IsHitTestVisible = false;

        var fade = new DoubleAnimation(0, new Duration(Duration)) { EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => close();
        window.BeginAnimation(UIElement.OpacityProperty, fade);
    }
}
