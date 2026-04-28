using System;

namespace Flecs;

// ============================================================================
// EntityName — builtin name component. Lookup keys off this. Reflection-free:
// a plain struct with a string field. Mirrors EcsName.
// ============================================================================
public readonly record struct EntityName(string Value);

// ============================================================================
// EntityId — handle for an entity (id + generation). Mirrors ecs_entity_t.
// ============================================================================
public readonly record struct EntityId(uint Id, ushort Generation) : IComparable<EntityId>
{
    internal ulong Raw => ((ulong)Generation << 32) | Id;
    public static EntityId Dead => default;
    public bool IsValid => Id != 0;
    public int CompareTo(EntityId other) => Id.CompareTo(other.Id);
}

// ============================================================================
// Id — 64-bit packed component identifier (mirrors ecs_id_t).
//   - Non-pair: low 32 bits = entity id (no generation), high bits 0.
//   - Pair: bit 63 set, bits 62..32 = relation id, bits 31..0 = target id.
// Used as table signature element. Generation is tracked separately on the
// entity record; tables index by id alone.
// ============================================================================
public readonly record struct Id(ulong Value) : IComparable<Id>
{
    internal const ulong PairFlag = 1UL << 63;
    internal const ulong RelationMask = 0x7FFFFFFFu;
    internal const ulong TargetMask = 0xFFFFFFFFu;

    public bool IsPair => (Value & PairFlag) != 0;
    public uint Relation => IsPair ? (uint)((Value >> 32) & RelationMask) : 0u;
    public uint Target => IsPair ? (uint)(Value & TargetMask) : 0u;
    public uint Component => IsPair ? 0u : (uint)(Value & TargetMask);

    public int CompareTo(Id other) => Value.CompareTo(other.Value);

    public static implicit operator Id(EntityId e) => new((ulong)e.Id);

    public static Id MakePair(EntityId relation, EntityId target)
        => new(PairFlag | ((ulong)relation.Id << 32) | (ulong)target.Id);

    public override string ToString()
        => IsPair ? $"(#{Relation}, #{Target})" : $"#{Component}";
}
