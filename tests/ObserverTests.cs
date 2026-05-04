using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class ObserverTests
{
    [Fact]
    public void Observer_Typed_OnAdd_Fires()
    {
        var w = new World();
        var seen = new List<uint>();
        w.Observer<Position>(Event.OnAdd, (EventIter it, ref Position _) => seen.Add(it.Entity.Id));
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(b, new Position(0, 0));
        Assert.Equal(new[] { a.Id, b.Id }, seen);
    }

    [Fact]
    public void Observer_Typed_OnSet_Fires()
    {
        var w = new World();
        var values = new List<float>();
        w.Observer<Position>(Event.OnSet, (EventIter it, ref Position p) => values.Add(p.X));
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 0));
        w.Set(e, new Position(2, 0));
        Assert.Equal(new[] { 1f, 2f }, values);
    }

    [Fact]
    public void Observer_Typed_Multicast()
    {
        var w = new World();
        int a = 0, b = 0;
        w.Observer<Position>(Event.OnAdd, (EventIter it, ref Position _) => a++);
        w.Observer<Position>(Event.OnAdd, (EventIter it, ref Position _) => b++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        Assert.Equal(1, a);
        Assert.Equal(1, b);
    }

    [Fact]
    public void Observer_Tag_OnAdd()
    {
        var w = new World();
        w.Tag<TagA>();
        int hits = 0;
        w.Observer<TagA>(Event.OnAdd, it => hits++);
        var e1 = w.CreateEntity();
        var e2 = w.CreateEntity();
        w.Add<TagA>(e1);
        w.Add<TagA>(e2);
        Assert.Equal(2, hits);
    }

    [Fact]
    public void Observer_Tag_OnRemove()
    {
        var w = new World();
        w.Tag<TagA>();
        int hits = 0;
        w.Observer<TagA>(Event.OnRemove, it => hits++);
        var e = w.CreateEntity();
        w.Add<TagA>(e);
        w.Remove<TagA>(e);
        Assert.Equal(1, hits);
    }

    [Fact]
    public void Observer_Pair_OnAdd()
    {
        var w = new World();
        int hits = 0;
        w.Observer<Likes, Apple>(Event.OnAdd, it => hits++);
        var e = w.CreateEntity();
        w.Add<Likes, Apple>(e);
        Assert.Equal(1, hits);
    }

    [Fact]
    public void Observer_Tag_OnRemove_FiresOnDelete()
    {
        var w = new World();
        w.Tag<TagA>();
        int hits = 0;
        w.Observer<TagA>(Event.OnRemove, it => hits++);
        var e = w.CreateEntity();
        w.Add<TagA>(e);
        w.Delete(e);
        Assert.Equal(1, hits);
    }

    // ===== Multi-term observers =====

    [Fact]
    public void MultiObserver_OnAdd_FiresWhenBothPresent()
    {
        var w = new World();
        int hits = 0;
        // OnAdd fires at component-add time, before Set writes the value —
        // so refs reflect default-init for the just-added term. We only
        // assert on hit count here; OnSet test covers value-aware dispatch.
        w.Observer<Position, Velocity>(Event.OnAdd, (EventIter it, ref Position p, ref Velocity v) => hits++);
        var e = w.CreateEntity();
        w.Set(e, new Position(10, 20));
        Assert.Equal(0, hits);                    // only Position so far
        w.Set(e, new Velocity(3, 4));             // adding Velocity → fires
        Assert.Equal(1, hits);
    }

    [Fact]
    public void MultiObserver_OnSet_SeesActualValues()
    {
        var w = new World();
        int hits = 0;
        float lastX = 0f, lastDx = 0f;
        w.Observer<Position, Velocity>(Event.OnSet, (EventIter it, ref Position p, ref Velocity v) =>
        {
            hits++;
            lastX = p.X;
            lastDx = v.Dx;
        });
        var e = w.CreateEntity();
        w.Set(e, new Position(10, 20));   // Velocity missing → no fire
        w.Set(e, new Velocity(3, 4));     // entity has both; OnSet for Velocity → fires post-write
        Assert.Equal(1, hits);
        Assert.Equal(10, lastX);
        Assert.Equal(3, lastDx);
    }

    [Fact]
    public void MultiObserver_DoesNotFireWhenOnlyOneTermPresent()
    {
        var w = new World();
        int hits = 0;
        w.Observer<Position, Velocity>(Event.OnAdd, (EventIter it, ref Position _, ref Velocity _) => hits++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        Assert.Equal(0, hits);
    }

    [Fact]
    public void MultiObserver_FiresFromEitherTermAdd()
    {
        var w = new World();
        int hits = 0;
        w.Observer<Position, Velocity>(Event.OnAdd, (EventIter it, ref Position _, ref Velocity _) => hits++);
        // Order 1: Position then Velocity → Velocity add fires.
        var e1 = w.CreateEntity();
        w.Set(e1, new Position(0, 0));
        w.Set(e1, new Velocity(0, 0));
        Assert.Equal(1, hits);
        // Order 2: Velocity then Position → Position add fires.
        var e2 = w.CreateEntity();
        w.Set(e2, new Velocity(0, 0));
        w.Set(e2, new Position(0, 0));
        Assert.Equal(2, hits);
    }

    [Fact]
    public void MultiObserver_OnSet_FiresOnEitherUpdate()
    {
        var w = new World();
        int hits = 0;
        w.Observer<Position, Velocity>(Event.OnSet, (EventIter it, ref Position _, ref Velocity _) => hits++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));   // OnSet fires for Position; entity lacks Velocity → no dispatch
        Assert.Equal(0, hits);
        w.Set(e, new Velocity(0, 0));   // entity now has both; Velocity OnSet fires → dispatch
        Assert.Equal(1, hits);
        w.Set(e, new Position(1, 1));   // Position OnSet, both still present → dispatch
        Assert.Equal(2, hits);
    }

    [Fact]
    public void MultiObserver_OnRemove_FiresWhenStillBothPresentAtRemove()
    {
        var w = new World();
        int hits = 0;
        w.Observer<Position, Velocity>(Event.OnRemove, (EventIter it, ref Position _, ref Velocity _) => hits++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Velocity(0, 0));
        // Remove fires BEFORE the column is gone — refs still resolvable.
        w.Remove<Velocity>(e);
        Assert.Equal(1, hits);
    }

    private sealed class ObsCtx { public int Sum; }

    [Fact]
    public void Observer_Ctx_ReadInsideBody()
    {
        var w = new World();
        var state = new ObsCtx();
        w.Observer<Position>(Event.OnSet, (EventIter it, ref Position p) =>
        {
            it.Ctx<ObsCtx>().Sum += (int)p.X;
        }).SetCtx(state);
        var e = w.CreateEntity();
        w.Set(e, new Position(10, 0));
        w.Set(e, new Position(5, 0));
        Assert.Equal(15, state.Sum);
    }

    [Fact]
    public void Observer_SetEnabled_GatesDispatch()
    {
        var w = new World();
        int hits = 0;
        var h = w.Observer<Position>(Event.OnSet, (EventIter it, ref Position _) => hits++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        Assert.Equal(1, hits);
        h.SetEnabled(false);
        w.Set(e, new Position(1, 0));
        Assert.Equal(1, hits);
        h.SetEnabled(true);
        w.Set(e, new Position(2, 0));
        Assert.Equal(2, hits);
    }

    [Fact]
    public void MultiObserver_RespectsIsAInheritance()
    {
        var w = new World();
        int hits = 0;
        w.Observer<Position, Velocity>(Event.OnAdd, (EventIter it, ref Position _, ref Velocity _) => hits++);
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(7, 7));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        // Adding Velocity to inst — Position satisfied via IsA → fires.
        w.Set(inst, new Velocity(1, 1));
        Assert.Equal(1, hits);
    }

    // ===== Lifecycle / disposal =====

    [Fact]
    public void Observer_OnAdd_DoesNotFireOnSubsequentSet()
    {
        var w = new World();
        int onAdd = 0;
        w.Observer<Position>(Event.OnAdd, (EventIter _, ref Position _) => onAdd++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Position(1, 0));
        w.Set(e, new Position(2, 0));
        Assert.Equal(1, onAdd);
    }

    [Fact]
    public void Observer_OnAdd_FiresAgainAfterReadd()
    {
        var w = new World();
        int onAdd = 0;
        w.Observer<Position>(Event.OnAdd, (EventIter _, ref Position _) => onAdd++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Remove<Position>(e);
        w.Set(e, new Position(0, 0));
        Assert.Equal(2, onAdd);
    }

    [Fact]
    public void Observer_DistinctEventsTrackedSeparately()
    {
        var w = new World();
        int onAdd = 0, onSet = 0, onRemove = 0;
        w.Observer<Position>(Event.OnAdd, (EventIter _, ref Position _) => onAdd++);
        w.Observer<Position>(Event.OnSet, (EventIter _, ref Position _) => onSet++);
        w.Observer<Position>(Event.OnRemove, (EventIter _, ref Position _) => onRemove++);
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 1));     // OnAdd + OnSet
        w.Set(e, new Position(2, 2));     // OnSet
        w.Remove<Position>(e);            // OnRemove
        Assert.Equal(1, onAdd);
        Assert.Equal(2, onSet);
        Assert.Equal(1, onRemove);
    }

    // ===== Defer interactions =====

    [Fact]
    public void Observer_DeferredAdd_FiresOnFlush()
    {
        var w = new World();
        int hits = 0;
        w.Observer<Position>(Event.OnAdd, (EventIter _, ref Position _) => hits++);
        var e = w.CreateEntity();
        using (w.Defer())
        {
            w.Set(e, new Position(0, 0));
            Assert.Equal(0, hits);
        }
        Assert.Equal(1, hits);
    }

    [Fact]
    public void Observer_DeferredAddRemove_NetZeroNoFire()
    {
        var w = new World();
        int onAdd = 0, onRemove = 0;
        w.Observer<Position>(Event.OnAdd, (EventIter _, ref Position _) => onAdd++);
        w.Observer<Position>(Event.OnRemove, (EventIter _, ref Position _) => onRemove++);
        var e = w.CreateEntity();
        using (w.Defer())
        {
            w.Set(e, new Position(0, 0));
            w.Remove<Position>(e);
        }
        // Net effect: component is absent. Both ops applied at flush — OnAdd
        // fires when the column appears, OnRemove fires when it goes.
        Assert.Equal(1, onAdd);
        Assert.Equal(1, onRemove);
        Assert.False(w.Has<Position>(e));
    }

    // ===== Specific-pair observer =====

    [Fact]
    public void Observer_SpecificPair_FiresOnlyForThatPair()
    {
        var w = new World();
        int hits = 0;
        w.Observer<Likes, Apple>(Event.OnAdd, _ => hits++);
        var e = w.CreateEntity();
        w.Add<Likes, Orange>(e);   // wrong target → no fire
        Assert.Equal(0, hits);
        w.Add<Likes, Apple>(e);    // matches → fires
        Assert.Equal(1, hits);
    }

    [Fact]
    public void Observer_OnRemove_NotFiredIfNeverHadComponent()
    {
        var w = new World();
        int hits = 0;
        w.Observer<Position>(Event.OnRemove, (EventIter _, ref Position _) => hits++);
        var e = w.CreateEntity();
        w.Remove<Position>(e); // never had → no-op, no fire
        Assert.Equal(0, hits);
    }

    [Fact]
    public void Observer_DoesNotSelfTriggerWhenBodyMutatesUnrelatedEntity()
    {
        // Body adds Position to a different entity inside Defer — must not
        // recurse infinitely.
        var w = new World();
        int hits = 0;
        var second = w.CreateEntity();
        w.Observer<Position>(Event.OnAdd, (EventIter it, ref Position _) =>
        {
            hits++;
            if (it.Entity.Id != second.Id)
            {
                using (w.Defer()) w.Set(second, new Position(0, 0));
            }
        });
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        Assert.Equal(2, hits); // first for e, then for second on flush
    }
}
