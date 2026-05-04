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
        Assert.True(w.HasInheritedVia<Position>(child, w.Relations.ChildOf));
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
        ref var p = ref w.GetInheritedVia<Position>(child, w.Relations.ChildOf);
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
        ref var p = ref w.GetInheritedVia<Position>(child, w.Relations.ChildOf);
        p.X = 99;
        Assert.Equal(99, w.Get<Position>(grand).X);
    }

    [Fact]
    public void TryGetInheritedVia_FalseWhenNoChain()
    {
        var w = new World();
        var lone = w.CreateEntity();
        w.Component<Position>();
        Assert.False(w.TryGetInheritedVia<Position>(lone, w.Relations.ChildOf, out var _));
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

    [Fact]
    public void WithScope_SystemParentedToScope()
    {
        var w = new World();
        var scope = w.CreateEntity();
        SystemHandle h;
        using (w.WithScope(scope))
        {
            h = w.System("scoped_sys", w.OnUpdate, _ => { });
        }
        Assert.Equal(scope.Id, w.GetParent(h.Entity).Id);
    }

    [Fact]
    public void WithScope_TimerParentedToScope()
    {
        var w = new World();
        var scope = w.CreateEntity();
        EntityId t;
        using (w.WithScope(scope))
        {
            t = w.Timer(0.5f);
        }
        Assert.Equal(scope.Id, w.GetParent(t).Id);
    }

    [Fact]
    public void WithScope_RateParentedToScope()
    {
        var w = new World();
        var scope = w.CreateEntity();
        var src = w.Timer(0.1f);
        EntityId r;
        using (w.WithScope(scope))
        {
            r = w.Rate(src, 2);
        }
        Assert.Equal(scope.Id, w.GetParent(r).Id);
    }

    [Fact]
    public void WithScope_CreatePhaseParentedToScope()
    {
        var w = new World();
        var scope = w.CreateEntity();
        EntityId p;
        using (w.WithScope(scope))
        {
            p = w.CreatePhase("scoped_phase");
        }
        Assert.Equal(scope.Id, w.GetParent(p).Id);
    }

    [Fact]
    public void NoScope_SystemHasNoParent()
    {
        var w = new World();
        var h = w.System("unscoped_sys", w.OnUpdate, _ => { });
        Assert.False(w.GetParent(h.Entity).IsValid);
    }

    [Fact]
    public void NoScope_TimerHasNoParent()
    {
        var w = new World();
        var t = w.Timer(0.5f);
        Assert.False(w.GetParent(t).IsValid);
    }

    // ===== Components / tags / pipelines / events inside WithScope =====
    // Mirrors flecs C: every entity-creation op while a scope is active
    // gets (ChildOf, scope) added. Components and tags additionally get
    // auto-named so path lookup ("Scope.TypeName") resolves them.

    public struct ScopedComp { public int V; }
    public struct ScopedTag { }

    [Fact]
    public void WithScope_ComponentParentedAndNamed()
    {
        var w = new World();
        var scope = w.CreateEntity();
        w.SetName(scope, "ManualScope");
        EntityId compEnt;
        using (w.WithScope(scope))
        {
            compEnt = w.Component<ScopedComp>();
        }
        Assert.Equal(scope.Id, w.GetParent(compEnt).Id);
        Assert.Equal(nameof(ScopedComp), w.GetName(compEnt));
        // Path lookup resolves component by Scope.Type.
        Assert.Equal(compEnt.Id, w.Lookup($"ManualScope.{nameof(ScopedComp)}").Id);
    }

    [Fact]
    public void WithScope_TagParentedAndNamed()
    {
        var w = new World();
        var scope = w.CreateEntity();
        w.SetName(scope, "TagsHere");
        EntityId tagEnt;
        using (w.WithScope(scope))
        {
            tagEnt = w.Tag<ScopedTag>();
        }
        Assert.Equal(scope.Id, w.GetParent(tagEnt).Id);
        Assert.Equal(nameof(ScopedTag), w.GetName(tagEnt));
    }

    [Fact]
    public void WithScope_PipelineParentedToScope()
    {
        var w = new World();
        var scope = w.CreateEntity();
        EntityId p;
        using (w.WithScope(scope))
        {
            p = w.CreatePipeline().With(w.PipelineMeta.SystemTag).Build();
        }
        Assert.Equal(scope.Id, w.GetParent(p).Id);
    }

    [Fact]
    public void WithScope_PlainEntityParentedToScope()
    {
        var w = new World();
        var scope = w.CreateEntity();
        EntityId e;
        using (w.WithScope(scope))
        {
            e = w.CreateEntity();
        }
        Assert.Equal(scope.Id, w.GetParent(e).Id);
    }

    [Fact]
    public void WithScope_NamedEntityParentedAndLookable()
    {
        var w = new World();
        var scope = w.CreateEntity();
        w.SetName(scope, "Outer");
        EntityId leaf;
        using (w.WithScope(scope))
        {
            leaf = w.Entity("Leaf").Id;
        }
        Assert.Equal(scope.Id, w.GetParent(leaf).Id);
        Assert.Equal(leaf.Id, w.Lookup("Outer.Leaf").Id);
    }

    public struct ScopedEvt { }

    [Fact]
    public void WithScope_CustomEventTypeRegisteredUnderScope()
    {
        // Observer<TEvent>(...) auto-registers TEvent as a tag-style entity.
        // While scope is active, that registration is parented + named.
        var w = new World();
        var scope = w.CreateEntity();
        w.SetName(scope, "EvtScope");
        using (w.WithScope(scope))
        {
            w.Observer<ScopedEvt>(_ => { });
        }
        // Path lookup resolves the registered event type as Scope.Type.
        var found = w.Lookup($"EvtScope.{nameof(ScopedEvt)}");
        Assert.True(found.IsValid);
    }

    [Fact]
    public void NestedWithScope_InnerEntityParentedToInnerScope()
    {
        var w = new World();
        var outer = w.CreateEntity();
        var inner = w.CreateEntity();
        w.SetParent(inner, outer);
        EntityId leaf;
        using (w.WithScope(outer))
        using (w.WithScope(inner))
        {
            leaf = w.CreateEntity();
        }
        // Innermost scope wins.
        Assert.Equal(inner.Id, w.GetParent(leaf).Id);
    }

    [Fact]
    public void NoScope_ComponentRemainsAtRoot()
    {
        var w = new World();
        var compEnt = w.Component<ScopedComp>();
        Assert.False(w.GetParent(compEnt).IsValid);
        Assert.Null(w.GetName(compEnt));
    }

    public struct ScopedComp2 { public int V; }
    public struct ScopedTag2 { }

    [Fact]
    public void DeleteScope_CascadesToAllChildren()
    {
        // Delete(scope) should tear down everything created inside it via the
        // ChildOf OnDeleteTarget=Delete policy. Mirrors flecs C cleanup of
        // a module entity.
        var w = new World();
        var scope = w.CreateEntity();
        SystemHandle sys; EntityId tmr; EntityId phase; EntityId entity; EntityId compEnt;
        using (w.WithScope(scope))
        {
            sys = w.System("s", w.Phases.OnUpdate, _ => { });
            tmr = w.Timer(0.5f);
            phase = w.CreatePhase("p");
            entity = w.CreateEntity();
            compEnt = w.Component<ScopedComp2>();
        }
        w.Delete(scope);
        Assert.False(w.IsAlive(scope));
        Assert.False(w.IsAlive(sys.Entity));
        Assert.False(w.IsAlive(tmr));
        Assert.False(w.IsAlive(phase));
        Assert.False(w.IsAlive(entity));
        Assert.False(w.IsAlive(compEnt));
    }

    [Fact]
    public void DeleteScope_DeletedSystemNotRunByProgress()
    {
        var w = new World();
        int hits = 0;
        var scope = w.CreateEntity();
        using (w.WithScope(scope))
        {
            w.System("s", w.Phases.OnUpdate, _ => hits++);
        }
        w.Progress(0);
        Assert.Equal(1, hits);
        w.Delete(scope);
        w.Progress(0);
        Assert.Equal(1, hits);
    }

    [Fact]
    public void DeleteChildren_TearsDownScopeButKeepsScope()
    {
        var w = new World();
        var scope = w.CreateEntity();
        SystemHandle sys; EntityId tmr; EntityId entity;
        using (w.WithScope(scope))
        {
            sys = w.System("s", w.Phases.OnUpdate, _ => { });
            tmr = w.Timer(0.5f);
            entity = w.CreateEntity();
        }
        w.DeleteChildren(scope);
        Assert.True(w.IsAlive(scope));
        Assert.False(w.IsAlive(sys.Entity));
        Assert.False(w.IsAlive(tmr));
        Assert.False(w.IsAlive(entity));
    }

    [Fact]
    public void DeleteChildren_NoChildrenIsNoop()
    {
        var w = new World();
        var lone = w.CreateEntity();
        w.DeleteChildren(lone);
        Assert.True(w.IsAlive(lone));
    }

    [Fact]
    public void DeleteChildren_DeadParentIsNoop()
    {
        var w = new World();
        var p = w.CreateEntity();
        w.Delete(p);
        w.DeleteChildren(p); // no throw
    }

    [Fact]
    public void DeleteChildren_ScopeReusableAfterTeardown()
    {
        var w = new World();
        var scope = w.CreateEntity();
        using (w.WithScope(scope)) w.System("first", w.Phases.OnUpdate, _ => { });
        w.DeleteChildren(scope);
        // Re-populate.
        SystemHandle second;
        using (w.WithScope(scope)) second = w.System("second", w.Phases.OnUpdate, _ => { });
        Assert.True(w.IsAlive(second.Entity));
        Assert.Equal(scope.Id, w.GetParent(second.Entity).Id);
    }
}
