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
        w.Observer<Position>(Event.OnAdd, (World W, EntityId e, ref Position _) => seen.Add(e.Id));
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
        w.Observer<Position>(Event.OnSet, (World W, EntityId e, ref Position p) => values.Add(p.X));
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
        w.Observer<Position>(Event.OnAdd, (World W, EntityId e, ref Position _) => a++);
        w.Observer<Position>(Event.OnAdd, (World W, EntityId e, ref Position _) => b++);
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
        w.Observer<TagA>(Event.OnAdd, (W, e) => hits++);
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
        w.Observer<TagA>(Event.OnRemove, (W, e) => hits++);
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
        w.Observer<Likes, Apple>(Event.OnAdd, (W, e) => hits++);
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
        w.Observer<TagA>(Event.OnRemove, (W, e) => hits++);
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
        w.Observer<Position, Velocity>(Event.OnAdd, (World W, EntityId e, ref Position p, ref Velocity v) => hits++);
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
        w.Observer<Position, Velocity>(Event.OnSet, (World W, EntityId e, ref Position p, ref Velocity v) =>
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
        w.Observer<Position, Velocity>(Event.OnAdd, (World W, EntityId e, ref Position _, ref Velocity _) => hits++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        Assert.Equal(0, hits);
    }

    [Fact]
    public void MultiObserver_FiresFromEitherTermAdd()
    {
        var w = new World();
        int hits = 0;
        w.Observer<Position, Velocity>(Event.OnAdd, (World W, EntityId e, ref Position _, ref Velocity _) => hits++);
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
        w.Observer<Position, Velocity>(Event.OnSet, (World W, EntityId e, ref Position _, ref Velocity _) => hits++);
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
        w.Observer<Position, Velocity>(Event.OnRemove, (World W, EntityId e, ref Position _, ref Velocity _) => hits++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Velocity(0, 0));
        // Remove fires BEFORE the column is gone — refs still resolvable.
        w.Remove<Velocity>(e);
        Assert.Equal(1, hits);
    }

    [Fact]
    public void MultiObserver_RespectsIsAInheritance()
    {
        var w = new World();
        int hits = 0;
        w.Observer<Position, Velocity>(Event.OnAdd, (World W, EntityId e, ref Position _, ref Velocity _) => hits++);
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(7, 7));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        // Adding Velocity to inst — Position satisfied via IsA → fires.
        w.Set(inst, new Velocity(1, 1));
        Assert.Equal(1, hits);
    }
}
