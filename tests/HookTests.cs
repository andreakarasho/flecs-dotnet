using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class HookTests
{
    [Fact]
    public void Hook_OnAdd_FiresOnFirstAdd()
    {
        var w = new World();
        int calls = 0;
        w.Hooks<Position>().OnAdd = (World W, EntityId e, ref Position _) => calls++;
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Hook_OnAdd_DoesNotFireOnSubsequentSet()
    {
        var w = new World();
        int calls = 0;
        w.Hooks<Position>().OnAdd = (World W, EntityId e, ref Position _) => calls++;
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Position(1, 1));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Hook_OnSet_FiresEverySet()
    {
        var w = new World();
        int calls = 0;
        w.Hooks<Position>().OnSet = (World W, EntityId e, ref Position _) => calls++;
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Position(1, 1));
        w.Set(e, new Position(2, 2));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Hook_OnRemove_FiresOnRemove()
    {
        var w = new World();
        int calls = 0;
        w.Hooks<Position>().OnRemove = (World W, EntityId e, ref Position _) => calls++;
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Remove<Position>(e);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Hook_OnRemove_FiresOnDelete()
    {
        var w = new World();
        int calls = 0;
        w.Hooks<Position>().OnRemove = (World W, EntityId e, ref Position _) => calls++;
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Delete(e);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Hook_Ctor_RunsBeforeOnAdd()
    {
        var w = new World();
        var order = new List<string>();
        w.Hooks<Position>().Ctor = (World W, EntityId e, ref Position p) => { order.Add("ctor"); p.X = -1; };
        w.Hooks<Position>().OnAdd = (World W, EntityId e, ref Position p) => order.Add($"onadd:{p.X}");
        var e = w.CreateEntity();
        w.Set(e, new Position(5, 5));
        Assert.Equal(new[] { "ctor", "onadd:-1", }, order.GetRange(0, 2));
    }

    [Fact]
    public void Hook_Dtor_FiresAfterOnRemove()
    {
        var w = new World();
        var order = new List<string>();
        w.Hooks<Position>().OnRemove = (World W, EntityId e, ref Position _) => order.Add("onremove");
        w.Hooks<Position>().Dtor = (World W, EntityId e, ref Position _) => order.Add("dtor");
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Remove<Position>(e);
        Assert.Equal(new[] { "onremove", "dtor" }, order);
    }

    [Fact]
    public void Hook_OnSet_SeesNewValue()
    {
        var w = new World();
        float observed = 0;
        w.Hooks<Position>().OnSet = (World W, EntityId e, ref Position p) => observed = p.X;
        var e = w.CreateEntity();
        w.Set(e, new Position(42, 0));
        Assert.Equal(42, observed);
    }

    [Fact]
    public void Hook_OnRemove_SeesValueBeforeRemoval()
    {
        var w = new World();
        float observed = 0;
        w.Hooks<Position>().OnRemove = (World W, EntityId e, ref Position p) => observed = p.X;
        var e = w.CreateEntity();
        w.Set(e, new Position(7, 0));
        w.Remove<Position>(e);
        Assert.Equal(7, observed);
    }
}
