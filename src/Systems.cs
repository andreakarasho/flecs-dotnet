namespace Flecs;

// ============================================================================
// Modules — packaged registration. Implement IModule.Build to register
// components, systems, observers, etc. Imported once per World; reimport is a
// no-op. Mirrors flecs ECS_IMPORT.
// ============================================================================
public interface IModule { void Build(World world); }

// ============================================================================
// Systems — System = (name, phase, action). Progress dispatches phases in
// builtin order; within a phase, the pipeline DAG groups systems into waves
// by r/w conflict and runs each wave (concurrent if workers configured).
// Mirrors flecs ecs_system_desc_t (subset).
// ============================================================================
public delegate void SystemAction(Iter iter);

// Per-dispatch context handed to a system body. Carries world, the running
// SystemHandle, and the frame's delta time. Mirrors flecs.NET `Iter` (the
// shape of ecs_iter_t-wrapper passed into system callbacks). Keeps system
// identity explicit instead of stashing it in ThreadStatic state.
public readonly ref struct Iter
{
    public World World { get; }
    public SystemHandle System { get; }
    public float DeltaTime { get; }

    internal Iter(World world, SystemHandle system, float deltaTime)
    {
        World = world;
        System = system;
        DeltaTime = deltaTime;
    }

    // Typed user-context accessor — equivalent to (T)System.Ctx. Throws
    // InvalidOperationException if ctx is null or not assignable to T.
    // No reference-type constraint: value-type ctx works (stored boxed
    // inside SystemHandle.Ctx; unbox per call).
    public T Ctx<T>()
    {
        if (System.Ctx is T t) return t;
        ThrowHelper.SystemCtxWrongType(typeof(T));
        return default!;
    }
}

public sealed class SystemHandle
{
    public string Name { get; }
    public EntityId Phase { get; internal set; }
    // Backing entity. Tagged with the reserved System id; user can add custom
    // tags to opt the system in/out of pipeline filters. Pipeline matching
    // checks (withIds present AND withoutIds absent) on this entity.
    public EntityId Entity { get; internal set; }
    public bool Enabled { get; private set; } = true;
    // True when caller asserts the action is safe to run concurrently with
    // other ParallelSafe systems whose r/w sets don't conflict. Default false:
    // unknown side effects → serialize. System<T...> sugar sets this true.
    public bool ParallelSafe { get; private set; }
    // Optional tick source — system runs only on Progress calls where the
    // bound source's TickSource.Tick is true. Default 0 = run every Progress.
    // Sources are timers (world.Timer) or rate filters (world.Rate).
    public EntityId TickSource { get; private set; }
    // Optional user context. Stashed alongside the handle; readable inside
    // the system body via Iter.System.Ctx or iter.Ctx<T>().
    // Mirrors flecs ecs_system_desc_t.ctx.
    public object? Ctx { get; private set; }

    // Pipeline-internal plumbing: Action delegate plus r/w component-id sets
    // used by the DAG. Default empty (treated as "writes anything" —
    // pessimistic, forces serialization). System<T...> sugar populates
    // WriteIds from typed args; user can swap via SetReads / SetWrites.
    internal SystemAction Action { get; set; }
    internal Id[] ReadIds { get; private set; } = Array.Empty<Id>();
    internal Id[] WriteIds { get; private set; } = Array.Empty<Id>();

    internal SystemHandle(string name, EntityId phase, SystemAction action)
    { Name = name; Phase = phase; Action = action; }

    // Fluent setters — all return this for chain. Direct setters intentionally
    // absent: a SystemHandle is configured at registration time, not mutated
    // ad-hoc. The only field flecs typically mutates post-create is Ctx, which
    // is exposed read-only here; SetCtx replaces it.
    public SystemHandle SetReads(params Id[] ids) { ReadIds = ids; return this; }
    public SystemHandle SetWrites(params Id[] ids) { WriteIds = ids; return this; }
    public SystemHandle SetParallelSafe(bool v = true) { ParallelSafe = v; return this; }
    public SystemHandle SetCtx(object? ctx) { Ctx = ctx; return this; }
    public SystemHandle SetEnabled(bool v) { Enabled = v; return this; }
    public SystemHandle SetTickSource(EntityId t) { TickSource = t; return this; }

    // Conflict: this writes to anything other reads or writes, or vice versa.
    internal bool ConflictsWith(SystemHandle other)
    {
        if (Overlaps(WriteIds, other.ReadIds)) return true;
        if (Overlaps(WriteIds, other.WriteIds)) return true;
        if (Overlaps(ReadIds, other.WriteIds)) return true;
        return false;
    }

    private static bool Overlaps(Id[] a, Id[] b)
    {
        if (a.Length == 0 || b.Length == 0) return false;
        // Sorted ascending — merge-style scan.
        int i = 0, j = 0;
        while (i < a.Length && j < b.Length)
        {
            if (a[i] == b[j]) return true;
            if (a[i].Value < b[j].Value) i++;
            else j++;
        }
        return false;
    }
}
