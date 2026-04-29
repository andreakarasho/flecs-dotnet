using System.Threading;
using Xunit;

namespace Flecs.Tests;

// Pipeline DAG + r/w metadata + Stage routing + parallel wave execution.
public class PipelineTests
{
    [Fact]
    public void Query_ReadIds_DefaultEmpty()
    {
        var w = new World();
        var q = w.Query<Position, Velocity>();
        Assert.Empty(q.ReadIds);
        Assert.Equal(2, q.WriteIds.Length);
    }

    [Fact]
    public void Query_Read_MovesTermFromWriteToRead()
    {
        var w = new World();
        var q = w.Query<Position, Velocity>().Read<Velocity>();
        Assert.Single(q.ReadIds);
        Assert.Single(q.WriteIds);
        Assert.Equal(w.IdOf<Velocity>(), q.ReadIds[0]);
        Assert.Equal(w.IdOf<Position>(), q.WriteIds[0]);
    }

    [Fact]
    public void Pipeline_NoConflict_PacksIntoSingleWave()
    {
        var w = new World();
        // Two systems on same phase, disjoint write sets → one wave.
        var qa = w.Query<Position>();
        var qb = w.Query<Velocity>();
        w.System("A", w.OnUpdate, qa, (EntityId _, ref Position _p) => { });
        w.System("B", w.OnUpdate, qb, (EntityId _, ref Velocity _v) => { });
        var waves = w.GetPhaseWaves(w.OnUpdate);
        Assert.Single(waves);
        Assert.Equal(2, waves[0].Count);
    }

    [Fact]
    public void Pipeline_WriteWriteConflict_SeparatesIntoWaves()
    {
        var w = new World();
        var qa = w.Query<Position>();
        var qb = w.Query<Position>();
        w.System("A", w.OnUpdate, qa, (EntityId _, ref Position _p) => { });
        w.System("B", w.OnUpdate, qb, (EntityId _, ref Position _p) => { });
        var waves = w.GetPhaseWaves(w.OnUpdate);
        Assert.Equal(2, waves.Count);
    }

    [Fact]
    public void Pipeline_ReadRead_PacksIntoSingleWave()
    {
        var w = new World();
        var qa = w.Query<Position>().Read<Position>();
        var qb = w.Query<Position>().Read<Position>();
        w.System("A", w.OnUpdate, qa, (EntityId _, ref Position _p) => { });
        w.System("B", w.OnUpdate, qb, (EntityId _, ref Position _p) => { });
        var waves = w.GetPhaseWaves(w.OnUpdate);
        Assert.Single(waves);
        Assert.Equal(2, waves[0].Count);
    }

    [Fact]
    public void Pipeline_ReadWrite_Conflict()
    {
        var w = new World();
        var qa = w.Query<Position, Velocity>().Read<Velocity>();
        var qb = w.Query<Velocity>(); // writes Velocity
        w.System("A", w.OnUpdate, qa, (EntityId _, ref Position _p, ref Velocity _v) => { });
        w.System("B", w.OnUpdate, qb, (EntityId _, ref Velocity _v) => { });
        var waves = w.GetPhaseWaves(w.OnUpdate);
        Assert.Equal(2, waves.Count);
    }

    [Fact]
    public void Progress_Sequential_RunsAllSystems()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Velocity(1, 2));
        w.System<Position, Velocity>("Move", w.OnUpdate,
            (EntityId _, ref Position p, ref Velocity v) =>
            { p.X += v.Dx; p.Y += v.Dy; });
        w.Progress(0f);
        Assert.Equal(new Position(1, 2), w.Get<Position>(e));
    }

    [Fact]
    public void Progress_Parallel_NonConflicting_ProducesSameResult()
    {
        var w = new World();
        // Pre-register components so worker threads don't race on type lookup.
        w.Component<Position>(); w.Component<Velocity>();
        w.UseWorkers(2);
        var ents = new EntityId[1000];
        for (int i = 0; i < ents.Length; i++)
        {
            ents[i] = w.CreateEntity();
            w.Set(ents[i], new Position(0, 0));
            w.Set(ents[i], new Velocity(1, 1));
        }
        // Two disjoint-write systems — pack into one wave, run parallel.
        w.System<Position>("BumpPos", w.OnUpdate,
            (EntityId _, ref Position p) => { p.X += 1; });
        w.System<Velocity>("BumpVel", w.OnUpdate,
            (EntityId _, ref Velocity v) => { v.Dx += 1; });
        for (int i = 0; i < 5; i++) w.Progress(0f);
        for (int i = 0; i < ents.Length; i++)
        {
            Assert.Equal(5f, w.Get<Position>(ents[i]).X);
            Assert.Equal(6f, w.Get<Velocity>(ents[i]).Dx);
        }
    }

    [Fact]
    public void Stage_QueuesMutations_FlushedAfterWave()
    {
        var w = new World();
        w.Component<Position>(); w.Component<Velocity>();
        w.UseWorkers(2);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        // System adds Velocity inside parallel wave — must queue, not race.
        w.System<Position>("AddVel", w.OnUpdate,
            (EntityId ent, ref Position _p) => { w.Set(ent, new Velocity(7, 8)); });
        w.Progress(0f);
        Assert.True(w.Owns<Velocity>(e));
        Assert.Equal(new Velocity(7, 8), w.Get<Velocity>(e));
    }

    [Fact]
    public void UseWorkers_Zero_RevertsToSequential()
    {
        var w = new World();
        w.UseWorkers(2);
        w.UseWorkers(0);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.System<Position>("Bump", w.OnUpdate,
            (EntityId _, ref Position p) => { p.X += 1; });
        w.Progress(0f);
        Assert.Equal(1f, w.Get<Position>(e).X);
    }

    [Fact]
    public void Pipeline_NotParallelSafe_AlwaysOwnWave()
    {
        var w = new World();
        // System() factory (no typed sugar) defaults ParallelSafe = false.
        w.System("A", w.OnUpdate, (World _, float _dt) => { });
        w.System("B", w.OnUpdate, (World _, float _dt) => { });
        var waves = w.GetPhaseWaves(w.OnUpdate);
        Assert.Equal(2, waves.Count);
    }
}
