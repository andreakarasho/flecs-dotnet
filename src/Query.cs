using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Flecs;

// ============================================================================
// Query callbacks. Per-arity delegate to avoid boxing / allocation.
// ============================================================================
public delegate void EachAction<T1>(EntityId e, ref T1 c1);
public delegate void EachAction<T1, T2>(EntityId e, ref T1 c1, ref T2 c2);
public delegate void EachAction<T1, T2, T3>(EntityId e, ref T1 c1, ref T2 c2, ref T3 c3);
public delegate void EachAction<T1, T2, T3, T4>(EntityId e, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4);
public delegate void EachAction<T1, T2, T3, T4, T5>(EntityId e, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4, ref T5 c5);
public delegate void EachAction<T1, T2, T3, T4, T5, T6>(EntityId e, ref T1 c1, ref T2 c2, ref T3 c3, ref T4 c4, ref T5 c5, ref T6 c6);

// ============================================================================
// Iter — typed bulk-iteration handle. Run callback receives this; provides raw
// column Span<T> for tight inner loops (avoids per-row delegate call). Mirrors
// ecs_iter_t.
// ============================================================================
public ref struct Iter<T1> where T1 : struct
{
    internal readonly World _world;
    internal readonly Table _table;
    internal readonly Column<T1> _col1;
    internal Iter(World w, Table t, Id c1)
    {
        _world = w;
        _table = t;
        _col1 = (Column<T1>)t.Columns[t.IndexOf(c1)]!;
    }
    public int Count => _table.Count;
    public EntityId Entity(int row) => _table.Entities[row];
    public Span<T1> Field1() => _col1.AsSpan();
    // Optional column for this table. Empty Span when absent. Per-table O(1)
    // lookup; cheaper than per-row TryGetRef.
    public Span<T> OptionalField<T>() where T : struct => IterOpt.Field<T>(_world, _table);
    public bool HasOptional<T>() where T : struct => IterOpt.Has<T>(_world, _table);
}

public ref struct Iter<T1, T2> where T1 : struct where T2 : struct
{
    internal readonly World _world;
    internal readonly Table _table;
    internal readonly int _count;
    // Precomputed Spans — avoids per-call AsSpan chain through Column field.
    // JIT loves field loads over property/method indirection.
    internal readonly Span<T1> _f1;
    internal readonly Span<T2> _f2;

    internal Iter(World w, Table t, Id c1, Id c2)
    {
        _world = w;
        _table = t;
        _count = t.Count;
        _f1 = ((Column<T1>)t.Columns[t.IndexOf(c1)]!).AsSpan();
        _f2 = ((Column<T2>)t.Columns[t.IndexOf(c2)]!).AsSpan();
    }

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _count;
    }
    public EntityId Entity(int row) => _table.Entities[row];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T1> Field1() => _f1;
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<T2> Field2() => _f2;

    public Span<T> OptionalField<T>() where T : struct => IterOpt.Field<T>(_world, _table);
    public bool HasOptional<T>() where T : struct => IterOpt.Has<T>(_world, _table);
}

public ref struct Iter<T1, T2, T3> where T1 : struct where T2 : struct where T3 : struct
{
    internal readonly World _world;
    internal readonly Table _table;
    internal readonly Column<T1> _col1;
    internal readonly Column<T2> _col2;
    internal readonly Column<T3> _col3;
    internal Iter(World w, Table t, Id c1, Id c2, Id c3)
    {
        _world = w;
        _table = t;
        _col1 = (Column<T1>)t.Columns[t.IndexOf(c1)]!;
        _col2 = (Column<T2>)t.Columns[t.IndexOf(c2)]!;
        _col3 = (Column<T3>)t.Columns[t.IndexOf(c3)]!;
    }
    public int Count => _table.Count;
    public EntityId Entity(int row) => _table.Entities[row];
    public Span<T1> Field1() => _col1.AsSpan();
    public Span<T2> Field2() => _col2.AsSpan();
    public Span<T3> Field3() => _col3.AsSpan();
    public Span<T> OptionalField<T>() where T : struct => IterOpt.Field<T>(_world, _table);
    public bool HasOptional<T>() where T : struct => IterOpt.Has<T>(_world, _table);
}

// Shared helpers for optional-field lookups in Iter ref structs.
internal static class IterOpt
{
    public static Span<T> Field<T>(World w, Table t) where T : struct
    {
        var id = w.IdOf<T>();
        if (!t.Has(id)) return Span<T>.Empty;
        return ((Column<T>)t.Columns[t.IndexOf(id)]!).AsSpan();
    }
    public static bool Has<T>(World w, Table t) where T : struct
    {
        var id = w.IdOf<T>();
        return t.Has(id);
    }
}

public ref struct Iter<T1, T2, T3, T4>
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct
{
    internal readonly World _world;
    internal readonly Table _table;
    internal readonly Column<T1> _col1;
    internal readonly Column<T2> _col2;
    internal readonly Column<T3> _col3;
    internal readonly Column<T4> _col4;
    internal Iter(World w, Table t, Id c1, Id c2, Id c3, Id c4)
    {
        _world = w;
        _table = t;
        _col1 = (Column<T1>)t.Columns[t.IndexOf(c1)]!;
        _col2 = (Column<T2>)t.Columns[t.IndexOf(c2)]!;
        _col3 = (Column<T3>)t.Columns[t.IndexOf(c3)]!;
        _col4 = (Column<T4>)t.Columns[t.IndexOf(c4)]!;
    }
    public int Count => _table.Count;
    public EntityId Entity(int row) => _table.Entities[row];
    public Span<T1> Field1() => _col1.AsSpan();
    public Span<T2> Field2() => _col2.AsSpan();
    public Span<T3> Field3() => _col3.AsSpan();
    public Span<T4> Field4() => _col4.AsSpan();
    public Span<T> OptionalField<T>() where T : struct => IterOpt.Field<T>(_world, _table);
    public bool HasOptional<T>() where T : struct => IterOpt.Has<T>(_world, _table);
}

public ref struct Iter<T1, T2, T3, T4, T5>
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
{
    internal readonly World _world;
    internal readonly Table _table;
    internal readonly Column<T1> _col1;
    internal readonly Column<T2> _col2;
    internal readonly Column<T3> _col3;
    internal readonly Column<T4> _col4;
    internal readonly Column<T5> _col5;
    internal Iter(World w, Table t, Id c1, Id c2, Id c3, Id c4, Id c5)
    {
        _world = w;
        _table = t;
        _col1 = (Column<T1>)t.Columns[t.IndexOf(c1)]!;
        _col2 = (Column<T2>)t.Columns[t.IndexOf(c2)]!;
        _col3 = (Column<T3>)t.Columns[t.IndexOf(c3)]!;
        _col4 = (Column<T4>)t.Columns[t.IndexOf(c4)]!;
        _col5 = (Column<T5>)t.Columns[t.IndexOf(c5)]!;
    }
    public int Count => _table.Count;
    public EntityId Entity(int row) => _table.Entities[row];
    public Span<T1> Field1() => _col1.AsSpan();
    public Span<T2> Field2() => _col2.AsSpan();
    public Span<T3> Field3() => _col3.AsSpan();
    public Span<T4> Field4() => _col4.AsSpan();
    public Span<T5> Field5() => _col5.AsSpan();
    public Span<T> OptionalField<T>() where T : struct => IterOpt.Field<T>(_world, _table);
    public bool HasOptional<T>() where T : struct => IterOpt.Has<T>(_world, _table);
}

public ref struct Iter<T1, T2, T3, T4, T5, T6>
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
{
    internal readonly World _world;
    internal readonly Table _table;
    internal readonly Column<T1> _col1;
    internal readonly Column<T2> _col2;
    internal readonly Column<T3> _col3;
    internal readonly Column<T4> _col4;
    internal readonly Column<T5> _col5;
    internal readonly Column<T6> _col6;
    internal Iter(World w, Table t, Id c1, Id c2, Id c3, Id c4, Id c5, Id c6)
    {
        _world = w;
        _table = t;
        _col1 = (Column<T1>)t.Columns[t.IndexOf(c1)]!;
        _col2 = (Column<T2>)t.Columns[t.IndexOf(c2)]!;
        _col3 = (Column<T3>)t.Columns[t.IndexOf(c3)]!;
        _col4 = (Column<T4>)t.Columns[t.IndexOf(c4)]!;
        _col5 = (Column<T5>)t.Columns[t.IndexOf(c5)]!;
        _col6 = (Column<T6>)t.Columns[t.IndexOf(c6)]!;
    }
    public int Count => _table.Count;
    public EntityId Entity(int row) => _table.Entities[row];
    public Span<T1> Field1() => _col1.AsSpan();
    public Span<T2> Field2() => _col2.AsSpan();
    public Span<T3> Field3() => _col3.AsSpan();
    public Span<T4> Field4() => _col4.AsSpan();
    public Span<T5> Field5() => _col5.AsSpan();
    public Span<T6> Field6() => _col6.AsSpan();
    public Span<T> OptionalField<T>() where T : struct => IterOpt.Field<T>(_world, _table);
    public bool HasOptional<T>() where T : struct => IterOpt.Has<T>(_world, _table);
}

public delegate void IterAction<T1>(in Iter<T1> it) where T1 : struct;
public delegate void IterAction<T1, T2>(in Iter<T1, T2> it) where T1 : struct where T2 : struct;
public delegate void IterAction<T1, T2, T3>(in Iter<T1, T2, T3> it) where T1 : struct where T2 : struct where T3 : struct;
public delegate void IterAction<T1, T2, T3, T4>(in Iter<T1, T2, T3, T4> it)
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct;
public delegate void IterAction<T1, T2, T3, T4, T5>(in Iter<T1, T2, T3, T4, T5> it)
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct;
public delegate void IterAction<T1, T2, T3, T4, T5, T6>(in Iter<T1, T2, T3, T4, T5, T6> it)
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct;

// ============================================================================
// QueryBase — shared infrastructure: signature, table-cache, change detection,
// Or-group support. Concrete typed Query<T...> derive.
// ============================================================================
public abstract class QueryBase
{
    internal readonly World _world;
    protected internal Id[] _with;
    protected internal Id[] _without = Array.Empty<Id>();
    protected internal List<Id[]>? _orGroups;
    protected internal readonly List<Table> _matched = new();
    protected internal int _matchedUpTo;
    // Opt-in: when true, match also includes tables whose entities satisfy
    // 'with' terms via Self+Up(IsA). Each callbacks resolve shared refs from
    // the ancestor archetype. Run/Iter remain literal — inherited-only tables
    // are skipped at Run time. Mirrors flecs query inheritance semantics.
    protected internal bool _inherited;
    private Dictionary<int, int>? _lastVersion;

    protected QueryBase(World w, Id[] with) { _world = w; _with = with; }

    protected void Reset() { _matched.Clear(); _matchedUpTo = 0; _lastVersion?.Clear(); }

    protected void AddWith(Id id) { _with = QueryUtil.AppendSorted(_with, id); Reset(); }
    protected void AddWithout(Id id) { _without = QueryUtil.AppendSorted(_without, id); Reset(); }
    protected void AddOr(Id[] group) { (_orGroups ??= new List<Id[]>()).Add(group); Reset(); }
    protected void SetInherited() { if (!_inherited) { _inherited = true; Reset(); } }

    protected internal void Rematch()
    {
        var tables = _world._tablesById;
        for (int i = _matchedUpTo + 1; i < tables.Count; i++)
        {
            var t = tables[i];
            if (t == null) continue;
            if (QueryUtil.Matches(t, _with, _without, _orGroups, _world.Wildcard.Id,
                    _inherited ? _world : null))
                _matched.Add(t);
        }
        _matchedUpTo = tables.Count - 1;
    }

    public int MatchedTableCount { get { Rematch(); return _matched.Count; } }

    // True if any matched table changed since last Each / Run / MarkObserved.
    public bool IsChanged()
    {
        Rematch();
        if (_matched.Count == 0) return false;
        if (_lastVersion == null) return true;
        for (int i = 0; i < _matched.Count; i++)
        {
            var t = _matched[i];
            if (!_lastVersion.TryGetValue(t.Id, out var v) || v != t.Version) return true;
        }
        return false;
    }

    protected void MarkObserved()
    {
        var dict = _lastVersion ??= new Dictionary<int, int>();
        for (int i = 0; i < _matched.Count; i++)
        {
            var t = _matched[i];
            dict[t.Id] = t.Version;
        }
    }

    // Internal hook for ref-struct enumerators that can't call protected
    // members directly across types.
    internal void MarkObservedInternal() => MarkObserved();

    // Resolve a per-table column source for term 'id'. When the table holds
    // 'id' directly: (col, sharedRow=-1) — caller indexes by row r.
    // When inheritance is enabled and the table only holds it via IsA:
    // (col-from-ancestor, sharedRow=ancestor-row) — caller passes sharedRow.
    // (null, -1) when absent (only legitimate for optional terms).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal (Column<T>? col, int sharedRow) ResolveSource<T>(Table t, Id id) where T : struct
    {
        if (t.Has(id)) return ((Column<T>?)t.Columns[t.IndexOf(id)], -1);
        if (_inherited && t.Count > 0)
        {
            var seed = t.Entities[0];
            var (found, src, row) = _world.FindInIsAChain(seed, id);
            if (found && src != null && src.Has(id))
                return ((Column<T>?)src.Columns[src.IndexOf(id)], row);
        }
        return (null, -1);
    }
}

internal static class QueryUtil
{
    // worldForInherit non-null enables Self+Up(IsA) matching for 'with' terms:
    // a table missing a term directly still matches if any entity in the table
    // can reach the term via IsA. Without/Or stay literal (Self-only).
    public static bool Matches(Table t, Id[] with, Id[] without, List<Id[]>? orGroups, uint wildcard,
        World? worldForInherit = null)
    {
        for (int i = 0; i < with.Length; i++)
            if (!MatchesIdOrInherited(t, with[i], wildcard, worldForInherit)) return false;
        for (int i = 0; i < without.Length; i++)
            if (MatchesId(t, without[i], wildcard)) return false;
        if (orGroups != null)
        {
            for (int g = 0; g < orGroups.Count; g++)
            {
                var group = orGroups[g];
                bool any = false;
                for (int i = 0; i < group.Length; i++)
                    if (MatchesIdOrInherited(t, group[i], wildcard, worldForInherit)) { any = true; break; }
                if (!any) return false;
            }
        }
        return true;
    }

    // Self-or-Up matcher. Falls back to FindInIsAChain when literal miss.
    // Empty tables can't satisfy via inheritance (no entity to seed the walk),
    // which is fine: empty tables are skipped during iteration anyway.
    private static bool MatchesIdOrInherited(Table t, Id id, uint wildcard, World? worldForInherit)
    {
        if (MatchesId(t, id, wildcard)) return true;
        if (worldForInherit == null || t.Count == 0) return false;
        var seed = t.Entities[0];
        var (found, _, _) = worldForInherit.FindInIsAChain(seed, id);
        return found;
    }

    // Matches a single Id (handles pair wildcards). For non-wildcard ids
    // delegates to Table.Has. For pair-with-wildcard scans pair entries.
    public static bool MatchesId(Table t, Id queryId, uint wildcard)
    {
        if (!queryId.IsPair) return t.Has(queryId);
        uint qRel = queryId.Relation;
        uint qTgt = queryId.Target;
        bool relWild = qRel == wildcard;
        bool tgtWild = qTgt == wildcard;
        if (!relWild && !tgtWild) return t.Has(queryId);
        var ids = t.ComponentIds;
        for (int i = 0; i < ids.Length; i++)
        {
            var id = ids[i];
            if (!id.IsPair) continue;
            if (!relWild && id.Relation != qRel) continue;
            if (!tgtWild && id.Target != qTgt) continue;
            return true;
        }
        return false;
    }

    public static Id[] AppendSorted(Id[] src, Id id)
    {
        int n = src.Length;
        int insert = 0;
        while (insert < n && src[insert].Value < id.Value) insert++;
        if (insert < n && src[insert] == id) return src;
        var dst = new Id[n + 1];
        Array.Copy(src, 0, dst, 0, insert);
        dst[insert] = id;
        if (insert < n) Array.Copy(src, insert, dst, insert + 1, n - insert);
        return dst;
    }

    // Per-row ref resolver. Branches on shared vs own; null col → NullRef.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T Resolve<T>(Column<T>? col, int sharedRow, int r) where T : struct
    {
        if (col == null) return ref Unsafe.NullRef<T>();
        return ref col.GetRef(sharedRow < 0 ? r : sharedRow);
    }

    public static Id[] Remove(Id[] src, Id id)
    {
        int idx = Array.IndexOf(src, id);
        if (idx < 0) return src;
        var dst = new Id[src.Length - 1];
        Array.Copy(src, 0, dst, 0, idx);
        Array.Copy(src, idx + 1, dst, idx, src.Length - idx - 1);
        return dst;
    }
}

// ============================================================================
// Query<T1...> — cached archetype-table query. Re-matches lazily for new
// tables created since last iter. Iteration wraps body in defer scope —
// mutation in callback safe. Mirrors ecs_query_t with table-cache.
// ============================================================================
public sealed class Query<T1> : QueryBase where T1 : struct
{
    internal readonly Id _c1;
    private bool _t1Optional;

    internal Query(World w) : base(w, new[] { (Id)w.Component<T1>() })
    { _c1 = _with[0]; }

    public Query<T1> With<T>() where T : struct { AddWith(_world.IdOf<T>()); return this; }
    public Query<T1> With(Id id) { AddWith(id); return this; }
    public Query<T1> Without<T>() where T : struct { AddWithout(_world.IdOf<T>()); return this; }
    public Query<T1> Without<TR, TT>() where TR : struct where TT : struct { AddWithout(_world.Pair<TR, TT>()); return this; }
    public Query<T1> Without(Id id) { AddWithout(id); return this; }
    public Query<T1> Or<TA, TB>() where TA : struct where TB : struct
    { AddOr(new[] { _world.IdOf<TA>(), _world.IdOf<TB>() }); return this; }
    public Query<T1> Or<TA, TB, TC>() where TA : struct where TB : struct where TC : struct
    { AddOr(new[] { _world.IdOf<TA>(), _world.IdOf<TB>(), _world.IdOf<TC>() }); return this; }

    // Mark a typed slot as optional: matching ignores the constraint, and
    // Each callback receives `Unsafe.NullRef<T>()` for rows where the column
    // is absent. Caller checks via `Unsafe.IsNullRef(ref v)`.
    public Query<T1> Optional<T>() where T : struct
    {
        if (typeof(T) != typeof(T1))
            throw new ArgumentException($"Optional<{typeof(T).Name}>: T must be T1 of this query.");
        if (!_t1Optional) { _t1Optional = true; _with = QueryUtil.Remove(_with, _c1); Reset(); }
        return this;
    }

    // Opt-in to Self+Up(IsA) matching. Each() yields shared refs from prefab
    // archetypes when entities inherit terms via IsA. Run() and the foreach
    // enumerators stay literal — inherited-only tables are skipped there.
    public Query<T1> Inherited() { SetInherited(); return this; }

    // Foreach-iterable. Yields Row<T1>, no delegate dispatch.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TableEnumerator<T1> GetEnumerator() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1> Rows() => new(this);

    public void Each(EachAction<T1> action)
    {
        using var _ = _world.Defer();
        Rematch();
        for (int ti = 0; ti < _matched.Count; ti++)
        {
            var t = _matched[ti];
            if (t.Count == 0) continue;
            var (col1, shared1) = ResolveSource<T1>(t, _c1);
            if (col1 == null && !_t1Optional) continue;
            var ents = t.Entities;
            int n = t.Count;
            for (int r = 0; r < n; r++)
                action(ents[r], ref QueryUtil.Resolve(col1, shared1, r));
        }
        MarkObserved();
    }

    public void Run(IterAction<T1> action)
    {
        using var _ = _world.Defer();
        Rematch();
        for (int ti = 0; ti < _matched.Count; ti++)
        {
            var t = _matched[ti];
            if (t.Count == 0) continue;
            if (_inherited && !t.Has(_c1)) continue;
            var it = new Iter<T1>(_world, t, _c1);
            action(in it);
        }
        MarkObserved();
    }
}

public sealed class Query<T1, T2> : QueryBase where T1 : struct where T2 : struct
{
    internal readonly Id _c1, _c2;
    private bool _t1Optional, _t2Optional;

    internal Query(World w)
        : base(w, QueryUtil.AppendSorted(new[] { (Id)w.Component<T1>() }, (Id)w.Component<T2>()))
    {
        _c1 = (Id)w.Component<T1>();
        _c2 = (Id)w.Component<T2>();
    }

    public Query<T1, T2> With<T>() where T : struct { AddWith(_world.IdOf<T>()); return this; }
    public Query<T1, T2> With(Id id) { AddWith(id); return this; }
    public Query<T1, T2> Without<T>() where T : struct { AddWithout(_world.IdOf<T>()); return this; }
    public Query<T1, T2> Without<TR, TT>() where TR : struct where TT : struct { AddWithout(_world.Pair<TR, TT>()); return this; }
    public Query<T1, T2> Without(Id id) { AddWithout(id); return this; }
    public Query<T1, T2> Or<TA, TB>() where TA : struct where TB : struct
    { AddOr(new[] { _world.IdOf<TA>(), _world.IdOf<TB>() }); return this; }
    public Query<T1, T2> Or<TA, TB, TC>() where TA : struct where TB : struct where TC : struct
    { AddOr(new[] { _world.IdOf<TA>(), _world.IdOf<TB>(), _world.IdOf<TC>() }); return this; }

    // Mark T1 or T2 as optional. Match ignores it. Each callback receives
    // Unsafe.NullRef<T>() for absent rows.
    public Query<T1, T2> Optional<T>() where T : struct
    {
        if (typeof(T) == typeof(T1))
        {
            if (!_t1Optional) { _t1Optional = true; _with = QueryUtil.Remove(_with, _c1); Reset(); }
        }
        else if (typeof(T) == typeof(T2))
        {
            if (!_t2Optional) { _t2Optional = true; _with = QueryUtil.Remove(_with, _c2); Reset(); }
        }
        else
        {
            throw new ArgumentException($"Optional<{typeof(T).Name}>: T must be T1 or T2 of this query.");
        }
        return this;
    }

    public Query<T1, T2> Inherited() { SetInherited(); return this; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TableEnumerator<T1, T2> GetEnumerator() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2> Rows() => new(this);

    public void Each(EachAction<T1, T2> action)
    {
        using var _ = _world.Defer();
        Rematch();
        for (int ti = 0; ti < _matched.Count; ti++)
        {
            var t = _matched[ti];
            if (t.Count == 0) continue;
            var (col1, s1) = ResolveSource<T1>(t, _c1);
            var (col2, s2) = ResolveSource<T2>(t, _c2);
            if ((col1 == null && !_t1Optional) || (col2 == null && !_t2Optional)) continue;
            var ents = t.Entities;
            int n = t.Count;
            // Fast path — both own (no shared, no optional miss).
            if (col1 != null && col2 != null && s1 < 0 && s2 < 0)
            {
                for (int r = 0; r < n; r++)
                    action(ents[r], ref col1.GetRef(r), ref col2.GetRef(r));
            }
            else
            {
                for (int r = 0; r < n; r++)
                    action(ents[r],
                        ref QueryUtil.Resolve(col1, s1, r),
                        ref QueryUtil.Resolve(col2, s2, r));
            }
        }
        MarkObserved();
    }

    public void Run(IterAction<T1, T2> action)
    {
        using var _ = _world.Defer();
        Rematch();
        for (int ti = 0; ti < _matched.Count; ti++)
        {
            var t = _matched[ti];
            if (t.Count == 0) continue;
            if (_inherited && (!t.Has(_c1) || !t.Has(_c2))) continue;
            var it = new Iter<T1, T2>(_world, t, _c1, _c2);
            action(in it);
        }
        MarkObserved();
    }
}

public sealed class Query<T1, T2, T3> : QueryBase where T1 : struct where T2 : struct where T3 : struct
{
    internal readonly Id _c1, _c2, _c3;
    private bool _t1Optional, _t2Optional, _t3Optional;

    private static Id[] BuildWith(World w)
    {
        var c1 = (Id)w.Component<T1>();
        var c2 = (Id)w.Component<T2>();
        var c3 = (Id)w.Component<T3>();
        var tmp = QueryUtil.AppendSorted(new[] { c1 }, c2);
        return QueryUtil.AppendSorted(tmp, c3);
    }

    internal Query(World w) : base(w, BuildWith(w))
    {
        _c1 = (Id)w.Component<T1>();
        _c2 = (Id)w.Component<T2>();
        _c3 = (Id)w.Component<T3>();
    }

    public Query<T1, T2, T3> With<T>() where T : struct { AddWith(_world.IdOf<T>()); return this; }
    public Query<T1, T2, T3> With(Id id) { AddWith(id); return this; }
    public Query<T1, T2, T3> Without<T>() where T : struct { AddWithout(_world.IdOf<T>()); return this; }
    public Query<T1, T2, T3> Without<TR, TT>() where TR : struct where TT : struct { AddWithout(_world.Pair<TR, TT>()); return this; }
    public Query<T1, T2, T3> Without(Id id) { AddWithout(id); return this; }
    public Query<T1, T2, T3> Or<TA, TB>() where TA : struct where TB : struct
    { AddOr(new[] { _world.IdOf<TA>(), _world.IdOf<TB>() }); return this; }
    public Query<T1, T2, T3> Or<TA, TB, TC>() where TA : struct where TB : struct where TC : struct
    { AddOr(new[] { _world.IdOf<TA>(), _world.IdOf<TB>(), _world.IdOf<TC>() }); return this; }

    public Query<T1, T2, T3> Optional<T>() where T : struct
    {
        if (typeof(T) == typeof(T1)) { if (!_t1Optional) { _t1Optional = true; _with = QueryUtil.Remove(_with, _c1); Reset(); } }
        else if (typeof(T) == typeof(T2)) { if (!_t2Optional) { _t2Optional = true; _with = QueryUtil.Remove(_with, _c2); Reset(); } }
        else if (typeof(T) == typeof(T3)) { if (!_t3Optional) { _t3Optional = true; _with = QueryUtil.Remove(_with, _c3); Reset(); } }
        else throw new ArgumentException($"Optional<{typeof(T).Name}>: T must be T1, T2, or T3 of this query.");
        return this;
    }

    public Query<T1, T2, T3> Inherited() { SetInherited(); return this; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TableEnumerator<T1, T2, T3> GetEnumerator() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2, T3> Rows() => new(this);

    public void Each(EachAction<T1, T2, T3> action)
    {
        using var _ = _world.Defer();
        Rematch();
        for (int ti = 0; ti < _matched.Count; ti++)
        {
            var t = _matched[ti];
            if (t.Count == 0) continue;
            var (col1, s1) = ResolveSource<T1>(t, _c1);
            var (col2, s2) = ResolveSource<T2>(t, _c2);
            var (col3, s3) = ResolveSource<T3>(t, _c3);
            if ((col1 == null && !_t1Optional)
                || (col2 == null && !_t2Optional)
                || (col3 == null && !_t3Optional)) continue;
            var ents = t.Entities;
            int n = t.Count;
            if (col1 != null && col2 != null && col3 != null && s1 < 0 && s2 < 0 && s3 < 0)
            {
                for (int r = 0; r < n; r++)
                    action(ents[r], ref col1.GetRef(r), ref col2.GetRef(r), ref col3.GetRef(r));
            }
            else
            {
                for (int r = 0; r < n; r++)
                    action(ents[r],
                        ref QueryUtil.Resolve(col1, s1, r),
                        ref QueryUtil.Resolve(col2, s2, r),
                        ref QueryUtil.Resolve(col3, s3, r));
            }
        }
        MarkObserved();
    }

    public void Run(IterAction<T1, T2, T3> action)
    {
        using var _ = _world.Defer();
        Rematch();
        for (int ti = 0; ti < _matched.Count; ti++)
        {
            var t = _matched[ti];
            if (t.Count == 0) continue;
            if (_inherited && (!t.Has(_c1) || !t.Has(_c2) || !t.Has(_c3))) continue;
            var it = new Iter<T1, T2, T3>(_world, t, _c1, _c2, _c3);
            action(in it);
        }
        MarkObserved();
    }
}

public sealed class Query<T1, T2, T3, T4> : QueryBase
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct
{
    internal readonly Id _c1, _c2, _c3, _c4;

    private static Id[] BuildWith(World w)
    {
        var c1 = (Id)w.Component<T1>();
        var c2 = (Id)w.Component<T2>();
        var c3 = (Id)w.Component<T3>();
        var c4 = (Id)w.Component<T4>();
        var t1 = QueryUtil.AppendSorted(new[] { c1 }, c2);
        var t2 = QueryUtil.AppendSorted(t1, c3);
        return QueryUtil.AppendSorted(t2, c4);
    }

    internal Query(World w) : base(w, BuildWith(w))
    {
        _c1 = (Id)w.Component<T1>();
        _c2 = (Id)w.Component<T2>();
        _c3 = (Id)w.Component<T3>();
        _c4 = (Id)w.Component<T4>();
    }

    public Query<T1, T2, T3, T4> With<T>() where T : struct { AddWith(_world.IdOf<T>()); return this; }
    public Query<T1, T2, T3, T4> With(Id id) { AddWith(id); return this; }
    public Query<T1, T2, T3, T4> Without<T>() where T : struct { AddWithout(_world.IdOf<T>()); return this; }
    public Query<T1, T2, T3, T4> Without<TR, TT>() where TR : struct where TT : struct { AddWithout(_world.Pair<TR, TT>()); return this; }
    public Query<T1, T2, T3, T4> Without(Id id) { AddWithout(id); return this; }
    public Query<T1, T2, T3, T4> Or<TA, TB>() where TA : struct where TB : struct
    { AddOr(new[] { _world.IdOf<TA>(), _world.IdOf<TB>() }); return this; }
    public Query<T1, T2, T3, T4> Or<TA, TB, TC>() where TA : struct where TB : struct where TC : struct
    { AddOr(new[] { _world.IdOf<TA>(), _world.IdOf<TB>(), _world.IdOf<TC>() }); return this; }

    public Query<T1, T2, T3, T4> Inherited() { SetInherited(); return this; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TableEnumerator<T1, T2, T3, T4> GetEnumerator() => new(this);

    public void Each(EachAction<T1, T2, T3, T4> action)
    {
        using var _ = _world.Defer();
        Rematch();
        for (int ti = 0; ti < _matched.Count; ti++)
        {
            var t = _matched[ti];
            if (t.Count == 0) continue;
            var (col1, s1) = ResolveSource<T1>(t, _c1);
            var (col2, s2) = ResolveSource<T2>(t, _c2);
            var (col3, s3) = ResolveSource<T3>(t, _c3);
            var (col4, s4) = ResolveSource<T4>(t, _c4);
            if (col1 == null || col2 == null || col3 == null || col4 == null) continue;
            var ents = t.Entities;
            int n = t.Count;
            if (s1 < 0 && s2 < 0 && s3 < 0 && s4 < 0)
            {
                for (int r = 0; r < n; r++)
                    action(ents[r], ref col1.GetRef(r), ref col2.GetRef(r),
                                    ref col3.GetRef(r), ref col4.GetRef(r));
            }
            else
            {
                for (int r = 0; r < n; r++)
                    action(ents[r],
                        ref QueryUtil.Resolve(col1, s1, r),
                        ref QueryUtil.Resolve(col2, s2, r),
                        ref QueryUtil.Resolve(col3, s3, r),
                        ref QueryUtil.Resolve(col4, s4, r));
            }
        }
        MarkObserved();
    }

    public void Run(IterAction<T1, T2, T3, T4> action)
    {
        using var _ = _world.Defer();
        Rematch();
        for (int ti = 0; ti < _matched.Count; ti++)
        {
            var t = _matched[ti];
            if (t.Count == 0) continue;
            if (_inherited && (!t.Has(_c1) || !t.Has(_c2) || !t.Has(_c3) || !t.Has(_c4))) continue;
            var it = new Iter<T1, T2, T3, T4>(_world, t, _c1, _c2, _c3, _c4);
            action(in it);
        }
        MarkObserved();
    }
}

public sealed class Query<T1, T2, T3, T4, T5> : QueryBase
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
{
    internal readonly Id _c1, _c2, _c3, _c4, _c5;

    private static Id[] BuildWith(World w)
    {
        var ids = new[] {
            (Id)w.Component<T1>(), (Id)w.Component<T2>(), (Id)w.Component<T3>(),
            (Id)w.Component<T4>(), (Id)w.Component<T5>(),
        };
        Id[] result = Array.Empty<Id>();
        for (int i = 0; i < ids.Length; i++) result = QueryUtil.AppendSorted(result, ids[i]);
        return result;
    }

    internal Query(World w) : base(w, BuildWith(w))
    {
        _c1 = (Id)w.Component<T1>(); _c2 = (Id)w.Component<T2>(); _c3 = (Id)w.Component<T3>();
        _c4 = (Id)w.Component<T4>(); _c5 = (Id)w.Component<T5>();
    }

    public Query<T1, T2, T3, T4, T5> Without<T>() where T : struct { AddWithout(_world.IdOf<T>()); return this; }
    public Query<T1, T2, T3, T4, T5> Without(Id id) { AddWithout(id); return this; }
    public Query<T1, T2, T3, T4, T5> Or<TA, TB>() where TA : struct where TB : struct
    { AddOr(new[] { _world.IdOf<TA>(), _world.IdOf<TB>() }); return this; }

    public Query<T1, T2, T3, T4, T5> Inherited() { SetInherited(); return this; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TableEnumerator<T1, T2, T3, T4, T5> GetEnumerator() => new(this);

    public void Each(EachAction<T1, T2, T3, T4, T5> action)
    {
        using var _ = _world.Defer();
        Rematch();
        for (int ti = 0; ti < _matched.Count; ti++)
        {
            var t = _matched[ti];
            if (t.Count == 0) continue;
            var (col1, s1) = ResolveSource<T1>(t, _c1);
            var (col2, s2) = ResolveSource<T2>(t, _c2);
            var (col3, s3) = ResolveSource<T3>(t, _c3);
            var (col4, s4) = ResolveSource<T4>(t, _c4);
            var (col5, s5) = ResolveSource<T5>(t, _c5);
            if (col1 == null || col2 == null || col3 == null || col4 == null || col5 == null) continue;
            var ents = t.Entities;
            int n = t.Count;
            if (s1 < 0 && s2 < 0 && s3 < 0 && s4 < 0 && s5 < 0)
            {
                for (int r = 0; r < n; r++)
                    action(ents[r], ref col1.GetRef(r), ref col2.GetRef(r),
                                    ref col3.GetRef(r), ref col4.GetRef(r), ref col5.GetRef(r));
            }
            else
            {
                for (int r = 0; r < n; r++)
                    action(ents[r],
                        ref QueryUtil.Resolve(col1, s1, r),
                        ref QueryUtil.Resolve(col2, s2, r),
                        ref QueryUtil.Resolve(col3, s3, r),
                        ref QueryUtil.Resolve(col4, s4, r),
                        ref QueryUtil.Resolve(col5, s5, r));
            }
        }
        MarkObserved();
    }

    public void Run(IterAction<T1, T2, T3, T4, T5> action)
    {
        using var _ = _world.Defer();
        Rematch();
        for (int ti = 0; ti < _matched.Count; ti++)
        {
            var t = _matched[ti];
            if (t.Count == 0) continue;
            if (_inherited && (!t.Has(_c1) || !t.Has(_c2) || !t.Has(_c3) || !t.Has(_c4) || !t.Has(_c5))) continue;
            var it = new Iter<T1, T2, T3, T4, T5>(_world, t, _c1, _c2, _c3, _c4, _c5);
            action(in it);
        }
        MarkObserved();
    }
}

public sealed class Query<T1, T2, T3, T4, T5, T6> : QueryBase
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
{
    internal readonly Id _c1, _c2, _c3, _c4, _c5, _c6;

    private static Id[] BuildWith(World w)
    {
        var ids = new[] {
            (Id)w.Component<T1>(), (Id)w.Component<T2>(), (Id)w.Component<T3>(),
            (Id)w.Component<T4>(), (Id)w.Component<T5>(), (Id)w.Component<T6>(),
        };
        Id[] result = Array.Empty<Id>();
        for (int i = 0; i < ids.Length; i++) result = QueryUtil.AppendSorted(result, ids[i]);
        return result;
    }

    internal Query(World w) : base(w, BuildWith(w))
    {
        _c1 = (Id)w.Component<T1>(); _c2 = (Id)w.Component<T2>(); _c3 = (Id)w.Component<T3>();
        _c4 = (Id)w.Component<T4>(); _c5 = (Id)w.Component<T5>(); _c6 = (Id)w.Component<T6>();
    }

    public Query<T1, T2, T3, T4, T5, T6> Without<T>() where T : struct { AddWithout(_world.IdOf<T>()); return this; }
    public Query<T1, T2, T3, T4, T5, T6> Without(Id id) { AddWithout(id); return this; }

    public Query<T1, T2, T3, T4, T5, T6> Inherited() { SetInherited(); return this; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public TableEnumerator<T1, T2, T3, T4, T5, T6> GetEnumerator() => new(this);

    public void Each(EachAction<T1, T2, T3, T4, T5, T6> action)
    {
        using var _ = _world.Defer();
        Rematch();
        for (int ti = 0; ti < _matched.Count; ti++)
        {
            var t = _matched[ti];
            if (t.Count == 0) continue;
            var (col1, s1) = ResolveSource<T1>(t, _c1);
            var (col2, s2) = ResolveSource<T2>(t, _c2);
            var (col3, s3) = ResolveSource<T3>(t, _c3);
            var (col4, s4) = ResolveSource<T4>(t, _c4);
            var (col5, s5) = ResolveSource<T5>(t, _c5);
            var (col6, s6) = ResolveSource<T6>(t, _c6);
            if (col1 == null || col2 == null || col3 == null
                || col4 == null || col5 == null || col6 == null) continue;
            var ents = t.Entities;
            int n = t.Count;
            if (s1 < 0 && s2 < 0 && s3 < 0 && s4 < 0 && s5 < 0 && s6 < 0)
            {
                for (int r = 0; r < n; r++)
                    action(ents[r], ref col1.GetRef(r), ref col2.GetRef(r), ref col3.GetRef(r),
                                    ref col4.GetRef(r), ref col5.GetRef(r), ref col6.GetRef(r));
            }
            else
            {
                for (int r = 0; r < n; r++)
                    action(ents[r],
                        ref QueryUtil.Resolve(col1, s1, r),
                        ref QueryUtil.Resolve(col2, s2, r),
                        ref QueryUtil.Resolve(col3, s3, r),
                        ref QueryUtil.Resolve(col4, s4, r),
                        ref QueryUtil.Resolve(col5, s5, r),
                        ref QueryUtil.Resolve(col6, s6, r));
            }
        }
        MarkObserved();
    }

    public void Run(IterAction<T1, T2, T3, T4, T5, T6> action)
    {
        using var _ = _world.Defer();
        Rematch();
        for (int ti = 0; ti < _matched.Count; ti++)
        {
            var t = _matched[ti];
            if (t.Count == 0) continue;
            if (_inherited && (!t.Has(_c1) || !t.Has(_c2) || !t.Has(_c3)
                            || !t.Has(_c4) || !t.Has(_c5) || !t.Has(_c6))) continue;
            var it = new Iter<T1, T2, T3, T4, T5, T6>(_world, t, _c1, _c2, _c3, _c4, _c5, _c6);
            action(in it);
        }
        MarkObserved();
    }
}
