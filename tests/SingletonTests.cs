using Xunit;

namespace Flecs.Tests;

public class SingletonTests
{
    [Fact]
    public void HasSingleton_FalseInitially()
    {
        var w = new World();
        Assert.False(w.HasSingleton<Score>());
    }

    [Fact]
    public void SetSingleton_StoresValue()
    {
        var w = new World();
        w.SetSingleton(new Score(42));
        Assert.True(w.HasSingleton<Score>());
        Assert.Equal(42, w.GetSingleton<Score>().Value);
    }

    [Fact]
    public void SetSingleton_OverwritesValue()
    {
        var w = new World();
        w.SetSingleton(new Score(1));
        w.SetSingleton(new Score(2));
        Assert.Equal(2, w.GetSingleton<Score>().Value);
    }

    [Fact]
    public void GetSingleton_RefAllowsMutation()
    {
        var w = new World();
        w.SetSingleton(new Score(0));
        ref var s = ref w.GetSingleton<Score>();
        s = new Score(99);
        Assert.Equal(99, w.GetSingleton<Score>().Value);
    }

    [Fact]
    public void RemoveSingleton_Clears()
    {
        var w = new World();
        w.SetSingleton(new Score(1));
        w.RemoveSingleton<Score>();
        Assert.False(w.HasSingleton<Score>());
    }

    [Fact]
    public void RemoveSingleton_UnregisteredTypeNoop()
    {
        var w = new World();
        w.RemoveSingleton<Score>(); // no throw
        Assert.False(w.HasSingleton<Score>());
    }
}
