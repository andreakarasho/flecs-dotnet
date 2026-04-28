using Xunit;
using System;
using System.Diagnostics;

namespace Flecs.Tests;

public class EdgeCacheTests
{
    [Fact]
    public void AddRemove_ThousandsOfTransitionsCorrect()
    {
        var w = new World();
        var entities = new EntityId[100];
        for (int i = 0; i < entities.Length; i++)
            entities[i] = w.CreateEntity();
        // Repeatedly add+remove same component on each — edges hit hot.
        for (int iter = 0; iter < 50; iter++)
        {
            foreach (var e in entities)
            {
                w.Set(e, new Position(iter, iter));
                Assert.True(w.Has<Position>(e));
            }
            foreach (var e in entities)
            {
                w.Remove<Position>(e);
                Assert.False(w.Has<Position>(e));
            }
        }
    }

    [Fact]
    public void EdgeCache_SymmetricAddRemove_RoundTripsToSameTable()
    {
        var w = new World();
        var a = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        int tablesBefore = w.TableCount;
        // Round trip: remove + add — should not create new tables on the
        // remove path (target is root, already exists) nor on the add (cached
        // edge to original Position table).
        w.Remove<Position>(a);
        w.Set(a, new Position(1, 1));
        Assert.Equal(tablesBefore, w.TableCount);
        Assert.True(w.Has<Position>(a));
    }

    [Fact]
    public void EdgeCache_ManyEntitiesSameTransitionShareEdge()
    {
        var w = new World();
        // First call from root → Position table creates edge AND target table.
        var seed = w.CreateEntity();
        w.Set(seed, new Position(0, 0));
        int tablesAfterSeed = w.TableCount;
        // Subsequent Set on fresh entities follow the cached edge —
        // no new tables.
        for (int i = 0; i < 50; i++)
        {
            var e = w.CreateEntity();
            w.Set(e, new Position(i, i));
        }
        Assert.Equal(tablesAfterSeed, w.TableCount);
    }

    [Fact]
    public void EdgeCache_DoesNotAffectMutationCorrectness()
    {
        var w = new World();
        var e = w.CreateEntity();
        // Multi-transition correctness — values must persist through migrations.
        w.Set(e, new Position(1, 2));
        w.Set(e, new Velocity(3, 4));   // archetype migration
        w.Set(e, new Health(5));        // another migration
        w.Remove<Velocity>(e);                   // back-edge migration
        Assert.Equal(1, w.Get<Position>(e).X);
        Assert.Equal(5, w.Get<Health>(e).Value);
        Assert.False(w.Has<Velocity>(e));
    }

    [Fact]
    public void AddRemove_HotPath_PerformsReasonably()
    {
        // Smoke perf check — not strict timing, just confirms hot loop runs
        // without pathological slowdown. Edge cache should make this fast.
        var w = new World();
        var ents = new EntityId[1000];
        for (int i = 0; i < ents.Length; i++) ents[i] = w.CreateEntity();
        var sw = Stopwatch.StartNew();
        for (int iter = 0; iter < 100; iter++)
        {
            for (int i = 0; i < ents.Length; i++) w.Set(ents[i], new Position(i, i));
            for (int i = 0; i < ents.Length; i++) w.Remove<Position>(ents[i]);
        }
        sw.Stop();
        // 100 iters × 1000 entities × 2 transitions = 200k ops. Should well
        // under a second on any modern machine.
        Assert.True(sw.ElapsedMilliseconds < 5000,
            $"Hot transitions too slow: {sw.ElapsedMilliseconds}ms");
    }
}
