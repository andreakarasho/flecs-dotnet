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
}
