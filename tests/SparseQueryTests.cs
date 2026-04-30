using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

// Iteration over queries that include sparse terms. Sparse terms don't gate
// archetype match — RowEnumerator filter path checks SparseStorage.Has per
// row + resolves Ptr<T> via SparseStorage.GetRef.
public class SparseQueryTests
{
    [Fact]
    public void PureSparse_IteratesHoldersOnly()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0));
        var b = w.CreateEntity(); // no Position
        var c = w.CreateEntity(); w.Set(c, new Position(3, 0));
        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position>())
            seen.Add(row.Entity.Id);
        Assert.Contains(a.Id, seen);
        Assert.Contains(c.Id, seen);
        Assert.DoesNotContain(b.Id, seen);
    }

    [Fact]
    public void PureSparse_RefMutationPersists()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0));
        var b = w.CreateEntity(); w.Set(b, new Position(2, 0));
        foreach (var row in w.Query<Position>())
            row.Component1.Value.X *= 10f;
        Assert.Equal(10f, w.Get<Position>(a).X);
        Assert.Equal(20f, w.Get<Position>(b).X);
    }

    [Fact]
    public void Mixed_ArchetypeAndSparse_IntersectionOnly()
    {
        var w = new World();
        w.Component<Position>();
        w.Component<Velocity>();
        w.MarkSparse<Velocity>();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0)); w.Set(a, new Velocity(10, 0));
        var b = w.CreateEntity(); w.Set(b, new Position(2, 0)); // no Velocity
        var c = w.CreateEntity(); w.Set(c, new Velocity(30, 0)); // no Position
        var byEnt = new Dictionary<uint, (float, float)>();
        foreach (var (p, v) in w.Query<Position, Velocity>())
        {
            // Capture in closure-friendly form.
        }
        // Re-iterate to populate (refs can't be captured across closures).
        foreach (var row in w.Query<Position, Velocity>())
            byEnt[row.Entity.Id] = (row.Component1.Value.X, row.Component2.Value.Dx);
        Assert.Single(byEnt);
        Assert.True(byEnt.ContainsKey(a.Id));
        Assert.Equal((1f, 10f), byEnt[a.Id]);
    }

    [Fact]
    public void Mixed_IteratesAcrossArchetypes()
    {
        var w = new World();
        w.Component<Position>();
        w.Component<Velocity>();
        w.MarkSparse<Velocity>();
        // Multiple archetypes containing Position.
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0)); w.Add<TagA>(a); w.Set(a, new Velocity(7, 0));
        var b = w.CreateEntity(); w.Set(b, new Position(2, 0)); w.Set(b, new Velocity(8, 0));
        var c = w.CreateEntity(); w.Set(c, new Position(3, 0)); // no Velocity
        var dxByEnt = new Dictionary<uint, float>();
        foreach (var row in w.Query<Position, Velocity>())
            dxByEnt[row.Entity.Id] = row.Component2.Value.Dx;
        Assert.Equal(2, dxByEnt.Count);
        Assert.Equal(7f, dxByEnt[a.Id]);
        Assert.Equal(8f, dxByEnt[b.Id]);
    }

    [Fact]
    public void Sparse_RespectsWithoutFilter()
    {
        var w = new World();
        w.Component<Position>();
        w.Component<Velocity>();
        w.MarkSparse<Velocity>();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0)); w.Set(a, new Velocity(10, 0));
        var b = w.CreateEntity(); w.Set(b, new Position(2, 0)); w.Set(b, new Velocity(20, 0));
        w.Add<Boss>(b);
        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position, Velocity>().Without<Boss>())
            seen.Add(row.Entity.Id);
        Assert.Contains(a.Id, seen);
        Assert.DoesNotContain(b.Id, seen);
    }

    [Fact]
    public void Sparse_RespectsWithFilter()
    {
        var w = new World();
        w.Component<Position>();
        w.Component<Velocity>();
        w.MarkSparse<Velocity>();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0)); w.Set(a, new Velocity(10, 0));
        var b = w.CreateEntity(); w.Set(b, new Position(2, 0)); w.Set(b, new Velocity(20, 0));
        w.Add<TagA>(a);
        var seen = new HashSet<uint>();
        foreach (var row in w.Query<Position, Velocity>().With<TagA>())
            seen.Add(row.Entity.Id);
        Assert.Single(seen);
        Assert.Contains(a.Id, seen);
    }

    [Fact]
    public void Sparse_RemoveDuringIteration_DefersUntilExit()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0));
        var b = w.CreateEntity(); w.Set(b, new Position(2, 0));
        int seen = 0;
        foreach (var row in w.Query<Position>())
        {
            seen++;
            // Mutate during iter — readonly scope queues.
            w.Remove<Position>(row.Entity);
        }
        Assert.Equal(2, seen);
        Assert.False(w.Has<Position>(a));
        Assert.False(w.Has<Position>(b));
    }

    [Fact]
    public void Sparse_HigherArity_Arity4()
    {
        var w = new World();
        w.Component<Position>(); w.Component<Velocity>();
        w.Component<Health>(); w.Component<Mana>();
        w.MarkSparse<Health>();
        w.MarkSparse<Mana>();
        var a = w.CreateEntity();
        w.Set(a, new Position(0, 0)); w.Set(a, new Velocity(0, 0));
        w.Set(a, new Health(7)); w.Set(a, new Mana(8));
        var b = w.CreateEntity();
        w.Set(b, new Position(0, 0)); w.Set(b, new Velocity(0, 0));
        w.Set(b, new Health(9)); // no Mana
        int hits = 0;
        int sum = 0;
        foreach (var row in w.Query<Position, Velocity, Health, Mana>())
        {
            hits++;
            sum += row.Component3.Value.Value + row.Component4.Value.Value;
        }
        Assert.Equal(1, hits);
        Assert.Equal(7 + 8, sum);
    }

    [Fact]
    public void Sparse_DenseChange_NewEntitiesPickedUp()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkSparse<Position>();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0));
        var q = w.Query<Position>();
        int firstPass = 0;
        foreach (var _ in q) firstPass++;
        Assert.Equal(1, firstPass);
        var b = w.CreateEntity(); w.Set(b, new Position(2, 0));
        int secondPass = 0;
        foreach (var _ in q) secondPass++;
        Assert.Equal(2, secondPass);
    }
}
