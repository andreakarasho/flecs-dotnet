using Xunit;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Flecs.Tests;

public class QueryOptionalBuilderTests
{
    [Fact]
    public void Optional_T2_MatchesEntitiesWithAndWithoutT2()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(a, new Velocity(1, 1));
        w.Set(b, new Position(0, 0));
        // b has no Velocity
        var seen = new HashSet<uint>();
        w.Query<Position, Velocity>()
            .Optional<Velocity>()
            .Each((EntityId e, ref Position _, ref Velocity _) => seen.Add(e.Id));
        Assert.Equal(2, seen.Count);
        Assert.Contains(a.Id, seen);
        Assert.Contains(b.Id, seen);
    }

    [Fact]
    public void Optional_T2_NullRefWhenAbsent()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(a, new Velocity(7, 8));
        w.Set(b, new Position(0, 0));
        var nullByEnt = new Dictionary<uint, bool>();
        w.Query<Position, Velocity>()
            .Optional<Velocity>()
            .Each((EntityId e, ref Position _, ref Velocity v) =>
                nullByEnt[e.Id] = Unsafe.IsNullRef(ref v));
        Assert.False(nullByEnt[a.Id]);
        Assert.True(nullByEnt[b.Id]);
    }

    [Fact]
    public void Optional_T2_RefMutationPersistsToRealSlot()
    {
        var w = new World();
        var a = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(a, new Velocity(2, 3));
        w.Query<Position, Velocity>()
            .Optional<Velocity>()
            .Each((EntityId _, ref Position p, ref Velocity v) =>
            {
                if (Unsafe.IsNullRef(ref v)) return;
                v.Dx *= 10f;
                p.X = 99;
            });
        Assert.Equal(99, w.Get<Position>(a).X);
        Assert.Equal(20f, w.Get<Velocity>(a).Dx);
    }

    [Fact]
    public void Optional_T1_MatchesEntitiesWithoutT1Too()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(a, new Velocity(0, 0));
        w.Set(b, new Velocity(0, 0));
        // b has no Position
        var seen = new HashSet<uint>();
        w.Query<Position, Velocity>()
            .Optional<Position>()
            .Each((EntityId e, ref Position _, ref Velocity _) => seen.Add(e.Id));
        Assert.Equal(2, seen.Count);
        Assert.Contains(a.Id, seen);
        Assert.Contains(b.Id, seen);
    }

    [Fact]
    public void Optional_OnSingleArityQuery()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        // b has no Position — but it's optional
        var seen = new HashSet<uint>();
        w.Query<Position>()
            .Optional<Position>()
            .Each((EntityId e, ref Position _) => seen.Add(e.Id));
        Assert.Contains(a.Id, seen);
        Assert.Contains(b.Id, seen);
    }

    [Fact]
    public void Optional_WrongTypeThrows()
    {
        var w = new World();
        Assert.Throws<ArgumentException>(() =>
            w.Query<Position, Velocity>().Optional<Health>());
    }

    [Fact]
    public void Optional_Arity3_MarkLastOnly()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(a, new Velocity(0, 0));
        w.Set(a, new Health(99));
        w.Set(b, new Position(0, 0));
        w.Set(b, new Velocity(0, 0));
        // b has no Health
        var ints = new Dictionary<uint, int?>();
        w.Query<Position, Velocity, Health>()
            .Optional<Health>()
            .Each((EntityId e, ref Position _, ref Velocity _, ref Health h) =>
            {
                ints[e.Id] = Unsafe.IsNullRef(ref h) ? (int?)null : h.Value;
            });
        Assert.Equal(99, ints[a.Id]);
        Assert.Null(ints[b.Id]);
    }

    [Fact]
    public void Optional_FastPathStillFiresWhenAllPresent()
    {
        var w = new World();
        var a = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(a, new Velocity(1, 2));
        bool sawNonNull = false;
        w.Query<Position, Velocity>()
            .Optional<Velocity>()
            .Each((EntityId _, ref Position _, ref Velocity v) =>
                sawNonNull = !Unsafe.IsNullRef(ref v));
        Assert.True(sawNonNull);
    }

    [Fact]
    public void Optional_RespectsWithoutFilter()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(b, new Position(0, 0));
        w.Set(b, new Velocity(0, 0));
        w.Add<Boss>(a);
        var seen = new HashSet<uint>();
        w.Query<Position, Velocity>()
            .Optional<Velocity>()
            .Without<Boss>()
            .Each((EntityId e, ref Position _, ref Velocity _) => seen.Add(e.Id));
        Assert.Single(seen);
        Assert.Contains(b.Id, seen);
    }
}
