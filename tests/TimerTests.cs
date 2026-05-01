using Xunit;

namespace Flecs.Tests;

public class TimerTests
{
    [Fact]
    public void Timer_TicksAtPeriod()
    {
        var w = new World();
        var t = w.Timer(1.0f);
        // Below period — no tick.
        w.Progress(0.5f);
        Assert.False(w.Get<TickSource>(t).Tick);
        // Crosses period — tick.
        w.Progress(0.6f);
        Assert.True(w.Get<TickSource>(t).Tick);
        // Next frame, accumulator reset, no tick.
        w.Progress(0.1f);
        Assert.False(w.Get<TickSource>(t).Tick);
    }

    [Fact]
    public void Timer_GatesSystem()
    {
        var w = new World();
        var t = w.Timer(0.25f);
        int hits = 0;
        w.System("slow", w.OnUpdate, _ => hits++).SetTickSource(t);
        w.Progress(0.1f); // 0.1
        w.Progress(0.1f); // 0.2
        w.Progress(0.1f); // 0.3 → tick, hit
        w.Progress(0.1f); // 0.15
        w.Progress(0.1f); // 0.25 → tick, hit
        Assert.Equal(2, hits);
    }

    [Fact]
    public void Rate_DividesSourceByCount()
    {
        var w = new World();
        var t = w.Timer(0.1f);
        var r = w.Rate(t, 3);   // ticks every 3rd t-tick
        int hitsT = 0, hitsR = 0;
        w.System("fast", w.OnUpdate, _ => hitsT++).SetTickSource(t);
        w.System("slow", w.OnUpdate, _ => hitsR++).SetTickSource(r);
        for (int i = 0; i < 10; i++) w.Progress(0.1f);
        Assert.Equal(10, hitsT);
        Assert.Equal(3, hitsR);
    }

    [Fact]
    public void Rate_OnProgressEveryFrame()
    {
        var w = new World();
        // SourceId 0 → drive off Progress every frame.
        var r = w.Rate(default, 4);
        int hits = 0;
        w.System("every4", w.OnUpdate, _ => hits++).SetTickSource(r);
        for (int i = 0; i < 12; i++) w.Progress(0.016f);
        Assert.Equal(3, hits);
    }

    [Fact]
    public void System_NoTickSource_RunsEveryProgress()
    {
        var w = new World();
        int hits = 0;
        w.System("always", w.OnUpdate, _ => hits++);
        for (int i = 0; i < 5; i++) w.Progress(0f);
        Assert.Equal(5, hits);
    }

    [Fact]
    public void Timer_MultipleSystemsShareSource()
    {
        var w = new World();
        var t = w.Timer(0.5f);
        int aHits = 0, bHits = 0;
        w.System("a", w.OnUpdate, _ => aHits++).SetTickSource(t);
        w.System("b", w.OnUpdate, _ => bHits++).SetTickSource(t);
        for (int i = 0; i < 5; i++) w.Progress(0.5f);
        Assert.Equal(5, aHits);
        Assert.Equal(5, bHits);
    }

    [Fact]
    public void Timer_AccumulatesCarryover()
    {
        var w = new World();
        // Powers-of-two periods keep float math exact for the test.
        var t = w.Timer(0.25f);
        // 0.5 → tick, accumulator=0.25 carry → keeps ticking next frame.
        w.Progress(0.5f);
        Assert.True(w.Get<TickSource>(t).Tick);
        Assert.Equal(0.25f, w.Get<Timer>(t).Accumulated, 5);
    }

    [Fact]
    public void Rate_ChainedRateFilters()
    {
        var w = new World();
        var t = w.Timer(0.1f);
        var r1 = w.Rate(t, 2);   // every 2 t-ticks
        var r2 = w.Rate(r1, 3);  // every 3 r1-ticks = every 6 t-ticks
        int hits = 0;
        w.System("rare", w.OnUpdate, _ => hits++).SetTickSource(r2);
        for (int i = 0; i < 18; i++) w.Progress(0.1f);
        Assert.Equal(3, hits);
    }
}
