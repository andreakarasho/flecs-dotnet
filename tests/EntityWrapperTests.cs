using System.Linq;
using Xunit;

namespace Flecs.Tests;

// Smoke tests for the Entity fluent wrapper. Forwarders are 1-liners but
// catch swapped-arg typos (e.g. SetParent direction) and confirm the
// implicit EntityId conversion threads through pair / query / Has APIs.
public class EntityWrapperTests
{
    [Fact]
    public void Factory_Entity_NewHandleAlive()
    {
        var w = new World();
        var e = w.Entity();
        Assert.True(e.IsValid);
        Assert.True(e.IsAlive);
    }

    [Fact]
    public void Factory_EntityWithName_SetsName()
    {
        var w = new World();
        var e = w.Entity("Alice");
        Assert.Equal("Alice", e.Name);
    }

    [Fact]
    public void Factory_EntityWrap_DoesNotCreateNew()
    {
        var w = new World();
        var raw = w.CreateEntity();
        var wrapped = w.Entity(raw);
        Assert.Equal(raw, wrapped.Id);
        Assert.Same(w, wrapped.World);
    }

    [Fact]
    public void ImplicitConversion_ToEntityId_Works()
    {
        var w = new World();
        var e = w.Entity();
        EntityId asId = e;
        Assert.Equal(e.Id, asId);
    }

    [Fact]
    public void ImplicitConversion_ToId_Works()
    {
        var w = new World();
        var e = w.Entity();
        Id asId = e;
        Assert.Equal(e.Id.Id, asId.Component);
    }

    [Fact]
    public void Chain_SetAddTag_AppliesAll()
    {
        var w = new World();
        var e = w.Entity()
            .Set(new Position(1, 2))
            .Set(new Velocity(3, 4))
            .Add<TagA>();

        Assert.True(e.Has<Position>());
        Assert.True(e.Has<Velocity>());
        Assert.True(e.Has<TagA>());
        Assert.Equal(1, e.Get<Position>().X);
        Assert.Equal(4, e.Get<Velocity>().Dy);
    }

    [Fact]
    public void Remove_DropsComponent()
    {
        var w = new World();
        var e = w.Entity().Set(new Position(0, 0));
        e.Remove<Position>();
        Assert.False(e.Has<Position>());
    }

    [Fact]
    public void TypedPair_AddHasRemove()
    {
        var w = new World();
        var e = w.Entity().Add<Likes, Apple>();
        Assert.True(e.Has<Likes, Apple>());
        e.Remove<Likes, Apple>();
        Assert.False(e.Has<Likes, Apple>());
    }

    [Fact]
    public void EntityPair_AddViaIds()
    {
        var w = new World();
        var alice = w.Entity();
        var bob = w.Entity();
        var likes = w.Component<Likes>();
        bob.Add(likes, alice);
        Assert.True(w.Has(bob, w.Pair(likes, alice)));
    }

    [Fact]
    public void Toggle_FlipsEnabledBit()
    {
        var w = new World();
        w.MarkCanToggle(w.Component<Position>());
        var e = w.Entity().Set(new Position(0, 0));
        Assert.True(e.IsEnabled<Position>());
        e.Toggle<Position>();
        Assert.False(e.IsEnabled<Position>());
        e.SetEnabled<Position>(true);
        Assert.True(e.IsEnabled<Position>());
    }

    [Fact]
    public void EnableDisable_TogglesActivation()
    {
        var w = new World();
        var e = w.Entity();
        Assert.True(e.IsEnabled());
        e.Disable();
        Assert.False(e.IsEnabled());
        e.Enable();
        Assert.True(e.IsEnabled());
    }

    [Fact]
    public void Destroy_KillsEntity()
    {
        var w = new World();
        var e = w.Entity();
        e.Destroy();
        Assert.False(e.IsAlive);
    }

    [Fact]
    public void Clone_ProducesDistinctAliveCopy()
    {
        var w = new World();
        var src = w.Entity().Set(new Position(7, 8));
        var dst = src.Clone();
        Assert.NotEqual(src.Id, dst.Id);
        Assert.True(dst.IsAlive);
        Assert.Equal(7, dst.Get<Position>().X);
    }

    [Fact]
    public void SetParent_LinksHierarchy()
    {
        var w = new World();
        var parent = w.Entity("P");
        var child = w.Entity("C").SetParent(parent);
        Assert.Equal(parent.Id, child.Parent.Id);
        Assert.True(child.HasParent(parent));
    }

    [Fact]
    public void AddChild_InverseOfSetParent()
    {
        var w = new World();
        var parent = w.Entity();
        var child = w.Entity();
        parent.AddChild(child);
        Assert.True(w.Entity(child).HasParent(parent));
        Assert.Equal(parent.Id, w.Entity(child).Parent.Id);
    }

    [Fact]
    public void Children_EnumeratesChildEntities()
    {
        var w = new World();
        var p = w.Entity();
        var a = w.Entity().SetParent(p);
        var b = w.Entity().SetParent(p);
        var kids = p.Children().ToHashSet();
        Assert.Contains(a.Id, kids);
        Assert.Contains(b.Id, kids);
    }

    [Fact]
    public void ClearParent_DropsHierarchyLink()
    {
        var w = new World();
        var p = w.Entity();
        var c = w.Entity().SetParent(p);
        c.ClearParent();
        Assert.False(c.HasParent(p));
    }

    [Fact]
    public void IsA_AndInheritance()
    {
        var w = new World();
        var prefab = w.Entity().Set(new Position(9, 9));
        w.MarkInheritable(w.Component<Position>());
        var inst = w.Entity().SetIsA(prefab);
        Assert.True(inst.HasIsA(prefab));
        Assert.True(inst.HasInherited<Position>());
        Assert.Equal(9, inst.GetInherited<Position>().X);
    }

    [Fact]
    public void Equality_MatchesById()
    {
        var w = new World();
        var raw = w.CreateEntity();
        var a = w.Entity(raw);
        var b = w.Entity(raw);
        Assert.True(a.Equals(b));
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    // ===== Path / DeleteChildren / Target forwarders =====

    [Fact]
    public void Path_ReturnsDottedPath()
    {
        var w = new World();
        var p = w.Entity("Outer");
        var c = w.Entity("Leaf").SetParent(p);
        Assert.Equal("Outer.Leaf", c.Path);
    }

    [Fact]
    public void DeleteChildren_TearsDownChildren()
    {
        var w = new World();
        var p = w.Entity("Mod");
        var c1 = w.Entity("a").SetParent(p);
        var c2 = w.Entity("b").SetParent(p);
        p.DeleteChildren();
        Assert.True(p.IsAlive);
        Assert.False(c1.IsAlive);
        Assert.False(c2.IsAlive);
    }

    [Fact]
    public void GetTarget_FluentForwarder()
    {
        var w = new World();
        var p = w.Entity();
        var c = w.Entity().SetParent(p);
        Assert.Equal(p.Id.Id, c.GetTarget(w.Relations.ChildOf).Id);
    }

    [Fact]
    public void GetTargets_NonExclusive_AllReturned()
    {
        var w = new World();
        var p1 = w.Entity();
        var p2 = w.Entity();
        var inst = w.Entity().SetIsA(p1).SetIsA(p2);
        var ids = new System.Collections.Generic.HashSet<uint>();
        foreach (var t in inst.GetTargets(w.Relations.IsA)) ids.Add(t.Id);
        Assert.Contains(p1.Id.Id, ids);
        Assert.Contains(p2.Id.Id, ids);
    }
}
