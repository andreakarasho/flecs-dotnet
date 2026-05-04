using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class HookTests
{
    [Fact]
    public void Hook_OnAdd_FiresOnFirstAdd()
    {
        var w = new World();
        int calls = 0;
        w.Hooks<Position>().SetOnAdd((World W, EntityId e, ref Position _) => calls++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Hook_OnAdd_DoesNotFireOnSubsequentSet()
    {
        var w = new World();
        int calls = 0;
        w.Hooks<Position>().SetOnAdd((World W, EntityId e, ref Position _) => calls++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Position(1, 1));
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Hook_OnSet_FiresEverySet()
    {
        var w = new World();
        int calls = 0;
        w.Hooks<Position>().SetOnSet((World W, EntityId e, ref Position _) => calls++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Position(1, 1));
        w.Set(e, new Position(2, 2));
        Assert.Equal(3, calls);
    }

    [Fact]
    public void Hook_OnRemove_FiresOnRemove()
    {
        var w = new World();
        int calls = 0;
        w.Hooks<Position>().SetOnRemove((World W, EntityId e, ref Position _) => calls++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Remove<Position>(e);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Hook_OnRemove_FiresOnDelete()
    {
        var w = new World();
        int calls = 0;
        w.Hooks<Position>().SetOnRemove((World W, EntityId e, ref Position _) => calls++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Delete(e);
        Assert.Equal(1, calls);
    }

    [Fact]
    public void Hook_Ctor_RunsBeforeOnAdd()
    {
        var w = new World();
        var order = new List<string>();
        w.Hooks<Position>().SetCtor((World W, EntityId e, ref Position p) => { order.Add("ctor"); p.X = -1; });
        w.Hooks<Position>().SetOnAdd((World W, EntityId e, ref Position p) => order.Add($"onadd:{p.X}"));
        var e = w.CreateEntity();
        w.Set(e, new Position(5, 5));
        Assert.Equal(new[] { "ctor", "onadd:-1", }, order.GetRange(0, 2));
    }

    [Fact]
    public void Hook_Dtor_FiresAfterOnRemove()
    {
        var w = new World();
        var order = new List<string>();
        w.Hooks<Position>().SetOnRemove((World W, EntityId e, ref Position _) => order.Add("onremove"));
        w.Hooks<Position>().SetDtor((World W, EntityId e, ref Position _) => order.Add("dtor"));
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Remove<Position>(e);
        Assert.Equal(new[] { "onremove", "dtor" }, order);
    }

    [Fact]
    public void Hook_OnSet_SeesNewValue()
    {
        var w = new World();
        float observed = 0;
        w.Hooks<Position>().SetOnSet((World W, EntityId e, ref Position p) => observed = p.X);
        var e = w.CreateEntity();
        w.Set(e, new Position(42, 0));
        Assert.Equal(42, observed);
    }

    [Fact]
    public void Hook_OnRemove_SeesValueBeforeRemoval()
    {
        var w = new World();
        float observed = 0;
        w.Hooks<Position>().SetOnRemove((World W, EntityId e, ref Position p) => observed = p.X);
        var e = w.CreateEntity();
        w.Set(e, new Position(7, 0));
        w.Remove<Position>(e);
        Assert.Equal(7, observed);
    }

    // ===== Move / Copy =====

    [Fact]
    public void Hook_Move_FiresOnArchetypeChange()
    {
        var w = new World();
        int moves = 0;
        w.Hooks<Position>().SetMove((World W, EntityId e, ref Position s, ref Position d) =>
        {
            moves++;
            d = s;
        });
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        // Archetype change — adding a tag forces table migration.
        w.Add<TagA>(e);
        Assert.True(moves >= 1);
        Assert.Equal(1, w.Get<Position>(e).X);
    }

    [Fact]
    public void Hook_Copy_FiresOnClone()
    {
        var w = new World();
        int copies = 0;
        w.Hooks<Position>().SetCopy((World W, EntityId e, ref Position s, ref Position d) =>
        {
            copies++;
            d = s;
        });
        var src = w.CreateEntity();
        w.Set(src, new Position(9, 9));
        var dst = w.Clone(src);
        Assert.Equal(1, copies);
        Assert.Equal(9, w.Get<Position>(dst).X);
    }

    [Fact]
    public void Hook_Copy_DefaultIsBitwiseCopy()
    {
        var w = new World();
        // No Copy hook set — default behavior is plain assignment.
        w.Component<Position>();
        var src = w.CreateEntity();
        w.Set(src, new Position(3, 4));
        var dst = w.Clone(src);
        Assert.Equal(3, w.Get<Position>(dst).X);
        Assert.Equal(4, w.Get<Position>(dst).Y);
    }

    [Fact]
    public void Hook_Move_DefaultPreservesValueOnMigrate()
    {
        // No Move hook — migration falls back to bitwise copy.
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(11, 22));
        w.Add<TagA>(e); // forces archetype move
        Assert.Equal(11, w.Get<Position>(e).X);
        Assert.Equal(22, w.Get<Position>(e).Y);
    }

    // ===== Ordering / interaction =====

    [Fact]
    public void Hook_OnSet_DoesNotFireOnRemove()
    {
        var w = new World();
        int onSet = 0;
        w.Hooks<Position>().SetOnSet((World W, EntityId e, ref Position _) => onSet++);
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 1)); // 1
        w.Remove<Position>(e);        // not OnSet
        Assert.Equal(1, onSet);
    }

    [Fact]
    public void Hook_OnAdd_DoesNotFireOnReadd()
    {
        // Re-adding after removal is a fresh add — should fire OnAdd.
        var w = new World();
        int onAdd = 0;
        w.Hooks<Position>().SetOnAdd((World W, EntityId e, ref Position _) => onAdd++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));   // OnAdd 1
        w.Remove<Position>(e);
        w.Set(e, new Position(1, 1));   // OnAdd 2
        Assert.Equal(2, onAdd);
    }

    [Fact]
    public void Hook_OnSet_FiresOnClone()
    {
        // Clone should fire OnSet on dst so observers see the cloned value.
        var w = new World();
        var seen = new List<float>();
        w.Hooks<Position>().SetOnSet((World W, EntityId e, ref Position p) => seen.Add(p.X));
        var src = w.CreateEntity();
        w.Set(src, new Position(5, 0));
        seen.Clear();
        w.Clone(src);
        Assert.Single(seen);
        Assert.Equal(5, seen[0]);
    }

    [Fact]
    public void Hook_NotFired_AfterRebind()
    {
        // SetOnAdd(null) detaches the hook — subsequent Set must not invoke.
        var w = new World();
        int onAdd = 0;
        var hooks = w.Hooks<Position>();
        hooks.SetOnAdd((World W, EntityId e, ref Position _) => onAdd++);
        var e1 = w.CreateEntity();
        w.Set(e1, new Position(0, 0));
        hooks.SetOnAdd(null);
        var e2 = w.CreateEntity();
        w.Set(e2, new Position(0, 0));
        Assert.Equal(1, onAdd);
    }

    [Fact]
    public void Hook_Replaced_OnlyLatestRuns()
    {
        var w = new World();
        int a = 0, b = 0;
        var hooks = w.Hooks<Position>();
        hooks.SetOnAdd((World W, EntityId e, ref Position _) => a++);
        hooks.SetOnAdd((World W, EntityId e, ref Position _) => b++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        Assert.Equal(0, a);
        Assert.Equal(1, b);
    }

    [Fact]
    public void Hook_OnSet_AfterCtor_AppliesUserValue()
    {
        // Ctor pre-fills, then user value should overwrite by Set time.
        var w = new World();
        Position observed = default;
        w.Hooks<Position>().SetCtor((World W, EntityId e, ref Position p) => p = new Position(-1, -1));
        w.Hooks<Position>().SetOnSet((World W, EntityId e, ref Position p) => observed = p);
        var e = w.CreateEntity();
        w.Set(e, new Position(7, 8));
        Assert.Equal(7, observed.X);
        Assert.Equal(8, observed.Y);
    }
}
