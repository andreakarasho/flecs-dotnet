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

    // ===== Lifecycle edges =====

    [Fact]
    public void Delete_RemovesAllComponents()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 1));
        w.Add<TagA>(e);
        w.Delete(e);
        // Recycle slot — fresh handle has no components.
        var fresh = w.CreateEntity();
        Assert.False(w.Has<Position>(fresh));
        Assert.False(w.Has<TagA>(fresh));
    }

    [Fact]
    public void Has_OnDeadEntity_ReturnsFalse()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Add<TagA>(e);
        w.Delete(e);
        Assert.False(w.Has<TagA>(e));
    }

    [Fact]
    public void Add_OnDeadEntity_Throws()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Delete(e);
        Assert.Throws<System.InvalidOperationException>(() => w.Add<TagA>(e));
    }

    [Fact]
    public void Set_OnDeadEntity_Throws()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Delete(e);
        Assert.Throws<System.InvalidOperationException>(() => w.Set(e, new Position(0, 0)));
    }

    [Fact]
    public void Recycle_StaleHandleNotMistakenForReused()
    {
        // Stale handle (old gen) and fresh handle (new gen) share id but
        // diverge by generation; mutations via stale handle must throw.
        var w = new World();
        var stale = w.CreateEntity();
        w.Delete(stale);
        var fresh = w.CreateEntity();
        Assert.Equal(stale.Id, fresh.Id);
        Assert.NotEqual(stale.Generation, fresh.Generation);
        Assert.False(w.IsAlive(stale));
        Assert.True(w.IsAlive(fresh));
        Assert.Throws<System.InvalidOperationException>(() => w.Add<TagA>(stale));
    }

    [Fact]
    public void Clone_EntityIsDistinct()
    {
        var w = new World();
        var src = w.CreateEntity();
        w.Set(src, new Position(1, 2));
        var dst = w.Clone(src);
        Assert.NotEqual(src.Id, dst.Id);
        Assert.Equal(w.Get<Position>(src), w.Get<Position>(dst));
        // Mutation on clone must not affect source.
        w.Get<Position>(dst) = new Position(99, 99);
        Assert.Equal(new Position(1, 2), w.Get<Position>(src));
    }

    [Fact]
    public void Clone_EmptyEntityYieldsEmptyClone()
    {
        var w = new World();
        var src = w.CreateEntity();
        var dst = w.Clone(src);
        Assert.True(w.IsAlive(dst));
        Assert.False(w.Has<Position>(dst));
    }

    [Fact]
    public void IsValid_DistinctFromIsAlive()
    {
        var w = new World();
        var e = w.CreateEntity();
        Assert.True(e.IsValid);
        Assert.True(w.IsAlive(e));
        w.Delete(e);
        // The handle still encodes a non-zero id (IsValid checks id > 0).
        Assert.True(e.IsValid);
        Assert.False(w.IsAlive(e));
    }
}
