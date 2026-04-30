using System.Collections.Generic;

namespace Flecs;

// ============================================================================
// Custom pipelines.
//
// A pipeline is an entity carrying a PipelineFilter component (With + Without
// id sets). Every SystemHandle has a backing entity tagged with the reserved
// `SystemTag` id; user-defined tags can be added via world.Add(handle.Entity,
// myTag) to opt the system in/out of pipelines.
//
// World holds one _activePipeline EntityId. Default invalid = no filter (every
// System runs). After world.SetPipeline(custom), RebuildPipelineLocked applies
// the filter: a system is included iff its entity holds every WithIds entry
// AND none of the WithoutIds entries.
//
// Builder usage:
//   var p = world.CreatePipeline()
//                .With(world.SystemTag)
//                .Without<MenuScene>()
//                .Build();
//   world.SetPipeline(p);
// ============================================================================

public record struct PipelineFilter(Id[] WithIds, Id[] WithoutIds);

public sealed class PipelineBuilder
{
    private readonly World _world;
    private readonly List<Id> _with = new();
    private readonly List<Id> _without = new();

    internal PipelineBuilder(World w) { _world = w; }

    public PipelineBuilder With<T>() where T : struct
    {
        _with.Add((Id)_world.GetOrRegisterAny<T>());
        return this;
    }
    public PipelineBuilder With(Id id) { _with.Add(id); return this; }
    public PipelineBuilder With(EntityId e) { _with.Add((Id)e); return this; }

    public PipelineBuilder Without<T>() where T : struct
    {
        _without.Add((Id)_world.GetOrRegisterAny<T>());
        return this;
    }
    public PipelineBuilder Without(Id id) { _without.Add(id); return this; }
    public PipelineBuilder Without(EntityId e) { _without.Add((Id)e); return this; }

    public EntityId Build()
    {
        var e = _world.CreateEntity();
        _world.Add(e, (Id)_world.Pipeline);
        _world.Set(e, new PipelineFilter(_with.ToArray(), _without.ToArray()));
        return e;
    }
}

public sealed partial class World
{
    public PipelineBuilder CreatePipeline() => new PipelineBuilder(this);

    // Activate pipeline p. Until SetPipeline is called, no filter is applied
    // (every system runs). Pass default to clear (revert to "all systems").
    public void SetPipeline(EntityId p)
    {
        lock (_lock)
        {
            _activePipeline = p;
            _pipelineDirty = true;
        }
    }

    public EntityId GetPipeline()
    {
        lock (_lock) return _activePipeline;
    }

    // Returns true when the system entity passes the active pipeline filter.
    // No filter (default _activePipeline OR pipeline lacks PipelineFilter) →
    // always true.
    internal bool SystemMatchesActivePipeline(SystemHandle s)
    {
        if (!_activePipeline.IsValid) return true;
        if (!IsAlive(_activePipeline)) return true;
        if (!Has<PipelineFilter>(_activePipeline)) return true;
        ref var f = ref Get<PipelineFilter>(_activePipeline);
        var ent = s.Entity;
        if (!ent.IsValid) return true; // pre-pipeline systems (shouldn't happen)
        for (int i = 0; i < f.WithIds.Length; i++)
            if (!Has(ent, f.WithIds[i])) return false;
        for (int i = 0; i < f.WithoutIds.Length; i++)
            if (Has(ent, f.WithoutIds[i])) return false;
        return true;
    }

    // Internal exposure for PipelineBuilder. Calls into Component<T> /
    // Tag<T>-style auto-registration by tag-or-component path.
    internal EntityId GetOrRegisterAny<T>() where T : struct
    {
        lock (_lock) return GetOrRegisterAnyLocked<T>();
    }
}
