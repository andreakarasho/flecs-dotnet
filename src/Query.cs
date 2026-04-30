using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Flecs;

// ============================================================================
// Query iteration — sole API is RowEnumerator (per-row foreach). Use
// `query.Rows()` or `foreach (var (...) in query)` directly.
// ============================================================================
// ============================================================================
// QueryBase — shared infrastructure: signature, table-cache, change detection,
// Or-group support. Concrete typed Query<T...> derive.
// ============================================================================
// Per-term traversal config. When a term has an entry, matching/source
// resolution walks the configured relation chain. Default for an absent
// term: literal Self. Mirrors flecs term src/trav/depth.
internal struct TermTraversal
{
    public uint Relation;   // relation entity id to walk (IsA / ChildOf / custom)
    public int MaxDepth;    // -1 = unlimited, 1 = direct only (Parent), etc.
}

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
    // Per-term traversal overrides. An entry wins over _inherited for that
    // specific term; absent entries fall back to _inherited (IsA) or literal.
    internal Dictionary<Id, TermTraversal>? _termTraversals;
    // Cascade ordering relation. 0 = no cascade. After Rematch, _matched is
    // sorted by RelationDepth ascending (ancestors first). Mirrors flecs
    // ecs_query_t cascade. Typical use: ChildOf for transform propagation.
    internal uint _cascadeRel;
    private Dictionary<int, int>? _lastVersion;

    // True iff matching considers anything beyond literal Self for any term.
    // Cached (not a computed property) so hot loops can branch on a single
    // field load rather than re-evaluating the dict null-check + count.
    // Updated by SetInherited / SetTermTraversal / SetCascade.
    internal bool _anyInheritance;

    // Subset of `_with` whose ids are Union pairs (relation marked Union).
    // Computed lazily via EnsureUnionWith. Union pairs don't gate archetype
    // match — RowEnumerator filter path checks per-row HasTarget here.
    internal Id[]? _unionWith;
    internal bool HasUnionWith => _unionWith is { Length: > 0 };

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void EnsureUnionWith()
    {
        if (_unionWith != null) return;
        List<Id>? list = null;
        for (int i = 0; i < _with.Length; i++)
        {
            var id = _with[i];
            if (id.IsPair && _world.IsUnionRel(id.Relation))
                (list ??= new List<Id>()).Add(id);
        }
        _unionWith = list?.ToArray() ?? Array.Empty<Id>();
    }

    // Per-row Union pair gate. Caller already verified HasUnionWith. Returns
    // true if every Union with-pair matches the entity's current target.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool MatchesUnionWith(uint entId)
    {
        var u = _unionWith!;
        for (int i = 0; i < u.Length; i++)
        {
            var pair = u[i];
            if (!_world._unionStorage[pair.Relation].HasTarget(entId, pair.Target))
                return false;
        }
        return true;
    }

    // Read-only term ids. Default: every term in `_with` is a writer; ids
    // here override to read-only. Used by the pipeline DAG to detect r/w
    // conflicts between concurrent systems. Mirrors flecs term inout=In.
    internal HashSet<Id>? _reads;

    // Read-set as Id[] (allocated lazily for pipeline analysis).
    public Id[] ReadIds
    {
        get
        {
            if (_reads == null || _reads.Count == 0) return Array.Empty<Id>();
            var arr = new Id[_reads.Count];
            int i = 0;
            foreach (var id in _reads) arr[i++] = id;
            Array.Sort(arr);
            return arr;
        }
    }

    // Write-set: every required term not flagged read.
    public Id[] WriteIds
    {
        get
        {
            if (_reads == null || _reads.Count == 0) return _with;
            var list = new List<Id>(_with.Length);
            for (int i = 0; i < _with.Length; i++)
                if (!_reads.Contains(_with[i])) list.Add(_with[i]);
            return list.ToArray();
        }
    }

    protected void MarkRead(Id id)
    {
        (_reads ??= new HashSet<Id>()).Add(id);
    }

    protected QueryBase(World w, Id[] with) { _world = w; _with = with; }

    protected void Reset() { _matched.Clear(); _matchedUpTo = 0; _lastVersion?.Clear(); _unionWith = null; }

    protected void AddWith(Id id) { _with = QueryUtil.AppendSorted(_with, id); Reset(); }
    protected void AddWithout(Id id) { _without = QueryUtil.AppendSorted(_without, id); Reset(); }
    protected void AddOr(Id[] group) { (_orGroups ??= new List<Id[]>()).Add(group); Reset(); }
    protected void SetInherited()
    {
        if (!_inherited) { _inherited = true; _anyInheritance = true; Reset(); }
    }

    // Add per-term traversal override. Wins over _inherited for this term.
    protected void SetTermTraversal(Id id, uint relation, int maxDepth)
    {
        _termTraversals ??= new Dictionary<Id, TermTraversal>();
        _termTraversals[id] = new TermTraversal { Relation = relation, MaxDepth = maxDepth };
        _anyInheritance = true;
        Reset();
    }

    // Enable depth-ordered iteration. Subsequent Each/Run/enum visits matched
    // tables in ascending RelationDepth order — ancestors before descendants.
    protected void SetCascade(uint relation)
    {
        if (_cascadeRel != relation) { _cascadeRel = relation; Reset(); }
    }

    protected internal void Rematch()
    {
        var tables = _world._tablesById;
        var worldForInherit = _anyInheritance ? _world : null;
        bool added = false;
        for (int i = _matchedUpTo + 1; i < tables.Count; i++)
        {
            var t = tables[i];
            if (t == null) continue;
            if (QueryUtil.Matches(t, _with, _without, _orGroups, _world.Wildcard.Id,
                    worldForInherit, _inherited, _termTraversals, _world))
            { _matched.Add(t); added = true; }
        }
        _matchedUpTo = tables.Count - 1;
        if (added && _cascadeRel != 0) SortMatchedByCascade();
    }

    // Sort _matched in ascending depth via _cascadeRel. Empty tables sort
    // last so iteration's `Count == 0` skip stays cheap. Allocates a temp
    // pair array — only on cascade queries, only when match set grows.
    private void SortMatchedByCascade()
    {
        int n = _matched.Count;
        if (n <= 1) return;
        var pairs = new (Table t, int depth)[n];
        for (int i = 0; i < n; i++)
        {
            var t = _matched[i];
            int d = (t.Count == 0) ? int.MaxValue
                                   : _world.RelationDepth(t.Entities[0], _cascadeRel);
            pairs[i] = (t, d);
        }
        Array.Sort(pairs, (a, b) => a.depth.CompareTo(b.depth));
        for (int i = 0; i < n; i++) _matched[i] = pairs[i].t;
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

    // Resolve the CanToggle bitset for term 'id' on table 't'. Returns null
    // when the term is not toggleable, the table has no bitsets, or the source
    // is shared (inherited) — shared CanToggle bits live on ancestor rows and
    // would disable every instance via a single ancestor flip; treat shared
    // refs as always enabled instead. Iteration's row-skip uses this: a null
    // result means "no per-row filter needed for this term".
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal Bitset? ResolveBitset(Table t, Id id, int sharedRow)
    {
        if (sharedRow >= 0) return null;
        if (!t.HasAnyBitset) return null;
        if (!t.Has(id)) return null;
        return t.Bits[t.IndexOf(id)];
    }

    // Resolve a per-table column source for term 'id'. When the table holds
    // 'id' directly: (col, sharedRow=-1) — caller indexes by row r.
    // When inheritance is enabled and the table only holds it via IsA:
    // (col-from-ancestor, sharedRow=ancestor-row) — caller passes sharedRow.
    // (null, -1) when absent (only legitimate for optional terms).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal (Column<T>? col, int sharedRow) ResolveSource<T>(Table t, Id id) where T : struct
    {
        if (t.Has(id)) return ((Column<T>?)t.Columns[t.IndexOf(id)], -1);
        if (t.Count == 0) return (null, -1);
        var seed = t.Entities[0];
        // Per-term override wins; else fall back to query-wide IsA inheritance.
        if (_termTraversals != null && _termTraversals.TryGetValue(id, out var trv))
        {
            var (found, src, row) = _world.FindInChain(seed, id, trv.Relation,
                blockable: true, maxDepth: trv.MaxDepth);
            if (found && src != null && src.Has(id))
                return ((Column<T>?)src.Columns[src.IndexOf(id)], row);
            return (null, -1);
        }
        if (_inherited)
        {
            var (found, src, row) = _world.FindInIsAChain(seed, id);
            if (found && src != null && src.Has(id))
                return ((Column<T>?)src.Columns[src.IndexOf(id)], row);
        }
        return (null, -1);
    }
}

internal static class QueryUtil
{
    // worldForInherit non-null enables Self+Up matching for 'with' / 'or' terms.
    // inheritedDefault=true → terms without explicit traversal walk IsA.
    // termTraversals provides per-term overrides (relation + maxDepth).
    // Without stays literal (Self-only) — flecs parity.
    public static bool Matches(Table t, Id[] with, Id[] without, List<Id[]>? orGroups, uint wildcard,
        World? worldForInherit = null, bool inheritedDefault = false,
        Dictionary<Id, TermTraversal>? termTraversals = null,
        World? worldForSparse = null)
    {
        for (int i = 0; i < with.Length; i++)
        {
            // Sparse terms don't gate archetype match — value lives in
            // SparseStorage<T> outside the archetype. Per-row Has check
            // happens during iteration (RowEnumerator filter path).
            if (worldForSparse != null && worldForSparse.IsSparseId(with[i])) continue;
            // Union pair terms are also non-fragmenting — relation never
            // enters archetype. Per-row HasTarget gates iteration.
            if (worldForSparse != null && with[i].IsPair && worldForSparse.IsUnionRel(with[i].Relation)) continue;
            if (!MatchesIdOrInherited(t, with[i], wildcard, worldForInherit, inheritedDefault, termTraversals))
                return false;
        }
        for (int i = 0; i < without.Length; i++)
            if (MatchesId(t, without[i], wildcard)) return false;
        if (orGroups != null)
        {
            for (int g = 0; g < orGroups.Count; g++)
            {
                var group = orGroups[g];
                bool any = false;
                for (int i = 0; i < group.Length; i++)
                {
                    if (worldForSparse != null && worldForSparse.IsSparseId(group[i])) { any = true; break; }
                    if (MatchesIdOrInherited(t, group[i], wildcard, worldForInherit, inheritedDefault, termTraversals))
                    { any = true; break; }
                }
                if (!any) return false;
            }
        }
        return true;
    }

    // Self-or-Up matcher. Per-term traversal wins over inheritedDefault.
    // Empty tables can't satisfy via inheritance — skipped at iteration anyway.
    private static bool MatchesIdOrInherited(Table t, Id id, uint wildcard,
        World? worldForInherit, bool inheritedDefault,
        Dictionary<Id, TermTraversal>? termTraversals)
    {
        if (MatchesId(t, id, wildcard)) return true;
        if (worldForInherit == null || t.Count == 0) return false;
        var seed = t.Entities[0];
        if (termTraversals != null && termTraversals.TryGetValue(id, out var trv))
        {
            var (found, _, _) = worldForInherit.FindInChain(seed, id, trv.Relation,
                blockable: true, maxDepth: trv.MaxDepth);
            return found;
        }
        if (inheritedDefault)
        {
            var (found, _, _) = worldForInherit.FindInIsAChain(seed, id);
            return found;
        }
        return false;
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
    internal bool _t1Optional;

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
            ThrowHelper.OptionalTypeMismatch(typeof(T), "T1");
        if (!_t1Optional) { _t1Optional = true; _with = QueryUtil.Remove(_with, _c1); Reset(); }
        return this;
    }

    // Opt-in to Self+Up(IsA) matching. Each() yields shared refs from prefab
    // archetypes when entities inherit terms via IsA. Run() and the foreach
    // enumerators stay literal — inherited-only tables are skipped there.
    public Query<T1> Inherited() { SetInherited(); return this; }

    // Mark a term as read-only for r/w analysis (pipeline DAG). Default is
    // write. Has no effect on iteration semantics; only changes what the
    // pipeline considers conflicting between concurrent systems.
    public Query<T1> Read<T>() where T : struct { MarkRead(_world.IdOf<T>()); return this; }

    // Per-term traversal (Self+Up). Up<T>() walks IsA chain (default).
    // Up<T>(rel) walks any relation. Parent<T>() = direct parent only via
    // ChildOf. Mirrors flecs term src=Up/Parent with optional trav relation.
    public Query<T1> Up<T>() where T : struct
    { SetTermTraversal(_world.IdOf<T>(), _world.IsA.Id, -1); return this; }
    public Query<T1> Up<T>(EntityId relation) where T : struct
    { SetTermTraversal(_world.IdOf<T>(), relation.Id, -1); return this; }
    public Query<T1> Parent<T>() where T : struct
    { SetTermTraversal(_world.IdOf<T>(), _world.ChildOf.Id, 1); return this; }
    // Cascade<T>: Up<T>(rel) + ancestors-first iteration. Default rel ChildOf.
    public Query<T1> Cascade<T>() where T : struct
    {
        SetTermTraversal(_world.IdOf<T>(), _world.ChildOf.Id, -1);
        SetCascade(_world.ChildOf.Id);
        return this;
    }
    public Query<T1> Cascade<T>(EntityId relation) where T : struct
    {
        SetTermTraversal(_world.IdOf<T>(), relation.Id, -1);
        SetCascade(relation.Id);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1> GetEnumerator() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1> Rows() => new(this);
}

public sealed class Query<T1, T2> : QueryBase where T1 : struct where T2 : struct
{
    internal readonly Id _c1, _c2;
    internal bool _t1Optional, _t2Optional;

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
            ThrowHelper.OptionalTypeMismatch(typeof(T), "T1 or T2");
        }
        return this;
    }

    public Query<T1, T2> Inherited() { SetInherited(); return this; }
    public Query<T1, T2> Read<T>() where T : struct { MarkRead(_world.IdOf<T>()); return this; }

    public Query<T1, T2> Up<T>() where T : struct
    { SetTermTraversal(_world.IdOf<T>(), _world.IsA.Id, -1); return this; }
    public Query<T1, T2> Up<T>(EntityId relation) where T : struct
    { SetTermTraversal(_world.IdOf<T>(), relation.Id, -1); return this; }
    public Query<T1, T2> Parent<T>() where T : struct
    { SetTermTraversal(_world.IdOf<T>(), _world.ChildOf.Id, 1); return this; }
    public Query<T1, T2> Cascade<T>() where T : struct
    {
        SetTermTraversal(_world.IdOf<T>(), _world.ChildOf.Id, -1);
        SetCascade(_world.ChildOf.Id);
        return this;
    }
    public Query<T1, T2> Cascade<T>(EntityId relation) where T : struct
    {
        SetTermTraversal(_world.IdOf<T>(), relation.Id, -1);
        SetCascade(relation.Id);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2> GetEnumerator() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2> Rows() => new(this);
}

public sealed class Query<T1, T2, T3> : QueryBase where T1 : struct where T2 : struct where T3 : struct
{
    internal readonly Id _c1, _c2, _c3;
    internal bool _t1Optional, _t2Optional, _t3Optional;

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
        else ThrowHelper.OptionalTypeMismatch(typeof(T), "T1, T2, or T3");
        return this;
    }

    public Query<T1, T2, T3> Inherited() { SetInherited(); return this; }
    public Query<T1, T2, T3> Read<T>() where T : struct { MarkRead(_world.IdOf<T>()); return this; }

    public Query<T1, T2, T3> Up<T>() where T : struct
    { SetTermTraversal(_world.IdOf<T>(), _world.IsA.Id, -1); return this; }
    public Query<T1, T2, T3> Up<T>(EntityId relation) where T : struct
    { SetTermTraversal(_world.IdOf<T>(), relation.Id, -1); return this; }
    public Query<T1, T2, T3> Parent<T>() where T : struct
    { SetTermTraversal(_world.IdOf<T>(), _world.ChildOf.Id, 1); return this; }
    public Query<T1, T2, T3> Cascade<T>() where T : struct
    {
        SetTermTraversal(_world.IdOf<T>(), _world.ChildOf.Id, -1);
        SetCascade(_world.ChildOf.Id);
        return this;
    }
    public Query<T1, T2, T3> Cascade<T>(EntityId relation) where T : struct
    {
        SetTermTraversal(_world.IdOf<T>(), relation.Id, -1);
        SetCascade(relation.Id);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2, T3> GetEnumerator() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2, T3> Rows() => new(this);
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
    public Query<T1, T2, T3, T4> Read<T>() where T : struct { MarkRead(_world.IdOf<T>()); return this; }

    public Query<T1, T2, T3, T4> Up<T>() where T : struct
    { SetTermTraversal(_world.IdOf<T>(), _world.IsA.Id, -1); return this; }
    public Query<T1, T2, T3, T4> Up<T>(EntityId relation) where T : struct
    { SetTermTraversal(_world.IdOf<T>(), relation.Id, -1); return this; }
    public Query<T1, T2, T3, T4> Parent<T>() where T : struct
    { SetTermTraversal(_world.IdOf<T>(), _world.ChildOf.Id, 1); return this; }
    public Query<T1, T2, T3, T4> Cascade<T>() where T : struct
    {
        SetTermTraversal(_world.IdOf<T>(), _world.ChildOf.Id, -1);
        SetCascade(_world.ChildOf.Id);
        return this;
    }
    public Query<T1, T2, T3, T4> Cascade<T>(EntityId relation) where T : struct
    {
        SetTermTraversal(_world.IdOf<T>(), relation.Id, -1);
        SetCascade(relation.Id);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2, T3, T4> GetEnumerator() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2, T3, T4> Rows() => new(this);
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
    public Query<T1, T2, T3, T4, T5> Read<T>() where T : struct { MarkRead(_world.IdOf<T>()); return this; }

    public Query<T1, T2, T3, T4, T5> Up<T>() where T : struct
    { SetTermTraversal(_world.IdOf<T>(), _world.IsA.Id, -1); return this; }
    public Query<T1, T2, T3, T4, T5> Up<T>(EntityId relation) where T : struct
    { SetTermTraversal(_world.IdOf<T>(), relation.Id, -1); return this; }
    public Query<T1, T2, T3, T4, T5> Parent<T>() where T : struct
    { SetTermTraversal(_world.IdOf<T>(), _world.ChildOf.Id, 1); return this; }
    public Query<T1, T2, T3, T4, T5> Cascade<T>() where T : struct
    {
        SetTermTraversal(_world.IdOf<T>(), _world.ChildOf.Id, -1);
        SetCascade(_world.ChildOf.Id);
        return this;
    }
    public Query<T1, T2, T3, T4, T5> Cascade<T>(EntityId relation) where T : struct
    {
        SetTermTraversal(_world.IdOf<T>(), relation.Id, -1);
        SetCascade(relation.Id);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2, T3, T4, T5> GetEnumerator() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2, T3, T4, T5> Rows() => new(this);
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
    public Query<T1, T2, T3, T4, T5, T6> Read<T>() where T : struct { MarkRead(_world.IdOf<T>()); return this; }

    public Query<T1, T2, T3, T4, T5, T6> Up<T>() where T : struct
    { SetTermTraversal(_world.IdOf<T>(), _world.IsA.Id, -1); return this; }
    public Query<T1, T2, T3, T4, T5, T6> Up<T>(EntityId relation) where T : struct
    { SetTermTraversal(_world.IdOf<T>(), relation.Id, -1); return this; }
    public Query<T1, T2, T3, T4, T5, T6> Parent<T>() where T : struct
    { SetTermTraversal(_world.IdOf<T>(), _world.ChildOf.Id, 1); return this; }
    public Query<T1, T2, T3, T4, T5, T6> Cascade<T>() where T : struct
    {
        SetTermTraversal(_world.IdOf<T>(), _world.ChildOf.Id, -1);
        SetCascade(_world.ChildOf.Id);
        return this;
    }
    public Query<T1, T2, T3, T4, T5, T6> Cascade<T>(EntityId relation) where T : struct
    {
        SetTermTraversal(_world.IdOf<T>(), relation.Id, -1);
        SetCascade(relation.Id);
        return this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2, T3, T4, T5, T6> GetEnumerator() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2, T3, T4, T5, T6> Rows() => new(this);
}
