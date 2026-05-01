using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class YieldExistingTests
{
    [Fact]
    public void YieldExisting_TypedOnAdd_FiresForEachHolder()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        w.Set(a, new Position(1, 1));
        w.Set(b, new Position(2, 2));
        // c has no Position
        var seen = new List<uint>();
        w.Observer<Position>(Event.OnAdd,
            (EventIter it, ref Position _) => seen.Add(it.Entity.Id),
            yieldExisting: true);
        Assert.Equal(2, seen.Count);
        Assert.Contains(a.Id, seen);
        Assert.Contains(b.Id, seen);
        Assert.DoesNotContain(c.Id, seen);
    }

    [Fact]
    public void YieldExisting_TypedSeesCurrentValue()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(7, 8));
        float x = 0;
        w.Observer<Position>(Event.OnAdd,
            (EventIter it, ref Position p) => x = p.X,
            yieldExisting: true);
        Assert.Equal(7, x);
    }

    [Fact]
    public void YieldExisting_TagOnAdd_FiresForEachHolder()
    {
        var w = new World();
        w.Tag<TagA>();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Add<TagA>(a);
        w.Add<TagA>(b);
        var seen = new HashSet<uint>();
        w.Observer<TagA>(Event.OnAdd, it => seen.Add(it.Entity.Id), yieldExisting: true);
        Assert.Equal(2, seen.Count);
        Assert.Contains(a.Id, seen);
        Assert.Contains(b.Id, seen);
    }

    [Fact]
    public void YieldExisting_PairOnAdd_FiresForEachHolder()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Add<Likes, Apple>(a);
        w.Add<Likes, Apple>(b);
        var seen = new HashSet<uint>();
        w.Observer<Likes, Apple>(Event.OnAdd, it => seen.Add(it.Entity.Id), yieldExisting: true);
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void YieldExisting_FalseDoesNotFire()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        int hits = 0;
        w.Observer<Position>(Event.OnAdd,
            (EventIter it, ref Position _) => hits++);
        Assert.Equal(0, hits);
    }

    [Fact]
    public void YieldExisting_FiresInAdditionToFutureEvents()
    {
        var w = new World();
        var a = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        int hits = 0;
        w.Observer<Position>(Event.OnAdd,
            (EventIter it, ref Position _) => hits++,
            yieldExisting: true);
        Assert.Equal(1, hits); // retroactive
        var b = w.CreateEntity();
        w.Set(b, new Position(1, 1));
        Assert.Equal(2, hits); // plus future
    }

    [Fact]
    public void YieldExisting_OnRemove_NoRetroactiveFire()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        int hits = 0;
        w.Observer<Position>(Event.OnRemove,
            (EventIter it, ref Position _) => hits++,
            yieldExisting: true);
        // Living entity hasn't lost it — no retroactive fire.
        Assert.Equal(0, hits);
        w.Remove<Position>(e);
        Assert.Equal(1, hits);
    }

    [Fact]
    public void YieldExisting_OnSet_FiresForEachHolder()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(1, 0));
        w.Set(b, new Position(2, 0));
        var values = new List<float>();
        w.Observer<Position>(Event.OnSet,
            (EventIter it, ref Position p) => values.Add(p.X),
            yieldExisting: true);
        Assert.Equal(2, values.Count);
        Assert.Contains(1f, values);
        Assert.Contains(2f, values);
    }

    [Fact]
    public void YieldExisting_AcrossMultipleArchetypes()
    {
        var w = new World();
        // a: Position only; b: Position + Velocity (different archetype)
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(b, new Position(0, 0));
        w.Set(b, new Velocity(0, 0));
        int hits = 0;
        w.Observer<Position>(Event.OnAdd,
            (EventIter it, ref Position _) => hits++,
            yieldExisting: true);
        Assert.Equal(2, hits);
    }
}
