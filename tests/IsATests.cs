using Xunit;
using System;

namespace Flecs.Tests;

public class IsATests
{
    [Fact]
    public void SetIsA_AddsPairToEntity()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        Assert.True(w.HasIsA(inst, prefab));
    }

    [Fact]
    public void HasInherited_TrueForAncestorComponent()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(10, 20));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        Assert.False(w.Has<Position>(inst));        // direct: no
        Assert.True(w.HasInherited<Position>(inst)); // via IsA: yes
    }

    [Fact]
    public void GetInherited_ReturnsAncestorValue()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(7, 8));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        ref var p = ref w.GetInherited<Position>(inst);
        Assert.Equal(7, p.X);
        Assert.Equal(8, p.Y);
    }

    [Fact]
    public void GetInherited_RefMutatesSharedAncestorValue()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(0, 0));
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.SetIsA(a, prefab);
        w.SetIsA(b, prefab);
        ref var pa = ref w.GetInherited<Position>(a);
        pa.X = 99;
        // Both inheritors see the mutation — shared state.
        Assert.Equal(99, w.GetInherited<Position>(b).X);
        Assert.Equal(99, w.Get<Position>(prefab).X);
    }

    [Fact]
    public void GetInherited_DirectOverridesAncestor()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(1, 1));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        // Override on instance.
        w.Set(inst, new Position(99, 99));
        Assert.Equal(99, w.GetInherited<Position>(inst).X);
        // Prefab unchanged.
        Assert.Equal(1, w.Get<Position>(prefab).X);
    }

    [Fact]
    public void GetInherited_ThrowsWhenNotFoundAnywhere()
    {
        var w = new World();
        var inst = w.CreateEntity();
        w.Component<Position>();
        Assert.Throws<InvalidOperationException>(() => w.GetInherited<Position>(inst));
    }

    [Fact]
    public void TryGetInherited_FalseWhenMissing()
    {
        var w = new World();
        w.Component<Position>();
        var inst = w.CreateEntity();
        Assert.False(w.TryGetInherited<Position>(inst, out var p));
        Assert.Equal(default, p);
    }

    [Fact]
    public void TryGetInherited_TrueAndCopiesValue()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(3, 4));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        Assert.True(w.TryGetInherited<Position>(inst, out var p));
        Assert.Equal(3, p.X);
        Assert.Equal(4, p.Y);
    }

    [Fact]
    public void Inheritance_DeepChain()
    {
        var w = new World();
        var grand = w.CreateEntity();
        var parent = w.CreateEntity();
        var child = w.CreateEntity();
        w.Set(grand, new Position(11, 22));
        w.SetIsA(parent, grand);
        w.SetIsA(child, parent);
        Assert.True(w.HasInherited<Position>(child));
        Assert.Equal(11, w.GetInherited<Position>(child).X);
    }

    [Fact]
    public void Inheritance_DiamondVisitsOnce()
    {
        var w = new World();
        // Diamond:    A
        //            / \
        //           B   C
        //            \ /
        //             D
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        var d = w.CreateEntity();
        w.Set(a, new Position(7, 7));
        w.SetIsA(b, a);
        w.SetIsA(c, a);
        w.SetIsA(d, b);
        w.SetIsA(d, c);
        Assert.Equal(7, w.GetInherited<Position>(d).X);
    }

    [Fact]
    public void Inheritance_CyclesNotInfiniteLoop()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        // Build a cycle a -> b -> a. Pathological but cycle-safe walk.
        w.SetIsA(a, b);
        w.SetIsA(b, a);
        // Neither has Position; should return false, not loop.
        Assert.False(w.HasInherited<Position>(a));
    }

    [Fact]
    public void Inheritance_TagInheritance()
    {
        var w = new World();
        w.Tag<Boss>();
        var prefab = w.CreateEntity();
        w.Add<Boss>(prefab);
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        Assert.False(w.Has<Boss>(inst));
        Assert.True(w.HasInherited<Boss>(inst));
    }

    [Fact]
    public void Inheritance_PairInheritance()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Add<Likes, Apple>(prefab);
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        Assert.False(w.Has<Likes, Apple>(inst));
        Assert.True(w.HasInherited<Likes, Apple>(inst));
    }

    [Fact]
    public void Inheritance_FirstHitWins()
    {
        var w = new World();
        // A: pos=(1,1)
        // B IsA A: pos=(2,2) — closer to inst
        // inst IsA B
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(1, 1));
        w.SetIsA(b, a);
        w.Set(b, new Position(2, 2));
        var inst = w.CreateEntity();
        w.SetIsA(inst, b);
        // Closer ancestor wins.
        Assert.Equal(2, w.GetInherited<Position>(inst).X);
    }
}
