using System.IO;
using System.Windows;
using System.Windows.Media;
using InfiniteSpace.Native;

namespace InfiniteSpace.Spaces;

/// <summary>Saving a space to a file of its own, and opening one again (see <see cref="SpaceFile"/>).</summary>
internal sealed partial class SpaceManager
{
    /// <summary>
    /// The space at a screen DIP point, ready to save (see <see cref="SpaceFile.Snapshot"/>), with a splash
    /// there so it shows which one. Screen notes aren't spaces and can't be saved.
    /// </summary>
    /// <returns>null when no space is showing there.</returns>
    public SpaceRecord? SnapshotAt(Point at)
    {
        if (SpaceAt(at) is not { } space)
            return null;
        Bake(space); // the record in the coordinates it is shown in
        space.Window.Splash(at);
        return SpaceFile.Snapshot(space.Record);
    }

    /// <summary>
    /// Puts a saved space (see <see cref="SpaceFile.Read"/>) on screen with its top-left at <paramref name="at"/>
    /// (screen DIPs), on the app window there or the desktop. It never merges with spaces it lands on.
    /// </summary>
    public void Open(SpaceRecord saved, Point at)
    {
        saved.Transform(new Matrix(1, 0, 0, 1, Math.Round(at.X, 2), Math.Round(at.Y, 2)));
        IntPtr host = AppWindows.WindowAt(at);
        saved.Host = Describe(host);
        Place(saved, host, crossing: []);
        Persist(); // opened files' text goes to the app's own folder now
    }
}
