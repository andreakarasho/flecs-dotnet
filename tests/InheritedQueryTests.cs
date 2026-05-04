using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class InheritedQueryTests
{
    [Fact]
    public void LiteralByDefault_DoesNotMatchInherited()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(10, 10));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        var seen = new List<uint>();
        foreach (var row in w.Query<Position>()) seen.Add(row.Entity.Id);
        Assert.Single(seen);
        Assert.Equal(prefab.Id, seen[0]);
    }

    [Fact]
    public void Inherited_MatchesInstance()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(10, 10));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        var seen = new List<uint>();
        foreach (var row in w.Query<Position>().Inherited()) seen.Add(row.Entity.Id);
        Assert.Equal(2, seen.Count);
        Assert.Contains(prefab.Id, seen);
        Assert.Contains(inst.Id, seen);
    }

    [Fact]
    public void Inherited_SharedRefReflectsPrefabValue()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(7, 8));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        var values = new List<float>();
        foreach (var row in w.Query<Position>().Inherited())
            values.Add(row.Component1.Value.X);
        Assert.Equal(2, values.Count);
        Assert.All(values, x => Assert.Equal(7, x));
    }

    [Fact]
    public void Inherited_OwnOverridesShared()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(1, 1));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        w.Set(inst, new Position(99, 99));

        var byEntity = new Dictionary<uint, float>();
        foreach (var row in w.Query<Position>().Inherited())
            byEntity[row.Entity.Id] = row.Component1.Value.X;
        Assert.Equal(1, byEntity[prefab.Id]);
        Assert.Equal(99, byEntity[inst.Id]);
    }

    [Fact]
    public void Inherited_TwoTerms_MixedOwnAndShared()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(5, 5));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        w.Set(inst, new Velocity(2, 3));

        int hits = 0;
        float gotX = 0f, gotDx = 0f;
        foreach (var row in w.Query<Position, Velocity>().Inherited())
        {
            if (row.Entity.Id != inst.Id) continue;
            hits++;
            gotX = row.Component1.Value.X;
            gotDx = row.Component2.Value.Dx;
        }
        Assert.Equal(1, hits);
        Assert.Equal(5, gotX);
        Assert.Equal(2, gotDx);
    }

    [Fact]
    public void Inherited_VisitsBothOwnAndSharedTables()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(1, 1));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        int rowsSeen = 0;
        bool sawShared = false;
        foreach (var row in w.Query<Position>().Inherited())
        {
            rowsSeen++;
            if (row.IsShared1) sawShared = true;
        }
        Assert.Equal(2, rowsSeen);
        Assert.True(sawShared);
    }

    [Fact]
    public void Inherited_DeepChain()
    {
        var w = new World();
        var grand = w.CreateEntity();
        w.Set(grand, new Position(11, 11));
        var parent = w.CreateEntity();
        w.SetIsA(parent, grand);
        var child = w.CreateEntity();
        w.SetIsA(child, parent);

        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position>().Inherited())
            seen.Add(row.Entity.Id);
        Assert.Contains(grand.Id, seen);
        Assert.Contains(parent.Id, seen);
        Assert.Contains(child.Id, seen);
    }

    [Fact]
    public void Inherited_RespectsWithout()
    {
        var w = new World();
        w.Tag<Boss>();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(0, 0));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        var bossInst = w.CreateEntity();
        w.SetIsA(bossInst, prefab);
        w.Add<Boss>(bossInst);

        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position>().Inherited().Without<Boss>())
            seen.Add(row.Entity.Id);
        Assert.Contains(prefab.Id, seen);
        Assert.Contains(inst.Id, seen);
        Assert.DoesNotContain(bossInst.Id, seen);
    }

    [Fact]
    public void Inherited_NoPrefabHolders_OnlyDirectMatches()
    {
        // No IsA chain — Inherited() should still return direct holders.
        var w = new World();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0));
        var b = w.CreateEntity(); w.Set(b, new Position(2, 0));
        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position>().Inherited())
            seen.Add(row.Entity.Id);
        Assert.Equal(2, seen.Count);
        Assert.Contains(a.Id, seen);
        Assert.Contains(b.Id, seen);
    }

    [Fact]
    public void Inherited_RespectsDontInherit()
    {
        var w = new World();
        var compEnt = w.Component<Position>();
        w.MarkDontInherit(compEnt);
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(7, 7));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        // inst should NOT appear — Position blocked from inheritance.
        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position>().Inherited())
            seen.Add(row.Entity.Id);
        Assert.Contains(prefab.Id, seen);
        Assert.DoesNotContain(inst.Id, seen);
    }

    [Fact]
    public void Inherited_PrefabDeleteRemovesInst()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(1, 1));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        // Default OnDeleteTarget for IsA is Remove → drop the IsA pair.
        w.Delete(prefab);
        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position>().Inherited())
            seen.Add(row.Entity.Id);
        Assert.DoesNotContain(prefab.Id, seen);
        // inst no longer inherits Position — pair gone.
        Assert.DoesNotContain(inst.Id, seen);
    }
}
