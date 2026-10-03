using System.IO;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;

namespace InfiniteSpace.Spaces;

/// <summary>
/// Saves spaces without ever holding up a frame: changes that come close together are saved once, a
/// moment after they stop (or at least every couple of seconds while they don't), and the disk is
/// written on a background thread, newest save first, one at a time.
/// </summary>
/// <remarks>
/// The spaces are read on the UI thread, which owns them: what undo can no longer reach is let go
/// (<see cref="SpaceRecord.TrimHistory"/>), and the JSON is made (<see cref="SpaceStore.Prepare"/>). Only
/// the file work, which antivirus scanners and slow disks can stretch to whole seconds, runs elsewhere.
/// </remarks>
internal sealed class SaveQueue
{
    /// <summary>How long changes must stop before they are saved.</summary>
    internal static readonly TimeSpan Quiet = TimeSpan.FromMilliseconds(400);

    /// <summary>The longest changes go unsaved while they keep coming.</summary>
    internal static readonly TimeSpan Longest = TimeSpan.FromSeconds(2);

    private readonly SpaceStore _store;
    private readonly ILogger _logger;
    private readonly Func<IEnumerable<SpaceRecord>> _records;
    private readonly DispatcherTimer _timer;
    private readonly object _gate = new();
    private DateTime? _firstUnsaved;
    private SpaceStore.PendingSave? _next; // the newest save not yet written; older ones are skipped
    private Task _writer = Task.CompletedTask;
    private bool _writing;

    /// <param name="records">The spaces to save, read on the UI thread when a save is due.</param>
    public SaveQueue(SpaceStore store, ILogger logger, Dispatcher dispatcher, Func<IEnumerable<SpaceRecord>> records)
    {
        _store = store;
        _logger = logger;
        _records = records;
        _timer = new DispatcherTimer(Quiet, DispatcherPriority.Background, (_, _) => SaveDue(), dispatcher);
        _timer.Stop();
    }

    /// <summary>Something changed: it is saved once changes stop for a moment (UI thread).</summary>
    public void Request()
    {
        var now = DateTime.UtcNow;
        _firstUnsaved ??= now;
        _timer.Stop();
        var wait = Quiet;
        var due = _firstUnsaved.Value + Longest - now;
        if (due < wait)
            wait = due < TimeSpan.Zero ? TimeSpan.Zero : due;
        _timer.Interval = wait;
        _timer.Start();
    }

    /// <summary>
    /// Saves whatever is waiting and waits for the disk (UI thread): before the app exits, and wherever
    /// the saved file must be up to date right away.
    /// </summary>
    public void Flush()
    {
        _timer.Stop();
        if (_firstUnsaved is not null)
            Enqueue(Prepare());
        Task writer;
        lock (_gate)
            writer = _writer;
        if (!writer.Wait(TimeSpan.FromSeconds(10)))
            _logger.LogWarning("Saving spaces to {File} is taking very long", _store.FilePath);
    }

    private void SaveDue()
    {
        _timer.Stop();
        if (_firstUnsaved is not null)
            Enqueue(Prepare());
    }

    private SpaceStore.PendingSave? Prepare()
    {
        _firstUnsaved = null;
        try
        {
            var records = _records().ToList();
            foreach (var record in records)
                record.TrimHistory();
            return _store.Prepare(records);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            _logger.LogError(ex, "Could not save spaces to {File}", _store.FilePath);
            return null;
        }
    }

    private void Enqueue(SpaceStore.PendingSave? save)
    {
        if (save is null)
            return;
        lock (_gate)
        {
            _next = save; // a save still waiting is older: this one has everything it had
            if (_writing)
                return; // the writer picks it up when it is done with the one it is on
            _writing = true;
            _writer = Task.Run(WriteAll);
        }
    }

    private void WriteAll()
    {
        while (true)
        {
            SpaceStore.PendingSave? save;
            lock (_gate)
            {
                save = _next;
                _next = null;
                if (save is null)
                {
                    _writing = false;
                    return;
                }
            }
            try
            {
                save.Write();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logger.LogError(ex, "Could not save spaces to {File}", _store.FilePath);
            }
        }
    }
}
