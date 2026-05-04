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

    [Fact]
    public void SetSingleton_MultipleTypes_Independent()
    {
        var w = new World();
        w.SetSingleton(new Score(7));
        w.SetSingleton(new Health(3));
        Assert.Equal(7, w.GetSingleton<Score>().Value);
        Assert.Equal(3, w.GetSingleton<Health>().Value);
    }

    [Fact]
    public void RemoveSingleton_DoesNotAffectOther()
    {
        var w = new World();
        w.SetSingleton(new Score(1));
        w.SetSingleton(new Health(2));
        w.RemoveSingleton<Score>();
        Assert.False(w.HasSingleton<Score>());
        Assert.True(w.HasSingleton<Health>());
        Assert.Equal(2, w.GetSingleton<Health>().Value);
    }

    [Fact]
    public void Query_MatchesSingleton()
    {
        var w = new World();
        w.SetSingleton(new Score(99));
        int found = 0;
        foreach (var row in w.Query<Score>()) { found++; Assert.Equal(99, row.Component1.Value.Value); }
        Assert.Equal(1, found);
    }

    [Fact]
    public void SetSingleton_FiresOnSetObserver()
    {
        var w = new World();
        int onSet = 0;
        w.Observer<Score>(Event.OnSet, (EventIter _, ref Score s) => onSet++);
        w.SetSingleton(new Score(1));
        w.SetSingleton(new Score(2));
        Assert.Equal(2, onSet);
    }

    [Fact]
    public void HasSingleton_TrueAfterMutationViaRef()
    {
        var w = new World();
        w.SetSingleton(new Score(0));
        w.GetSingleton<Score>() = new Score(123);
        Assert.True(w.HasSingleton<Score>());
        Assert.Equal(123, w.GetSingleton<Score>().Value);
    }
}
