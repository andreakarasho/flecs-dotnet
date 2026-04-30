using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Flecs;

// ============================================================================
// Per-relation Union storage. Mirrors flecs Union trait: a relation TR marked
// Union enforces single (TR, *) per entity AND stores the target in a side
// table keyed by entity id. Switching target is O(1) and does not migrate
// the entity's archetype — the (TR, *) pair never enters the archetype.
//
// Trade-off vs Exclusive (which also enforces single target): Exclusive
// fragments archetypes per target; Union does not. Best fit for state-machine
// targets that change frequently (Movement: Walking/Running/Idle).
//
// Iteration: Query<...>().With<TR, TT>() gates per-row when TR is Union — the
// archetype check is skipped (mirrors Sparse), per-row Has(entId, target) is
// applied during RowEnumerator filter path.
// ============================================================================

internal sealed class UnionStorage
{
    private readonly Dictionary<uint, uint> _entToTarget = new();
    private readonly uint _relId;

    public UnionStorage(uint relId) { _relId = relId; }
    public uint RelationId => _relId;
    public int Count => _entToTarget.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(uint ent) => _entToTarget.ContainsKey(ent);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool HasTarget(uint ent, uint target)
        => _entToTarget.TryGetValue(ent, out var t) && t == target;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public uint GetTarget(uint ent)
        => _entToTarget.TryGetValue(ent, out var t) ? t : 0u;

    // Returns (had, prev). 'had' = entry existed before. 'prev' = previous
    // target (0 if !had). Caller fires OnRemove(prev pair) + OnAdd(new pair)
    // when target changes; pure-overwrite of same target is a no-op.
    public bool Set(uint ent, uint target, out uint prev)
    {
        bool had = _entToTarget.TryGetValue(ent, out prev);
        _entToTarget[ent] = target;
        return had;
    }

    public bool Remove(uint ent, out uint prev)
    {
        if (_entToTarget.TryGetValue(ent, out prev))
        {
            _entToTarget.Remove(ent);
            return true;
        }
        prev = 0;
        return false;
    }

    public IEnumerable<KeyValuePair<uint, uint>> Entries => _entToTarget;
}
