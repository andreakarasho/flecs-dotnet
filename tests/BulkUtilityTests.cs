using Xunit;

namespace Flecs.Tests;

public class BulkUtilityTests
{
    [Fact]
    public void Count_ZeroWhenUnregistered()
    {
        var w = new World();
        Assert.Equal(0, w.Count<Position>());
    }

    [Fact]
    public void Count_TallyAcrossTables()
    {
        var w = new World();
        for (int i = 0; i < 4; i++)
        {
            var e = w.CreateEntity();
            w.Set(e, new Position(0, 0));
        }
        // Two of them also get a velocity → different archetype.
        var ents = new System.Collections.Generic.List<EntityId>();
        foreach (var row in w.Query<Position>()) ents.Add(row.Entity);
        w.Set(ents[0], new Velocity(0, 0));
        w.Set(ents[1], new Velocity(0, 0));
        Assert.Equal(4, w.Count<Position>());
        Assert.Equal(2, w.Count<Velocity>());
    }

    [Fact]
    public void Clone_CopiesArchetype()
    {
        var w = new World();
        var src = w.CreateEntity();
        w.Set(src, new Position(1, 2));
        w.Set(src, new Health(99));
        var dst = w.Clone(src);
        Assert.True(w.Has<Position>(dst));
        Assert.True(w.Has<Health>(dst));
        Assert.Equal(1, w.Get<Position>(dst).X);
        Assert.Equal(99, w.Get<Health>(dst).Value);
    }

    [Fact]
    public void Clone_IsIndependentEntity()
    {
        var w = new World();
        var src = w.CreateEntity();
        w.Set(src, new Position(1, 1));
        var dst = w.Clone(src);
        w.Set(dst, new Position(99, 99));
        Assert.Equal(1, w.Get<Position>(src).X);
    }

    [Fact]
    public void Clone_OfRootEntityYieldsRootEntity()
    {
        var w = new World();
        var src = w.CreateEntity();
        var dst = w.Clone(src);
        Assert.True(w.IsAlive(dst));
        Assert.NotEqual(src.Id, dst.Id);
    }

    [Fact]
    public void BulkNew_CreatesNEntities()
    {
        var w = new World();
        var ents = w.BulkNew<Position>(10);
        Assert.Equal(10, ents.Length);
        foreach (var e in ents)
        {
            Assert.True(w.IsAlive(e));
            Assert.True(w.Has<Position>(e));
        }
    }

    [Fact]
    public void BulkNew_ZeroCountReturnsEmpty()
    {
        var w = new World();
        Assert.Empty(w.BulkNew<Position>(0));
    }

    [Fact]
    public void Disable_AddsDisabledTag()
    {
        var w = new World();
        var e = w.CreateEntity();
        Assert.True(w.IsEnabled(e));
        w.Disable(e);
        Assert.False(w.IsEnabled(e));
    }

    [Fact]
    public void Enable_RemovesDisabledTag()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Disable(e);
        w.Enable(e);
        Assert.True(w.IsEnabled(e));
    }

    [Fact]
    public void Disabled_QueryWithoutFiltersOut()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(b, new Position(0, 0));
        w.Disable(b);
        int hits = 0;
        foreach (var _ in w.Query<Position>().Without(w.States.Disabled)) hits++;
        Assert.Equal(1, hits);
    }

    [Fact]
    public void BulkNew_LargeCount_AllAlive()
    {
        var w = new World();
        var ents = w.BulkNew<Position>(1000);
        Assert.Equal(1000, ents.Length);
        var distinct = new System.Collections.Generic.HashSet<uint>();
        foreach (var e in ents) distinct.Add(e.Id);
        Assert.Equal(1000, distinct.Count);
    }

    [Fact]
    public void Count_Pair_CountsPairHolders()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        var t = w.CreateEntity();
        for (int i = 0; i < 4; i++)
        {
            var e = w.CreateEntity();
            w.Add(e, rel, t);
        }
        Assert.Equal(4, w.Count(w.Pair(rel, t)));
    }

    [Fact]
    public void Disable_Idempotent()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Disable(e);
        w.Disable(e); // re-disable — no-op
        Assert.False(w.IsEnabled(e));
        w.Enable(e);
        Assert.True(w.IsEnabled(e));
    }

    [Fact]
    public void Clone_HierarchyShape_PreservesParent()
    {
        var w = new World();
        var p = w.CreateEntity();
        var src = w.CreateEntity();
        w.SetParent(src, p);
        w.Set(src, new Position(1, 1));
        var dst = w.Clone(src);
        // Cloned entity inherits ChildOf relation by default.
        Assert.Equal(p.Id, w.GetParent(dst).Id);
    }

    // ===== RemoveAll =====

    [Fact]
    public void RemoveAll_Tag_DropsFromEveryHolder()
    {
        var w = new World();
        var ents = new EntityId[5];
        for (int i = 0; i < ents.Length; i++) { ents[i] = w.CreateEntity(); w.Add<TagA>(ents[i]); }
        w.RemoveAll<TagA>();
        foreach (var e in ents) Assert.False(w.Has<TagA>(e));
        // Holders still alive.
        foreach (var e in ents) Assert.True(w.IsAlive(e));
    }

    [Fact]
    public void RemoveAll_Component_DropsFromEveryHolder()
    {
        var w = new World();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 1));
        var b = w.CreateEntity(); w.Set(b, new Position(2, 2)); w.Add<TagA>(b);
        var c = w.CreateEntity(); w.Add<TagA>(c); // no Position
        w.RemoveAll<Position>();
        Assert.False(w.Has<Position>(a));
        Assert.False(w.Has<Position>(b));
        Assert.True(w.Has<TagA>(b)); // unrelated tag preserved
        Assert.True(w.Has<TagA>(c));
    }

    [Fact]
    public void RemoveAll_Pair_DropsAllPairsForRelTarget()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        var t = w.CreateEntity();
        var holders = new EntityId[4];
        for (int i = 0; i < holders.Length; i++) { holders[i] = w.CreateEntity(); w.Add(holders[i], rel, t); }
        w.RemoveAll(w.Pair(rel, t));
        foreach (var h in holders) Assert.False(w.Has(h, w.Pair(rel, t)));
    }

    [Fact]
    public void RemoveAll_Unregistered_NoOp()
    {
        var w = new World();
        w.RemoveAll<Position>(); // never registered — no throw
    }

    [Fact]
    public void RemoveAll_NoHolders_NoOp()
    {
        var w = new World();
        w.Component<Position>();
        w.RemoveAll<Position>();
    }
}
