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
public delegate void SystemAction(World world, float deltaTime);

public sealed class SystemHandle
{
    public string Name { get; }
    public EntityId Phase { get; internal set; }
    public SystemAction Action { get; internal set; }
    public bool Enabled { get; set; } = true;
    // R/W component-id sets used by the pipeline DAG. Default empty (treated
    // as "writes anything" — pessimistic, forces serialization). System<T...>
    // sugar populates WriteIds from typed args; user can swap via SetReads /
    // SetWrites on the handle. Pure read-only systems (no writes) can run
    // concurrent with anything.
    public Id[] ReadIds { get; private set; } = Array.Empty<Id>();
    public Id[] WriteIds { get; private set; } = Array.Empty<Id>();
    // True when caller asserts the action is safe to run concurrently with
    // other ParallelSafe systems whose r/w sets don't conflict. Default false:
    // unknown side effects → serialize. System<T...> sugar sets this true.
    public bool ParallelSafe { get; internal set; }

    internal SystemHandle(string name, EntityId phase, SystemAction action)
    { Name = name; Phase = phase; Action = action; }

    public SystemHandle SetReads(params Id[] ids) { ReadIds = ids; return this; }
    public SystemHandle SetWrites(params Id[] ids) { WriteIds = ids; return this; }
    public SystemHandle SetParallelSafe(bool v = true) { ParallelSafe = v; return this; }

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
