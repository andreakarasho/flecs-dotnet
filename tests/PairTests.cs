using Xunit;

namespace Flecs.Tests;

public class PairTests
{
    [Fact]
    public void Pair_TypedEncodingHasPairFlag()
    {
        var w = new World();
        var pair = w.Pair<Likes, Apple>();
        Assert.True(pair.IsPair);
        Assert.NotEqual(0u, pair.Relation);
        Assert.NotEqual(0u, pair.Target);
    }

    [Fact]
    public void Add_TypedPair()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Add<Likes, Apple>(e);
        Assert.True(w.Has<Likes, Apple>(e));
    }

    [Fact]
    public void Remove_TypedPair()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Add<Likes, Apple>(e);
        w.Remove<Likes, Apple>(e);
        Assert.False(w.Has<Likes, Apple>(e));
    }

    [Fact]
    public void Pair_DifferentRelationsAreDistinct()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Add<Likes, Apple>(e);
        w.Add<Hates, Apple>(e);
        Assert.True(w.Has<Likes, Apple>(e));
        Assert.True(w.Has<Hates, Apple>(e));
    }

    [Fact]
    public void Pair_DifferentTargetsAreDistinct()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Add<Likes, Apple>(e);
        w.Add<Likes, Orange>(e);
        Assert.True(w.Has<Likes, Apple>(e));
        Assert.True(w.Has<Likes, Orange>(e));
    }

    [Fact]
    public void Pair_RuntimeEntityTarget()
    {
        var w = new World();
        var e = w.CreateEntity();
        var target = w.CreateEntity();
        var rel = w.Tag<Likes>();
        w.Add(e, rel, target);
        Assert.True(w.Has(e, w.Pair(rel, target)));
    }

    [Fact]
    public void PairFlagBitSetForPairs()
    {
        EntityId rel = new(10, 1);
        EntityId tgt = new(20, 1);
        var p = Id.MakePair(rel, tgt);
        Assert.True(p.IsPair);
        Assert.Equal(10u, p.Relation);
        Assert.Equal(20u, p.Target);
    }

    [Fact]
    public void NonPairIdHasNoPairFlag()
    {
        EntityId e = new(7, 1);
        Id id = (Id)e;
        Assert.False(id.IsPair);
        Assert.Equal(7u, id.Component);
    }

    // ===== Wildcard pair Has =====

    [Fact]
    public void Has_WildcardTarget_MatchesAnyTarget()
    {
        var w = new World();
        var e = w.CreateEntity();
        var rel = w.Tag<Likes>();
        var tgt = w.CreateEntity();
        w.Add(e, rel, tgt);
        Assert.True(w.Has(e, w.Pair(rel, w.Relations.Wildcard)));
    }

    [Fact]
    public void Has_WildcardTarget_FalseWhenNoSuchRelation()
    {
        var w = new World();
        var e = w.CreateEntity();
        var rel = w.Tag<Likes>();
        Assert.False(w.Has(e, w.Pair(rel, w.Relations.Wildcard)));
    }

    [Fact]
    public void Has_WildcardRelation_MatchesAnyRelation()
    {
        var w = new World();
        var e = w.CreateEntity();
        var rel = w.Tag<Likes>();
        var tgt = w.CreateEntity();
        w.Add(e, rel, tgt);
        Assert.True(w.Has(e, w.Pair(w.Relations.Wildcard, tgt)));
    }

    [Fact]
    public void Has_WildcardBoth_MatchesAnyPair()
    {
        var w = new World();
        var e = w.CreateEntity();
        var rel = w.Tag<Likes>();
        var tgt = w.CreateEntity();
        w.Add(e, rel, tgt);
        Assert.True(w.Has(e, w.Pair(w.Relations.Wildcard, w.Relations.Wildcard)));
    }

    [Fact]
    public void Has_WildcardBoth_FalseWhenEntityHasNoPairs()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Add<TagA>(e);
        Assert.False(w.Has(e, w.Pair(w.Relations.Wildcard, w.Relations.Wildcard)));
    }

    [Fact]
    public void PairWildcard_TypedRelationMatchesAnyTarget()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Add<Likes, Apple>(e);
        Assert.True(w.Has(e, w.PairWildcard<Likes>()));
    }

    [Fact]
    public void Has_WildcardTarget_InheritedViaIsA()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        var rel = w.Tag<Likes>();
        var tgt = w.CreateEntity();
        w.Add(prefab, rel, tgt);
        var inst = w.CreateEntity();
        w.Add(inst, w.Relations.IsA, prefab);
        Assert.True(w.Has(inst, w.Pair(rel, w.Relations.Wildcard)));
    }

    // ===== Id encoding =====

    [Fact]
    public void MakePair_PreservesRelationAndTarget()
    {
        var rel = new EntityId(0x1234, 1);
        var tgt = new EntityId(0xABCD, 1);
        var id = Id.MakePair(rel, tgt);
        Assert.True(id.IsPair);
        Assert.Equal(0x1234u, id.Relation);
        Assert.Equal(0xABCDu, id.Target);
        Assert.Equal(0u, id.Component); // pairs report 0 for Component accessor
    }

    [Fact]
    public void NonPair_ComponentAccessor_ReturnsId()
    {
        var e = new EntityId(42, 1);
        Id id = (Id)e;
        Assert.False(id.IsPair);
        Assert.Equal(42u, id.Component);
        Assert.Equal(0u, id.Relation);  // non-pair → 0
        Assert.Equal(0u, id.Target);
    }

    [Fact]
    public void Pair_DistinctRelationsTargets_DistinctIds()
    {
        var r1 = new EntityId(1, 1);
        var r2 = new EntityId(2, 1);
        var t1 = new EntityId(10, 1);
        var t2 = new EntityId(20, 1);
        Assert.NotEqual(Id.MakePair(r1, t1), Id.MakePair(r2, t1));
        Assert.NotEqual(Id.MakePair(r1, t1), Id.MakePair(r1, t2));
        Assert.Equal(Id.MakePair(r1, t1), Id.MakePair(r1, t1));
    }

    [Fact]
    public void Pair_HighestRelationTargetValuesEncodable()
    {
        // Max-uint-ish ids must round-trip without bit collision.
        var rel = new EntityId(0x7FFFFFFFu, 1);
        var tgt = new EntityId(0x7FFFFFFFu, 1);
        var id = Id.MakePair(rel, tgt);
        Assert.True(id.IsPair);
        Assert.Equal(0x7FFFFFFFu, id.Relation);
        Assert.Equal(0x7FFFFFFFu, id.Target);
    }

    [Fact]
    public void Pair_OrderingDeterministic()
    {
        // CompareTo by raw Value — used for stable sort/dedupe.
        var a = Id.MakePair(new(1, 1), new(2, 1));
        var b = Id.MakePair(new(1, 1), new(3, 1));
        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
        Assert.Equal(0, a.CompareTo(a));
    }

    [Fact]
    public void Pair_TypedAndRuntime_ResolveSame()
    {
        var w = new World();
        var typed = w.Pair<Likes, Apple>();
        var runtimeRel = w.Tag<Likes>();
        var runtimeTgt = w.Tag<Apple>();
        var runtime = w.Pair(runtimeRel, runtimeTgt);
        Assert.Equal(typed, runtime);
    }
}
