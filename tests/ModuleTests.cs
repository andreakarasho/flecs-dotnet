using Xunit;

namespace Flecs.Tests;

public class TestModule : IModule
{
    public int BuildCallCount;
    public void Build(World w)
    {
        BuildCallCount++;
        w.Component<Position>();
    }
}

// Distinct module type for idempotency tests; uses static counter.
public sealed class CountingModule : IModule
{
    public static int BuildCallCount;
    public void Build(World w)
    {
        BuildCallCount++;
        w.Component<Score>();
    }
}

public sealed class AnotherModule : IModule
{
    public static bool Built;
    public void Build(World w) { Built = true; w.Component<Mana>(); }
}

public class ModuleTests
{
    [Fact]
    public void Import_BuildsModule()
    {
        var w = new World();
        CountingModule.BuildCallCount = 0;
        w.Import<CountingModule>();
        Assert.Equal(1, CountingModule.BuildCallCount);
        // Component registered as side effect
        Assert.True(w.Component<Score>().IsValid);
    }

    [Fact]
    public void Import_Idempotent()
    {
        var w = new World();
        CountingModule.BuildCallCount = 0;
        w.Import<CountingModule>();
        w.Import<CountingModule>();
        w.Import<CountingModule>();
        Assert.Equal(1, CountingModule.BuildCallCount);
    }

    [Fact]
    public void Import_PerWorldScoping()
    {
        CountingModule.BuildCallCount = 0;
        var w1 = new World();
        var w2 = new World();
        w1.Import<CountingModule>();
        w2.Import<CountingModule>();
        Assert.Equal(2, CountingModule.BuildCallCount);
    }

    [Fact]
    public void Import_DistinctModulesIndependent()
    {
        var w = new World();
        AnotherModule.Built = false;
        w.Import<AnotherModule>();
        Assert.True(AnotherModule.Built);
    }

    // ===== Module scope behavior =====

    public sealed class SystemModule : IModule
    {
        public static SystemHandle? Sys;
        public static EntityId Tmr;
        public static EntityId Phs;
        public void Build(World w)
        {
            Sys = w.System("ModSys", w.Phases.OnUpdate, _ => { });
            Tmr = w.Timer(0.1f);
            Phs = w.CreatePhase("ModPhase");
        }
    }

    [Fact]
    public void Import_SystemInsideModule_ParentedToModule()
    {
        var w = new World();
        w.Import<SystemModule>();
        var modEnt = w.Lookup("SystemModule");
        Assert.True(modEnt.IsValid);
        Assert.Equal(modEnt.Id, w.GetParent(SystemModule.Sys!.Entity).Id);
        Assert.Equal(modEnt.Id, w.GetParent(SystemModule.Tmr).Id);
        Assert.Equal(modEnt.Id, w.GetParent(SystemModule.Phs).Id);
    }

    public sealed class NestedOuter : IModule
    {
        public static EntityId InnerEnt;
        public void Build(World w)
        {
            w.Import<NestedInner>();
            InnerEnt = w.Lookup("NestedInner");
        }
    }
    public sealed class NestedInner : IModule
    {
        public static EntityId LeafEnt;
        public void Build(World w)
        {
            LeafEnt = w.CreateEntity();
            w.SetName(LeafEnt, "Leaf");
        }
    }

    [Fact]
    public void Import_NestedModule_OuterParentsInner()
    {
        var w = new World();
        w.Import<NestedOuter>();
        var outerEnt = w.Lookup("NestedOuter");
        Assert.True(outerEnt.IsValid);
        Assert.True(NestedOuter.InnerEnt.IsValid);
        // NestedInner module entity must be parented to NestedOuter.
        Assert.Equal(outerEnt.Id, w.GetParent(NestedOuter.InnerEnt).Id);
        // Leaf inside inner is parented to inner.
        Assert.Equal(NestedOuter.InnerEnt.Id, w.GetParent(NestedInner.LeafEnt).Id);
    }

    [Fact]
    public void Import_LookupResolvesShortNameInsideBuild()
    {
        // Build runs with the module entity active as scope — short-name
        // lookups must resolve siblings created earlier in the same Build.
        var w = new World();
        w.Import<SiblingLookupModule>();
        Assert.True(SiblingLookupModule.LookedUp.IsValid);
        Assert.Equal(SiblingLookupModule.First.Id, SiblingLookupModule.LookedUp.Id);
    }

    public sealed class SiblingLookupModule : IModule
    {
        public static EntityId First;
        public static EntityId LookedUp;
        public void Build(World w)
        {
            First = w.CreateEntity();
            w.SetName(First, "First");
            LookedUp = w.Lookup("First");
        }
    }
}
