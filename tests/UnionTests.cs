using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

// Union trait — relation enforces single (rel, *) per entity, target stored
// in side-table, no archetype migration on switch. Mirrors flecs Union.
public class UnionTests
{
    public struct Movement { }
    public struct Walking { }
    public struct Running { }
    public struct Idle { }

    [Fact]
    public void MarkUnion_FlagsTrait()
    {
        var w = new World();
        Assert.False(w.IsUnion<Movement>());
        w.MarkUnion<Movement>();
        Assert.True(w.IsUnion<Movement>());
    }

    [Fact]
    public void Add_NoArchetypeMigration()
    {
        var w = new World();
        w.MarkUnion<Movement>();
        var e = w.CreateEntity();
        int tablesBefore = w.TableCount;
        w.Add<Movement, Walking>(e);
        Assert.Equal(tablesBefore, w.TableCount);
        w.Add<Movement, Running>(e);
        Assert.Equal(tablesBefore, w.TableCount);
    }

    [Fact]
    public void Add_OverwritesPreviousTarget()
    {
        var w = new World();
        w.MarkUnion<Movement>();
        var e = w.CreateEntity();
        w.Add<Movement, Walking>(e);
        Assert.True(w.Has<Movement, Walking>(e));
        Assert.False(w.Has<Movement, Running>(e));
        w.Add<Movement, Running>(e);
        Assert.False(w.Has<Movement, Walking>(e));
        Assert.True(w.Has<Movement, Running>(e));
    }

    [Fact]
    public void GetUnionTarget_ReturnsCurrent()
    {
        var w = new World();
        w.MarkUnion<Movement>();
        var e = w.CreateEntity();
        Assert.Equal(default, w.GetUnionTarget<Movement>(e));
        w.Add<Movement, Walking>(e);
        var walking = w.IdOf<Walking>();
        Assert.Equal(walking.Component, w.GetUnionTarget<Movement>(e).Id);
        w.Add<Movement, Running>(e);
        var running = w.IdOf<Running>();
        Assert.Equal(running.Component, w.GetUnionTarget<Movement>(e).Id);
    }

    [Fact]
    public void Remove_SpecificTarget_OnlyDropsIfMatches()
    {
        var w = new World();
        w.MarkUnion<Movement>();
        var e = w.CreateEntity();
        w.Add<Movement, Walking>(e);
        // Remove with non-matching target — no-op.
        w.Remove<Movement, Running>(e);
        Assert.True(w.Has<Movement, Walking>(e));
        // Remove with matching target — drops.
        w.Remove<Movement, Walking>(e);
        Assert.False(w.Has<Movement, Walking>(e));
        Assert.Equal(default, w.GetUnionTarget<Movement>(e));
    }

    [Fact]
    public void RemoveUnion_ClearsRegardlessOfTarget()
    {
        var w = new World();
        w.MarkUnion<Movement>();
        var e = w.CreateEntity();
        w.Add<Movement, Running>(e);
        w.RemoveUnion<Movement>(e);
        Assert.False(w.Has<Movement, Running>(e));
        Assert.Equal(default, w.GetUnionTarget<Movement>(e));
    }

    [Fact]
    public void Delete_CleansUnionEntry()
    {
        var w = new World();
        w.MarkUnion<Movement>();
        var a = w.CreateEntity(); w.Add<Movement, Walking>(a);
        var b = w.CreateEntity(); w.Add<Movement, Running>(b);
        w.Delete(a);
        Assert.False(w.IsAlive(a));
        Assert.True(w.Has<Movement, Running>(b));
    }

    [Fact]
    public void Hooks_FireOnSwitch()
    {
        var w = new World();
        w.MarkUnion<Movement>();
        // Tag pair hooks via Observer<TR, TT>.
        int addsRunning = 0, removesWalking = 0;
        w.Observer<Movement, Walking>(Event.OnRemove, _ => removesWalking++);
        w.Observer<Movement, Running>(Event.OnAdd, _ => addsRunning++);
        var e = w.CreateEntity();
        w.Add<Movement, Walking>(e);
        Assert.Equal(0, addsRunning);
        w.Add<Movement, Running>(e);
        Assert.Equal(1, removesWalking);
        Assert.Equal(1, addsRunning);
    }

    [Fact]
    public void Hooks_NoFireOnSameTargetReadd()
    {
        var w = new World();
        w.MarkUnion<Movement>();
        int adds = 0;
        w.Observer<Movement, Walking>(Event.OnAdd, _ => adds++);
        var e = w.CreateEntity();
        w.Add<Movement, Walking>(e);
        w.Add<Movement, Walking>(e);
        w.Add<Movement, Walking>(e);
        Assert.Equal(1, adds);
    }

    [Fact]
    public void Query_With_MatchesByTarget()
    {
        var w = new World();
        w.MarkUnion<Movement>();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0)); w.Add<Movement, Walking>(a);
        var b = w.CreateEntity(); w.Set(b, new Position(2, 0)); w.Add<Movement, Running>(b);
        var c = w.CreateEntity(); w.Set(c, new Position(3, 0)); // no Movement
        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position>().With(w.Pair<Movement, Walking>()))
            seen.Add(row.Entity.Id);
        Assert.Single(seen);
        Assert.Contains(a.Id, seen);
    }

    [Fact]
    public void Query_With_PicksUpAfterTargetSwitch()
    {
        var w = new World();
        w.MarkUnion<Movement>();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0)); w.Add<Movement, Walking>(a);
        var qWalk = w.Query<Position>().With(w.Pair<Movement, Walking>());
        var qRun = w.Query<Position>().With(w.Pair<Movement, Running>());
        int walkers = 0, runners = 0;
        foreach (var _ in qWalk) walkers++;
        foreach (var _ in qRun) runners++;
        Assert.Equal(1, walkers);
        Assert.Equal(0, runners);
        // Switch.
        w.Add<Movement, Running>(a);
        walkers = 0; runners = 0;
        foreach (var _ in qWalk) walkers++;
        foreach (var _ in qRun) runners++;
        Assert.Equal(0, walkers);
        Assert.Equal(1, runners);
    }
}
