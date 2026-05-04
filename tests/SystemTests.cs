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
        w.System("s", w.Phases.OnUpdate, _ => calls++);
        w.Progress(0.016f);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Progress_PhasesRunInOrder()
    {
        var w = new World();
        var order = new List<string>();
        w.System("c", w.Phases.OnStore, _ => order.Add("OnStore"));
        w.System("a", w.Phases.OnLoad, _ => order.Add("OnLoad"));
        w.System("b", w.Phases.OnUpdate, _ => order.Add("OnUpdate"));
        w.Progress(0);
        Assert.Equal(new[] { "OnLoad", "OnUpdate", "OnStore" }, order);
    }

    [Fact]
    public void Progress_RegistrationOrderWithinPhase()
    {
        var w = new World();
        var order = new List<string>();
        w.System("first", w.Phases.OnUpdate, _ => order.Add("first"));
        w.System("second", w.Phases.OnUpdate, _ => order.Add("second"));
        w.System("third", w.Phases.OnUpdate, _ => order.Add("third"));
        w.Progress(0);
        Assert.Equal(new[] { "first", "second", "third" }, order);
    }

    [Fact]
    public void System_DisabledSkipped()
    {
        var w = new World();
        int calls = 0;
        w.System("s", w.Phases.OnUpdate, _ => calls++).SetEnabled(false);
        w.Progress(0);
        Assert.Equal(0, calls);
    }

    [Fact]
    public void System_DeltaTimePropagated()
    {
        var w = new World();
        float seen = 0;
        w.System("s", w.Phases.OnUpdate, it => seen = it.DeltaTime);
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
        w.System<Position, Velocity>("integrate", w.Phases.OnUpdate, q =>
        {
            foreach (var (p, v) in q) p.Value.X += v.Value.Dx;
        });
        w.Progress(0);
        var sum = 0;
        foreach (var row in w.Query<Position>()) sum += (int)row.Component1.Value.X;
        Assert.Equal(0 + 1 + 2 + 3, sum);
    }

    // ===== Ctx / re-enable / repeat =====

    [Fact]
    public void System_CtxAccessibleInsideBody()
    {
        var w = new World();
        var bag = new List<int>();
        var h = w.System("s", w.Phases.OnUpdate, it =>
        {
            var b = it.Ctx<List<int>>();
            b.Add(b.Count + 1);
        });
        h.SetCtx(bag);
        w.Progress(0); w.Progress(0); w.Progress(0);
        Assert.Equal(new[] { 1, 2, 3 }, bag);
    }

    [Fact]
    public void System_ReEnableAfterDisable()
    {
        var w = new World();
        int calls = 0;
        var h = w.System("s", w.Phases.OnUpdate, _ => calls++);
        h.SetEnabled(false);
        w.Progress(0);
        Assert.Equal(0, calls);
        h.SetEnabled(true);
        w.Progress(0);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void System_NoEntities_StillRuns()
    {
        // System body runs once per frame regardless of matched entities.
        var w = new World();
        int calls = 0;
        w.System("s", w.Phases.OnUpdate, _ => calls++);
        w.Progress(0);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void System_MutatesViaDefer()
    {
        // System body using Defer to add a tag — effects must apply after Progress.
        var w = new World();
        var ents = new List<EntityId>();
        for (int i = 0; i < 3; i++) { var e = w.CreateEntity(); w.Set(e, new Position(i, 0)); ents.Add(e); }
        w.System<Position>("s", w.Phases.OnUpdate, q =>
        {
            using (w.Defer())
            {
                foreach (var row in q) w.Add<TagA>(row.Entity);
            }
        });
        w.Progress(0);
        Assert.All(ents, e => Assert.True(w.Has<TagA>(e)));
    }

    [Fact]
    public void System_OnStartFiresOnceAcrossManyProgress()
    {
        var w = new World();
        int onStart = 0, onUpdate = 0;
        w.System("once", w.Phases.OnStart, _ => onStart++);
        w.System("each", w.Phases.OnUpdate, _ => onUpdate++);
        for (int i = 0; i < 5; i++) w.Progress(0);
        Assert.Equal(1, onStart);
        Assert.Equal(5, onUpdate);
    }

    [Fact]
    public void System_DeleteRemovesFromPipeline()
    {
        var w = new World();
        int calls = 0;
        var h = w.System("s", w.Phases.OnUpdate, _ => calls++);
        w.Progress(0);
        Assert.Equal(1, calls);
        w.Delete(h.Entity);
        w.Progress(0);
        // Deleting the system entity should drop it from future progress.
        Assert.Equal(1, calls);
    }
}
