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

    // ===== Ordering / op interaction =====

    [Fact]
    public void Defer_AddThenRemoveSameFrame_NetEffectIsAbsent()
    {
        var w = new World();
        var e = w.CreateEntity();
        using (w.Defer())
        {
            w.Add<TagA>(e);
            w.Remove<TagA>(e);
        }
        Assert.False(w.Has<TagA>(e));
    }

    [Fact]
    public void Defer_RemoveThenAddSameFrame_NetEffectIsPresent()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Add<TagA>(e);
        using (w.Defer())
        {
            w.Remove<TagA>(e);
            w.Add<TagA>(e);
        }
        Assert.True(w.Has<TagA>(e));
    }

    [Fact]
    public void Defer_AddSameTagTwice_Idempotent()
    {
        var w = new World();
        var e = w.CreateEntity();
        using (w.Defer())
        {
            w.Add<TagA>(e);
            w.Add<TagA>(e);
        }
        Assert.True(w.Has<TagA>(e));
    }

    [Fact]
    public void Defer_SetThenAddOtherTag_BothApplied()
    {
        var w = new World();
        var e = w.CreateEntity();
        using (w.Defer())
        {
            w.Set(e, new Position(5, 6));
            w.Add<TagA>(e);
        }
        Assert.True(w.Has<TagA>(e));
        Assert.Equal(5, w.Get<Position>(e).X);
    }

    [Fact]
    public void Defer_DeleteThenAdd_DeleteWins()
    {
        var w = new World();
        var e = w.CreateEntity();
        using (w.Defer())
        {
            w.Delete(e);
            w.Add<TagA>(e); // entity already queued for delete
        }
        Assert.False(w.IsAlive(e));
    }

    [Fact]
    public void Defer_ObserverFiresOnFlush_NotInsideDefer()
    {
        var w = new World();
        w.Tag<TagA>();
        int onAdd = 0;
        w.Observer<TagA>(Event.OnAdd, _ => onAdd++);
        var e = w.CreateEntity();
        using (w.Defer())
        {
            w.Add<TagA>(e);
            Assert.Equal(0, onAdd); // not visible yet
        }
        Assert.Equal(1, onAdd);
    }

    [Fact]
    public void Defer_ObserverFiresOncePerEntity_EvenIfAddRepeated()
    {
        var w = new World();
        w.Tag<TagA>();
        int onAdd = 0;
        w.Observer<TagA>(Event.OnAdd, _ => onAdd++);
        var e = w.CreateEntity();
        using (w.Defer())
        {
            w.Add<TagA>(e);
            w.Add<TagA>(e);
        }
        Assert.Equal(1, onAdd);
    }

    [Fact]
    public void Defer_SetCollapsesToFinalValue_ObserverSeesFinal()
    {
        var w = new World();
        int seen = -1;
        w.Observer<Position>(Event.OnSet, (EventIter _, ref Position c) => seen = (int)c.X);
        var e = w.CreateEntity();
        using (w.Defer())
        {
            w.Set(e, new Position(1, 0));
            w.Set(e, new Position(2, 0));
            w.Set(e, new Position(99, 0));
        }
        Assert.Equal(99, seen);
    }

    [Fact]
    public void Defer_NestedScopes_SecondLevelDoesNotFlush()
    {
        var w = new World();
        var e = w.CreateEntity();
        using (w.Defer())
        {
            using (w.Defer())
            {
                w.Add<TagA>(e);
            }
            // After inner dispose, still under outer defer.
            Assert.False(w.Has<TagA>(e));
        }
        Assert.True(w.Has<TagA>(e));
    }
}
