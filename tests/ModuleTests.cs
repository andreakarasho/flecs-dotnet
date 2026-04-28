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
}
