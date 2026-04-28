using Xunit;

namespace Flecs.Tests;

public class EntityTests
{
    [Fact]
    public void CreateEntity_AssignsValidHandle()
    {
        var w = new World();
        var e = w.CreateEntity();
        Assert.True(e.IsValid);
        Assert.True(w.IsAlive(e));
    }

    [Fact]
    public void CreateEntity_UniqueIdsForDifferentEntities()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        Assert.NotEqual(a.Id, b.Id);
    }

    [Fact]
    public void Delete_MarksEntityNotAlive()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Delete(e);
        Assert.False(w.IsAlive(e));
    }

    [Fact]
    public void Delete_RecyclesIdWithBumpedGeneration()
    {
        var w = new World();
        var e = w.CreateEntity();
        var origId = e.Id;
        var origGen = e.Generation;
        w.Delete(e);
        var reused = w.CreateEntity();
        Assert.Equal(origId, reused.Id);
        Assert.NotEqual(origGen, reused.Generation);
        Assert.True(w.IsAlive(reused));
        // Stale handle still reads dead.
        Assert.False(w.IsAlive(e));
    }

    [Fact]
    public void IsAlive_StaleGenerationReturnsFalse()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Delete(e);
        w.CreateEntity(); // recycle
        Assert.False(w.IsAlive(e));
    }

    [Fact]
    public void Delete_DeadEntityIsNoop()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Delete(e);
        w.Delete(e); // second delete on dead handle: no throw
        Assert.False(w.IsAlive(e));
    }

    [Fact]
    public void AliveCount_TracksLiveEntities()
    {
        var w = new World();
        int baseline = w.AliveCount; // accounts for reserved entities
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        Assert.Equal(baseline + 2, w.AliveCount);
        w.Delete(a);
        Assert.Equal(baseline + 1, w.AliveCount);
    }

    [Fact]
    public void DefaultEntityIdNotValid()
    {
        EntityId zero = default;
        Assert.False(zero.IsValid);
    }
}
