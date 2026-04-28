using Xunit;
using System;

namespace Flecs.Tests;

public class AdvancedTraitTests
{
    // ---------- Acyclic ----------

    [Fact]
    public void ChildOf_DefaultAcyclic()
    {
        var w = new World();
        Assert.True(w.IsAcyclic(w.ChildOf));
    }

    [Fact]
    public void Acyclic_SelfReferenceThrows()
    {
        var w = new World();
        var e = w.CreateEntity();
        Assert.Throws<InvalidOperationException>(() => w.SetParent(e, e));
    }

    [Fact]
    public void Acyclic_SimpleCycleThrows()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.SetParent(a, b);
        // b -> a would close cycle a -> b -> a
        Assert.Throws<InvalidOperationException>(() => w.SetParent(b, a));
    }

    [Fact]
    public void Acyclic_DeepCycleThrows()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        w.SetParent(a, b);
        w.SetParent(b, c);
        // c -> a closes cycle
        Assert.Throws<InvalidOperationException>(() => w.SetParent(c, a));
    }

    [Fact]
    public void Acyclic_CustomRelation()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkAcyclic(rel);
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Add(a, rel, b);
        Assert.Throws<InvalidOperationException>(() => w.Add(b, rel, a));
    }

    [Fact]
    public void Unmark_Acyclic_AllowsCycle()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkAcyclic(rel);
        w.UnmarkAcyclic(rel);
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Add(a, rel, b);
        w.Add(b, rel, a);
        Assert.True(w.Has(a, w.Pair(rel, b)));
        Assert.True(w.Has(b, w.Pair(rel, a)));
    }

    // ---------- Symmetric ----------

    [Fact]
    public void Symmetric_AddMirrors()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkSymmetric(rel);
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Add(a, rel, b);
        Assert.True(w.Has(a, w.Pair(rel, b)));
        Assert.True(w.Has(b, w.Pair(rel, a)));
    }

    [Fact]
    public void Symmetric_RemoveMirrors()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkSymmetric(rel);
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Add(a, rel, b);
        w.Remove(a, w.Pair(rel, b));
        Assert.False(w.Has(a, w.Pair(rel, b)));
        Assert.False(w.Has(b, w.Pair(rel, a)));
    }

    [Fact]
    public void Symmetric_TerminatesOnReAdd()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkSymmetric(rel);
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Add(a, rel, b);
        // Re-add should be no-op (no infinite loop).
        w.Add(a, rel, b);
        Assert.True(w.Has(a, w.Pair(rel, b)));
    }

    [Fact]
    public void IsSymmetric_TracksMarking()
    {
        var w = new World();
        var rel = w.CreateEntity();
        Assert.False(w.IsSymmetric(rel));
        w.MarkSymmetric(rel);
        Assert.True(w.IsSymmetric(rel));
    }

    // ---------- Reflexive ----------

    [Fact]
    public void Reflexive_HasReflexiveTrueForSelf()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkReflexive(rel);
        var a = w.CreateEntity();
        Assert.True(w.HasReflexive(a, rel, a));
    }

    [Fact]
    public void NonReflexive_HasReflexiveFalseForSelf()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        var a = w.CreateEntity();
        Assert.False(w.HasReflexive(a, rel, a));
    }

    [Fact]
    public void Reflexive_TrueForExplicitPair()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkReflexive(rel);
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Add(a, rel, b);
        Assert.True(w.HasReflexive(a, rel, b)); // explicit
        Assert.True(w.HasReflexive(a, rel, a)); // self-reflexive
        Assert.False(w.HasReflexive(a, rel, w.CreateEntity())); // unrelated
    }

    // ---------- Transitive ----------

    [Fact]
    public void Transitive_DirectPairTrue()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkTransitive(rel);
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Add(a, rel, b);
        Assert.True(w.HasTransitive(a, rel, b));
    }

    [Fact]
    public void Transitive_TwoStepChain()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkTransitive(rel);
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        w.Add(a, rel, b);
        w.Add(b, rel, c);
        Assert.True(w.HasTransitive(a, rel, c));   // chain
        Assert.False(w.Has(a, w.Pair(rel, c)));     // no direct pair
    }

    [Fact]
    public void Transitive_DeepChain()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkTransitive(rel);
        var ents = new EntityId[5];
        for (int i = 0; i < 5; i++) ents[i] = w.CreateEntity();
        for (int i = 0; i < 4; i++) w.Add(ents[i], rel, ents[i + 1]);
        Assert.True(w.HasTransitive(ents[0], rel, ents[4]));
    }

    [Fact]
    public void Transitive_FalseWhenNoChain()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.MarkTransitive(rel);
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        w.Add(a, rel, b);
        // c not reachable from a
        Assert.False(w.HasTransitive(a, rel, c));
    }

    [Fact]
    public void IsTransitive_TracksMarking()
    {
        var w = new World();
        var rel = w.CreateEntity();
        Assert.False(w.IsTransitive(rel));
        w.MarkTransitive(rel);
        Assert.True(w.IsTransitive(rel));
    }
}
