#!/usr/bin/env python3
"""Generate Query<T1..TN> + RowEnumerator<T1..TN> + FilterState<T1..TN> for arities 7..16."""

ARITIES = list(range(7, 17))

def t_list(n):
    return ", ".join(f"T{i}" for i in range(1, n + 1))

def t_constraints(n):
    return " ".join(f"where T{i} : struct" for i in range(1, n + 1))

def gen_filterstate(n):
    cols = " ".join(f"public Column<T{i}>? Col{i};" for i in range(1, n + 1))
    shareds = ", ".join(f"Shared{i}" for i in range(1, n + 1))
    bs = ", ".join(f"Bs{i}" for i in range(1, n + 1))
    null_cols = " ".join(f"f.Col{i} = null;" for i in range(1, n + 1))
    null_bs = " ".join(f"f.Bs{i} = null;" for i in range(1, n + 1))
    cls = f"FilterState<{t_list(n)}>"
    return f"""
internal sealed class {cls}
    {t_constraints(n)}
{{
    {cols}
    public int {shareds};
    public Bitset? {bs};
    public Table? CurTable;

    [ThreadStatic] private static Stack<{cls}>? _pool;

    public static {cls} Rent()
    {{
        var p = _pool ??= new Stack<{cls}>(4);
        return p.Count > 0 ? p.Pop() : new {cls}();
    }}
    public static void Return({cls} f)
    {{
        {null_cols} {null_bs} f.CurTable = null;
        _pool!.Push(f);
    }}
}}
"""

def gen_query(n):
    Q = f"Query<{t_list(n)}>"
    cs = ", ".join(f"_c{i}" for i in range(1, n + 1))
    return f"""
public sealed class {Q} : QueryBase
    {t_constraints(n)}
{{
    internal readonly Id {cs};

    private static Id[] BuildWith(World w)
    {{
        var ids = new[] {{
            {", ".join(f"(Id)w.Component<T{i}>()" for i in range(1, n + 1))}
        }};
        Id[] result = Array.Empty<Id>();
        for (int i = 0; i < ids.Length; i++) result = QueryUtil.AppendSorted(result, ids[i]);
        return result;
    }}

    internal Query(World w) : base(w, BuildWith(w))
    {{
{chr(10).join(f'        _c{i} = (Id)w.Component<T{i}>();' for i in range(1, n + 1))}
    }}

    public {Q} With<T>() where T : struct {{ AddWith(_world.IdOf<T>()); return this; }}
    public {Q} With(Id id) {{ AddWith(id); return this; }}
    public {Q} Without<T>() where T : struct {{ AddWithout(_world.IdOf<T>()); return this; }}
    public {Q} Without<TR, TT>() where TR : struct where TT : struct {{ AddWithout(_world.Pair<TR, TT>()); return this; }}
    public {Q} Without(Id id) {{ AddWithout(id); return this; }}
    public {Q} Or<TA, TB>() where TA : struct where TB : struct
    {{ AddOr(new[] {{ _world.IdOf<TA>(), _world.IdOf<TB>() }}); return this; }}
    public {Q} Or<TA, TB, TC>() where TA : struct where TB : struct where TC : struct
    {{ AddOr(new[] {{ _world.IdOf<TA>(), _world.IdOf<TB>(), _world.IdOf<TC>() }}); return this; }}

    public {Q} Inherited() {{ SetInherited(); return this; }}
    public {Q} Read<T>() where T : struct {{ MarkRead(_world.IdOf<T>()); return this; }}

    public {Q} Up<T>() where T : struct
    {{ SetTermTraversal(_world.IdOf<T>(), _world.IsA.Id, -1); return this; }}
    public {Q} Up<T>(EntityId relation) where T : struct
    {{ SetTermTraversal(_world.IdOf<T>(), relation.Id, -1); return this; }}
    public {Q} Parent<T>() where T : struct
    {{ SetTermTraversal(_world.IdOf<T>(), _world.ChildOf.Id, 1); return this; }}
    public {Q} Cascade<T>() where T : struct
    {{
        SetTermTraversal(_world.IdOf<T>(), _world.ChildOf.Id, -1);
        SetCascade(_world.ChildOf.Id);
        return this;
    }}
    public {Q} Cascade<T>(EntityId relation) where T : struct
    {{
        SetTermTraversal(_world.IdOf<T>(), relation.Id, -1);
        SetCascade(relation.Id);
        return this;
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<{t_list(n)}> GetEnumerator() => new(this);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<{t_list(n)}> Rows() => new(this);
}}
"""

def gen_row_enum(n):
    R = f"RowEnumerator<{t_list(n)}>"
    F = f"FilterState<{t_list(n)}>"
    Q = f"Query<{t_list(n)}>"
    ptrs = "\n".join(f"    private Ptr<T{i}> _ptr{i};" for i in range(1, n + 1))
    strides = ", ".join(f"_stride{i}" for i in range(1, n + 1))

    ctor_check = "\n            || ".join(f"q._world.IsCanToggleId(q._c{i})" for i in range(1, n + 1))
    stride_init = " ".join(f"_stride{i} = 1;" for i in range(1, n + 1))

    component_props = "\n".join(
        f'    public Ptr<T{i}> Component{i} {{ [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr{i}; }}'
        for i in range(1, n + 1))
    isshared_props = "\n".join(f"    public bool IsShared{i} => _stride{i} == 0;" for i in range(1, n + 1))

    deconstruct_args = ", ".join(f"out Ptr<T{i}> p{i}" for i in range(1, n + 1))
    deconstruct_body = " ".join(f"p{i} = _ptr{i};" for i in range(1, n + 1))

    fast_advance = "\n".join(
        f"                _ptr{i}.Value = ref Unsafe.Add(ref _ptr{i}.Value, 1);" for i in range(1, n + 1))

    bitset_skips = "\n".join(
        f"            if (f.Bs{i} != null && !f.Bs{i}.Get(_rowIdx)) {{ _rowIdx++; continue; }}"
        for i in range(1, n + 1))
    filter_assigns = "\n".join(
        f"            _ptr{i}.Value = ref RowEnumeratorUtil.Resolve(f.Col{i}, f.Shared{i}, _rowIdx);"
        for i in range(1, n + 1))

    fast_table_setup = "\n".join(
        f"                var c{i} = (Column<T{i}>)t.Columns[t.IndexOf(_query._c{i})]!;"
        for i in range(1, n + 1))
    fast_ptr_init = "\n".join(
        f"                _ptr{i}.Value = ref MemoryMarshal.GetReference(c{i}.AsSpan());"
        for i in range(1, n + 1))

    resolve_calls = "\n".join(
        f"            var (col{i}, s{i}) = _query.ResolveSource<T{i}>(t, _query._c{i});"
        for i in range(1, n + 1))
    null_check = " || ".join(f"col{i} == null" for i in range(1, n + 1))
    f_col_assign = " ".join(f"f.Col{i} = col{i};" for i in range(1, n + 1))
    f_shared_assign = " ".join(f"f.Shared{i} = s{i};" for i in range(1, n + 1))
    stride_assign = "\n".join(f"            _stride{i} = s{i} < 0 ? 1 : 0;" for i in range(1, n + 1))
    bs_resolve = "\n".join(
        f"            f.Bs{i} = _query.ResolveBitset(t, _query._c{i}, s{i});"
        for i in range(1, n + 1))

    return f"""
public ref struct {R}
    {t_constraints(n)}
{{
    private readonly {Q} _query;
    private readonly bool _hasFilter;
    private ReadonlyScope _defer;
    private int _tableIdx;
    private int _rowIdx;
    private int _count;
    private bool _disposed;
{ptrs}
    private int {strides};
    private {F}? _filter;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal RowEnumerator({Q} q)
    {{
        _query = q;
        _hasFilter = q._anyInheritance
            || {ctor_check};
        _filter = _hasFilter ? {F}.Rent() : null;
        _defer = q._world.Readonly();
        q.Rematch();
        _tableIdx = -1;
        _rowIdx = -1;
        _count = 0;
        _disposed = false;
        {stride_init}
    }}

    public {R} Current
    {{
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this;
    }}
{component_props}
    public EntityId Entity
    {{
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_filter?.CurTable ?? _query._matched[_tableIdx]).Entities[_rowIdx];
    }}
{isshared_props}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Deconstruct({deconstruct_args})
    {{ {deconstruct_body} }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {{
        if (++_rowIdx < _count)
        {{
            if (!_hasFilter)
            {{
{fast_advance}
                return true;
            }}
            if (AdvanceFiltered()) return true;
        }}
        return MoveNextSlow();
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AdvanceFiltered()
    {{
        var f = _filter!;
        while (_rowIdx < _count)
        {{
{bitset_skips}
{filter_assigns}
            return true;
        }}
        return false;
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MoveNextSlow()
    {{
        var matched = CollectionsMarshal.AsSpan(_query._matched);
        while (true)
        {{
            _tableIdx++;
            if (_tableIdx >= matched.Length) return false;
            var t = matched[_tableIdx];
            int n = t.Count;
            if (n == 0) continue;
            if (!_hasFilter)
            {{
{fast_table_setup}
{fast_ptr_init}
                _count = n;
                _rowIdx = 0;
                return true;
            }}
{resolve_calls}
            if ({null_check}) continue;
            var f = _filter!;
            {f_col_assign}
            {f_shared_assign}
            f.CurTable = t;
{stride_assign}
{bs_resolve}
            _count = n;
            _rowIdx = 0;
            if (AdvanceFiltered()) return true;
        }}
    }}

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public {R} GetEnumerator() => this;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {{
        if (_disposed) return;
        _disposed = true;
        if (_filter != null) {F}.Return(_filter);
        _query.MarkObservedInternal();
        _defer.Dispose();
    }}
}}
"""

def gen_world_factories():
    lines = []
    for n in ARITIES:
        ts = t_list(n)
        cons = " ".join(f"where T{i} : struct" for i in range(1, n + 1))
        lines.append(f"    public Query<{ts}> Query<{ts}>() {cons} => new(this);")
    return "\n".join(lines)

def main():
    out = []
    out.append("""// Auto-generated by gen_arity.py — Query<T1..TN> + RowEnumerator + FilterState
// for arities 7..16. Re-generate via `python gen_arity.py` after editing the
// template; do not hand-edit. Mirrors the arity-1..6 forms in Iteration.cs /
// Query.cs (no Optional support — same as arity 4..6).
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Flecs;
""")
    for n in ARITIES:
        out.append(gen_filterstate(n))
        out.append(gen_query(n))
        out.append(gen_row_enum(n))
    with open("src/Query.Arity.cs", "w", encoding="utf-8") as f:
        f.write("\n".join(out))
    print("Wrote src/Query.Arity.cs")

    # Print World factory snippet for manual paste
    print("---- World factory snippet ----")
    print(gen_world_factories())

if __name__ == "__main__":
    main()
