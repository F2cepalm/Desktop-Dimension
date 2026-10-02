using System.Runtime.ExceptionServices;
using InfiniteSpace.Rendering;

namespace InfiniteSpace.Tests;

/// <summary>Runs test code on a thread of its own, as WPF and Direct2D want it, with a GPU device when asked.</summary>
internal static class Gpu
{
    /// <summary>Runs on an STA thread with a GPU device of its own (disposed afterwards).</summary>
    public static void Run(Action<GpuCompositor> action) =>
        RunOnSta(() =>
        {
            using var gpu = new GpuCompositor();
            action(gpu);
        });

    public static void RunOnSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
            ExceptionDispatchInfo.Capture(failure).Throw();
    }
}
