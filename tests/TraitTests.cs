using Xunit;
using System;
using System.Linq;

namespace Flecs.Tests;

public class TraitTests
{
    // ---------- Final ----------

    [Fact]
    public void MarkFinal_BlocksIsATarget()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.MarkFinal(prefab);
        var inst = w.CreateEntity();
        Assert.Throws<InvalidOperationException>(() => w.SetIsA(inst, prefab));
    }

    [Fact]
    public void Final_AllowsNonIsAPairs()
    {
        var w = new World();
        var target = w.CreateEntity();
        w.MarkFinal(target);
        var holder = w.CreateEntity();
        // Likes pair — not blocked by Final.
        w.Add(holder, w.Tag<Likes>(), target);
        Assert.True(w.Has(holder, w.Pair(w.Tag<Likes>(), target)));
    }

    [Fact]
    public void IsFinal_TracksMarking()
    {
        var w = new World();
        var e = w.CreateEntity();
        Assert.False(w.IsFinal(e));
        w.MarkFinal(e);
        Assert.True(w.IsFinal(e));
        w.UnmarkFinal(e);
        Assert.False(w.IsFinal(e));
    }

    [Fact]
    public void Unmark_FinalRestoresIsACapability()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.MarkFinal(prefab);
        w.UnmarkFinal(prefab);
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab); // no throw
        Assert.True(w.HasIsA(inst, prefab));
    }

    [Fact]
    public void Final_TaggedEntityHasFinalTag()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.MarkFinal(e);
        Assert.True(w.Has(e, (Id)w.RelationTraits.Final));
    }

    // ---------- Exclusive ----------

    [Fact]
    public void ChildOf_DefaultExclusive()
    {
        var w = new World();
        Assert.True(w.IsExclusive(w.Relations.ChildOf));
    }

    [Fact]
    public void IsA_NotExclusiveByDefault()
    {
        var w = new World();
        Assert.False(w.IsExclusive(w.Relations.IsA));
    }

    [Fact]
    public void ExclusiveChildOf_SetParentReplacesPrior()
    {
        var w = new World();
        var p1 = w.CreateEntity();
        var p2 = w.CreateEntity();
        var c = w.CreateEntity();
        w.SetParent(c, p1);
        w.SetParent(c, p2);
        Assert.False(w.HasParent(c, p1));
        Assert.True(w.HasParent(c, p2));
        Assert.Equal(p2.Id, w.GetParent(c).Id);
    }

    [Fact]
    public void ExclusiveChildOf_ChildrenReflectsReparent()
    {
        var w = new World();
        var p1 = w.CreateEntity();
        var p2 = w.CreateEntity();
        var c = w.CreateEntity();
        w.SetParent(c, p1);
        w.SetParent(c, p2);
        Assert.DoesNotContain(c.Id, w.Children(p1).Select(e => e.Id));
        Assert.Contains(c.Id, w.Children(p2).Select(e => e.Id));
    }

    [Fact]
    public void NonExclusiveIsA_KeepsMultipleTargets()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var d = w.CreateEntity();
        w.SetIsA(d, a);
        w.SetIsA(d, b);
        Assert.True(w.HasIsA(d, a));
        Assert.True(w.HasIsA(d, b));
    }

    [Fact]
    public void MarkExclusive_OnCustomRelation()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkExclusive(rel);
        var holder = w.CreateEntity();
        var t1 = w.CreateEntity();
        var t2 = w.CreateEntity();
        w.Add(holder, rel, t1);
        w.Add(holder, rel, t2);
        Assert.False(w.Has(holder, w.Pair(rel, t1)));
        Assert.True(w.Has(holder, w.Pair(rel, t2)));
    }

    [Fact]
    public void UnmarkExclusive_RestoresMultiTarget()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkExclusive(rel);
        w.UnmarkExclusive(rel);
        var holder = w.CreateEntity();
        var t1 = w.CreateEntity();
        var t2 = w.CreateEntity();
        w.Add(holder, rel, t1);
        w.Add(holder, rel, t2);
        Assert.True(w.Has(holder, w.Pair(rel, t1)));
        Assert.True(w.Has(holder, w.Pair(rel, t2)));
    }

    [Fact]
    public void ExclusiveReplace_FiresOnRemoveForOldPair()
    {
        var w = new World();
        int onRemove = 0;
        var p1 = w.CreateEntity();
        var p2 = w.CreateEntity();
        var c = w.CreateEntity();
        // Subscribe to specific (ChildOf, p1) pair removal — builtin OnRemove.
        w.Observer(Id.MakePair(w.Relations.ChildOf, p1), Event.OnRemove, it => onRemove++);
        w.SetParent(c, p1);
        w.SetParent(c, p2); // exclusive: removes (ChildOf, p1) → fires OnRemove
        Assert.Equal(1, onRemove);
    }

    [Fact]
    public void IsExclusive_TracksMarking()
    {
        var w = new World();
        var rel = w.CreateEntity();
        Assert.False(w.IsExclusive(rel));
        w.MarkExclusive(rel);
        Assert.True(w.IsExclusive(rel));
        w.UnmarkExclusive(rel);
        Assert.False(w.IsExclusive(rel));
    }

    [Fact]
    public void Exclusive_TaggedRelationHasExclusiveTag()
    {
        var w = new World();
        var rel = w.CreateEntity();
        w.MarkExclusive(rel);
        Assert.True(w.Has(rel, (Id)w.RelationTraits.Exclusive));
    }
}
