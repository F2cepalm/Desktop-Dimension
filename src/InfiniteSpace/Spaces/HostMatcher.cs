using System.IO;

namespace InfiniteSpace.Spaces;

/// <summary>
/// Decides which open windows get back the spaces of windows that were closed: a window of the same
/// app with the same title first (the same document), else any window of the same app.
/// </summary>
internal static class HostMatcher
{
    /// <summary>Spaces that were on one window, now waiting for it to open again.</summary>
    /// <param name="Group">Their shared <see cref="HostInfo.Window"/>.</param>
    internal readonly record struct Waiting(Guid Group, string App, string WindowClass, string Title);

    /// <summary>An open window that could take them.</summary>
    internal readonly record struct Open(IntPtr Handle, string App, string WindowClass, string Title);

    /// <summary>Programs whose windows are all alike (one host program for many store apps): only a title tells them apart.</summary>
    private static readonly HashSet<string> SharedHosts = new(StringComparer.OrdinalIgnoreCase) { "ApplicationFrameHost.exe" };

    /// <summary>
    /// Pairs waiting groups with open windows, one window per group, trying windows in the order given
    /// (front to back).
    /// </summary>
    /// <param name="busy">Windows that already carry spaces; they are not given more.</param>
    public static List<(Guid Group, IntPtr Window)> Match(IReadOnlyList<Waiting> waiting, IReadOnlyList<Open> windows, ISet<IntPtr> busy)
    {
        var pairs = new List<(Guid, IntPtr)>();
        var taken = new HashSet<IntPtr>(busy);
        var placed = new HashSet<Guid>();

        // The same document first.
        foreach (var group in waiting)
        {
            foreach (var window in windows)
            {
                if (taken.Contains(window.Handle) || !SameApp(group, window) || group.Title.Length == 0 || window.Title != group.Title)
                    continue;
                pairs.Add((group.Group, window.Handle));
                taken.Add(window.Handle);
                placed.Add(group.Group);
                break;
            }
        }

        // Then any window of the same app.
        foreach (var group in waiting)
        {
            if (placed.Contains(group.Group) || SharedHosts.Contains(Path.GetFileName(group.App)))
                continue;
            foreach (var window in windows)
            {
                if (taken.Contains(window.Handle) || !SameApp(group, window))
                    continue;
                pairs.Add((group.Group, window.Handle));
                taken.Add(window.Handle);
                placed.Add(group.Group);
                break;
            }
        }
        return pairs;
    }

    private static bool SameApp(Waiting group, Open window) =>
        string.Equals(group.App, window.App, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(group.WindowClass, window.WindowClass, StringComparison.Ordinal);
}
