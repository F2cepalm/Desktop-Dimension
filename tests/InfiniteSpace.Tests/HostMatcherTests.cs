using InfiniteSpace.Spaces;
using Open = InfiniteSpace.Spaces.HostMatcher.Open;
using Waiting = InfiniteSpace.Spaces.HostMatcher.Waiting;

namespace InfiniteSpace.Tests;

/// <summary>Which reopened window gets the spaces of a closed one.</summary>
public class HostMatcherTests
{
    private const string Notes = @"C:\Apps\notes.exe";
    private static readonly Guid A = Guid.NewGuid(), B = Guid.NewGuid();
    private static readonly HashSet<IntPtr> None = [];

    [Fact]
    public void TheSameDocument_WinsOverAnyWindowOfTheApp()
    {
        var waiting = new List<Waiting> { new(A, Notes, "Notes", "b.txt - Notes") };
        var open = new List<Open> { new(1, Notes, "Notes", "a.txt - Notes"), new(2, Notes, "Notes", "b.txt - Notes") };

        Assert.Equal([(A, (IntPtr)2)], HostMatcher.Match(waiting, open, None));
    }

    [Fact]
    public void WithoutTheSameTitle_AnyWindowOfTheAppWillDo_FrontMostFirst()
    {
        var waiting = new List<Waiting> { new(A, Notes, "Notes", "old.txt - Notes") };
        var open = new List<Open> { new(7, Notes, "Notes", "new.txt - Notes"), new(8, Notes, "Notes", "other.txt - Notes") };

        Assert.Equal([(A, (IntPtr)7)], HostMatcher.Match(waiting, open, None));
    }

    [Fact]
    public void OtherApps_AndOtherKindsOfWindow_AreNotTaken()
    {
        var waiting = new List<Waiting> { new(A, Notes, "Notes", "a.txt") };
        var open = new List<Open>
        {
            new(1, @"C:\Apps\paint.exe", "Notes", "a.txt"),     // another program
            new(2, Notes, "NotesSplash", "a.txt"),             // the same program's splash screen
        };

        Assert.Empty(HostMatcher.Match(waiting, open, None));
    }

    [Fact]
    public void ProgramPaths_AreCompared_IgnoringCase()
    {
        var waiting = new List<Waiting> { new(A, Notes, "Notes", "x") };
        var open = new List<Open> { new(3, Notes.ToUpperInvariant(), "Notes", "y") };

        Assert.Single(HostMatcher.Match(waiting, open, None));
    }

    [Fact]
    public void EachWindow_TakesOneGroup_AndBusyWindowsTakeNone()
    {
        var waiting = new List<Waiting> { new(A, Notes, "Notes", "a"), new(B, Notes, "Notes", "b") };
        var open = new List<Open> { new(1, Notes, "Notes", "z"), new(2, Notes, "Notes", "b") };

        var pairs = HostMatcher.Match(waiting, open, None);
        Assert.Equal(2, pairs.Count);
        Assert.Contains((B, (IntPtr)2), pairs);  // the title match first
        Assert.Contains((A, (IntPtr)1), pairs);

        Assert.Empty(HostMatcher.Match(waiting, open, new HashSet<IntPtr> { 1, 2 }));
    }

    [Fact]
    public void StoreApps_ShareOneHostProgram_SoOnlyTheTitleCounts()
    {
        const string frameHost = @"C:\Windows\System32\ApplicationFrameHost.exe";
        var waiting = new List<Waiting> { new(A, frameHost, "ApplicationFrameWindow", "Calculator") };

        Assert.Empty(HostMatcher.Match(waiting, [new(1, frameHost, "ApplicationFrameWindow", "Settings")], None));
        Assert.Single(HostMatcher.Match(waiting, [new(2, frameHost, "ApplicationFrameWindow", "Calculator")], None));
    }
}
