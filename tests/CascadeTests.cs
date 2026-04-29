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
        w.Query<Position>().Cascade<Position>()
            .Each((EntityId e, ref Position _) =>
            {
                if (e.Id == root.Id || e.Id == mid.Id || e.Id == leaf.Id)
                    order.Add(e.Id);
            });
        Assert.Equal(3, order.Count);
        // Verify ordering: root (depth 0) before mid (depth 1) before leaf (depth 2).
        int rootIdx = order.IndexOf(root.Id);
        int midIdx = order.IndexOf(mid.Id);
        int leafIdx = order.IndexOf(leaf.Id);
        Assert.True(rootIdx < midIdx);
        Assert.True(midIdx < leafIdx);
    }

    [Fact]
    public void Cascade_PropagatesParentTransform()
    {
        // Classic transform-propagation pattern: each entity's WorldX =
        // parent.WorldX + LocalX. Cascade ordering guarantees parent has
        // already been updated when child reads it.
        var w = new World();
        var root = w.CreateEntity();
        w.Set(root, new Position(10, 0));   // local 10, no parent → world 10

        var child = w.CreateEntity();
        w.Set(child, new Position(5, 0));    // local 5, parent world 10 → world 15
        w.SetParent(child, root);

        var grand = w.CreateEntity();
        w.Set(grand, new Position(2, 0));    // local 2, parent world 15 → world 17
        w.SetParent(grand, child);

        // Compute world X in cascade order. We track via a side dict so we
        // can read the parent's world value during the child callback.
        var worldX = new Dictionary<uint, float>();
        w.Query<Position>().Cascade<Position>()
            .Each((EntityId e, ref Position p) =>
            {
                var parent = w.GetParent(e);
                float baseX = parent.IsValid && worldX.TryGetValue(parent.Id, out var px) ? px : 0;
                worldX[e.Id] = baseX + p.X;
            });
        Assert.Equal(10, worldX[root.Id]);
        Assert.Equal(15, worldX[child.Id]);
        Assert.Equal(17, worldX[grand.Id]);
    }

    [Fact]
    public void Cascade_CustomRelation()
    {
        var w = new World();
        // Custom relation: w.IsA serves as a stand-in tree relation.
        var prefab = w.CreateEntity(); w.Set(prefab, new Position(1, 1));
        var sub = w.CreateEntity(); w.Set(sub, new Position(2, 2)); w.SetIsA(sub, prefab);
        var leaf = w.CreateEntity(); w.Set(leaf, new Position(3, 3)); w.SetIsA(leaf, sub);

        var order = new List<uint>();
        w.Query<Position>().Cascade<Position>(w.IsA)
            .Each((EntityId e, ref Position _) =>
            {
                if (e.Id == prefab.Id || e.Id == sub.Id || e.Id == leaf.Id)
                    order.Add(e.Id);
            });
        int p = order.IndexOf(prefab.Id);
        int s = order.IndexOf(sub.Id);
        int l = order.IndexOf(leaf.Id);
        Assert.True(p < s);
        Assert.True(s < l);
    }

    [Fact]
    public void Cascade_RootsBeforeAllDescendants()
    {
        // Two parallel trees. Cascade order: roots first, then children.
        var w = new World();
        var rootA = w.CreateEntity(); w.Set(rootA, new Position(0, 0));
        var rootB = w.CreateEntity(); w.Set(rootB, new Position(0, 0));
        var childA = w.CreateEntity(); w.Set(childA, new Position(0, 0)); w.SetParent(childA, rootA);
        var childB = w.CreateEntity(); w.Set(childB, new Position(0, 0)); w.SetParent(childB, rootB);

        var order = new List<uint>();
        w.Query<Position>().Cascade<Position>()
            .Each((EntityId e, ref Position _) => order.Add(e.Id));

        // Both roots must precede both children.
        int posChildA = order.IndexOf(childA.Id);
        int posChildB = order.IndexOf(childB.Id);
        Assert.True(order.IndexOf(rootA.Id) < posChildA);
        Assert.True(order.IndexOf(rootA.Id) < posChildB);
        Assert.True(order.IndexOf(rootB.Id) < posChildA);
        Assert.True(order.IndexOf(rootB.Id) < posChildB);
    }

    [Fact]
    public void Cascade_RunIteration_RespectsOrder()
    {
        var w = new World();
        var root = w.CreateEntity(); w.Set(root, new Position(1, 0));
        var mid = w.CreateEntity(); w.Set(mid, new Position(2, 0)); w.SetParent(mid, root);
        var leaf = w.CreateEntity(); w.Set(leaf, new Position(3, 0)); w.SetParent(leaf, mid);

        var seenInOrder = new List<uint>();
        w.Query<Position>().Cascade<Position>()
            .Run((in Iter<Position> it) =>
            {
                for (int r = 0; r < it.Count; r++)
                    seenInOrder.Add(it.Entity(r).Id);
            });
        int rootI = seenInOrder.IndexOf(root.Id);
        int midI = seenInOrder.IndexOf(mid.Id);
        int leafI = seenInOrder.IndexOf(leaf.Id);
        Assert.True(rootI >= 0 && midI > rootI && leafI > midI);
    }
}
