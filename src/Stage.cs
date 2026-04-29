using System.Collections.Generic;

namespace Flecs;

// ============================================================================
// Stage — per-worker command buffer. While a stage is the current ThreadStatic
// stage, every structural mutation issued through World (Add / Remove / Set /
// Delete / Toggle) is appended to this stage's queue rather than applied. The
// world flushes stages in registration order at wave-merge points. Mirrors
// flecs ecs_stage_t.
//
// Stages are reused across Progress calls; their queues drain on Flush. Reads
// against the world remain valid throughout — readonly mode is in force during
// parallel wave execution, so structural state is stable.
// ============================================================================
public sealed class Stage
{
    internal readonly World World;
    public int Id { get; }
    internal readonly List<Command> Queue = new();

    [System.ThreadStatic] private static Stage? _current;
    internal static Stage? Current => _current;

    internal Stage(World world, int id) { World = world; Id = id; }

    internal static void SetCurrent(Stage s) => _current = s;
    internal static void ClearCurrent() => _current = null;

    // Drain queued commands into the world. Caller is the world's main thread
    // post-barrier; defer/readonly are inactive so commands apply directly.
    internal void Flush()
    {
        var q = Queue;
        for (int i = 0; i < q.Count; i++)
        {
            q[i].Apply(World);
            q[i].Recycle();
        }
        q.Clear();
    }

    // Public mutation API mirroring World — for callers that hold a stage
    // reference directly. Workers usually rely on the ThreadStatic + the
    // existing World.Add/Remove/Set/Delete routing, so these are convenience
    // entry points only.
    public void Add<T>(EntityId entity) where T : struct
    {
        var ent = World.IdOf<T>();
        Queue.Add(AddIdCmd.Rent(entity, ent));
    }

    public void Add(EntityId entity, Id id) => Queue.Add(AddIdCmd.Rent(entity, id));

    public void Remove<T>(EntityId entity) where T : struct
    {
        var ent = World.IdOf<T>();
        Queue.Add(RemoveIdCmd.Rent(entity, ent));
    }

    public void Remove(EntityId entity, Id id) => Queue.Add(RemoveIdCmd.Rent(entity, id));

    public void Set<T>(EntityId entity, T value) where T : struct
        => Queue.Add(SetCmd<T>.Rent(entity, value));

    public void Delete(EntityId entity) => Queue.Add(DeleteCmd.Rent(entity));
}
