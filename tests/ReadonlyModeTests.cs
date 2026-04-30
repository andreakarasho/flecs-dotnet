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
    public void IsReadonly_TrueDuringRows()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        bool seen = false;
        foreach (var _ in w.Query<Position>())
        {
            if (w.IsReadonly) seen = true;
        }
        Assert.True(seen);
        Assert.False(w.IsReadonly);
    }

    [Fact]
    public void Mutation_DuringIter_QueuesAndFlushesAfter()
    {
        var w = new World();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0));
        var b = w.CreateEntity(); w.Set(b, new Position(2, 0));
        int seen = 0;
        foreach (var row in w.Query<Position>())
        {
            seen++;
            var e = row.Entity;
            w.Set(e, new Velocity(1, 1));
            Assert.False(w.Owns<Velocity>(e)); // queued
        }
        Assert.Equal(2, seen);
        Assert.True(w.Owns<Velocity>(a));
        Assert.True(w.Owns<Velocity>(b));
    }

    [Fact]
    public void NestedDeferInsideReadonly_FlushDeferredUntilOutermostExit()
    {
        var w = new World();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0));
        bool sawAddedInside = false;
        foreach (var row in w.Query<Position>())
        {
            var e = row.Entity;
            using (w.Defer())
            {
                w.Set(e, new Velocity(2, 2));
            }
            sawAddedInside = w.Owns<Velocity>(e);
        }
        Assert.False(sawAddedInside);
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
            foreach (var row in w.Query<Position>())
                w.Set(row.Entity, new Velocity(1, 1));
        });
    }

    [Fact]
    public void StrictReadonly_AllowsMutateInsideExplicitDefer()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        w.StrictReadonly = true;
        foreach (var row in w.Query<Position>())
        {
            using (w.Defer())
                w.Set(row.Entity, new Velocity(1, 1));
        }
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
