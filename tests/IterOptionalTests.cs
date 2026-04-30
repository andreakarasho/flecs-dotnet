using Xunit;
using System.Runtime.CompilerServices;

namespace Flecs.Tests;

// Optional<T> via Query.Optional<T>() — RowEnumerator yields NullRef<T> when
// a row's table lacks the optional column. Caller checks Unsafe.IsNullRef.
public class IterOptionalTests
{
    [Fact]
    public void Optional_Present_YieldsValue()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Velocity(7, 8));
        Velocity captured = default;
        foreach (var (_, v) in w.Query<Position, Velocity>().Optional<Velocity>())
        {
            if (!Unsafe.IsNullRef(ref v.Value)) captured = v.Value;
        }
        Assert.Equal(7, captured.Dx);
        Assert.Equal(8, captured.Dy);
    }

    [Fact]
    public void Optional_Absent_YieldsNullRef()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        bool sawNull = false;
        foreach (var (_, v) in w.Query<Position, Velocity>().Optional<Velocity>())
        {
            if (Unsafe.IsNullRef(ref v.Value)) sawNull = true;
        }
        Assert.True(sawNull);
    }

    [Fact]
    public void Optional_TwoArchetypes_OneHasOneNot()
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
        foreach (var (_, v) in w.Query<Position, Velocity>().Optional<Velocity>())
        {
            total++;
            if (!Unsafe.IsNullRef(ref v.Value)) withV++;
        }
        Assert.Equal(3, total);
        Assert.Equal(2, withV);
    }

    [Fact]
    public void Optional_MutationPersists()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Velocity(2, 3));
        foreach (var (_, v) in w.Query<Position, Velocity>().Optional<Velocity>())
        {
            if (!Unsafe.IsNullRef(ref v.Value))
            {
                v.Value.Dx *= 10f;
                v.Value.Dy *= 10f;
            }
        }
        Assert.Equal(20f, w.Get<Velocity>(e).Dx);
        Assert.Equal(30f, w.Get<Velocity>(e).Dy);
    }
}
