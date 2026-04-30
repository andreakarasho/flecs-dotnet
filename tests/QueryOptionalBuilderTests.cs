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
        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position, Velocity>().Optional<Velocity>())
            seen.Add(row.Entity.Id);
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
        foreach (var row in w.Query<Position, Velocity>().Optional<Velocity>())
            nullByEnt[row.Entity.Id] = Unsafe.IsNullRef(ref row.Component2.Value);
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
        foreach (var (p, v) in w.Query<Position, Velocity>().Optional<Velocity>())
        {
            if (Unsafe.IsNullRef(ref v.Value)) continue;
            v.Value.Dx *= 10f;
            p.Value.X = 99;
        }
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
        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position, Velocity>().Optional<Position>())
            seen.Add(row.Entity.Id);
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
        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position>().Optional<Position>())
            seen.Add(row.Entity.Id);
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
        var ints = new Dictionary<uint, int?>();
        foreach (var row in w.Query<Position, Velocity, Health>().Optional<Health>())
        {
            ref var h = ref row.Component3.Value;
            ints[row.Entity.Id] = Unsafe.IsNullRef(ref h) ? (int?)null : h.Value;
        }
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
        foreach (var row in w.Query<Position, Velocity>().Optional<Velocity>())
            sawNonNull = !Unsafe.IsNullRef(ref row.Component2.Value);
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
        foreach (var row in w.Query<Position, Velocity>().Optional<Velocity>().Without<Boss>())
            seen.Add(row.Entity.Id);
        Assert.Single(seen);
        Assert.Contains(b.Id, seen);
    }
}
