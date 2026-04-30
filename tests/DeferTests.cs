using Xunit;
using System;

namespace Flecs.Tests;

public class DeferTests
{
    [Fact]
    public void Defer_QueuesAddUntilEnd()
    {
        var w = new World();
        var e = w.CreateEntity();
        using (w.Defer())
        {
            w.Add<TagA>(e);
            Assert.False(w.Has<TagA>(e)); // queued, not visible yet
        }
        Assert.True(w.Has<TagA>(e));
    }

    [Fact]
    public void Defer_QueuesSetUntilEnd()
    {
        var w = new World();
        var e = w.CreateEntity();
        using (w.Defer())
        {
            w.Set(e, new Position(7, 8));
            Assert.False(w.Has<Position>(e));
        }
        Assert.Equal(7, w.Get<Position>(e).X);
    }

    [Fact]
    public void Defer_QueuesRemove()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Add<TagA>(e);
        using (w.Defer())
        {
            w.Remove<TagA>(e);
            Assert.True(w.Has<TagA>(e));
        }
        Assert.False(w.Has<TagA>(e));
    }

    [Fact]
    public void Defer_QueuesDelete()
    {
        var w = new World();
        var e = w.CreateEntity();
        using (w.Defer())
        {
            w.Delete(e);
            Assert.True(w.IsAlive(e));
        }
        Assert.False(w.IsAlive(e));
    }

    [Fact]
    public void Defer_NestedScopesFlushOnOutermostExit()
    {
        var w = new World();
        var e = w.CreateEntity();
        using (w.Defer())
        {
            w.Add<TagA>(e);
            using (w.Defer())
            {
                w.Add<TagB>(e);
                Assert.False(w.Has<TagB>(e));
            }
            // Inner scope exit does NOT flush.
            Assert.False(w.Has<TagA>(e));
            Assert.False(w.Has<TagB>(e));
        }
        Assert.True(w.Has<TagA>(e));
        Assert.True(w.Has<TagB>(e));
    }

    [Fact]
    public void Defer_MultipleSetsCollapsedToFinalValue()
    {
        var w = new World();
        var e = w.CreateEntity();
        using (w.Defer())
        {
            w.Set(e, new Position(1, 1));
            w.Set(e, new Position(2, 2));
            w.Set(e, new Position(3, 3));
        }
        Assert.Equal(3, w.Get<Position>(e).X);
    }

    [Fact]
    public void Defer_AddInsideQuerySafe()
    {
        var w = new World();
        for (int i = 0; i < 3; i++)
        {
            var e = w.CreateEntity();
            w.Set(e, new Position(i, i));
        }
        // Iter wraps body in readonly — mutating Adds queue safely.
        foreach (var row in w.Query<Position>()) w.Add<TagA>(row.Entity);
        int tagged = 0;
        foreach (var row in w.Query<Position>())
        {
            if (w.Has<TagA>(row.Entity)) tagged++;
        }
        Assert.Equal(3, tagged);
    }

    [Fact]
    public void EndDefer_WithoutBegin_Throws()
    {
        var w = new World();
        Assert.Throws<InvalidOperationException>(() => w.EndDefer());
    }

    [Fact]
    public void IsDeferred_ReflectsState()
    {
        var w = new World();
        Assert.False(w.IsDeferred);
        using (w.Defer())
        {
            Assert.True(w.IsDeferred);
        }
        Assert.False(w.IsDeferred);
    }
}
