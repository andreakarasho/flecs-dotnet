using System;
using System.Runtime.CompilerServices;

namespace Flecs;

// ============================================================================
// Sparse-set storage for components opted into the Sparse trait. Mirrors flecs
// non-fragmenting component storage: the component id never enters an
// entity's archetype, so Set/Remove on a sparse component does not migrate
// the entity. Lookups go through a per-component SparseStorage<T> keyed by
// raw entity id.
//
// Layout (classic sparse-set):
//   _sparse[entId]      → dense slot index, or -1 if absent
//   _dense[denseIdx]    → component value
//   _denseEnts[denseIdx]→ entity id (for swap-back on remove)
//   _count              → live entries
//
// Refs into the dense array remain stable until a Set grows the array.
// Same hazard as Column<T>; callers must not hold refs across mutations.
//
// Iteration over sparse-only queries is NYI — Query<SparseT> currently
// requires the term to live in an archetype, so a pure-sparse query matches
// nothing. Mixed queries (e.g. Query<Position, SparseHealth>) are also NYI;
// callers can side-band via world.Get<SparseHealth>(entity) inside an
// archetype-driven foreach.
// ============================================================================

internal interface ISparseStorage
{
    bool Has(uint entId);
    void OnEntityDelete(World w, EntityId entity);
}

internal sealed class SparseStorage<T> : ISparseStorage where T : struct
{
    private int[] _sparse;
    private T[] _dense;
    private uint[] _denseEnts;
    private int _count;
    private readonly Id _compId;

    public SparseStorage(Id compId)
    {
        _compId = compId;
        _sparse = new int[64];
        Array.Fill(_sparse, -1);
        _dense = new T[16];
        _denseEnts = new uint[16];
    }

    public int Count => _count;
    public Id ComponentId => _compId;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Has(uint entId)
    {
        if (entId >= (uint)_sparse.Length) return false;
        int idx = _sparse[entId];
        return idx >= 0 && idx < _count && _denseEnts[idx] == entId;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ref T GetRef(uint entId)
    {
        if (!Has(entId)) return ref Unsafe.NullRef<T>();
        return ref _dense[_sparse[entId]];
    }

    // Returns true iff the entry was newly added (vs. overwritten).
    public bool Set(uint entId, T value)
    {
        EnsureSparse(entId);
        int idx = _sparse[entId];
        bool isNew = idx < 0 || idx >= _count || _denseEnts[idx] != entId;
        if (isNew)
        {
            if (_count == _dense.Length)
            {
                int n = _dense.Length * 2;
                Array.Resize(ref _dense, n);
                Array.Resize(ref _denseEnts, n);
            }
            _dense[_count] = value;
            _denseEnts[_count] = entId;
            _sparse[entId] = _count;
            _count++;
        }
        else
        {
            _dense[idx] = value;
        }
        return isNew;
    }

    public bool TryRemove(uint entId, out T removed)
    {
        if (!Has(entId)) { removed = default; return false; }
        int idx = _sparse[entId];
        removed = _dense[idx];
        int last = --_count;
        if (idx != last)
        {
            _dense[idx] = _dense[last];
            uint movedEnt = _denseEnts[last];
            _denseEnts[idx] = movedEnt;
            _sparse[movedEnt] = idx;
        }
        _dense[last] = default; // release refs in T if any
        _sparse[entId] = -1;
        return true;
    }

    private void EnsureSparse(uint entId)
    {
        if (entId < (uint)_sparse.Length) return;
        int n = _sparse.Length;
        while (n <= entId) n *= 2;
        int oldN = _sparse.Length;
        Array.Resize(ref _sparse, n);
        for (int i = oldN; i < n; i++) _sparse[i] = -1;
    }

    // Entity-delete cleanup: fire OnRemove + Dtor hooks then drop the entry.
    // Caller already holds the world lock.
    void ISparseStorage.OnEntityDelete(World w, EntityId entity)
    {
        if (!Has(entity.Id)) return;
        int idx = _sparse[entity.Id];
        var hooks = w.GetTypeHooksRaw(_compId) as TypeHooks<T>;
        hooks?.OnRemove?.Invoke(w, entity, ref _dense[idx]);
        hooks?.Dtor?.Invoke(w, entity, ref _dense[idx]);
        w.GetIdHooksRaw(_compId)?.OnRemove?.Invoke(w, entity);
        w.DispatchMultiObsRaw(Event.OnRemove, entity, _compId);
        TryRemove(entity.Id, out _);
    }
}
