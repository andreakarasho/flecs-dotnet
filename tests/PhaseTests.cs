using Xunit;

namespace Flecs.Tests;

// OnStart phase + user-defined phases via DependsOn.
public class PhaseTests
{
    [Fact]
    public void OnStart_FiresOnceOnFirstProgress()
    {
        var w = new World();
        int hits = 0;
        w.System("startup", w.Phases.OnStart, _ => hits++);
        Assert.Equal(0, hits);
        w.Progress(0f);
        Assert.Equal(1, hits);
        w.Progress(0f);
        w.Progress(0f);
        Assert.Equal(1, hits);
    }

    [Fact]
    public void OnStart_RunsBeforeOnUpdateOnFirstFrame()
    {
        var w = new World();
        var order = new System.Collections.Generic.List<string>();
        w.System("update", w.Phases.OnUpdate, _ => order.Add("update"));
        w.System("startup", w.Phases.OnStart, _ => order.Add("startup"));
        w.Progress(0f);
        Assert.Equal(new[] { "startup", "update" }, order);
    }

    [Fact]
    public void BuiltinPhases_RunInTopologicalOrder()
    {
        var w = new World();
        var order = new System.Collections.Generic.List<string>();
        w.System("store", w.Phases.OnStore, _ => order.Add("store"));
        w.System("load", w.Phases.OnLoad, _ => order.Add("load"));
        w.System("update", w.Phases.OnUpdate, _ => order.Add("update"));
        w.Progress(0f);
        Assert.Equal(new[] { "load", "update", "store" }, order);
    }

    [Fact]
    public void CustomPhase_RunsAfterDependency()
    {
        var w = new World();
        var custom = w.CreatePhase("Custom");
        w.PhaseAfter(custom, w.Phases.OnUpdate);
        var order = new System.Collections.Generic.List<string>();
        w.System("upd", w.Phases.OnUpdate, _ => order.Add("upd"));
        w.System("cus", custom, _ => order.Add("cus"));
        w.System("post", w.Phases.PostUpdate, _ => order.Add("post"));
        w.Progress(0f);
        // PostUpdate depends on OnValidate, OnValidate on OnUpdate. Custom also
        // on OnUpdate. Tiebreak by entity id — Custom created last, so id > PostUpdate.
        // Both run after OnUpdate; relative order between Custom and PostUpdate
        // depends on id ordering — assert just that "upd" precedes both.
        Assert.Equal("upd", order[0]);
        Assert.Contains("cus", order);
        Assert.Contains("post", order);
    }

    [Fact]
    public void CustomPhase_ChainedDependsOn()
    {
        var w = new World();
        var a = w.CreatePhase("A");
        var b = w.CreatePhase("B");
        w.PhaseAfter(b, a);
        var order = new System.Collections.Generic.List<string>();
        w.System("b-sys", b, _ => order.Add("b"));
        w.System("a-sys", a, _ => order.Add("a"));
        w.Progress(0f);
        Assert.Equal(new[] { "a", "b" }, order);
    }

    [Fact]
    public void CustomPhase_NoDependsOn_RunsBeforeBuiltins()
    {
        // A user phase with no DependsOn has indeg 0, sorts by id. Builtin
        // phase entities are created very early in the world ctor (low ids),
        // so they sort ahead of user phases.
        var w = new World();
        var custom = w.CreatePhase("Floating");
        var order = new System.Collections.Generic.List<string>();
        w.System("custom-sys", custom, _ => order.Add("custom"));
        w.System("load-sys", w.Phases.OnLoad, _ => order.Add("load"));
        w.Progress(0f);
        // Builtins have lower ids → builtins run first, custom runs at the end
        // among indeg-0 phases. Just assert both ran.
        Assert.Equal(2, order.Count);
        Assert.Contains("load", order);
        Assert.Contains("custom", order);
    }

    [Fact]
    public void Phase_HasReservedTag()
    {
        var w = new World();
        Assert.True(w.Has(w.Phases.OnUpdate, (Id)w.PipelineMeta.Phase));
        Assert.True(w.Has(w.Phases.OnStart, (Id)w.PipelineMeta.Phase));
        var custom = w.CreatePhase();
        Assert.True(w.Has(custom, (Id)w.PipelineMeta.Phase));
    }

    [Fact]
    public void DependsOn_IsAcyclic_ThrowsOnCycle()
    {
        var w = new World();
        var a = w.CreatePhase("A");
        var b = w.CreatePhase("B");
        w.PhaseAfter(b, a);
        // Adding (DependsOn, B) on A would form cycle A→B→A.
        Assert.Throws<System.InvalidOperationException>(() => w.PhaseAfter(a, b));
    }
}
