namespace InfiniteSpace.Native;

/// <summary>
/// The process is system-DPI aware (see app.manifest), so one scale converts every DIP coordinate
/// (WPF overlay, saved outlines) to the device pixels used by Win32 on every monitor.
/// </summary>
internal static class Dpi
{
    /// <summary>Device pixels per DIP.</summary>
    public static double Scale { get; } = Win32.GetDpiForSystem() / 96.0;
}
