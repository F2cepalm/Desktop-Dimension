using System.Windows;

namespace InfiniteSpace.Shell;

/// <summary>
/// Whether the mouse moved lately, from positions sampled now and then. T and E only belong to a
/// space while it did: someone typing into the app under a space, with the mouse resting on it,
/// keeps their letters.
/// </summary>
/// <param name="recent">How long after the mouse last moved it still counts as moving.</param>
internal sealed class CursorActivity(TimeSpan recent)
{
    private Point? _last;
    private TimeSpan _movedAt;

    /// <param name="cursor">Where the mouse is now.</param>
    /// <param name="now">The time now, on any clock that only goes forward.</param>
    /// <returns>Whether the mouse moved within the last <c>recent</c> (a first sample counts as a move).</returns>
    public bool Sample(Point cursor, TimeSpan now)
    {
        if (_last != cursor)
        {
            _last = cursor;
            _movedAt = now;
        }
        return now - _movedAt < recent;
    }
}
