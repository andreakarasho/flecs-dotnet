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
}
