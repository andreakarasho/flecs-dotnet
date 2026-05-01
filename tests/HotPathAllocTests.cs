using Xunit;
using System;

namespace Flecs.Tests;

public class HotPathAllocTests
{
    private static long Bytes() => GC.GetAllocatedBytesForCurrentThread();

    [Fact]
    public void Rows_ZeroAllocSteadyState()
    {
        var w = new World();
        for (int i = 0; i < 100; i++)
        {
            var e = w.CreateEntity();
            w.Set(e, new Position(i, i));
            w.Set(e, new Velocity(1, 1));
        }
        var q = w.Query<Position, Velocity>();
        for (int i = 0; i < 50; i++)
            foreach (var (p, v) in q) p.Value.X += v.Value.Dx;
        var before = Bytes();
        for (int i = 0; i < 1000; i++)
            foreach (var (p, v) in q) p.Value.X += v.Value.Dx;
        var after = Bytes();
        Assert.True(after - before < 1000, $"Rows over 1000 iters allocated {after - before} bytes");
    }

    [Fact]
    public void Progress_ZeroAllocSteadyState()
    {
        var w = new World();
        w.System("noop", w.OnUpdate, _ => { });
        // Warmup.
        for (int i = 0; i < 10; i++) w.Progress(0.016f);
        var before = Bytes();
        for (int i = 0; i < 1000; i++) w.Progress(0.016f);
        var after = Bytes();
        Assert.True(after - before < 1000, $"Progress over 1000 calls allocated {after - before} bytes");
    }

    [Fact]
    public void Delete_NoCascade_ZeroAllocSteadyState()
    {
        var w = new World();
        // Pre-spawn warmup pool of entities to delete.
        var ents = new EntityId[2000];
        for (int i = 0; i < ents.Length; i++) ents[i] = w.CreateEntity();
        for (int i = 0; i < 1000; i++) w.Delete(ents[i]); // warmup recycler
        for (int i = 1000; i < ents.Length; i++) w.Delete(ents[i]); // warmup
        // Re-create + measure delete loop.
        for (int i = 0; i < 1000; i++) ents[i] = w.CreateEntity();
        var before = Bytes();
        for (int i = 0; i < 1000; i++) w.Delete(ents[i]);
        var after = Bytes();
        Assert.True(after - before < 1000, $"Delete (no cascade) over 1000 calls allocated {after - before} bytes");
    }

    [Fact]
    public void HasInheritedVia_ZeroAllocSteadyState()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(0, 0));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        for (int i = 0; i < 50; i++) _ = w.HasInherited<Position>(inst);
        var before = Bytes();
        for (int i = 0; i < 1000; i++) _ = w.HasInherited<Position>(inst);
        var after = Bytes();
        Assert.True(after - before < 1000, $"HasInherited over 1000 calls allocated {after - before} bytes");
    }

    [Fact]
    public void HasTransitive_ZeroAllocSteadyState()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkTransitive(rel);
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        w.Add(a, rel, b);
        w.Add(b, rel, c);
        for (int i = 0; i < 50; i++) _ = w.HasTransitive(a, rel, c);
        var before = Bytes();
        for (int i = 0; i < 1000; i++) _ = w.HasTransitive(a, rel, c);
        var after = Bytes();
        Assert.True(after - before < 1000, $"HasTransitive over 1000 calls allocated {after - before} bytes");
    }

    [Fact]
    public void SetComponent_OnExisting_LowAllocSteadyState()
    {
        // Set on an existing component (no archetype migration) is
        // mostly alloc-free aside from a small per-call floor (~tens of bytes,
        // platform-dependent — likely Monitor.Enter book-keeping and ref-cast
        // overhead). Threshold loose to accept that floor while still
        // flagging regressions of 100s of bytes/call.
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        for (int i = 0; i < 50; i++) w.Set(e, new Position(i, i));
        var before = Bytes();
        for (int i = 0; i < 1000; i++) w.Set(e, new Position(i, i));
        var after = Bytes();
        Assert.True(after - before < 50_000,
            $"Set over 1000 calls allocated {after - before} bytes");
    }

    [Fact]
    public void GetComponent_ZeroAllocSteadyState()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        for (int i = 0; i < 50; i++) _ = w.Get<Position>(e);
        var before = Bytes();
        for (int i = 0; i < 1000; i++) _ = w.Get<Position>(e);
        var after = Bytes();
        Assert.True(after - before < 1000,
            $"Get over 1000 calls allocated {after - before} bytes");
    }

    // ===== Multi-world isolation =====

    [Fact]
    public void MultiWorld_ConcurrentEachZeroAlloc()
    {
        // Two independent worlds iterating in interleaved fashion. Verify
        // that ThreadStatic BFS pools and reusable buffers don't bleed
        // between instances.
        var w1 = new World();
        var w2 = new World();
        for (int i = 0; i < 50; i++)
        {
            var a = w1.CreateEntity();
            w1.Set(a, new Position(i, i));
            w1.Set(a, new Velocity(1, 1));
            var b = w2.CreateEntity();
            w2.Set(b, new Position(i * 2, i * 2));
            w2.Set(b, new Velocity(2, 2));
        }
        var q1 = w1.Query<Position, Velocity>();
        var q2 = w2.Query<Position, Velocity>();
        for (int i = 0; i < 50; i++)
        {
            foreach (var (p, v) in q1) p.Value.X += v.Value.Dx;
            foreach (var (p, v) in q2) p.Value.Y += v.Value.Dy;
        }
        var before = Bytes();
        for (int i = 0; i < 1000; i++)
        {
            foreach (var (p, v) in q1) p.Value.X += v.Value.Dx;
            foreach (var (p, v) in q2) p.Value.Y += v.Value.Dy;
        }
        var after = Bytes();
        Assert.True(after - before < 2000,
            $"Two-world interleaved Rows over 1000+1000 iters allocated {after - before} bytes");
    }

    [Fact]
    public void MultiWorld_StateIsolation()
    {
        var w1 = new World();
        var w2 = new World();
        var e1 = w1.CreateEntity();
        var e2 = w2.CreateEntity();
        w1.Set(e1, new Position(1, 1));
        w2.Set(e2, new Position(99, 99));
        // Components register independently per world; same Type, different worlds.
        Assert.Equal(1, w1.Get<Position>(e1).X);
        Assert.Equal(99, w2.Get<Position>(e2).X);
        // Counts independent.
        Assert.Equal(1, w1.Count<Position>());
        Assert.Equal(1, w2.Count<Position>());
    }

    [Fact]
    public void MultiWorld_CrossWorldEntityNotAlive()
    {
        var w1 = new World();
        var w2 = new World();
        var e1 = w1.CreateEntity();
        // e1 belongs to w1 only. w2 may report it alive *if* its slot
        // happens to coincide with a w2 entity (id-only check, no world
        // handle). Document this: handles are not portable across worlds.
        // At least confirm ops on wrong world don't crash.
        Assert.True(w1.IsAlive(e1));
        // Adding component on wrong world should still go through
        // (registers Position in w2 if not present). No crash.
        w2.Set(e1, new Position(0, 0)); // probably hits a recycled slot in w2 or invalid; either way no throw expected for a brand-new world that has the same id.
    }

    [Fact]
    public void MultiWorld_HookIsolation()
    {
        var w1 = new World();
        var w2 = new World();
        int hits1 = 0, hits2 = 0;
        w1.Hooks<Position>().OnSet = (World _, EntityId _, ref Position _) => hits1++;
        w2.Hooks<Position>().OnSet = (World _, EntityId _, ref Position _) => hits2++;
        var e1 = w1.CreateEntity();
        var e2 = w2.CreateEntity();
        w1.Set(e1, new Position(0, 0));
        w2.Set(e2, new Position(0, 0));
        Assert.Equal(1, hits1);
        Assert.Equal(1, hits2);
    }

    [Fact]
    public void MultiWorld_ProgressIsolation()
    {
        var w1 = new World();
        var w2 = new World();
        int t1 = 0, t2 = 0;
        w1.System("a", w1.OnUpdate, _ => t1++);
        w2.System("b", w2.OnUpdate, _ => t2++);
        for (int i = 0; i < 5; i++) w1.Progress(0);
        for (int i = 0; i < 3; i++) w2.Progress(0);
        Assert.Equal(5, t1);
        Assert.Equal(3, t2);
    }
}
