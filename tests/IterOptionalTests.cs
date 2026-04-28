using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class IterOptionalTests
{
    [Fact]
    public void Iter_OptionalField_PresentSpan()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Velocity(7, 8));
        Velocity captured = default;
        w.Query<Position>().Run((in Iter<Position> it) =>
        {
            var v = it.OptionalField<Velocity>();
            if (v.Length > 0) captured = v[0];
        });
        Assert.Equal(7, captured.Dx);
        Assert.Equal(8, captured.Dy);
    }

    [Fact]
    public void Iter_OptionalField_AbsentEmpty()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        // no Velocity
        int len = -1;
        w.Query<Position>().Run((in Iter<Position> it) =>
        {
            len = it.OptionalField<Velocity>().Length;
        });
        Assert.Equal(0, len);
    }

    [Fact]
    public void Iter_HasOptional_Reflects()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(a, new Velocity(0, 0));
        w.Set(b, new Position(0, 0));

        var byTable = new List<bool>();
        w.Query<Position>().Run((in Iter<Position> it) => byTable.Add(it.HasOptional<Velocity>()));
        Assert.Contains(true, byTable);
        Assert.Contains(false, byTable);
    }

    [Fact]
    public void Iter_OptionalField_MutationPersists()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Velocity(2, 3));
        w.Query<Position>().Run((in Iter<Position> it) =>
        {
            var v = it.OptionalField<Velocity>();
            for (int r = 0; r < v.Length; r++)
            {
                v[r].Dx *= 10f;
                v[r].Dy *= 10f;
            }
        });
        Assert.Equal(20f, w.Get<Velocity>(e).Dx);
        Assert.Equal(30f, w.Get<Velocity>(e).Dy);
    }

    [Fact]
    public void Iter_OptionalField_TwoArchetypes_OneHasOneNot()
    {
        var w = new World();
        for (int i = 0; i < 3; i++)
        {
            var e = w.CreateEntity();
            w.Set(e, new Position(0, 0));
            if (i % 2 == 0) w.Set(e, new Velocity(1, 1));
        }
        int withV = 0;
        int total = 0;
        w.Query<Position>().Run((in Iter<Position> it) =>
        {
            var v = it.OptionalField<Velocity>();
            total += it.Count;
            if (v.Length > 0) withV += it.Count;
        });
        Assert.Equal(3, total);
        Assert.Equal(2, withV); // i=0,2
    }

    [Fact]
    public void Iter_OptionalField_HigherArity()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Velocity(0, 0));
        w.Set(e, new Health(99));
        int captured = 0;
        w.Query<Position, Velocity>().Run((in Iter<Position, Velocity> it) =>
        {
            var h = it.OptionalField<Health>();
            if (h.Length > 0) captured = h[0].Value;
        });
        Assert.Equal(99, captured);
    }
}
