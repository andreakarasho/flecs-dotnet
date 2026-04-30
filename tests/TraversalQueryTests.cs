using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class TraversalQueryTests
{
    [Fact]
    public void Up_DefaultIsA_MatchesAndResolvesShared()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(7, 7));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        var byEntity = new Dictionary<uint, float>();
        foreach (var row in w.Query<Position>().Up<Position>())
            byEntity[row.Entity.Id] = row.Component1.Value.X;
        Assert.Equal(7, byEntity[prefab.Id]);
        Assert.Equal(7, byEntity[inst.Id]);
    }

    [Fact]
    public void Up_CustomRelation_ChildOf()
    {
        var w = new World();
        var parent = w.CreateEntity();
        w.Set(parent, new Position(3, 4));
        var child = w.CreateEntity();
        w.SetParent(child, parent);

        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position>().Up<Position>(w.ChildOf))
            seen.Add(row.Entity.Id);
        Assert.Contains(parent.Id, seen);
        Assert.Contains(child.Id, seen);
    }

    [Fact]
    public void Up_ChildOf_DeepChain()
    {
        var w = new World();
        var grand = w.CreateEntity();
        w.Set(grand, new Position(11, 11));
        var parent = w.CreateEntity();
        w.SetParent(parent, grand);
        var child = w.CreateEntity();
        w.SetParent(child, parent);

        var byEntity = new Dictionary<uint, float>();
        foreach (var row in w.Query<Position>().Up<Position>(w.ChildOf))
            byEntity[row.Entity.Id] = row.Component1.Value.X;
        Assert.Equal(11, byEntity[grand.Id]);
        Assert.Equal(11, byEntity[parent.Id]);
        Assert.Equal(11, byEntity[child.Id]);
    }

    [Fact]
    public void Parent_DirectOnly_DoesNotReachGrandparent()
    {
        var w = new World();
        var grand = w.CreateEntity();
        w.Set(grand, new Position(11, 11));
        var parent = w.CreateEntity();
        w.SetParent(parent, grand);
        var child = w.CreateEntity();
        w.SetParent(child, parent);

        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position>().Parent<Position>())
            seen.Add(row.Entity.Id);
        Assert.Contains(grand.Id, seen);
        Assert.Contains(parent.Id, seen);
        Assert.DoesNotContain(child.Id, seen);
    }

    [Fact]
    public void Up_PerTerm_Mixed_OwnAndAncestor()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(5, 5));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        w.Set(inst, new Velocity(2, 3));
        int hits = 0;
        float gotX = 0f, gotDx = 0f;
        foreach (var row in w.Query<Position, Velocity>().Up<Position>())
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
    public void Up_TermOverride_BeatsInheritedDefault()
    {
        var w = new World();
        var parent = w.CreateEntity();
        w.Set(parent, new Position(9, 9));
        var child = w.CreateEntity();
        w.SetParent(child, parent);

        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position>().Inherited().Up<Position>(w.ChildOf))
            seen.Add(row.Entity.Id);
        Assert.Contains(parent.Id, seen);
        Assert.Contains(child.Id, seen);
    }

    [Fact]
    public void Up_OwnOverridesShared()
    {
        var w = new World();
        var parent = w.CreateEntity();
        w.Set(parent, new Position(1, 1));
        var child = w.CreateEntity();
        w.SetParent(child, parent);
        w.Set(child, new Position(99, 99));

        var byEntity = new Dictionary<uint, float>();
        foreach (var row in w.Query<Position>().Up<Position>(w.ChildOf))
            byEntity[row.Entity.Id] = row.Component1.Value.X;
        Assert.Equal(1, byEntity[parent.Id]);
        Assert.Equal(99, byEntity[child.Id]);
    }

    [Fact]
    public void Iter_VisitsTraversedTables_WithSharedFlag()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(1, 1));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        int rowsSeen = 0;
        bool sawShared = false;
        foreach (var row in w.Query<Position>().Up<Position>())
        {
            rowsSeen++;
            if (row.IsShared1) sawShared = true;
        }
        Assert.Equal(2, rowsSeen);
        Assert.True(sawShared);
    }

    [Fact]
    public void PerRow_ResolvesSharedAndOwn()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(5, 5));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        w.Set(inst, new Velocity(2, 3));

        var posByEnt = new Dictionary<uint, float>();
        var dxByEnt = new Dictionary<uint, float>();
        foreach (var row in w.Query<Position, Velocity>().Up<Position>())
        {
            posByEnt[row.Entity.Id] = row.Component1.Value.X;
            dxByEnt[row.Entity.Id] = row.Component2.Value.Dx;
        }
        Assert.Equal(5, posByEnt[inst.Id]);
        Assert.Equal(2, dxByEnt[inst.Id]);
    }
}
