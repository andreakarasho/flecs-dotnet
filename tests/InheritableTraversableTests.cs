using Xunit;
using System;

namespace Flecs.Tests;

public class InheritableTraversableTests
{
    // ---------- Inheritable / DontInherit ----------

    [Fact]
    public void IsInheritable_DefaultTrue()
    {
        var w = new World();
        var compEnt = w.Component<Position>();
        Assert.True(w.IsInheritable(compEnt));
        Assert.False(w.IsDontInherit(compEnt));
    }

    [Fact]
    public void DontInherit_BlocksIsAPropagation_GetInheritedThrows()
    {
        var w = new World();
        var compEnt = w.Component<Position>();
        w.MarkDontInherit(compEnt);

        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(7, 8));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        Assert.False(w.HasInherited<Position>(inst));
        Assert.Throws<InvalidOperationException>(() => w.GetInherited<Position>(inst));
    }

    [Fact]
    public void DontInherit_DirectAccessStillWorks()
    {
        var w = new World();
        var compEnt = w.Component<Position>();
        w.MarkDontInherit(compEnt);
        var e = w.CreateEntity();
        w.Set(e, new Position(5, 6));
        Assert.True(w.HasInherited<Position>(e));
        Assert.Equal(5, w.GetInherited<Position>(e).X);
    }

    [Fact]
    public void MarkInheritable_RestoresIsAPropagation()
    {
        var w = new World();
        var compEnt = w.Component<Position>();
        w.MarkDontInherit(compEnt);
        w.MarkInheritable(compEnt);

        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(1, 2));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        Assert.True(w.HasInherited<Position>(inst));
    }

    [Fact]
    public void DontInherit_Tag_Blocks()
    {
        var w = new World();
        var tagEnt = w.Tag<Boss>();
        w.MarkDontInherit(tagEnt);

        var prefab = w.CreateEntity();
        w.Add<Boss>(prefab);
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        Assert.False(w.HasInherited<Boss>(inst));
    }

    [Fact]
    public void DontInherit_PairRelation_Blocks()
    {
        var w = new World();
        var likes = w.Tag<Likes>();
        w.MarkDontInherit(likes);

        var prefab = w.CreateEntity();
        w.Add<Likes, Apple>(prefab);
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        Assert.False(w.HasInherited<Likes, Apple>(inst));
    }

    [Fact]
    public void TryGetInherited_RespectsDontInherit()
    {
        var w = new World();
        var compEnt = w.Component<Position>();
        w.MarkDontInherit(compEnt);

        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(1, 1));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        Assert.False(w.TryGetInherited<Position>(inst, out var _));
    }

    [Fact]
    public void DontInherit_TaggedEntityHasTag()
    {
        var w = new World();
        var compEnt = w.Component<Position>();
        w.MarkDontInherit(compEnt);
        Assert.True(w.Has(compEnt, (Id)w.DontInherit));
        // Inheritable tag dropped (if it was added).
        w.MarkInheritable(compEnt);
        Assert.False(w.Has(compEnt, (Id)w.DontInherit));
        Assert.True(w.Has(compEnt, (Id)w.Inheritable));
    }

    // ---------- Traversable ----------

    [Fact]
    public void ChildOf_DefaultTraversable()
    {
        var w = new World();
        Assert.True(w.IsTraversable(w.ChildOf));
    }

    [Fact]
    public void IsA_DefaultTraversable()
    {
        var w = new World();
        Assert.True(w.IsTraversable(w.IsA));
    }

    [Fact]
    public void CustomRelation_NotTraversableByDefault()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        Assert.False(w.IsTraversable(rel));
    }

    [Fact]
    public void MarkTraversable_TogglesFlag()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkTraversable(rel);
        Assert.True(w.IsTraversable(rel));
        w.UnmarkTraversable(rel);
        Assert.False(w.IsTraversable(rel));
    }

    [Fact]
    public void MarkTraversable_TaggedRelationHasTraversableTag()
    {
        var w = new World();
        var rel = w.CreateEntity();
        w.MarkTraversable(rel);
        Assert.True(w.Has(rel, (Id)w.Traversable));
    }
}
