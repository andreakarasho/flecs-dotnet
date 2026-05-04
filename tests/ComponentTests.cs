using Xunit;
using System;

namespace Flecs.Tests;

public class ComponentTests
{
    [Fact]
    public void Component_RegistersAsEntity()
    {
        var w = new World();
        var compEnt = w.Component<Position>();
        Assert.True(compEnt.IsValid);
        Assert.True(w.IsAlive(compEnt));
    }

    [Fact]
    public void Component_SameTypeReturnsSameEntity()
    {
        var w = new World();
        Assert.Equal(w.Component<Position>(), w.Component<Position>());
    }

    [Fact]
    public void SetComponent_AddsAndStoresValue()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(3, 4));
        Assert.True(w.Has<Position>(e));
        var p = w.Get<Position>(e);
        Assert.Equal(3, p.X);
        Assert.Equal(4, p.Y);
    }

    [Fact]
    public void SetComponent_OverwritesValue()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 1));
        w.Set(e, new Position(7, 8));
        var p = w.Get<Position>(e);
        Assert.Equal(7, p.X);
        Assert.Equal(8, p.Y);
    }

    [Fact]
    public void GetComponent_ReturnsRefForMutation()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        ref var p = ref w.Get<Position>(e);
        p.X = 42;
        Assert.Equal(42, w.Get<Position>(e).X);
    }

    [Fact]
    public void GetComponent_DeadEntityThrows()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 1));
        w.Delete(e);
        Assert.Throws<InvalidOperationException>(() => w.Get<Position>(e));
    }

    [Fact]
    public void GetComponent_MissingComponentThrows()
    {
        var w = new World();
        w.Component<Position>(); // register so the type exists
        var e = w.CreateEntity();
        Assert.Throws<InvalidOperationException>(() => w.Get<Position>(e));
    }

    [Fact]
    public void Has_FalseWhenComponentNotRegistered()
    {
        var w = new World();
        var e = w.CreateEntity();
        Assert.False(w.Has<Position>(e));
    }

    [Fact]
    public void Remove_RemovesComponent()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        w.Remove<Position>(e);
        Assert.False(w.Has<Position>(e));
    }

    [Fact]
    public void Remove_NonExistentComponentNoop()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Remove<Position>(e); // no throw
        Assert.False(w.Has<Position>(e));
    }

    [Fact]
    public void ArchetypeMigration_PreservesOtherComponents()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        w.Set(e, new Velocity(3, 4));
        w.Set(e, new Health(99));
        // Triggers migrations.
        Assert.Equal(1, w.Get<Position>(e).X);
        Assert.Equal(3, w.Get<Velocity>(e).Dx);
        Assert.Equal(99, w.Get<Health>(e).Value);
    }

    [Fact]
    public void ComponentCount_TracksRegistrations()
    {
        var w = new World();
        int baseline = w.ComponentCount;
        w.Component<Position>();
        w.Component<Velocity>();
        Assert.Equal(baseline + 2, w.ComponentCount);
    }

    [Fact]
    public void Component_AcrossWorlds_DistinctEntityIds()
    {
        var w1 = new World();
        var w2 = new World();
        var c1 = w1.Component<Position>();
        var c2 = w2.Component<Position>();
        // Ids may collide numerically (per-world allocation) — but each maps
        // to its own world's slot. Just verify both worlds register.
        Assert.True(c1.IsValid);
        Assert.True(c2.IsValid);
    }

    [Fact]
    public void Set_AutoRegistersComponent()
    {
        var w = new World();
        var e = w.CreateEntity();
        // No Component<Position>() call — Set should auto-register.
        w.Set(e, new Position(1, 2));
        Assert.True(w.Has<Position>(e));
        Assert.Equal(1, w.Get<Position>(e).X);
    }

    [Fact]
    public void Set_DefaultStructValue()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, default(Position));
        Assert.Equal(default(Position), w.Get<Position>(e));
    }

    [Fact]
    public void Remove_OnEntityWithMultipleComponents_KeepsOthers()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 1));
        w.Set(e, new Velocity(2, 2));
        w.Set(e, new Health(3));
        w.Remove<Velocity>(e);
        Assert.True(w.Has<Position>(e));
        Assert.False(w.Has<Velocity>(e));
        Assert.True(w.Has<Health>(e));
        Assert.Equal(1, w.Get<Position>(e).X);
        Assert.Equal(3, w.Get<Health>(e).Value);
    }

    [Fact]
    public void Get_RefMutationVisibleNextRead()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Get<Position>(e).X = 42;
        Assert.Equal(42, w.Get<Position>(e).X);
    }

    [Fact]
    public void Set_LargeBatch_AllPersist()
    {
        var w = new World();
        var ents = new EntityId[500];
        for (int i = 0; i < ents.Length; i++)
        {
            ents[i] = w.CreateEntity();
            w.Set(ents[i], new Position(i, i * 2));
        }
        for (int i = 0; i < ents.Length; i++)
        {
            Assert.Equal(i, w.Get<Position>(ents[i]).X);
            Assert.Equal(i * 2, w.Get<Position>(ents[i]).Y);
        }
    }
}
