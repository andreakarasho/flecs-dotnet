using Xunit;
using System.Runtime.CompilerServices;

namespace Flecs.Tests;

// Sparse trait — non-fragmenting per-component storage. Set/Get/Has/Owns/
// Remove route through SparseStorage<T> instead of archetype columns. Adding
// a sparse component does not migrate the entity. Iteration over sparse-only
// queries is NYI (Query<SparseT> matches nothing currently).
public class SparseStorageTests
{
    [Fact]
    public void MarkSparse_FlagsTrait()
    {
        var w = new World();
        w.Component<Position>();
        Assert.False(w.IsSparse<Position>());
        w.MarkSparse<Position>();
        Assert.True(w.IsSparse<Position>());
    }

    [Fact]
    public void Set_NoArchetypeMigration()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        var e = w.CreateEntity();
        int tablesBefore = w.TableCount;
        w.Set(e, new Position(3, 4));
        Assert.Equal(tablesBefore, w.TableCount);
    }

    [Fact]
    public void Get_RoundTrip()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        var e = w.CreateEntity();
        w.Set(e, new Position(7, 9));
        Assert.Equal(7, w.Get<Position>(e).X);
        Assert.Equal(9, w.Get<Position>(e).Y);
    }

    [Fact]
    public void Has_TrueAfterSet_FalseBefore()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        var e = w.CreateEntity();
        Assert.False(w.Has<Position>(e));
        Assert.False(w.Owns<Position>(e));
        w.Set(e, new Position(0, 0));
        Assert.True(w.Has<Position>(e));
        Assert.True(w.Owns<Position>(e));
    }

    [Fact]
    public void Remove_DropsEntry()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        w.Remove<Position>(e);
        Assert.False(w.Has<Position>(e));
    }

    [Fact]
    public void TryGetRef_PresentMutates()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 1));
        ref var p = ref w.TryGetRef<Position>(e);
        Assert.False(Unsafe.IsNullRef(ref p));
        p.X = 99;
        Assert.Equal(99, w.Get<Position>(e).X);
    }

    [Fact]
    public void TryGetRef_AbsentNullRef()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        var e = w.CreateEntity();
        ref var p = ref w.TryGetRef<Position>(e);
        Assert.True(Unsafe.IsNullRef(ref p));
    }

    [Fact]
    public void TryGetComponent_PresentAndAbsent()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        var e = w.CreateEntity();
        Assert.False(w.TryGetComponent<Position>(e, out _));
        w.Set(e, new Position(5, 6));
        Assert.True(w.TryGetComponent<Position>(e, out var v));
        Assert.Equal(new Position(5, 6), v);
    }

    [Fact]
    public void Set_IdempotentOverwrite()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 1));
        w.Set(e, new Position(2, 2));
        Assert.Equal(2, w.Get<Position>(e).X);
    }

    [Fact]
    public void Delete_CleansSparseEntry()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(1, 0));
        w.Set(b, new Position(2, 0));
        w.Delete(a);
        Assert.False(w.Has<Position>(a));
        Assert.True(w.Has<Position>(b));
        Assert.Equal(2, w.Get<Position>(b).X);
    }

    [Fact]
    public void OnSet_HookFires()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        int hits = 0;
        w.Hooks<Position>().SetOnSet((World _, EntityId _, ref Position _) => hits++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Position(1, 1));
        Assert.Equal(2, hits);
    }

    [Fact]
    public void OnAdd_HookFiresOnceOnFirstSet()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        int adds = 0;
        w.Hooks<Position>().SetOnAdd((World _, EntityId _, ref Position _) => adds++);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Position(1, 1));
        Assert.Equal(1, adds);
    }

    [Fact]
    public void OnRemove_HookFiresOnRemoveAndDelete()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        int rms = 0;
        w.Hooks<Position>().SetOnRemove((World _, EntityId _, ref Position _) => rms++);
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(b, new Position(0, 0));
        w.Remove<Position>(a);
        w.Delete(b);
        Assert.Equal(2, rms);
    }

    [Fact]
    public void Sparse_AcrossManyEntities_ScalesAndSwapsBack()
    {
        var w = new World();
        w.Component<Health>();
        w.MarkSparse<Health>();
        var ents = new EntityId[200];
        for (int i = 0; i < ents.Length; i++)
        {
            ents[i] = w.CreateEntity();
            w.Set(ents[i], new Health(i));
        }
        // Remove every other entity's sparse entry; remaining must keep values.
        for (int i = 0; i < ents.Length; i += 2)
            w.Remove<Health>(ents[i]);
        for (int i = 1; i < ents.Length; i += 2)
            Assert.Equal(i, w.Get<Health>(ents[i]).Value);
        for (int i = 0; i < ents.Length; i += 2)
            Assert.False(w.Has<Health>(ents[i]));
    }

    [Fact]
    public void Sparse_DoesNotAddIdToArchetype()
    {
        var w = new World();
        w.Component<Position>();
        w.Component<Velocity>();
        w.MarkSparse<Position>();
        var e = w.CreateEntity();
        w.Set(e, new Velocity(1, 1));
        int tablesBefore = w.TableCount;
        w.Set(e, new Position(2, 3));
        // No new archetype — sparse comp lives outside archetype.
        Assert.Equal(tablesBefore, w.TableCount);
        Assert.True(w.Has<Position>(e));
        Assert.True(w.Has<Velocity>(e));
    }
}
