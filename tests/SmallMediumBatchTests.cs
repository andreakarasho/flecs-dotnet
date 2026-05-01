using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

// ===== Toggle / SetEnabled =====
public class ToggleTests
{
    [Fact]
    public void Toggle_FlipsOnAndOff()
    {
        var w = new World();
        w.Tag<TagA>();
        var e = w.CreateEntity();
        Assert.False(w.Has<TagA>(e));
        w.Toggle<TagA>(e);
        Assert.True(w.Has<TagA>(e));
        w.Toggle<TagA>(e);
        Assert.False(w.Has<TagA>(e));
    }

    [Fact]
    public void SetEnabled_TrueAddsIfMissing()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.SetEnabled<TagA>(e, true);
        Assert.True(w.Has<TagA>(e));
        // Idempotent.
        w.SetEnabled<TagA>(e, true);
        Assert.True(w.Has<TagA>(e));
    }

    [Fact]
    public void SetEnabled_FalseRemovesIfPresent()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Add<TagA>(e);
        w.SetEnabled<TagA>(e, false);
        Assert.False(w.Has<TagA>(e));
        // Idempotent.
        w.SetEnabled<TagA>(e, false);
        Assert.False(w.Has<TagA>(e));
    }
}

// ===== Copy/Move hook wiring =====
public class CopyMoveHookTests
{
    [Fact]
    public void MoveHook_FiresDuringArchetypeMigration()
    {
        var w = new World();
        int moves = 0;
        w.Hooks<Position>().SetMove((World W, EntityId e, ref Position src, ref Position dst) =>
        {
            moves++;
            dst = src;
            src = default;
        });
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        // Cause an archetype migration: Add a Velocity, forces move.
        w.Set(e, new Velocity(0, 0));
        Assert.True(moves > 0);
        Assert.Equal(1, w.Get<Position>(e).X);
    }

    [Fact]
    public void CopyHook_FiresDuringClone()
    {
        var w = new World();
        int copies = 0;
        w.Hooks<Position>().SetCopy((World W, EntityId e, ref Position src, ref Position dst) =>
        {
            copies++;
            dst = src;
        });
        var src = w.CreateEntity();
        w.Set(src, new Position(7, 8));
        var dst = w.Clone(src);
        Assert.True(copies > 0);
        Assert.Equal(7, w.Get<Position>(dst).X);
    }

    [Fact]
    public void NoMoveHook_PlainCopySemantics()
    {
        // Default behavior unchanged when no hook set.
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(3, 4));
        w.Set(e, new Velocity(0, 0));
        Assert.Equal(3, w.Get<Position>(e).X);
    }
}

// ===== Term sources via *InheritedVia =====
public class InheritedViaTests
{
    [Fact]
    public void HasInheritedVia_ChildOfChain()
    {
        var w = new World();
        var grand = w.CreateEntity();
        var parent = w.CreateEntity();
        var child = w.CreateEntity();
        w.Set(grand, new Position(11, 22));
        w.SetParent(parent, grand);
        w.SetParent(child, parent);
        // Walk via ChildOf.
        Assert.True(w.HasInheritedVia<Position>(child, w.ChildOf));
    }

    [Fact]
    public void GetInheritedVia_ReturnsAncestorRef()
    {
        var w = new World();
        var grand = w.CreateEntity();
        var parent = w.CreateEntity();
        var child = w.CreateEntity();
        w.Set(grand, new Position(7, 8));
        w.SetParent(parent, grand);
        w.SetParent(child, parent);
        ref var p = ref w.GetInheritedVia<Position>(child, w.ChildOf);
        Assert.Equal(7, p.X);
    }

    [Fact]
    public void GetInheritedVia_RefMutatesShared()
    {
        var w = new World();
        var grand = w.CreateEntity();
        var child = w.CreateEntity();
        w.Set(grand, new Position(0, 0));
        w.SetParent(child, grand);
        ref var p = ref w.GetInheritedVia<Position>(child, w.ChildOf);
        p.X = 99;
        Assert.Equal(99, w.Get<Position>(grand).X);
    }

    [Fact]
    public void TryGetInheritedVia_FalseWhenNoChain()
    {
        var w = new World();
        var lone = w.CreateEntity();
        w.Component<Position>();
        Assert.False(w.TryGetInheritedVia<Position>(lone, w.ChildOf, out var _));
    }

    [Fact]
    public void HasInheritedVia_CustomRelation()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(1, 1));
        var inst = w.CreateEntity();
        w.Add(inst, rel, prefab);
        Assert.True(w.HasInheritedVia<Position>(inst, rel));
    }
}

// ===== Module-scoped namespacing =====
public class ScopedComponent { } // unused — placeholder

public sealed class ScopedModule : IModule
{
    public struct ModPosition { public float X, Y; }
    public struct ModTag { }
    public void Build(World w)
    {
        w.Component<ModPosition>();
        w.Tag<ModTag>();
    }
}

public class ModuleScopingTests
{
    [Fact]
    public void Import_ModuleEntityCreatedWithName()
    {
        var w = new World();
        w.Import<ScopedModule>();
        var modEnt = w.Lookup("ScopedModule");
        Assert.True(modEnt.IsValid);
    }

    [Fact]
    public void Import_RegisteredComponentScopedAsChild()
    {
        var w = new World();
        w.Import<ScopedModule>();
        var modEnt = w.Lookup("ScopedModule");
        // Component entity is a child of the module entity.
        var posComp = w.Component<ScopedModule.ModPosition>();
        Assert.Equal(modEnt.Id, w.GetParent(posComp).Id);
    }

    [Fact]
    public void Import_PathLookupResolvesScopedComponent()
    {
        var w = new World();
        w.Import<ScopedModule>();
        var byPath = w.Lookup("ScopedModule.ModPosition");
        var byType = w.Component<ScopedModule.ModPosition>();
        Assert.Equal(byType.Id, byPath.Id);
    }

    [Fact]
    public void WithScope_ManualScopeApplies()
    {
        var w = new World();
        var scope = w.CreateEntity();
        w.SetName(scope, "Foo");
        EntityId child;
        using (w.WithScope(scope))
        {
            child = w.CreateEntity();
        }
        Assert.Equal(scope.Id, w.GetParent(child).Id);
    }

    [Fact]
    public void WithScope_RestoresPriorAfterDispose()
    {
        var w = new World();
        var s1 = w.CreateEntity();
        var s2 = w.CreateEntity();
        using (w.WithScope(s1))
        {
            using (w.WithScope(s2))
            {
                Assert.Equal(s2.Id, w.CurrentScope.Id);
            }
            Assert.Equal(s1.Id, w.CurrentScope.Id);
        }
        Assert.False(w.CurrentScope.IsValid);
    }

    [Fact]
    public void DefaultWorld_NoAutoNamingForComponents()
    {
        // Default behavior preserved: no scope set → no auto-name +
        // no ChildOf added when registering components.
        var w = new World();
        var e = w.Component<Position>();
        Assert.Null(w.GetName(e));
        Assert.False(w.GetParent(e).IsValid);
    }
}
