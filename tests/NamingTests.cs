using Xunit;

namespace Flecs.Tests;

public class NamingTests
{
    [Fact]
    public void SetName_StoresName()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.SetName(e, "alice");
        Assert.Equal("alice", w.GetName(e));
    }

    [Fact]
    public void GetName_NullWhenUnnamed()
    {
        var w = new World();
        var e = w.CreateEntity();
        Assert.Null(w.GetName(e));
    }

    [Fact]
    public void Lookup_FindsTopLevelByName()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.SetName(e, "alice");
        Assert.Equal(e.Id, w.Lookup("alice").Id);
    }

    [Fact]
    public void Lookup_ReturnsDefaultForUnknown()
    {
        var w = new World();
        Assert.False(w.Lookup("ghost").IsValid);
    }

    [Fact]
    public void Lookup_PathTraversesChildOf()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        w.SetName(a, "a");
        w.SetName(b, "b");
        w.SetName(c, "c");
        w.SetParent(b, a);
        w.SetParent(c, b);
        Assert.Equal(c.Id, w.Lookup("a.b.c").Id);
    }

    [Fact]
    public void Lookup_PathFailsOnWrongParent()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var c = w.CreateEntity();
        w.SetName(a, "a");
        w.SetName(b, "b");
        w.SetName(c, "c");
        w.SetParent(c, b); // c child of b, not a
        Assert.False(w.Lookup("a.c").IsValid);
    }

    [Fact]
    public void Lookup_NamedNonRootNotMatchedAtRoot()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.SetName(a, "a");
        w.SetName(b, "b");
        w.SetParent(b, a);
        // "b" alone is not a root-level entity.
        Assert.False(w.Lookup("b").IsValid);
    }

    [Fact]
    public void SetName_OverwritesPrior()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.SetName(e, "first");
        w.SetName(e, "second");
        Assert.Equal("second", w.GetName(e));
    }
}
