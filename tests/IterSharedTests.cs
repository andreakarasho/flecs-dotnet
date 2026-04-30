using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class IterSharedTests
{
    [Fact]
    public void Shared_OwnIteration_NoSharedFlag()
    {
        var w = new World();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0));
        var b = w.CreateEntity(); w.Set(b, new Position(2, 0));

        int rows = 0;
        bool anyShared = false;
        foreach (var row in w.Query<Position>())
        {
            rows++;
            if (row.IsShared1) anyShared = true;
        }
        Assert.Equal(2, rows);
        Assert.False(anyShared);
    }

    [Fact]
    public void Shared_InheritedFlag_SetForSharedTerm()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(42, 0));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);

        bool sawShared = false;
        float sharedValue = 0f;
        foreach (var row in w.Query<Position>().Inherited())
        {
            if (row.IsShared1)
            {
                sawShared = true;
                sharedValue = row.Component1.Value.X;
            }
        }
        Assert.True(sawShared);
        Assert.Equal(42, sharedValue);
    }

    [Fact]
    public void Shared_BroadcastsAcrossAllInheritors()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(7, 7));
        var inst1 = w.CreateEntity(); w.SetIsA(inst1, prefab);
        var inst2 = w.CreateEntity(); w.SetIsA(inst2, prefab);
        var inst3 = w.CreateEntity(); w.SetIsA(inst3, prefab);

        int instRows = 0;
        float instX = 0f;
        foreach (var row in w.Query<Position>().Inherited())
        {
            if (row.IsShared1) { instRows++; instX = row.Component1.Value.X; }
        }
        Assert.Equal(3, instRows); // inst1, inst2, inst3 all see shared
        Assert.Equal(7, instX);
    }

    [Fact]
    public void Shared_MutatingPrefab_AffectsAllInheritors()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(0, 0));
        var inst1 = w.CreateEntity(); w.SetIsA(inst1, prefab);
        var inst2 = w.CreateEntity(); w.SetIsA(inst2, prefab);

        w.Get<Position>(prefab).X = 99; // direct mutation on prefab
        Assert.Equal(99, w.Get<Position>(prefab).X);
        Assert.Equal(99, w.Get<Position>(inst1).X);
        Assert.Equal(99, w.Get<Position>(inst2).X);
    }

    [Fact]
    public void Shared_TwoTerm_OneSharedOneOwn()
    {
        var w = new World();
        var prefab = w.CreateEntity();
        w.Set(prefab, new Position(5, 5));
        var inst = w.CreateEntity();
        w.SetIsA(inst, prefab);
        w.Set(inst, new Velocity(2, 3));

        bool ranOnInst = false;
        foreach (var row in w.Query<Position, Velocity>().Up<Position>())
        {
            // inst's table: Position shared, Velocity own.
            if (!row.IsShared1) continue;
            Assert.False(row.IsShared2);
            ref var p = ref row.Component1.Value;
            ref var v = ref row.Component2.Value;
            Assert.Equal(5, p.X);
            Assert.Equal(2, v.Dx);
            ranOnInst = true;
        }
        Assert.True(ranOnInst);
    }
}
