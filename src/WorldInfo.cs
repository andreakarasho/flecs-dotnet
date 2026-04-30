namespace Flecs;

// ============================================================================
// WorldInfo — snapshot of world counters + frame stats. Mirrors flecs
// ecs_world_info_t. Read via world.GetInfo(). Returns by value; safe to
// stash. Does not auto-update — call again for fresh numbers.
//
// Counts:
//   AliveEntities    — currently live (excludes recycled slots).
//   RecycledEntities — slots in the recycle stack waiting reuse.
//   TableCount       — archetype table count (excludes root sentinel).
//   EmptyTableCount  — subset of tables with Count == 0.
//   ComponentCount   — value-bearing components registered.
//   TagCount         — tag entities registered (no value).
//   SystemCount      — registered systems across all phases.
//   CanToggleCount   — components opted into bitset toggle storage.
//   SparseCount      — components opted into sparse-set storage.
//   UnionCount       — relations opted into Union (1-of-N) semantics.
//
// Frame stats (bumped by Progress):
//   FrameCount     — Progress invocations since world ctor.
//   LastDeltaTime  — deltaTime arg of most recent Progress.
//   TotalTime      — accumulated deltaTime across all Progress calls.
// ============================================================================
public readonly struct WorldInfo
{
    public int AliveEntities { get; init; }
    public int RecycledEntities { get; init; }
    public int TableCount { get; init; }
    public int EmptyTableCount { get; init; }
    public int ComponentCount { get; init; }
    public int TagCount { get; init; }
    public int SystemCount { get; init; }
    public int CanToggleCount { get; init; }
    public int SparseCount { get; init; }
    public int UnionCount { get; init; }
    public long FrameCount { get; init; }
    public float LastDeltaTime { get; init; }
    public double TotalTime { get; init; }
}
