using Xunit;

namespace Flecs.Tests;

public class SystemCtxTests
{
    private sealed class GameState { public int Score; public string Name = ""; }

    [Fact]
    public void Ctx_ReadInsideBody()
    {
        var w = new World();
        var state = new GameState { Score = 42, Name = "boot" };
        var s = w.System("read-ctx", w.OnUpdate, it =>
        {
            var gs = it.Ctx<GameState>();
            gs.Score++;
        });
        s.Ctx = state;
        w.Progress(0f);
        w.Progress(0f);
        Assert.Equal(44, state.Score);
    }

    [Fact]
    public void SetCtx_FluentSetter()
    {
        var w = new World();
        var state = new GameState { Score = 10 };
        w.System("x", w.OnUpdate, it => it.Ctx<GameState>().Score++)
         .SetCtx(state);
        w.Progress(0f);
        Assert.Equal(11, state.Score);
    }

    [Fact]
    public void Iter_SystemPointsAtRunningHandle()
    {
        var w = new World();
        SystemHandle? seen = null;
        var s = w.System("self", w.OnUpdate, it => seen = it.System);
        w.Progress(0f);
        Assert.Same(s, seen);
    }

    [Fact]
    public void Ctx_NoCtx_Throws()
    {
        var w = new World();
        bool threw = false;
        w.System("nope", w.OnUpdate, it =>
        {
            try { _ = it.Ctx<GameState>(); }
            catch (System.InvalidOperationException) { threw = true; }
        });
        w.Progress(0f);
        Assert.True(threw);
    }

    [Fact]
    public void Ctx_PerSystem_DistinctValues()
    {
        var w = new World();
        var a = new GameState { Name = "A" };
        var b = new GameState { Name = "B" };
        string seenA = "", seenB = "";
        w.System("a", w.OnUpdate, it => seenA = it.Ctx<GameState>().Name).SetCtx(a);
        w.System("b", w.OnUpdate, it => seenB = it.Ctx<GameState>().Name).SetCtx(b);
        w.Progress(0f);
        Assert.Equal("A", seenA);
        Assert.Equal("B", seenB);
    }
}
