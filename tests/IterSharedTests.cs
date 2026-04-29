using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class IterSharedTests
{
    [Fact]
    public void Iter_Field1_OwnReturnsCountSpan()
    {
        var w = new World();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0));
        var b = w.CreateEntity(); w.Set(b, new Position(2, 0));

        int totalLen = 0;
        bool anyShared = false;
        w.Query<Position>().Run((in Iter<Position> it) =>
        {
            totalLen += it.Field1().Length;
            if (it.IsShared1) anyShared = true;
        });
        Assert.Equal(2, totalLen);
        Assert.False(anyShared);
    }

    [Fact]
    public void Iter_Field1_SharedReturnsLength1Span()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(42, 0));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        int sharedSpanLen = 0;
        float sharedValue = 0f;
        w.Query<Position>().Inherited().Run((in Iter<Position> it) =>
        {
            if (!it.IsShared1) return;
            var span = it.Field1();
            sharedSpanLen = span.Length;
            sharedValue = span[0].X;
        });
        Assert.Equal(1, sharedSpanLen);
        Assert.Equal(42, sharedValue);
    }

    [Fact]
    public void Iter_At1_BroadcastsSharedAcrossAllRows()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(7, 7));
        // Multiple instances all sharing the prefab.
        var inst1 = w.CreateEntity(); w.SetIsA(inst1, prefab);
        var inst2 = w.CreateEntity(); w.SetIsA(inst2, prefab);
        var inst3 = w.CreateEntity(); w.SetIsA(inst3, prefab);

        var perEntity = new Dictionary<uint, float>();
        w.Query<Position>().Inherited().Run((in Iter<Position> it) =>
        {
            for (int r = 0; r < it.Count; r++)
                perEntity[it.Entity(r).Id] = it.At1(r).X;
        });
        Assert.Equal(7, perEntity[prefab.Id]);
        Assert.Equal(7, perEntity[inst1.Id]);
        Assert.Equal(7, perEntity[inst2.Id]);
        Assert.Equal(7, perEntity[inst3.Id]);
    }

    [Fact]
    public void Iter_MutatingShared_AffectsAllInheritors()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(0, 0));
        var inst1 = w.CreateEntity(); w.SetIsA(inst1, prefab);
        var inst2 = w.CreateEntity(); w.SetIsA(inst2, prefab);

        // Mutate via shared ref through Iter.
        w.Query<Position>().Inherited().Run((in Iter<Position> it) =>
        {
            // Find the shared-only table and bump its single value once.
            if (it.IsShared1)
            {
                ref var p = ref it.At1(0);
                p.X = 99;
            }
        });
        // All instances + prefab now see 99 (single shared cell).
        Assert.Equal(99, w.Get<Position>(prefab).X);
        Assert.Equal(99, w.Get<Position>(inst1).X);
        Assert.Equal(99, w.Get<Position>(inst2).X);
    }

    [Fact]
    public void Iter_TableEnumerator_VisitsInheritedTables()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(3, 3));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        int totalRows = 0;
        bool sawShared = false;
        foreach (var it in w.Query<Position>().Inherited())
        {
            totalRows += it.Count;
            if (it.IsShared1) sawShared = true;
        }
        Assert.Equal(2, totalRows);
        Assert.True(sawShared);
    }

    [Fact]
    public void Iter_TwoTerm_OneSharedOneOwn_AtAccessors()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(5, 5));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        w.Set(inst, new Velocity(2, 3));

        bool ranOnInst = false;
        w.Query<Position, Velocity>().Up<Position>().Run((in Iter<Position, Velocity> it) =>
        {
            // inst's table: Position shared, Velocity own.
            if (!it.IsShared1) return;
            Assert.False(it.IsShared2);
            for (int r = 0; r < it.Count; r++)
            {
                Assert.Equal(5, it.At1(r).X);
                Assert.Equal(2, it.At2(r).Dx);
            }
            ranOnInst = true;
        });
        Assert.True(ranOnInst);
    }
}
