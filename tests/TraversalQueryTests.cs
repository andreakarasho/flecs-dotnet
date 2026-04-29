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
        w.Query<Position>().Up<Position>()
            .Each((EntityId e, ref Position p) => byEntity[e.Id] = p.X);
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
        w.Query<Position>().Up<Position>(w.ChildOf)
            .Each((EntityId e, ref Position p) => seen.Add(e.Id));
        // parent has Position directly. child gets it via ChildOf.
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
        w.Query<Position>().Up<Position>(w.ChildOf)
            .Each((EntityId e, ref Position p) => byEntity[e.Id] = p.X);
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
        // parent has no Position; child has parent via ChildOf.
        w.SetParent(parent, grand);
        var child = w.CreateEntity();
        w.SetParent(child, parent);

        var seen = new HashSet<uint>();
        w.Query<Position>().Parent<Position>()
            .Each((EntityId e, ref Position _) => seen.Add(e.Id));
        // grand has Position directly → matches.
        // parent: direct parent grand has Position → matches via depth=1.
        // child: direct parent is parent (no Position); doesn't reach grand → no match.
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
        // Position via IsA, Velocity must be own.
        int hits = 0;
        float gotX = 0f, gotDx = 0f;
        w.Query<Position, Velocity>().Up<Position>()
            .Each((EntityId e, ref Position p, ref Velocity v) =>
            {
                if (e.Id != inst.Id) return;
                hits++;
                gotX = p.X;
                gotDx = v.Dx;
            });
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

        // Inherited() = IsA. But explicit Up<Position>(ChildOf) overrides that
        // for Position term. child has no IsA, has ChildOf → Position resolves.
        var seen = new HashSet<uint>();
        w.Query<Position>().Inherited().Up<Position>(w.ChildOf)
            .Each((EntityId e, ref Position _) => seen.Add(e.Id));
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
        w.Query<Position>().Up<Position>(w.ChildOf)
            .Each((EntityId e, ref Position p) => byEntity[e.Id] = p.X);
        Assert.Equal(1, byEntity[parent.Id]);
        Assert.Equal(99, byEntity[child.Id]);
    }

    [Fact]
    public void Run_VisitsTraversedTables_WithSharedSpans()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(1, 1));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        int rowsSeen = 0;
        bool sawShared = false;
        w.Query<Position>().Up<Position>().Run((in Iter<Position> it) =>
        {
            rowsSeen += it.Count;
            if (it.IsShared1) sawShared = true;
        });
        Assert.Equal(2, rowsSeen);
        Assert.True(sawShared);
    }

    [Fact]
    public void Run_AtN_ResolvesSharedAndOwnPerRow()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(5, 5));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        w.Set(inst, new Velocity(2, 3));   // own velocity, shared position

        var posByEnt = new Dictionary<uint, float>();
        var dxByEnt = new Dictionary<uint, float>();
        w.Query<Position, Velocity>().Up<Position>().Run((in Iter<Position, Velocity> it) =>
        {
            for (int r = 0; r < it.Count; r++)
            {
                var e = it.Entity(r);
                posByEnt[e.Id] = it.At1(r).X;
                dxByEnt[e.Id] = it.At2(r).Dx;
            }
        });
        // Only inst has both Position (shared) and Velocity (own).
        Assert.Equal(5, posByEnt[inst.Id]);
        Assert.Equal(2, dxByEnt[inst.Id]);
    }
}
