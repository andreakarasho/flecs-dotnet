using Xunit;

namespace Flecs.Tests;

public class WorldInfoTests
{
    [Fact]
    public void GetInfo_FreshWorld_HasReservedEntitiesOnly()
    {
        var w = new World();
        var info = w.GetInfo();
        // Builtin reserved entities (Wildcard, ChildOf, IsA, ...) count as alive.
        Assert.True(info.AliveEntities > 0);
        Assert.Equal(0, info.RecycledEntities);
        Assert.Equal(0, info.SystemCount);
        Assert.Equal(0, info.SparseCount);
        Assert.Equal(0, info.UnionCount);
        Assert.Equal(0, info.FrameCount);
        Assert.Equal(0f, info.LastDeltaTime);
        Assert.Equal(0.0, info.TotalTime);
    }

    [Fact]
    public void GetInfo_TracksAliveAndRecycled()
    {
        var w = new World();
        var before = w.GetInfo().AliveEntities;
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        var afterCreate = w.GetInfo();
        Assert.Equal(before + 2, afterCreate.AliveEntities);
        w.Delete(a);
        var afterDelete = w.GetInfo();
        Assert.Equal(before + 1, afterDelete.AliveEntities);
        Assert.Equal(1, afterDelete.RecycledEntities);
    }

    [Fact]
    public void GetInfo_TablesAndComponents()
    {
        var w = new World();
        w.Component<Position>();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        var info = w.GetInfo();
        Assert.True(info.TableCount >= 1);
        Assert.True(info.ComponentCount >= 1);
    }

    [Fact]
    public void GetInfo_TraitCounts()
    {
        var w = new World();
        w.Component<Position>();
        w.Component<Velocity>();
        w.MarkSparse<Position>();
        w.MarkCanToggle<Velocity>();
        w.MarkUnion<Likes>();
        var info = w.GetInfo();
        Assert.Equal(1, info.SparseCount);
        Assert.Equal(1, info.CanToggleCount);
        Assert.Equal(1, info.UnionCount);
    }

    [Fact]
    public void GetInfo_FrameStatsBumpedByProgress()
    {
        var w = new World();
        var i0 = w.GetInfo();
        Assert.Equal(0, i0.FrameCount);
        w.Progress(0.016f);
        w.Progress(0.020f);
        var i1 = w.GetInfo();
        Assert.Equal(2, i1.FrameCount);
        Assert.Equal(0.020f, i1.LastDeltaTime);
        Assert.Equal(0.036, i1.TotalTime, 5);
    }

    [Fact]
    public void GetInfo_SystemCount()
    {
        var w = new World();
        w.System("a", w.Phases.OnUpdate, _ => { });
        w.System("b", w.Phases.OnUpdate, _ => { });
        Assert.Equal(2, w.GetInfo().SystemCount);
    }

    [Fact]
    public void GetInfo_EmptyTableCount()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        var before = w.GetInfo().EmptyTableCount;
        w.Delete(e);
        var after = w.GetInfo();
        // Table that held Position is now empty.
        Assert.True(after.EmptyTableCount >= before);
    }
}
