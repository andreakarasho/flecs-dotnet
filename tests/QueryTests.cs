using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class QueryTests
{
    [Fact]
    public void Query_MatchesEntityWithRequiredComponent()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(1, 1));
        // b has no Position
        var hits = new HashSet<uint>();
        w.Query<Position>().Each((EntityId e, ref Position p) => hits.Add(e.Id));
        Assert.Contains(a.Id, hits);
        Assert.DoesNotContain(b.Id, hits);
    }

    [Fact]
    public void Query_TwoComponentMatch()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(a, new Velocity(0, 0));
        w.Set(b, new Position(0, 0)); // missing Velocity
        var matched = new List<uint>();
        w.Query<Position, Velocity>().Each((EntityId e, ref Position p, ref Velocity v) => matched.Add(e.Id));
        Assert.Single(matched);
        Assert.Equal(a.Id, matched[0]);
    }

    [Fact]
    public void Query_MutationViaRef()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        w.Query<Position>().Each((EntityId _, ref Position p) => { p.X = 99; });
        Assert.Equal(99, w.Get<Position>(e).X);
    }

    [Fact]
    public void Query_Without_ExcludesMatching()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(b, new Position(0, 0));
        w.Add<Frozen>(b);
        var hits = new HashSet<uint>();
        w.Query<Position>().Without<Frozen>().Each((EntityId e, ref Position _) => hits.Add(e.Id));
        Assert.Contains(a.Id, hits);
        Assert.DoesNotContain(b.Id, hits);
    }

    [Fact]
    public void Query_Or_MatchesAnyInGroup()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(b, new Position(0, 0));
        w.Set(c, new Position(0, 0));
        w.Add<Boss>(a);
        w.Add<Frozen>(b);
        // c has neither
        var hits = new HashSet<uint>();
        w.Query<Position>().Or<Boss, Frozen>().Each((EntityId e, ref Position _) => hits.Add(e.Id));
        Assert.Contains(a.Id, hits);
        Assert.Contains(b.Id, hits);
        Assert.DoesNotContain(c.Id, hits);
    }

    [Fact]
    public void Query_With_AddsExtraConstraint()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Add<TagA>(a);
        w.Set(b, new Position(0, 0));
        var hits = new HashSet<uint>();
        w.Query<Position>().With<TagA>().Each((EntityId e, ref Position _) => hits.Add(e.Id));
        Assert.Single(hits);
        Assert.Contains(a.Id, hits);
    }

    [Fact]
    public void Query_WildcardPair_MatchesAnyTarget()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(b, new Position(0, 0));
        w.Set(c, new Position(0, 0));
        w.Add<Likes, Apple>(a);
        w.Add<Likes, Orange>(b);
        // c has no Likes
        var hits = new HashSet<uint>();
        w.Query<Position>().With(w.PairWildcard<Likes>())
            .Each((EntityId e, ref Position _) => hits.Add(e.Id));
        Assert.Contains(a.Id, hits);
        Assert.Contains(b.Id, hits);
        Assert.DoesNotContain(c.Id, hits);
    }

    [Fact]
    public void Query_EmptyTablesSkipped()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Delete(e);
        // Table still exists but empty.
        int count = 0;
        w.Query<Position>().Each((EntityId _, ref Position _) => count++);
        Assert.Equal(0, count);
    }

    [Fact]
    public void Query_NewTableMatchedLazily()
    {
        var w = new World();
        var q = w.Query<Position>();
        var initial = q.MatchedTableCount;
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        Assert.True(q.MatchedTableCount > initial);
    }

    [Fact]
    public void Query_ChangeDetection_FalseAfterEach()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        var q = w.Query<Position>();
        q.Each((EntityId _, ref Position _) => { });
        Assert.False(q.IsChanged());
    }

    [Fact]
    public void Query_ChangeDetection_TrueAfterStructuralMutation()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        var q = w.Query<Position>();
        q.Each((EntityId _, ref Position _) => { });
        Assert.False(q.IsChanged());
        var e2 = w.CreateEntity();
        w.Set(e2, new Position(1, 1));
        Assert.True(q.IsChanged());
    }

    [Fact]
    public void Query_Run_GivesSpanAccess()
    {
        var w = new World();
        for (int i = 0; i < 5; i++)
        {
            var e = w.CreateEntity();
            w.Set(e, new Position(i, i));
        }
        int total = 0;
        w.Query<Position>().Run((in Iter<Position> it) =>
        {
            var span = it.Field1();
            for (int r = 0; r < it.Count; r++) total += (int)span[r].X;
        });
        Assert.Equal(0 + 1 + 2 + 3 + 4, total);
    }

    [Fact]
    public void Query_HigherArity_UpToSix()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Velocity(0, 0));
        w.Set(e, new Health(0));
        w.Set(e, new Mana(0));
        w.Set(e, new Damage(0));
        w.Set(e, new Defense(0));
        int hits = 0;
        w.Query<Position, Velocity, Health, Mana, Damage, Defense>().Each(
            (EntityId _, ref Position _, ref Velocity _, ref Health _, ref Mana _, ref Damage _, ref Defense _) => hits++);
        Assert.Equal(1, hits);
    }
}
