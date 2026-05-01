using Xunit;

namespace Flecs.Tests;

// Custom pipeline filtering: user-tagged systems opt in/out of phase dispatch
// based on the active pipeline's With/Without id sets.
public class PipelineCustomTests
{
    public struct MenuScene { }
    public struct GameScene { }

    [Fact]
    public void DefaultPipeline_InvalidId_RunsAllSystems()
    {
        var w = new World();
        int hits = 0;
        w.System("a", w.OnUpdate, _ => hits++);
        w.Progress(0f);
        Assert.Equal(1, hits);
    }

    [Fact]
    public void CustomPipeline_WithTag_OnlyTaggedSystemsRun()
    {
        var w = new World();
        var menuTag = (Id)w.Tag<MenuScene>();
        int menuHits = 0, gameHits = 0;
        var sm = w.System("menu", w.OnUpdate, _ => menuHits++);
        var sg = w.System("game", w.OnUpdate, _ => gameHits++);
        w.Add(sm.Entity, menuTag);
        // sg untagged.
        var p = w.CreatePipeline().With(w.SystemTag).With<MenuScene>().Build();
        w.SetPipeline(p);
        w.Progress(0f);
        Assert.Equal(1, menuHits);
        Assert.Equal(0, gameHits);
    }

    [Fact]
    public void CustomPipeline_WithoutTag_ExcludesTaggedSystems()
    {
        var w = new World();
        var menuTag = (Id)w.Tag<MenuScene>();
        int menuHits = 0, gameHits = 0;
        var sm = w.System("menu", w.OnUpdate, _ => menuHits++);
        var sg = w.System("game", w.OnUpdate, _ => gameHits++);
        w.Add(sm.Entity, menuTag);
        var p = w.CreatePipeline().With(w.SystemTag).Without<MenuScene>().Build();
        w.SetPipeline(p);
        w.Progress(0f);
        Assert.Equal(0, menuHits);
        Assert.Equal(1, gameHits);
    }

    [Fact]
    public void SetPipeline_SwitchAtRuntime_ReflectsImmediately()
    {
        var w = new World();
        int menuHits = 0, gameHits = 0;
        var sm = w.System("m", w.OnUpdate, _ => menuHits++);
        var sg = w.System("g", w.OnUpdate, _ => gameHits++);
        w.Add(sm.Entity, (Id)w.Tag<MenuScene>());
        w.Add(sg.Entity, (Id)w.Tag<GameScene>());
        var menuP = w.CreatePipeline().With<MenuScene>().Build();
        var gameP = w.CreatePipeline().With<GameScene>().Build();
        w.SetPipeline(menuP);
        w.Progress(0f);
        Assert.Equal(1, menuHits);
        Assert.Equal(0, gameHits);
        w.SetPipeline(gameP);
        w.Progress(0f);
        Assert.Equal(1, menuHits);
        Assert.Equal(1, gameHits);
    }

    [Fact]
    public void SetPipeline_Default_RestoresAllSystems()
    {
        var w = new World();
        int menuHits = 0, gameHits = 0;
        var sm = w.System("m", w.OnUpdate, _ => menuHits++);
        var sg = w.System("g", w.OnUpdate, _ => gameHits++);
        w.Add(sm.Entity, (Id)w.Tag<MenuScene>());
        var p = w.CreatePipeline().Without<MenuScene>().Build();
        w.SetPipeline(p);
        w.Progress(0f);
        Assert.Equal(0, menuHits);
        Assert.Equal(1, gameHits);
        w.SetPipeline(default); // back to "all systems run"
        w.Progress(0f);
        Assert.Equal(1, menuHits);
        Assert.Equal(2, gameHits);
    }

    [Fact]
    public void Pipeline_HasReservedTag()
    {
        var w = new World();
        var p = w.CreatePipeline().Build();
        Assert.True(w.Has(p, (Id)w.Pipeline));
        Assert.True(w.Has<PipelineFilter>(p));
    }

    [Fact]
    public void System_BackingEntityHasSystemTag()
    {
        var w = new World();
        var s = w.System("x", w.OnUpdate, _ => { });
        Assert.True(s.Entity.IsValid);
        Assert.True(w.Has(s.Entity, (Id)w.SystemTag));
    }
}
