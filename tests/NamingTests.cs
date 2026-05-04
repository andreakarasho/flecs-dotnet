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

    // ===== Scope-aware lookup =====
    // Mirrors flecs C tests in test/core/src/Hierarchies.c — short names
    // resolve relative to active scope, walking up ancestor chain, falling
    // back to root.

    [Fact]
    public void Lookup_ShortNameResolvesInScope()
    {
        var w = new World();
        var scope = w.CreateEntity(); w.SetName(scope, "Scope");
        var child = w.CreateEntity(); w.SetName(child, "Child");
        w.SetParent(child, scope);
        Assert.False(w.Lookup("Child").IsValid);
        using (w.WithScope(scope))
        {
            Assert.Equal(child.Id, w.Lookup("Child").Id);
        }
        Assert.False(w.Lookup("Child").IsValid);
    }

    [Fact]
    public void Lookup_WalksUpScopeChainForSibling()
    {
        var w = new World();
        var scope = w.CreateEntity(); w.SetName(scope, "Scope");
        var childScope = w.CreateEntity(); w.SetName(childScope, "ChildScope");
        var sibling = w.CreateEntity(); w.SetName(sibling, "Sibling");
        w.SetParent(childScope, scope);
        w.SetParent(sibling, scope);
        using (w.WithScope(childScope))
        {
            // "Sibling" not a direct child of ChildScope; walk up to Scope.
            Assert.Equal(sibling.Id, w.Lookup("Sibling").Id);
        }
    }

    [Fact]
    public void Lookup_FallsBackToRoot()
    {
        var w = new World();
        var scope = w.CreateEntity(); w.SetName(scope, "Scope");
        var rootEnt = w.CreateEntity(); w.SetName(rootEnt, "RootEnt");
        using (w.WithScope(scope))
        {
            Assert.Equal(rootEnt.Id, w.Lookup("RootEnt").Id);
        }
    }

    [Fact]
    public void Lookup_LeadingDotForcesRoot()
    {
        var w = new World();
        var scope = w.CreateEntity(); w.SetName(scope, "Scope");
        var child = w.CreateEntity(); w.SetName(child, "Same");
        var rootEnt = w.CreateEntity(); w.SetName(rootEnt, "Same");
        w.SetParent(child, scope);
        using (w.WithScope(scope))
        {
            // No leading dot — resolves in scope.
            Assert.Equal(child.Id, w.Lookup("Same").Id);
            // Leading dot — forces root.
            Assert.Equal(rootEnt.Id, w.Lookup(".Same").Id);
        }
    }

    [Fact]
    public void Lookup_FullPathFromRootStillWorks()
    {
        var w = new World();
        var a = w.CreateEntity(); w.SetName(a, "a");
        var b = w.CreateEntity(); w.SetName(b, "b");
        w.SetParent(b, a);
        using (w.WithScope(a))
        {
            // Full path from root resolves even when scope is set.
            Assert.Equal(b.Id, w.Lookup("a.b").Id);
        }
    }

    [Fact]
    public void Lookup_NestedScopeResolvesNestedShortName()
    {
        var w = new World();
        var outer = w.CreateEntity(); w.SetName(outer, "Outer");
        var inner = w.CreateEntity(); w.SetName(inner, "Inner");
        var leaf = w.CreateEntity(); w.SetName(leaf, "Leaf");
        w.SetParent(inner, outer);
        w.SetParent(leaf, inner);
        using (w.WithScope(inner))
        {
            Assert.Equal(leaf.Id, w.Lookup("Leaf").Id);
        }
    }

    [Fact]
    public void Lookup_ScopeMissDoesNotShadowRootMiss()
    {
        var w = new World();
        var scope = w.CreateEntity(); w.SetName(scope, "Scope");
        using (w.WithScope(scope))
        {
            Assert.False(w.Lookup("Ghost").IsValid);
        }
    }

    [Fact]
    public void Lookup_EmptyAfterLeadingDotReturnsDefault()
    {
        var w = new World();
        Assert.False(w.Lookup(".").IsValid);
        Assert.False(w.Lookup("").IsValid);
    }

    // ===== GetPath =====

    [Fact]
    public void GetPath_SingleNamedEntity_ReturnsName()
    {
        var w = new World();
        var e = w.CreateEntity(); w.SetName(e, "Alpha");
        Assert.Equal("Alpha", w.GetPath(e));
    }

    [Fact]
    public void GetPath_NestedChain_ReturnsDottedPath()
    {
        var w = new World();
        var a = w.CreateEntity(); w.SetName(a, "Outer");
        var b = w.CreateEntity(); w.SetName(b, "Inner");
        var c = w.CreateEntity(); w.SetName(c, "Leaf");
        w.SetParent(b, a);
        w.SetParent(c, b);
        Assert.Equal("Outer.Inner.Leaf", w.GetPath(c));
    }

    [Fact]
    public void GetPath_RoundTripWithLookup()
    {
        var w = new World();
        var a = w.CreateEntity(); w.SetName(a, "x");
        var b = w.CreateEntity(); w.SetName(b, "y");
        w.SetParent(b, a);
        var path = w.GetPath(b);
        Assert.Equal("x.y", path);
        Assert.Equal(b.Id, w.Lookup(path!).Id);
    }

    [Fact]
    public void GetPath_UnnamedEntity_ReturnsNull()
    {
        var w = new World();
        var e = w.CreateEntity();
        Assert.Null(w.GetPath(e));
    }

    [Fact]
    public void GetPath_UnnamedAncestor_ReturnsNull()
    {
        var w = new World();
        var a = w.CreateEntity(); // unnamed
        var b = w.CreateEntity(); w.SetName(b, "leaf");
        w.SetParent(b, a);
        Assert.Null(w.GetPath(b));
    }

    [Fact]
    public void GetPath_DeadEntity_ReturnsNull()
    {
        var w = new World();
        var e = w.CreateEntity(); w.SetName(e, "x");
        w.Delete(e);
        Assert.Null(w.GetPath(e));
    }

    [Fact]
    public void GetPath_ScopedComponent_ResolvesViaPath()
    {
        var w = new World();
        var scope = w.CreateEntity(); w.SetName(scope, "Mod");
        EntityId compEnt;
        using (w.WithScope(scope))
        {
            compEnt = w.Component<Position>();
        }
        Assert.Equal($"Mod.{nameof(Position)}", w.GetPath(compEnt));
    }
}
