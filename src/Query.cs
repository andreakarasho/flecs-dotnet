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
    internal Id[] _with;
    internal Id[] _without = Array.Empty<Id>();
    internal List<Id[]>? _orGroups;
    internal readonly List<Table> _matched = new();
    internal int _matchedUpTo;
    // Opt-in: when true, match also includes tables whose entities satisfy
    // 'with' terms via Self+Up(IsA). RowEnumerator resolves shared refs from
    // the ancestor archetype. Mirrors flecs query inheritance semantics.
    internal bool _inherited;
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

    // Per-row Union pair gate. Returns true when there are no Union with-pairs
    // (caller's call gets inlined to a single null/length check + return),
    // OR when every Union with-pair matches the entity's current target.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal bool MatchesUnionWith(uint entId)
    {
        var u = _unionWith;
        if (u is null || u.Length == 0) return true;
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

    // Write-set: every required term not flagged read. Returned array is a
    // fresh copy — caller may not mutate query state through it.
    public Id[] WriteIds
    {
        get
        {
            if (_reads == null || _reads.Count == 0) return (Id[])_with.Clone();
            var list = new List<Id>(_with.Length);
            for (int i = 0; i < _with.Length; i++)
                if (!_reads.Contains(_with[i])) list.Add(_with[i]);
            return list.ToArray();
        }
    }

    private protected void MarkRead(Id id)
    {
        (_reads ??= new HashSet<Id>()).Add(id);
    }

    private protected QueryBase(World w, Id[] with) { _world = w; _with = with; }

    private protected void Reset() { _matched.Clear(); _matchedUpTo = 0; _lastVersion?.Clear(); _unionWith = null; }

    private protected void AddWith(Id id) { _with = QueryUtil.AppendSorted(_with, id); Reset(); }
    private protected void AddWithout(Id id) { _without = QueryUtil.AppendSorted(_without, id); Reset(); }
    private protected void AddOr(Id[] group) { (_orGroups ??= new List<Id[]>()).Add(group); Reset(); }
    private protected void SetInherited()
    {
        if (!_inherited) { _inherited = true; _anyInheritance = true; Reset(); }
    }

    // Add per-term traversal override. Wins over _inherited for this term.
    private protected void SetTermTraversal(Id id, uint relation, int maxDepth)
    {
        _termTraversals ??= new Dictionary<Id, TermTraversal>();
        _termTraversals[id] = new TermTraversal { Relation = relation, MaxDepth = maxDepth };
        _anyInheritance = true;
        Reset();
    }

    // Enable depth-ordered iteration. Subsequent RowEnumerator visits matched
    // tables in ascending RelationDepth order — ancestors before descendants.
    private protected void SetCascade(uint relation)
    {
        if (_cascadeRel != relation) { _cascadeRel = relation; Reset(); }
    }

    internal void Rematch()
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

    // True if any matched table changed since last RowEnumerator dispose
    // (which calls MarkObserved).
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

    internal void MarkObserved()
    {
        var dict = _lastVersion ??= new Dictionary<int, int>();
        for (int i = 0; i < _matched.Count; i++)
        {
            var t = _matched[i];
            dict[t.Id] = t.Version;
        }
    }

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
