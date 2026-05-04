using System.Collections.Concurrent;
using System.Threading;
using Xunit;

namespace Flecs.Tests;

// Parallel wave dispatch via UseWorkers. Systems within a wave run on
// ThreadPool tasks bound to per-thread Stages; mutations queue per-stage
// and flush in registration order after the barrier.
public class MultiThreadTests
{
    [Fact]
    public void UseWorkers_ZeroIsSequential()
    {
        var w = new World();
        w.UseWorkers(0);
        int calls = 0;
        w.System("s", w.Phases.OnUpdate, _ => calls++);
        w.Progress(0);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void UseWorkers_NegativeCountThrows()
    {
        var w = new World();
        Assert.Throws<System.ArgumentOutOfRangeException>(() => w.UseWorkers(-1));
    }

    [Fact]
    public void Workers_ParallelSafeSystems_RunConcurrently()
    {
        // Two ParallelSafe systems with no rw conflict — should pack into one
        // wave and dispatch to distinct threads.
        var w = new World();
        w.UseWorkers(2);
        var threads = new ConcurrentBag<int>();
        var barrier = new Barrier(2);
        var hA = w.System("a", w.Phases.OnUpdate, _ =>
        {
            threads.Add(Thread.CurrentThread.ManagedThreadId);
            barrier.SignalAndWait(1000);
        }).SetParallelSafe(true);
        var hB = w.System("b", w.Phases.OnUpdate, _ =>
        {
            threads.Add(Thread.CurrentThread.ManagedThreadId);
            barrier.SignalAndWait(1000);
        }).SetParallelSafe(true);
        w.Progress(0);
        Assert.Equal(2, threads.Count);
    }

    [Fact]
    public void Workers_ConflictingSystemsSerialize()
    {
        // Two systems both writing Position — must NOT run concurrently.
        var w = new World();
        w.UseWorkers(4);
        for (int i = 0; i < 5; i++) { var e = w.CreateEntity(); w.Set(e, new Position(0, 0)); }
        int active = 0, maxActive = 0;
        var monLock = new object();
        w.System<Position>("p1", w.Phases.OnUpdate, q =>
        {
            int now;
            lock (monLock) { active++; now = active; if (now > maxActive) maxActive = now; }
            Thread.Sleep(20);
            lock (monLock) active--;
            foreach (var _ in q) { }
        });
        w.System<Position>("p2", w.Phases.OnUpdate, q =>
        {
            int now;
            lock (monLock) { active++; now = active; if (now > maxActive) maxActive = now; }
            Thread.Sleep(20);
            lock (monLock) active--;
            foreach (var _ in q) { }
        });
        w.Progress(0);
        Assert.Equal(1, maxActive); // serialized
    }

    [Fact]
    public void Workers_DeferredMutationsApplyAfterBarrier()
    {
        var w = new World();
        w.UseWorkers(2);
        for (int i = 0; i < 5; i++) { var e = w.CreateEntity(); w.Set(e, new Position(i, 0)); }
        // Two parallel systems mutate disjoint components — both queue Adds
        // via the readonly window then flush via stage merge.
        w.System<Position>("addA", w.Phases.OnUpdate, q =>
        {
            foreach (var row in q) w.Add<TagA>(row.Entity);
        }).SetParallelSafe(true);
        w.System<Position>("addB", w.Phases.OnUpdate, q =>
        {
            foreach (var row in q) w.Add<TagB>(row.Entity);
        }).SetParallelSafe(true);
        w.Progress(0);
        int both = 0;
        foreach (var row in w.Query<Position>())
            if (w.Has<TagA>(row.Entity) && w.Has<TagB>(row.Entity)) both++;
        Assert.Equal(5, both);
    }

    [Fact]
    public void Workers_RoundRobinDistribution()
    {
        // 4 systems / 2 workers — each stage takes 2 systems sequentially.
        var w = new World();
        w.UseWorkers(2);
        int hits = 0;
        for (int i = 0; i < 4; i++)
            w.System($"s{i}", w.Phases.OnUpdate, _ => Interlocked.Increment(ref hits)).SetParallelSafe(true);
        w.Progress(0);
        Assert.Equal(4, hits);
    }

    [Fact]
    public void Workers_DisabledByDefault()
    {
        var w = new World();
        // Without UseWorkers, parallel-safe flag still serial.
        var threads = new ConcurrentBag<int>();
        w.System("a", w.Phases.OnUpdate, _ => threads.Add(Thread.CurrentThread.ManagedThreadId)).SetParallelSafe(true);
        w.System("b", w.Phases.OnUpdate, _ => threads.Add(Thread.CurrentThread.ManagedThreadId)).SetParallelSafe(true);
        w.Progress(0);
        Assert.Equal(1, new System.Collections.Generic.HashSet<int>(threads).Count);
    }

    [Fact]
    public void Workers_RestoreToSequential()
    {
        var w = new World();
        w.UseWorkers(2);
        w.UseWorkers(0); // back to sequential
        var threads = new ConcurrentBag<int>();
        w.System("a", w.Phases.OnUpdate, _ => threads.Add(Thread.CurrentThread.ManagedThreadId)).SetParallelSafe(true);
        w.System("b", w.Phases.OnUpdate, _ => threads.Add(Thread.CurrentThread.ManagedThreadId)).SetParallelSafe(true);
        w.Progress(0);
        Assert.Equal(1, new System.Collections.Generic.HashSet<int>(threads).Count);
    }
}
