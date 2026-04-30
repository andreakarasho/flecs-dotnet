using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class CascadeTests
{
    [Fact]
    public void Cascade_DefaultChildOf_OrdersAncestorsFirst()
    {
        var w = new World();
        var root = w.CreateEntity(); w.Set(root, new Position(0, 0));
        var mid = w.CreateEntity(); w.Set(mid, new Position(0, 0)); w.SetParent(mid, root);
        var leaf = w.CreateEntity(); w.Set(leaf, new Position(0, 0)); w.SetParent(leaf, mid);

        var order = new List<uint>();
        foreach (var row in w.Query<Position>().Cascade<Position>())
        {
            var e = row.Entity;
            if (e.Id == root.Id || e.Id == mid.Id || e.Id == leaf.Id) order.Add(e.Id);
        }
        Assert.Equal(3, order.Count);
        int rootIdx = order.IndexOf(root.Id);
        int midIdx = order.IndexOf(mid.Id);
        int leafIdx = order.IndexOf(leaf.Id);
        Assert.True(rootIdx < midIdx);
        Assert.True(midIdx < leafIdx);
    }

    [Fact]
    public void Cascade_PropagatesParentTransform()
    {
        var w = new World();
        var root = w.CreateEntity();
        w.Set(root, new Position(10, 0));

        var child = w.CreateEntity();
        w.Set(child, new Position(5, 0));
        w.SetParent(child, root);

        var grand = w.CreateEntity();
        w.Set(grand, new Position(2, 0));
        w.SetParent(grand, child);

        var worldX = new Dictionary<uint, float>();
        foreach (var row in w.Query<Position>().Cascade<Position>())
        {
            var e = row.Entity;
            var parent = w.GetParent(e);
            float baseX = parent.IsValid && worldX.TryGetValue(parent.Id, out var px) ? px : 0;
            worldX[e.Id] = baseX + row.Component1.Value.X;
        }
        Assert.Equal(10, worldX[root.Id]);
        Assert.Equal(15, worldX[child.Id]);
        Assert.Equal(17, worldX[grand.Id]);
    }

    [Fact]
    public void Cascade_CustomRelation()
    {
        var w = new World();
        var prefab = w.CreateEntity(); w.Set(prefab, new Position(1, 1));
        var sub = w.CreateEntity(); w.Set(sub, new Position(2, 2)); w.SetIsA(sub, prefab);
        var leaf = w.CreateEntity(); w.Set(leaf, new Position(3, 3)); w.SetIsA(leaf, sub);

        var order = new List<uint>();
        foreach (var row in w.Query<Position>().Cascade<Position>(w.IsA))
        {
            var e = row.Entity;
            if (e.Id == prefab.Id || e.Id == sub.Id || e.Id == leaf.Id) order.Add(e.Id);
        }
        int p = order.IndexOf(prefab.Id);
        int s = order.IndexOf(sub.Id);
        int l = order.IndexOf(leaf.Id);
        Assert.True(p < s);
        Assert.True(s < l);
    }

    [Fact]
    public void Cascade_RootsBeforeAllDescendants()
    {
        var w = new World();
        var rootA = w.CreateEntity(); w.Set(rootA, new Position(0, 0));
        var rootB = w.CreateEntity(); w.Set(rootB, new Position(0, 0));
        var childA = w.CreateEntity(); w.Set(childA, new Position(0, 0)); w.SetParent(childA, rootA);
        var childB = w.CreateEntity(); w.Set(childB, new Position(0, 0)); w.SetParent(childB, rootB);

        var order = new List<uint>();
        foreach (var row in w.Query<Position>().Cascade<Position>())
            order.Add(row.Entity.Id);

        int posChildA = order.IndexOf(childA.Id);
        int posChildB = order.IndexOf(childB.Id);
        Assert.True(order.IndexOf(rootA.Id) < posChildA);
        Assert.True(order.IndexOf(rootA.Id) < posChildB);
        Assert.True(order.IndexOf(rootB.Id) < posChildA);
        Assert.True(order.IndexOf(rootB.Id) < posChildB);
    }

    [Fact]
    public void Cascade_Iteration_RespectsOrder()
    {
        var w = new World();
        var root = w.CreateEntity(); w.Set(root, new Position(1, 0));
        var mid = w.CreateEntity(); w.Set(mid, new Position(2, 0)); w.SetParent(mid, root);
        var leaf = w.CreateEntity(); w.Set(leaf, new Position(3, 0)); w.SetParent(leaf, mid);

        var seen = new List<uint>();
        foreach (var row in w.Query<Position>().Cascade<Position>())
            seen.Add(row.Entity.Id);
        int rootI = seen.IndexOf(root.Id);
        int midI = seen.IndexOf(mid.Id);
        int leafI = seen.IndexOf(leaf.Id);
        Assert.True(rootI >= 0 && midI > rootI && leafI > midI);
    }
}
