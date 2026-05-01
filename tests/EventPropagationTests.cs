using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public struct OnDamage { }

public class EventPropagationTests
{
    [Fact]
    public void Propagate_ChildOf_FiresOnSelfThenParent()
    {
        var w = new World();
        var seen = new List<uint>();
        w.Observer<OnDamage, TagA>(it => seen.Add(it.Entity.Id));

        var parent = w.CreateEntity();
        var child = w.CreateEntity();
        w.SetParent(child, parent);
        w.Emit<OnDamage, TagA>(child, w.Relations.ChildOf);

        Assert.Equal(2, seen.Count);
        Assert.Equal(child.Id, seen[0]);
        Assert.Equal(parent.Id, seen[1]);
    }

    [Fact]
    public void Propagate_ChildOf_DeepChain()
    {
        var w = new World();
        var seen = new List<uint>();
        w.Observer<OnDamage, TagA>(it => seen.Add(it.Entity.Id));

        var root = w.CreateEntity();
        var mid = w.CreateEntity();
        var leaf = w.CreateEntity();
        w.SetParent(mid, root);
        w.SetParent(leaf, mid);
        w.Emit<OnDamage, TagA>(leaf, w.Relations.ChildOf);

        Assert.Equal(new[] { leaf.Id, mid.Id, root.Id }, seen);
    }

    [Fact]
    public void Propagate_NoRelation_FiresOnlyOnSelf()
    {
        var w = new World();
        var seen = new List<uint>();
        w.Observer<OnDamage, TagA>(it => seen.Add(it.Entity.Id));

        var parent = w.CreateEntity();
        var child = w.CreateEntity();
        w.SetParent(child, parent);
        w.Emit<OnDamage, TagA>(child);

        Assert.Single(seen);
        Assert.Equal(child.Id, seen[0]);
    }

    [Fact]
    public void Propagate_StopsAtRoot()
    {
        var w = new World();
        int hits = 0;
        w.Observer<OnDamage, TagA>(it => hits++);

        var lone = w.CreateEntity();
        w.Emit<OnDamage, TagA>(lone, w.Relations.ChildOf);
        Assert.Equal(1, hits);
    }

    [Fact]
    public void Propagate_IsA_Bubbles()
    {
        var w = new World();
        var seen = new List<uint>();
        w.Observer<OnDamage, TagA>(it => seen.Add(it.Entity.Id));

        var prefab = w.CreateEntity();
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        w.Emit<OnDamage, TagA>(inst, w.Relations.IsA);

        Assert.Equal(new[] { inst.Id, prefab.Id }, seen);
    }

    [Fact]
    public void Propagate_DiamondVisitsEachOnce()
    {
        var w = new World();
        var seen = new List<uint>();
        w.Observer<OnDamage, TagA>(it => seen.Add(it.Entity.Id));

        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        var d = w.CreateEntity();
        w.SetIsA(b, a);
        w.SetIsA(c, a);
        w.SetIsA(d, b);
        w.SetIsA(d, c);
        w.Emit<OnDamage, TagA>(d, w.Relations.IsA);

        Assert.Equal(4, seen.Count);
        Assert.Equal(seen.Count, new HashSet<uint>(seen).Count);
    }

    [Fact]
    public void Propagate_CycleSafe()
    {
        var w = new World();
        int hits = 0;
        w.Observer<OnDamage, TagA>(it => hits++);

        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.SetIsA(a, b);
        w.SetIsA(b, a);
        w.Emit<OnDamage, TagA>(a, w.Relations.IsA);
        Assert.Equal(2, hits);
    }

    [Fact]
    public void Propagate_NoSubscribers_NoOp()
    {
        var w = new World();
        var p = w.CreateEntity();
        var c = w.CreateEntity();
        w.SetParent(c, p);
        w.Emit<OnDamage, TagA>(c, w.Relations.ChildOf);
    }

    [Fact]
    public void Propagate_PairEventBubbles()
    {
        var w = new World();
        var seen = new List<uint>();
        w.Observer<OnDamage, Likes, Apple>(it => seen.Add(it.Entity.Id));

        var parent = w.CreateEntity();
        var child = w.CreateEntity();
        w.SetParent(child, parent);
        w.Emit<OnDamage, Likes, Apple>(child, w.Relations.ChildOf);

        Assert.Equal(new[] { child.Id, parent.Id }, seen);
    }
}
