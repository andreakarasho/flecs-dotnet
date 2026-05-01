namespace Flecs;

// ============================================================================
// Nested-struct accessors over World's reserved builtin entities. Groups the
// flat set of ~25 builtins into categories so call sites read like:
//
//   world.Phases.OnUpdate
//   world.Relations.ChildOf
//   world.RelationTraits.Final
//   world.ComponentTraits.CanToggle
//   world.PipelineMeta.Phase
//   world.States.Disabled
//
// Each accessor is a readonly struct holding a World reference; properties
// forward to internal fields. Zero allocations, JIT-inlines through.
// ============================================================================

public readonly struct RelationsAccess
{
    private readonly World _w;
    internal RelationsAccess(World w) => _w = w;
    public EntityId ChildOf => _w.ChildOf;
    public EntityId IsA => _w.IsA;
    public EntityId DependsOn => _w.DependsOn;
    public EntityId Wildcard => _w.Wildcard;
}

public readonly struct StatesAccess
{
    private readonly World _w;
    internal StatesAccess(World w) => _w = w;
    public EntityId Disabled => _w.Disabled;
}

public readonly struct RelationTraitsAccess
{
    private readonly World _w;
    internal RelationTraitsAccess(World w) => _w = w;
    public EntityId Final => _w.Final;
    public EntityId Exclusive => _w.Exclusive;
    public EntityId Acyclic => _w.Acyclic;
    public EntityId Reflexive => _w.Reflexive;
    public EntityId Symmetric => _w.Symmetric;
    public EntityId Transitive => _w.Transitive;
    public EntityId Traversable => _w.Traversable;
}

public readonly struct ComponentTraitsAccess
{
    private readonly World _w;
    internal ComponentTraitsAccess(World w) => _w = w;
    public EntityId Inheritable => _w.Inheritable;
    public EntityId DontInherit => _w.DontInherit;
    public EntityId CanToggle => _w.CanToggle;
}

public readonly struct PipelineMetaAccess
{
    private readonly World _w;
    internal PipelineMetaAccess(World w) => _w = w;
    public EntityId Phase => _w.Phase;
    public EntityId SystemTag => _w.SystemTag;
    public EntityId Pipeline => _w.Pipeline;
}

public readonly struct PhasesAccess
{
    private readonly World _w;
    internal PhasesAccess(World w) => _w = w;
    public EntityId OnStart => _w.OnStart;
    public EntityId OnLoad => _w.OnLoad;
    public EntityId PostLoad => _w.PostLoad;
    public EntityId PreUpdate => _w.PreUpdate;
    public EntityId OnUpdate => _w.OnUpdate;
    public EntityId OnValidate => _w.OnValidate;
    public EntityId PostUpdate => _w.PostUpdate;
    public EntityId PreStore => _w.PreStore;
    public EntityId OnStore => _w.OnStore;
}

public sealed partial class World
{
    public RelationsAccess Relations => new(this);
    public StatesAccess States => new(this);
    public RelationTraitsAccess RelationTraits => new(this);
    public ComponentTraitsAccess ComponentTraits => new(this);
    public PipelineMetaAccess PipelineMeta => new(this);
    public PhasesAccess Phases => new(this);
}
