using Xunit;
using System;
using System.Linq;

namespace Flecs.Tests;

public class DeletePolicyTests
{
    [Fact]
    public void ChildOf_DefaultCascadesOnParentDelete()
    {
        var w = new World();
        var p = w.CreateEntity();
        var c1 = w.CreateEntity();
        var c2 = w.CreateEntity();
        w.SetParent(c1, p);
        w.SetParent(c2, p);
        w.Delete(p);
        Assert.False(w.IsAlive(c1));
        Assert.False(w.IsAlive(c2));
    }

    [Fact]
    public void ChildOf_CascadeWalksDeepTree()
    {
        var w = new World();
        var root = w.CreateEntity();
        var mid = w.CreateEntity();
        var leaf = w.CreateEntity();
        w.SetParent(mid, root);
        w.SetParent(leaf, mid);
        w.Delete(root);
        Assert.False(w.IsAlive(mid));
        Assert.False(w.IsAlive(leaf));
    }

    [Fact]
    public void OnDeleteTarget_OverrideToRemove()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        // Default is Remove for a non-ChildOf relation, but assert behavior
        // explicitly via override.
        w.SetOnDeleteTarget(rel, DeletePolicy.Remove);
        var holder = w.CreateEntity();
        var target = w.CreateEntity();
        w.Add(holder, rel, target);
        Assert.True(w.Has(holder, w.Pair(rel, target)));
        w.Delete(target);
        Assert.True(w.IsAlive(holder));
        Assert.False(w.Has(holder, w.Pair(rel, target)));
    }

    [Fact]
    public void OnDeleteTarget_OverrideToDelete()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.SetOnDeleteTarget(rel, DeletePolicy.Delete);
        var holder = w.CreateEntity();
        var target = w.CreateEntity();
        w.Add(holder, rel, target);
        w.Delete(target);
        Assert.False(w.IsAlive(holder));
    }

    [Fact]
    public void OnDeleteTarget_PanicThrows()
    {
        var w = new World();
        var rel = w.Tag<Likes>();
        w.SetOnDeleteTarget(rel, DeletePolicy.Panic);
        var holder = w.CreateEntity();
        var target = w.CreateEntity();
        w.Add(holder, rel, target);
        Assert.Throws<InvalidOperationException>(() => w.Delete(target));
        // Holder should still be alive — exception aborted teardown.
        Assert.True(w.IsAlive(holder));
    }

    [Fact]
    public void OnDeleteTarget_NoOpWhenNoHolders()
    {
        var w = new World();
        var p = w.CreateEntity();
        // No children — delete shouldn't enqueue anything.
        w.Delete(p);
        Assert.False(w.IsAlive(p));
    }

    [Fact]
    public void ChildOf_ParentDelete_DoesNotAffectUnrelated()
    {
        var w = new World();
        var p = w.CreateEntity();
        var c = w.CreateEntity();
        var unrelated = w.CreateEntity();
        w.SetParent(c, p);
        w.Delete(p);
        Assert.False(w.IsAlive(c));
        Assert.True(w.IsAlive(unrelated));
    }

    [Fact]
    public void ChildOf_CascadeFiresOnRemoveOnEachChild()
    {
        var w = new World();
        w.Tag<TagA>();
        int onRemoveCount = 0;
        w.Observer<TagA>(Event.OnRemove, it => onRemoveCount++);
        var p = w.CreateEntity();
        var c1 = w.CreateEntity(); w.Add<TagA>(c1); w.SetParent(c1, p);
        var c2 = w.CreateEntity(); w.Add<TagA>(c2); w.SetParent(c2, p);
        w.Delete(p);
        Assert.Equal(2, onRemoveCount);
    }

    [Fact]
    public void OnDelete_ComponentEntityRemovesFromHolders_Default()
    {
        var w = new World();
        var compEnt = w.Component<Position>();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        Assert.True(w.Has<Position>(e));
        // Deleting the component entity itself drops the component from
        // existing holders (default Remove policy).
        w.Delete(compEnt);
        Assert.True(w.IsAlive(e));
        Assert.False(w.Has<Position>(e));
    }

    [Fact]
    public void OnDelete_PanicPreventsDeletion()
    {
        var w = new World();
        var compEnt = w.Component<Position>();
        w.SetOnDelete(compEnt, DeletePolicy.Panic);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        Assert.Throws<InvalidOperationException>(() => w.Delete(compEnt));
        Assert.True(w.IsAlive(compEnt));
    }

    [Fact]
    public void Cascade_RecycledIdsAfterCascade()
    {
        var w = new World();
        var p = w.CreateEntity();
        var c = w.CreateEntity();
        w.SetParent(c, p);
        var pId = p.Id;
        var cId = c.Id;
        w.Delete(p);
        // Both ids should be recycled now.
        var fresh1 = w.CreateEntity();
        var fresh2 = w.CreateEntity();
        var freshIds = new[] { fresh1.Id, fresh2.Id };
        Assert.Contains(pId, freshIds);
        Assert.Contains(cId, freshIds);
    }

    [Fact]
    public void Cascade_FanOut()
    {
        var w = new World();
        var p = w.CreateEntity();
        var children = new EntityId[10];
        for (int i = 0; i < children.Length; i++)
        {
            children[i] = w.CreateEntity();
            w.SetParent(children[i], p);
        }
        w.Delete(p);
        Assert.All(children, c => Assert.False(w.IsAlive(c)));
    }

    // ===== Relation-side deletion (deleting the relation entity itself) =====

    [Fact]
    public void OnDelete_RelationDeleted_RemovesPairFromHolders_Default()
    {
        var w = new World();
        var rel = w.CreateEntity();
        var tgt = w.CreateEntity();
        var holder = w.CreateEntity();
        w.Add(holder, rel, tgt);
        Assert.True(w.Has(holder, w.Pair(rel, tgt)));
        w.Delete(rel);
        Assert.True(w.IsAlive(holder));
        Assert.False(w.Has(holder, w.Pair(rel, tgt)));
    }

    [Fact]
    public void OnDelete_RelationDeleted_DeletePolicyCascadesHolder()
    {
        var w = new World();
        var rel = w.CreateEntity();
        var tgt = w.CreateEntity();
        var holder = w.CreateEntity();
        w.Add(holder, rel, tgt);
        w.SetOnDelete(rel, DeletePolicy.Delete);
        w.Delete(rel);
        Assert.False(w.IsAlive(holder));
    }

    [Fact]
    public void OnDelete_RelationDeleted_PanicThrows()
    {
        var w = new World();
        var rel = w.CreateEntity();
        var tgt = w.CreateEntity();
        var holder = w.CreateEntity();
        w.Add(holder, rel, tgt);
        w.SetOnDelete(rel, DeletePolicy.Panic);
        Assert.Throws<InvalidOperationException>(() => w.Delete(rel));
        Assert.True(w.IsAlive(holder));
    }

    // ===== OnDeleteTarget matrix =====

    [Fact]
    public void OnDeleteTarget_DefaultRemovePairFromHolder()
    {
        var w = new World();
        var rel = w.CreateEntity();
        var tgt = w.CreateEntity();
        var holder = w.CreateEntity();
        w.Add(holder, rel, tgt);
        // Default policy when target deleted = Remove (drops pair).
        w.Delete(tgt);
        Assert.True(w.IsAlive(holder));
        Assert.False(w.Has(holder, w.Pair(rel, tgt)));
    }

    // ===== Multi-pair holder behavior =====

    [Fact]
    public void OnDelete_TargetDelete_OnlyRemovesAffectedPair()
    {
        var w = new World();
        var rel = w.CreateEntity();
        var t1 = w.CreateEntity();
        var t2 = w.CreateEntity();
        var holder = w.CreateEntity();
        w.Add(holder, rel, t1);
        w.Add(holder, rel, t2);
        w.Delete(t1);
        Assert.True(w.IsAlive(holder));
        Assert.False(w.Has(holder, w.Pair(rel, t1)));
        Assert.True(w.Has(holder, w.Pair(rel, t2)));
    }

    [Fact]
    public void OnDelete_DeletePolicyCascadesTransitively()
    {
        // A holds (R, B). B holds (R, C). R has Delete policy when target dies.
        // Deleting C cascades B (because (R,C) on B → Delete) → A (because
        // (R,B) on A → Delete).
        var w = new World();
        var rel = w.CreateEntity();
        w.SetOnDeleteTarget(rel, DeletePolicy.Delete);
        var c = w.CreateEntity();
        var b = w.CreateEntity();
        var a = w.CreateEntity();
        w.Add(b, rel, c);
        w.Add(a, rel, b);
        w.Delete(c);
        Assert.False(w.IsAlive(b));
        Assert.False(w.IsAlive(a));
    }

    [Fact]
    public void OnDelete_PanicAtDeepLevelStillThrows()
    {
        // Cascade into a holder with Panic should throw mid-cascade.
        var w = new World();
        var rel = w.CreateEntity();
        w.SetOnDeleteTarget(rel, DeletePolicy.Panic);
        var t = w.CreateEntity();
        var holder = w.CreateEntity();
        w.Add(holder, rel, t);
        Assert.Throws<InvalidOperationException>(() => w.Delete(t));
        Assert.True(w.IsAlive(holder));
        Assert.True(w.IsAlive(t));
    }
}
