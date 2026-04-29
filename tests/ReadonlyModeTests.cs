using System;
using Xunit;

namespace Flecs.Tests;

// World.Readonly — flag set during query iteration; structural mutations
// route through the command queue and flush at scope exit. Distinct from
// explicit Defer (user-controlled queueing).
public class ReadonlyModeTests
{
    [Fact]
    public void IsReadonly_FalseOutsideIter()
    {
        var w = new World();
        Assert.False(w.IsReadonly);
    }

    [Fact]
    public void IsReadonly_TrueDuringEach()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        bool seenReadonly = false;
        w.Query<Position>().Each((EntityId _, ref Position _p) =>
        {
            if (w.IsReadonly) seenReadonly = true;
        });
        Assert.True(seenReadonly);
        Assert.False(w.IsReadonly);
    }

    [Fact]
    public void IsReadonly_TrueDuringRows()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        bool seen = false;
        foreach (var p in w.Query<Position>().Rows())
        {
            if (w.IsReadonly) seen = true;
        }
        Assert.True(seen);
        Assert.False(w.IsReadonly);
    }

    [Fact]
    public void Mutation_DuringEach_QueuesAndFlushesAfter()
    {
        var w = new World();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0));
        var b = w.CreateEntity(); w.Set(b, new Position(2, 0));
        // Adding Velocity inside Each must not invalidate iteration. Effects
        // visible only after scope ends.
        int seen = 0;
        w.Query<Position>().Each((EntityId e, ref Position _p) =>
        {
            seen++;
            w.Set(e, new Velocity(1, 1));
            Assert.False(w.Owns<Velocity>(e)); // not yet applied — queued
        });
        Assert.Equal(2, seen);
        Assert.True(w.Owns<Velocity>(a));
        Assert.True(w.Owns<Velocity>(b));
    }

    [Fact]
    public void NestedDeferInsideReadonly_FlushDeferredUntilOutermostExit()
    {
        var w = new World();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0));
        bool addedSeenInside = true;
        w.Query<Position>().Each((EntityId e, ref Position _p) =>
        {
            using (w.Defer())
            {
                w.Set(e, new Velocity(2, 2));
            } // EndDefer must NOT flush — outer readonly still holds queue.
            // Velocity still queued.
            if (w.Owns<Velocity>(e)) addedSeenInside = true;
            else addedSeenInside = false;
        });
        Assert.False(addedSeenInside);
        Assert.True(w.Owns<Velocity>(a));
    }

    [Fact]
    public void StrictReadonly_ThrowsOnDirectMutate()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        w.StrictReadonly = true;
        Assert.Throws<InvalidOperationException>(() =>
        {
            w.Query<Position>().Each((EntityId ent, ref Position _p) =>
            {
                w.Set(ent, new Velocity(1, 1)); // would normally queue; strict throws
            });
        });
    }

    [Fact]
    public void StrictReadonly_AllowsMutateInsideExplicitDefer()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        w.StrictReadonly = true;
        // Explicit Defer satisfies the queueing intent.
        w.Query<Position>().Each((EntityId ent, ref Position _p) =>
        {
            using (w.Defer())
            {
                w.Set(ent, new Velocity(1, 1));
            }
        });
        Assert.True(w.Owns<Velocity>(e));
    }

    [Fact]
    public void EndReadonly_WithoutBegin_Throws()
    {
        var w = new World();
        Assert.Throws<InvalidOperationException>(() => w.EndReadonly());
    }

    [Fact]
    public void IsDeferred_DistinctFromIsReadonly()
    {
        var w = new World();
        using (w.Defer())
        {
            Assert.True(w.IsDeferred);
            Assert.False(w.IsReadonly);
        }
        using (w.Readonly())
        {
            Assert.False(w.IsDeferred);
            Assert.True(w.IsReadonly);
        }
    }
}
