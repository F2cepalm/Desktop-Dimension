using System.IO;
using System.Windows;
using System.Windows.Threading;
using InfiniteSpace.Native;
using InfiniteSpace.Spaces;
using Microsoft.Extensions.Logging;
using Microsoft.Win32;

namespace InfiniteSpace.Shell;

/// <summary>Saving the space under the mouse to a .spsf file (Ctrl+Alt+F, or Save on the wheel), and opening one.</summary>
internal sealed partial class ShellController
{
    private Point? _wheelAt; // where the wheel opened: Save and Open act there, not where its sector was clicked
    private bool _inDialog;

    /// <summary>Ctrl+Alt+F: saves the space under the mouse.</summary>
    private void SaveSpaceUnderMouse() => SaveSpaceAt(CursorOnScreen());

    /// <summary>The wheel's or toolbar's Save and Open: once the wheel has gone, at the point it opened at.</summary>
    private void SaveOrOpenFromMenu(Tool tool, ToolSource source)
    {
        var at = source == ToolSource.Wheel && _wheelAt is { } wheel ? wheel : CursorOnScreen();
        // After the wheel has closed and handed focus back, so the dialog comes up in front of it.
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            if (tool == Tool.Save)
                SaveSpaceAt(at, fromToolbar: source == ToolSource.Toolbar);
            else
                OpenSavedSpace(at);
        });
    }

    private void SaveSpaceAt(Point at, bool fromToolbar = false)
    {
        if (_inDialog)
            return;
        CloseOverlay();
        FadeInkTools();
        if (spaces.SnapshotAt(at) is not { } snapshot)
        {
            _tray?.Notify("Save", fromToolbar
                ? "Point at a space and press Ctrl+Alt+F, or open the wheel over it and pick Save."
                : "Point at a space to save it.");
            return;
        }
        string folder = SaveFolder();
        var dialog = new SaveFileDialog
        {
            Title = "Save the space",
            Filter = SpaceFile.Filter,
            DefaultExt = SpaceFile.Extension,
            AddExtension = true,
            InitialDirectory = folder,
            FileName = SpaceFile.SuggestedName(snapshot, DateTime.Now) + SpaceFile.Extension,
            OverwritePrompt = true,
        };
        if (ShowDialog(dialog) is not { } path)
            return;
        try
        {
            string temp = path + ".tmp";
            using (var stream = File.Create(temp))
                SpaceFile.Write(stream, snapshot, spaces.Theme);
            File.Move(temp, path, overwrite: true);
            RememberFolder(path);
            _tray?.Notify("Space saved", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not save the space to {File}", path);
            _tray?.Notify("Could not save the space", ex.Message);
        }
    }

    /// <summary>The wheel's Open: a saved space, chosen in the Open dialog, put on screen with its top-left at <paramref name="at"/>.</summary>
    private void OpenSavedSpace(Point at)
    {
        if (_inDialog)
            return;
        CloseOverlay();
        FadeInkTools();
        var dialog = new OpenFileDialog
        {
            Title = "Open a saved space",
            Filter = SpaceFile.Filter,
            InitialDirectory = SaveFolder(),
            CheckFileExists = true,
        };
        if (ShowDialog(dialog) is not { } path)
            return;
        try
        {
            SpaceRecord saved;
            using (var stream = File.OpenRead(path))
                saved = SpaceFile.Read(stream);
            spaces.Open(saved, at);
            RememberFolder(path);
        }
        catch (SpaceFileException ex)
        {
            _tray?.Notify("Could not open the space", ex.Message);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogError(ex, "Could not open the space in {File}", path);
            _tray?.Notify("Could not open the space", ex.Message);
        }
    }

    /// <summary>The folder last saved to or opened from, else this app's own in Documents (made on first use).</summary>
    private string SaveFolder()
    {
        if (_settings.SaveDirectory is { Length: > 0 } chosen && Directory.Exists(chosen))
            return chosen;
        string folder = SpaceFile.DefaultFolder;
        try
        {
            Directory.CreateDirectory(folder);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            logger.LogWarning(ex, "Could not make {Folder}", folder);
        }
        return folder;
    }

    private void RememberFolder(string path)
    {
        string? folder = Path.GetDirectoryName(path);
        if (folder is null || string.Equals(folder, _settings.SaveDirectory, StringComparison.OrdinalIgnoreCase))
            return;
        _settings.SaveDirectory = folder;
        SaveSettings();
    }

    /// <summary>
    /// Shows a file dialog in front of everything: the app never takes focus otherwise, so an invisible
    /// window of its own owns the dialog, and focus goes back to the app in front afterwards.
    /// </summary>
    /// <returns>The file picked, or null.</returns>
    private string? ShowDialog(FileDialog dialog)
    {
        IntPtr previousApp = Foreground.CaptureHost();
        var owner = new Window
        {
            Width = 0,
            Height = 0,
            WindowStyle = WindowStyle.None,
            ShowInTaskbar = false,
            ShowActivated = true,
            AllowsTransparency = true,
            Opacity = 0,
            Left = CursorOnScreen().X,
            Top = CursorOnScreen().Y,
        };
        _inDialog = true;
        try
        {
            owner.Show();
            owner.Activate();
            return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
        }
        finally
        {
            _inDialog = false;
            owner.Close();
            Foreground.RestoreTo(previousApp);
            spaces.Restack();
        }
    }
}
