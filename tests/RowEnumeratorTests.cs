using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class RowEnumeratorTests
{
    [Fact]
    public void Foreach_SingleArity_VisitsAllRows()
    {
        var w = new World();
        for (int i = 0; i < 5; i++)
        {
            var e = w.CreateEntity();
            w.Set(e, new Position(i, i));
        }
        var seen = new List<float>();
        foreach (var row in w.Query<Position>())
            seen.Add(row.Component1.Value.X);
        Assert.Equal(5, seen.Count);
        Assert.Contains(0f, seen);
        Assert.Contains(4f, seen);
    }

    [Fact]
    public void Foreach_TwoArity_PerRowDeconstruct()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        w.Set(e, new Velocity(10, 20));
        int hits = 0;
        foreach (var (p, v) in w.Query<Position, Velocity>())
        {
            Assert.Equal(1, p.Value.X);
            Assert.Equal(10, v.Value.Dx);
            hits++;
        }
        Assert.Equal(1, hits);
    }

    [Fact]
    public void Foreach_MutationPersistsToRealStorage()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Velocity(3, 4));
        foreach (var (p, v) in w.Query<Position, Velocity>())
        {
            p.Value.X = v.Value.Dx * 2;
            p.Value.Y = v.Value.Dy * 2;
        }
        Assert.Equal(6, w.Get<Position>(e).X);
        Assert.Equal(8, w.Get<Position>(e).Y);
    }

    [Fact]
    public void Foreach_EmptyTablesSkipped()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Delete(e);
        int rows = 0;
        foreach (var _ in w.Query<Position>()) rows++;
        Assert.Equal(0, rows);
    }

    [Fact]
    public void Foreach_ChangeDetection_FalseAfterIteration()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        var q = w.Query<Position>();
        foreach (var _ in q) { }
        Assert.False(q.IsChanged());
    }

    [Fact]
    public void Foreach_AcrossMultipleArchetypes_VisitsAll()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(1, 1));
        w.Set(b, new Position(2, 2));
        w.Set(b, new Velocity(0, 0));
        var sum = 0f;
        foreach (var row in w.Query<Position>())
            sum += row.Component1.Value.X;
        Assert.Equal(3f, sum);
    }

    [Fact]
    public void Foreach_Arity6_Full()
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
        foreach (var (_, _, _, _, d, _) in w.Query<Position, Velocity, Health, Mana, Damage, Defense>())
        {
            d.Value = new Damage(7);
            hits++;
        }
        Assert.Equal(1, hits);
        Assert.Equal(7, w.Get<Damage>(e).Value);
    }

    [Fact]
    public void Foreach_ZeroAllocSteadyState()
    {
        var w = new World();
        for (int i = 0; i < 100; i++)
        {
            var e = w.CreateEntity();
            w.Set(e, new Position(i, i));
            w.Set(e, new Velocity(1, 1));
        }
        var q = w.Query<Position, Velocity>();
        for (int i = 0; i < 50; i++)
            foreach (var (p, v) in q) p.Value.X += v.Value.Dx;
        var before = System.GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
            foreach (var (p, v) in q) p.Value.X += v.Value.Dx;
        var after = System.GC.GetAllocatedBytesForCurrentThread();
        Assert.True(after - before < 1000,
            $"Foreach over 1000 iters allocated {after - before} bytes");
    }
}
