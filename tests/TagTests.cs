using Xunit;
using System;

namespace Flecs.Tests;

public class TagTests
{
    [Fact]
    public void Tag_RegistersWithoutComponentInfo()
    {
        var w = new World();
        int baselineComps = w.ComponentCount;
        var tagEnt = w.Tag<TagA>();
        Assert.True(tagEnt.IsValid);
        Assert.Equal(baselineComps, w.ComponentCount); // tags do NOT increment component count
    }

    [Fact]
    public void Add_TagOnEntity()
    {
        var w = new World();
        w.Tag<TagA>();
        var e = w.CreateEntity();
        w.Add<TagA>(e);
        Assert.True(w.Has<TagA>(e));
    }

    [Fact]
    public void Add_TagAutoRegistersIfNew()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Add<TagA>(e); // no prior Tag<TagA>() call
        Assert.True(w.Has<TagA>(e));
    }

    [Fact]
    public void Remove_Tag()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Add<TagA>(e);
        w.Remove<TagA>(e);
        Assert.False(w.Has<TagA>(e));
    }

    [Fact]
    public void Tag_RegisteringAsComponentLaterThrows()
    {
        var w = new World();
        w.Tag<TagA>();
        Assert.Throws<InvalidOperationException>(() => w.Component<TagA>());
    }

    [Fact]
    public void Component_RegisteringAsTagLaterThrows()
    {
        var w = new World();
        w.Component<Position>();
        Assert.Throws<InvalidOperationException>(() => w.Tag<Position>());
    }

    [Fact]
    public void MultipleTagsOnEntity()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Add<TagA>(e);
        w.Add<TagB>(e);
        w.Add<TagC>(e);
        Assert.True(w.Has<TagA>(e));
        Assert.True(w.Has<TagB>(e));
        Assert.True(w.Has<TagC>(e));
    }
}
