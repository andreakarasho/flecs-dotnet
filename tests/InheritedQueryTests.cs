using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class InheritedQueryTests
{
    [Fact]
    public void Each_LiteralByDefault_DoesNotMatchInherited()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(10, 10));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        var seen = new List<uint>();
        w.Query<Position>().Each((EntityId e, ref Position _) => seen.Add(e.Id));
        // Default literal: only prefab itself owns Position.
        Assert.Single(seen);
        Assert.Equal(prefab.Id, seen[0]);
    }

    [Fact]
    public void Each_Inherited_MatchesInstance()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(10, 10));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        var seen = new List<uint>();
        w.Query<Position>().Inherited().Each((EntityId e, ref Position _) => seen.Add(e.Id));
        // Self+Up: prefab (own) + inst (via IsA).
        Assert.Equal(2, seen.Count);
        Assert.Contains(prefab.Id, seen);
        Assert.Contains(inst.Id, seen);
    }

    [Fact]
    public void Each_Inherited_SharedRefReflectsPrefabValue()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(7, 8));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        var values = new List<float>();
        w.Query<Position>().Inherited().Each((EntityId e, ref Position p) => values.Add(p.X));
        Assert.Equal(2, values.Count);
        Assert.All(values, x => Assert.Equal(7, x));
    }

    [Fact]
    public void Each_Inherited_OwnOverridesShared()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(1, 1));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        w.Set(inst, new Position(99, 99));   // override

        var byEntity = new Dictionary<uint, float>();
        w.Query<Position>().Inherited().Each((EntityId e, ref Position p) => byEntity[e.Id] = p.X);
        Assert.Equal(1, byEntity[prefab.Id]);
        Assert.Equal(99, byEntity[inst.Id]);
    }

    [Fact]
    public void Each_Inherited_TwoTerms_MixedOwnAndShared()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(5, 5));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        w.Set(inst, new Velocity(2, 3));      // own velocity, shared position

        int hits = 0;
        float gotX = 0f, gotDx = 0f;
        w.Query<Position, Velocity>().Inherited()
            .Each((EntityId e, ref Position p, ref Velocity v) =>
            {
                if (e.Id != inst.Id) return;
                hits++;
                gotX = p.X;
                gotDx = v.Dx;
            });
        Assert.Equal(1, hits);
        Assert.Equal(5, gotX);    // shared from prefab
        Assert.Equal(2, gotDx);   // own
    }

    [Fact]
    public void Run_Inherited_SkipsInheritedOnlyTables()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(1, 1));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        int rowsSeen = 0;
        w.Query<Position>().Inherited().Run((in Iter<Position> it) => rowsSeen += it.Count);
        // Run skips inherited-only tables; only prefab's own row counted.
        Assert.Equal(1, rowsSeen);
    }

    [Fact]
    public void Each_Inherited_DeepChain()
    {
        var w = new World();
        var grand = w.CreateEntity();
        w.Set(grand, new Position(11, 11));
        var parent = w.CreateEntity();
        w.SetIsA(parent, grand);
        var child = w.CreateEntity();
        w.SetIsA(child, parent);

        var seen = new HashSet<uint>();
        w.Query<Position>().Inherited().Each((EntityId e, ref Position _) => seen.Add(e.Id));
        Assert.Contains(grand.Id, seen);
        Assert.Contains(parent.Id, seen);
        Assert.Contains(child.Id, seen);
    }

    [Fact]
    public void Each_Inherited_RespectsWithout()
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
        w.Query<Position>().Inherited().Without<Boss>()
            .Each((EntityId e, ref Position _) => seen.Add(e.Id));
        Assert.Contains(prefab.Id, seen);
        Assert.Contains(inst.Id, seen);
        Assert.DoesNotContain(bossInst.Id, seen);
    }
}
