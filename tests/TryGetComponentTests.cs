using Xunit;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Flecs.Tests;

public class TryGetComponentTests
{
    [Fact]
    public void TryGetComponent_TrueAndCopiesValueWhenPresent()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(3, 4));
        Assert.True(w.TryGetComponent<Position>(e, out var p));
        Assert.Equal(3, p.X);
        Assert.Equal(4, p.Y);
    }

    [Fact]
    public void TryGetComponent_FalseWhenAbsent()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Component<Position>();
        Assert.False(w.TryGetComponent<Position>(e, out var p));
        Assert.Equal(default, p);
    }

    [Fact]
    public void TryGetComponent_FalseWhenUnregistered()
    {
        var w = new World();
        var e = w.CreateEntity();
        Assert.False(w.TryGetComponent<Position>(e, out _));
    }

    [Fact]
    public void TryGetComponent_FalseForDeadEntity()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Delete(e);
        Assert.False(w.TryGetComponent<Position>(e, out _));
    }

    [Fact]
    public void TryGetComponent_OptionalTermPatternInsideQueryEach()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(a, new Velocity(7, 8));
        w.Set(b, new Position(0, 0));
        // b lacks Velocity
        var dxByEnt = new Dictionary<uint, float?>();
        foreach (var row in w.Query<Position>())
        {
            var e = row.Entity;
            dxByEnt[e.Id] = w.TryGetComponent<Velocity>(e, out var v) ? v.Dx : (float?)null;
        }
        Assert.Equal(7f, dxByEnt[a.Id]);
        Assert.Null(dxByEnt[b.Id]);
    }

    [Fact]
    public void TryGetRef_PresentNotNullRef()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        ref var p = ref w.TryGetRef<Position>(e);
        Assert.False(Unsafe.IsNullRef(ref p));
        Assert.Equal(1, p.X);
    }

    [Fact]
    public void TryGetRef_AbsentNullRef()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Component<Position>();
        ref var p = ref w.TryGetRef<Position>(e);
        Assert.True(Unsafe.IsNullRef(ref p));
    }

    [Fact]
    public void TryGetRef_UnregisteredNullRef()
    {
        var w = new World();
        var e = w.CreateEntity();
        ref var p = ref w.TryGetRef<Position>(e);
        Assert.True(Unsafe.IsNullRef(ref p));
    }

    [Fact]
    public void TryGetRef_MutationPersistsToRealSlot()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        ref var p = ref w.TryGetRef<Position>(e);
        p.X = 99;
        p.Y = 88;
        Assert.Equal(99, w.Get<Position>(e).X);
        Assert.Equal(88, w.Get<Position>(e).Y);
    }

    [Fact]
    public void TryGetRef_OptionalMutationInsideQueryEach()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(a, new Velocity(2, 0));
        w.Set(b, new Position(0, 0));
        foreach (var row in w.Query<Position>())
        {
            ref var v = ref w.TryGetRef<Velocity>(row.Entity);
            if (!Unsafe.IsNullRef(ref v)) v.Dx *= 10f;
        }
        Assert.Equal(20f, w.Get<Velocity>(a).Dx);
        Assert.False(w.Has<Velocity>(b));
    }

    [Fact]
    public void TryGetRef_DeadEntityNullRef()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Delete(e);
        ref var p = ref w.TryGetRef<Position>(e);
        Assert.True(Unsafe.IsNullRef(ref p));
    }
}
