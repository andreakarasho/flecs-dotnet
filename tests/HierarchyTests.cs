using Xunit;
using System.Collections.Generic;
using System.Linq;

namespace Flecs.Tests;

public class HierarchyTests
{
    [Fact]
    public void SetParent_LinksChildToParent()
    {
        var w = new World();
        var p = w.CreateEntity();
        var c = w.CreateEntity();
        w.SetParent(c, p);
        Assert.True(w.HasParent(c, p));
        Assert.Equal(p.Id, w.GetParent(c).Id);
    }

    [Fact]
    public void GetParent_ReturnsDefaultWhenNone()
    {
        var w = new World();
        var e = w.CreateEntity();
        Assert.False(w.GetParent(e).IsValid);
    }

    [Fact]
    public void Children_EnumeratesDirectChildren()
    {
        var w = new World();
        var p = w.CreateEntity();
        var c1 = w.CreateEntity();
        var c2 = w.CreateEntity();
        var c3 = w.CreateEntity();
        w.SetParent(c1, p);
        w.SetParent(c2, p);
        w.SetParent(c3, p);
        var ids = w.Children(p).Select(e => e.Id).ToHashSet();
        Assert.Contains(c1.Id, ids);
        Assert.Contains(c2.Id, ids);
        Assert.Contains(c3.Id, ids);
        Assert.Equal(3, ids.Count);
    }

    [Fact]
    public void Children_DoesNotIncludeGrandchildren()
    {
        var w = new World();
        var root = w.CreateEntity();
        var mid = w.CreateEntity();
        var leaf = w.CreateEntity();
        w.SetParent(mid, root);
        w.SetParent(leaf, mid);
        var direct = w.Children(root).Select(e => e.Id).ToList();
        Assert.Single(direct);
        Assert.Equal(mid.Id, direct[0]);
    }

    [Fact]
    public void IsAncestor_WalksFullChain()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        var d = w.CreateEntity();
        w.SetParent(b, a);
        w.SetParent(c, b);
        w.SetParent(d, c);
        Assert.True(w.IsAncestor(a, d));
        Assert.True(w.IsAncestor(b, d));
        Assert.True(w.IsAncestor(c, d));
        Assert.False(w.IsAncestor(d, a));
    }

    [Fact]
    public void IsAncestor_FalseForUnrelated()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        Assert.False(w.IsAncestor(a, b));
    }

    [Fact]
    public void ClearParent_RemovesChildOf()
    {
        var w = new World();
        var p = w.CreateEntity();
        var c = w.CreateEntity();
        w.SetParent(c, p);
        w.ClearParent(c);
        Assert.False(w.GetParent(c).IsValid);
    }

    // ===== Reparent / change-parent =====

    [Fact]
    public void SetParent_ChangesParent_OldNoLongerHasChild()
    {
        var w = new World();
        var p1 = w.CreateEntity();
        var p2 = w.CreateEntity();
        var c = w.CreateEntity();
        w.SetParent(c, p1);
        w.SetParent(c, p2);
        Assert.Equal(p2.Id, w.GetParent(c).Id);
        Assert.DoesNotContain(c.Id, w.Children(p1).Select(e => e.Id));
        Assert.Contains(c.Id, w.Children(p2).Select(e => e.Id));
    }

    [Fact]
    public void SetParent_SelfThrowsOrIsRefused()
    {
        // Self-parenting must not produce a cyclic ChildOf — flecs rejects it.
        var w = new World();
        var e = w.CreateEntity();
        Assert.Throws<System.InvalidOperationException>(() => w.SetParent(e, e));
    }

    [Fact]
    public void Children_EmptyWhenNoChildren()
    {
        var w = new World();
        var p = w.CreateEntity();
        Assert.Empty(w.Children(p));
    }

    [Fact]
    public void Children_StaleAfterClearParent()
    {
        var w = new World();
        var p = w.CreateEntity();
        var c = w.CreateEntity();
        w.SetParent(c, p);
        w.ClearParent(c);
        Assert.Empty(w.Children(p));
    }

    [Fact]
    public void IsAncestor_FalseForSelf()
    {
        var w = new World();
        var e = w.CreateEntity();
        Assert.False(w.IsAncestor(e, e));
    }

    [Fact]
    public void HasParent_FalseAfterReparent()
    {
        var w = new World();
        var p1 = w.CreateEntity();
        var p2 = w.CreateEntity();
        var c = w.CreateEntity();
        w.SetParent(c, p1);
        w.SetParent(c, p2);
        Assert.False(w.HasParent(c, p1));
        Assert.True(w.HasParent(c, p2));
    }

    [Fact]
    public void ClearParent_OnRootEntityIsNoop()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.ClearParent(e); // no throw
        Assert.False(w.GetParent(e).IsValid);
    }

    [Fact]
    public void Hierarchy_DeepChain_GetParentReturnsImmediate()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        var d = w.CreateEntity();
        w.SetParent(b, a);
        w.SetParent(c, b);
        w.SetParent(d, c);
        // GetParent returns immediate parent, not root.
        Assert.Equal(c.Id, w.GetParent(d).Id);
        Assert.Equal(b.Id, w.GetParent(c).Id);
        Assert.Equal(a.Id, w.GetParent(b).Id);
    }

    // ===== GetTarget / GetTargets =====

    [Fact]
    public void GetTarget_Exclusive_ReturnsCurrent()
    {
        var w = new World();
        var p = w.CreateEntity();
        var c = w.CreateEntity();
        w.SetParent(c, p);
        Assert.Equal(p.Id, w.GetTarget(c, w.Relations.ChildOf).Id);
    }

    [Fact]
    public void GetTarget_NoSuchRelation_ReturnsDefault()
    {
        var w = new World();
        var e = w.CreateEntity();
        Assert.False(w.GetTarget(e, w.Relations.ChildOf).IsValid);
    }

    [Fact]
    public void GetTargets_NonExclusive_ReturnsAll()
    {
        var w = new World();
        var inst = w.CreateEntity();
        var p1 = w.CreateEntity();
        var p2 = w.CreateEntity();
        var p3 = w.CreateEntity();
        w.SetIsA(inst, p1);
        w.SetIsA(inst, p2);
        w.SetIsA(inst, p3);
        var ids = new System.Collections.Generic.HashSet<uint>();
        foreach (var t in w.GetTargets(inst, w.Relations.IsA)) ids.Add(t.Id);
        Assert.Contains(p1.Id, ids);
        Assert.Contains(p2.Id, ids);
        Assert.Contains(p3.Id, ids);
        Assert.Equal(3, ids.Count);
    }

    [Fact]
    public void GetTargets_NoMatches_Empty()
    {
        var w = new World();
        var e = w.CreateEntity();
        Assert.Empty(w.GetTargets(e, w.Relations.IsA));
    }

    [Fact]
    public void GetTarget_DeadEntity_ReturnsDefault()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Delete(e);
        Assert.False(w.GetTarget(e, w.Relations.ChildOf).IsValid);
    }
}
