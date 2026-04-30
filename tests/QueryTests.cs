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
        var q = w.Query<Position>();
        // RowEnumerator does not expose entity ids — verify count + value instead.
        int rows = 0;
        foreach (var row in q) { rows++; if (row.Component1.Value.X == 1f) hits.Add(a.Id); }
        Assert.Contains(a.Id, hits);
        Assert.Equal(1, rows);
    }

    [Fact]
    public void Query_TwoComponentMatch()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(7, 0));
        w.Set(a, new Velocity(0, 0));
        w.Set(b, new Position(0, 0)); // missing Velocity
        int matched = 0;
        float seen = 0;
        foreach (var (p, _) in w.Query<Position, Velocity>())
        {
            matched++;
            seen = p.Value.X;
        }
        Assert.Equal(1, matched);
        Assert.Equal(7f, seen);
    }

    [Fact]
    public void Query_MutationViaRef()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        foreach (var row in w.Query<Position>())
            row.Component1.Value.X = 99;
        Assert.Equal(99, w.Get<Position>(e).X);
    }

    [Fact]
    public void Query_Without_ExcludesMatching()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(1, 0));
        w.Set(b, new Position(2, 0));
        w.Add<Frozen>(b);
        var seen = new HashSet<float>();
        foreach (var row in w.Query<Position>().Without<Frozen>())
            seen.Add(row.Component1.Value.X);
        Assert.Contains(1f, seen);
        Assert.DoesNotContain(2f, seen);
    }

    [Fact]
    public void Query_Or_MatchesAnyInGroup()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        w.Set(a, new Position(1, 0));
        w.Set(b, new Position(2, 0));
        w.Set(c, new Position(3, 0));
        w.Add<Boss>(a);
        w.Add<Frozen>(b);
        var seen = new HashSet<float>();
        foreach (var row in w.Query<Position>().Or<Boss, Frozen>())
            seen.Add(row.Component1.Value.X);
        Assert.Contains(1f, seen);
        Assert.Contains(2f, seen);
        Assert.DoesNotContain(3f, seen);
    }

    [Fact]
    public void Query_With_AddsExtraConstraint()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(1, 0));
        w.Add<TagA>(a);
        w.Set(b, new Position(2, 0));
        var seen = new HashSet<float>();
        foreach (var row in w.Query<Position>().With<TagA>())
            seen.Add(row.Component1.Value.X);
        Assert.Single(seen);
        Assert.Contains(1f, seen);
    }

    [Fact]
    public void Query_WildcardPair_MatchesAnyTarget()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        w.Set(a, new Position(1, 0));
        w.Set(b, new Position(2, 0));
        w.Set(c, new Position(3, 0));
        w.Add<Likes, Apple>(a);
        w.Add<Likes, Orange>(b);
        var seen = new HashSet<float>();
        foreach (var row in w.Query<Position>().With(w.PairWildcard<Likes>()))
            seen.Add(row.Component1.Value.X);
        Assert.Contains(1f, seen);
        Assert.Contains(2f, seen);
        Assert.DoesNotContain(3f, seen);
    }

    [Fact]
    public void Query_EmptyTablesSkipped()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Delete(e);
        int count = 0;
        foreach (var _ in w.Query<Position>()) count++;
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
    public void Query_ChangeDetection_FalseAfterIter()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        var q = w.Query<Position>();
        foreach (var _ in q) { }
        Assert.False(q.IsChanged());
    }

    [Fact]
    public void Query_ChangeDetection_TrueAfterStructuralMutation()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        var q = w.Query<Position>();
        foreach (var _ in q) { }
        Assert.False(q.IsChanged());
        var e2 = w.CreateEntity();
        w.Set(e2, new Position(1, 1));
        Assert.True(q.IsChanged());
    }

    [Fact]
    public void Query_PerRow_TightLoop()
    {
        var w = new World();
        for (int i = 0; i < 5; i++)
        {
            var e = w.CreateEntity();
            w.Set(e, new Position(i, i));
        }
        int total = 0;
        foreach (var row in w.Query<Position>())
            total += (int)row.Component1.Value.X;
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
        foreach (var _ in w.Query<Position, Velocity, Health, Mana, Damage, Defense>())
            hits++;
        Assert.Equal(1, hits);
    }
}
