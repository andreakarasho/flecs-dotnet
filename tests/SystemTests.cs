using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class SystemTests
{
    [Fact]
    public void Progress_RunsRegisteredSystem()
    {
        var w = new World();
        int calls = 0;
        w.System("s", w.OnUpdate, _ => calls++);
        w.Progress(0.016f);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Progress_PhasesRunInOrder()
    {
        var w = new World();
        var order = new List<string>();
        w.System("c", w.OnStore, _ => order.Add("OnStore"));
        w.System("a", w.OnLoad, _ => order.Add("OnLoad"));
        w.System("b", w.OnUpdate, _ => order.Add("OnUpdate"));
        w.Progress(0);
        Assert.Equal(new[] { "OnLoad", "OnUpdate", "OnStore" }, order);
    }

    [Fact]
    public void Progress_RegistrationOrderWithinPhase()
    {
        var w = new World();
        var order = new List<string>();
        w.System("first", w.OnUpdate, _ => order.Add("first"));
        w.System("second", w.OnUpdate, _ => order.Add("second"));
        w.System("third", w.OnUpdate, _ => order.Add("third"));
        w.Progress(0);
        Assert.Equal(new[] { "first", "second", "third" }, order);
    }

    [Fact]
    public void System_DisabledSkipped()
    {
        var w = new World();
        int calls = 0;
        w.System("s", w.OnUpdate, _ => calls++).SetEnabled(false);
        w.Progress(0);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void System_DeltaTimePropagated()
    {
        var w = new World();
        float seen = 0;
        w.System("s", w.OnUpdate, it => seen = it.DeltaTime);
        w.Progress(0.123f);
        Assert.Equal(0.123f, seen);
    }

    [Fact]
    public void System_TypedQueryEachInvoked()
    {
        var w = new World();
        for (int i = 0; i < 3; i++)
        {
            var e = w.CreateEntity();
            w.Set(e, new Position(i, 0));
            w.Set(e, new Velocity(1, 0));
        }
        w.System<Position, Velocity>("integrate", w.OnUpdate, q =>
        {
            foreach (var (p, v) in q) p.Value.X += v.Value.Dx;
        });
        w.Progress(0);
        var sum = 0;
        foreach (var row in w.Query<Position>()) sum += (int)row.Component1.Value.X;
        Assert.Equal(0 + 1 + 2 + 3, sum);
    }
}
