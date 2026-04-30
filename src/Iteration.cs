using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Flecs;

// ============================================================================
// Ptr<T> + RowEnumerator<T...> — sole query iteration API.
//
//   foreach (var (pos, vel) in world.Query<Position, Velocity>().Rows())
//   {
//       pos.Value.X *= vel.Value.Dx;
//       pos.Value.Y *= vel.Value.Dy;
//   }
//
// Two internal modes selected at ctor:
//   • Fast path  — own-only, no Optional, no CanToggle. Constant-+1 pointer-
//                  stride loop. RowEnumerator struct stays slim so the per-
//                  iter Current copy doesn't dominate.
//   • Filter path — any of (inherit, Optional, CanToggle term). Per-table
//                  state lives in a heap-pooled FilterState<...> object so
//                  the fast-path struct doesn't carry it. AdvanceFiltered
//                  skips bitset-disabled rows and yields Unsafe.NullRef for
//                  absent Optional slots.
// ============================================================================

public ref struct Ptr<T> where T : struct
{
    public ref T Value;
}

internal static class RowEnumeratorUtil
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref T Resolve<T>(Column<T>? col, int shared, int rowIdx) where T : struct
    {
        if (col == null) return ref Unsafe.NullRef<T>();
        return ref col.GetRef(shared < 0 ? rowIdx : shared);
    }
}

// ============================================================================
// FilterState — per-arity heap holder for filter-path state. Pooled per
// closed-generic via a [ThreadStatic] stack so steady-state filter iteration
// is alloc-free. Single-thread foreach safe; nested foreach over same arity
// re-uses or grows pool naturally.
// ============================================================================

internal sealed class FilterState<T1> where T1 : struct
{
    public Column<T1>? Col1;
    public int Shared1;
    public Bitset? Bs1;
    public Table? CurTable;

    [ThreadStatic] private static Stack<FilterState<T1>>? _pool;

    public static FilterState<T1> Rent()
    {
        var p = _pool ??= new Stack<FilterState<T1>>(4);
        return p.Count > 0 ? p.Pop() : new FilterState<T1>();
    }
    public static void Return(FilterState<T1> f)
    {
        f.Col1 = null; f.Bs1 = null; f.CurTable = null;
        _pool!.Push(f);
    }
}

internal sealed class FilterState<T1, T2> where T1 : struct where T2 : struct
{
    public Column<T1>? Col1; public Column<T2>? Col2;
    public int Shared1, Shared2;
    public Bitset? Bs1, Bs2;
    public Table? CurTable;

    [ThreadStatic] private static Stack<FilterState<T1, T2>>? _pool;

    public static FilterState<T1, T2> Rent()
    {
        var p = _pool ??= new Stack<FilterState<T1, T2>>(4);
        return p.Count > 0 ? p.Pop() : new FilterState<T1, T2>();
    }
    public static void Return(FilterState<T1, T2> f)
    {
        f.Col1 = null; f.Col2 = null; f.Bs1 = null; f.Bs2 = null; f.CurTable = null;
        _pool!.Push(f);
    }
}

internal sealed class FilterState<T1, T2, T3>
    where T1 : struct where T2 : struct where T3 : struct
{
    public Column<T1>? Col1; public Column<T2>? Col2; public Column<T3>? Col3;
    public int Shared1, Shared2, Shared3;
    public Bitset? Bs1, Bs2, Bs3;
    public Table? CurTable;

    [ThreadStatic] private static Stack<FilterState<T1, T2, T3>>? _pool;

    public static FilterState<T1, T2, T3> Rent()
    {
        var p = _pool ??= new Stack<FilterState<T1, T2, T3>>(4);
        return p.Count > 0 ? p.Pop() : new FilterState<T1, T2, T3>();
    }
    public static void Return(FilterState<T1, T2, T3> f)
    {
        f.Col1 = null; f.Col2 = null; f.Col3 = null;
        f.Bs1 = null; f.Bs2 = null; f.Bs3 = null; f.CurTable = null;
        _pool!.Push(f);
    }
}

internal sealed class FilterState<T1, T2, T3, T4>
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct
{
    public Column<T1>? Col1; public Column<T2>? Col2;
    public Column<T3>? Col3; public Column<T4>? Col4;
    public int Shared1, Shared2, Shared3, Shared4;
    public Bitset? Bs1, Bs2, Bs3, Bs4;
    public Table? CurTable;

    [ThreadStatic] private static Stack<FilterState<T1, T2, T3, T4>>? _pool;

    public static FilterState<T1, T2, T3, T4> Rent()
    {
        var p = _pool ??= new Stack<FilterState<T1, T2, T3, T4>>(4);
        return p.Count > 0 ? p.Pop() : new FilterState<T1, T2, T3, T4>();
    }
    public static void Return(FilterState<T1, T2, T3, T4> f)
    {
        f.Col1 = null; f.Col2 = null; f.Col3 = null; f.Col4 = null;
        f.Bs1 = null; f.Bs2 = null; f.Bs3 = null; f.Bs4 = null; f.CurTable = null;
        _pool!.Push(f);
    }
}

internal sealed class FilterState<T1, T2, T3, T4, T5>
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
{
    public Column<T1>? Col1; public Column<T2>? Col2; public Column<T3>? Col3;
    public Column<T4>? Col4; public Column<T5>? Col5;
    public int Shared1, Shared2, Shared3, Shared4, Shared5;
    public Bitset? Bs1, Bs2, Bs3, Bs4, Bs5;
    public Table? CurTable;

    [ThreadStatic] private static Stack<FilterState<T1, T2, T3, T4, T5>>? _pool;

    public static FilterState<T1, T2, T3, T4, T5> Rent()
    {
        var p = _pool ??= new Stack<FilterState<T1, T2, T3, T4, T5>>(4);
        return p.Count > 0 ? p.Pop() : new FilterState<T1, T2, T3, T4, T5>();
    }
    public static void Return(FilterState<T1, T2, T3, T4, T5> f)
    {
        f.Col1 = null; f.Col2 = null; f.Col3 = null; f.Col4 = null; f.Col5 = null;
        f.Bs1 = null; f.Bs2 = null; f.Bs3 = null; f.Bs4 = null; f.Bs5 = null;
        f.CurTable = null;
        _pool!.Push(f);
    }
}

internal sealed class FilterState<T1, T2, T3, T4, T5, T6>
    where T1 : struct where T2 : struct where T3 : struct
    where T4 : struct where T5 : struct where T6 : struct
{
    public Column<T1>? Col1; public Column<T2>? Col2; public Column<T3>? Col3;
    public Column<T4>? Col4; public Column<T5>? Col5; public Column<T6>? Col6;
    public int Shared1, Shared2, Shared3, Shared4, Shared5, Shared6;
    public Bitset? Bs1, Bs2, Bs3, Bs4, Bs5, Bs6;
    public Table? CurTable;

    [ThreadStatic] private static Stack<FilterState<T1, T2, T3, T4, T5, T6>>? _pool;

    public static FilterState<T1, T2, T3, T4, T5, T6> Rent()
    {
        var p = _pool ??= new Stack<FilterState<T1, T2, T3, T4, T5, T6>>(4);
        return p.Count > 0 ? p.Pop() : new FilterState<T1, T2, T3, T4, T5, T6>();
    }
    public static void Return(FilterState<T1, T2, T3, T4, T5, T6> f)
    {
        f.Col1 = null; f.Col2 = null; f.Col3 = null;
        f.Col4 = null; f.Col5 = null; f.Col6 = null;
        f.Bs1 = null; f.Bs2 = null; f.Bs3 = null;
        f.Bs4 = null; f.Bs5 = null; f.Bs6 = null;
        f.CurTable = null;
        _pool!.Push(f);
    }
}

// ============================================================================
// RowEnumerator — slim ref struct. Filter-only state lives on the heap-pooled
// FilterState (referenced via `_filter`, null in fast path).
// ============================================================================

public ref struct RowEnumerator<T1> where T1 : struct
{
    private readonly Query<T1> _query;
    private readonly bool _hasFilter;
    private ReadonlyScope _defer;
    private int _tableIdx;
    private int _rowIdx;
    private int _count;
    private bool _disposed;
    private Ptr<T1> _ptr1;
    private int _stride1;
    private FilterState<T1>? _filter;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal RowEnumerator(Query<T1> q)
    {
        _query = q;
        _hasFilter = q._anyInheritance || q._t1Optional || q._world.IsCanToggleId(q._c1);
        _filter = _hasFilter ? FilterState<T1>.Rent() : null;
        _defer = q._world.Readonly();
        q.Rematch();
        _tableIdx = -1;
        _rowIdx = -1;
        _count = 0;
        _disposed = false;
        _stride1 = 1;
    }

    public Ptr<T1> Component1
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _ptr1;
    }
    public EntityId Entity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_filter?.CurTable ?? _query._matched[_tableIdx]).Entities[_rowIdx];
    }
    public bool IsShared1 => _stride1 == 0;

    public RowEnumerator<T1> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (++_rowIdx < _count)
        {
            if (!_hasFilter)
            {
                _ptr1.Value = ref Unsafe.Add(ref _ptr1.Value, 1);
                return true;
            }
            if (AdvanceFiltered()) return true;
        }
        return MoveNextSlow();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AdvanceFiltered()
    {
        var f = _filter!;
        while (_rowIdx < _count)
        {
            if (f.Bs1 != null && !f.Bs1.Get(_rowIdx)) { _rowIdx++; continue; }
            _ptr1.Value = ref RowEnumeratorUtil.Resolve(f.Col1, f.Shared1, _rowIdx);
            return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MoveNextSlow()
    {
        var matched = CollectionsMarshal.AsSpan(_query._matched);
        while (true)
        {
            _tableIdx++;
            if (_tableIdx >= matched.Length) return false;
            var t = matched[_tableIdx];
            int n = t.Count;
            if (n == 0) continue;
            if (!_hasFilter)
            {
                var c = (Column<T1>)t.Columns[t.IndexOf(_query._c1)]!;
                _ptr1.Value = ref MemoryMarshal.GetReference(c.AsSpan());
                _count = n;
                _rowIdx = 0;
                return true;
            }
            var (col1, s1) = _query.ResolveSource<T1>(t, _query._c1);
            if (col1 == null && !_query._t1Optional) continue;
            var f = _filter!;
            f.Col1 = col1; f.Shared1 = s1; f.CurTable = t;
            _stride1 = (s1 < 0 && col1 != null) ? 1 : 0;
            f.Bs1 = _query.ResolveBitset(t, _query._c1, s1);
            _count = n;
            _rowIdx = 0;
            if (AdvanceFiltered()) return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1> GetEnumerator() => this;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_filter != null) FilterState<T1>.Return(_filter);
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}

public ref struct RowEnumerator<T1, T2>
    where T1 : struct where T2 : struct
{
    private readonly Query<T1, T2> _query;
    private readonly bool _hasFilter;
    private ReadonlyScope _defer;
    private int _tableIdx;
    private int _rowIdx;
    private int _count;
    private bool _disposed;
    private Ptr<T1> _ptr1;
    private Ptr<T2> _ptr2;
    private int _stride1, _stride2;
    private FilterState<T1, T2>? _filter;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal RowEnumerator(Query<T1, T2> q)
    {
        _query = q;
        _hasFilter = q._anyInheritance || q._t1Optional || q._t2Optional
            || q._world.IsCanToggleId(q._c1) || q._world.IsCanToggleId(q._c2);
        _filter = _hasFilter ? FilterState<T1, T2>.Rent() : null;
        _defer = q._world.Readonly();
        q.Rematch();
        _tableIdx = -1;
        _rowIdx = -1;
        _count = 0;
        _disposed = false;
        _stride1 = 1; _stride2 = 1;
    }

    public RowEnumerator<T1, T2> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this;
    }

    public Ptr<T1> Component1 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr1; }
    public Ptr<T2> Component2 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr2; }
    public EntityId Entity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_filter?.CurTable ?? _query._matched[_tableIdx]).Entities[_rowIdx];
    }
    public bool IsShared1 => _stride1 == 0;
    public bool IsShared2 => _stride2 == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Deconstruct(out Ptr<T1> a, out Ptr<T2> b) { a = _ptr1; b = _ptr2; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (++_rowIdx < _count)
        {
            if (!_hasFilter)
            {
                _ptr1.Value = ref Unsafe.Add(ref _ptr1.Value, 1);
                _ptr2.Value = ref Unsafe.Add(ref _ptr2.Value, 1);
                return true;
            }
            if (AdvanceFiltered()) return true;
        }
        return MoveNextSlow();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AdvanceFiltered()
    {
        var f = _filter!;
        while (_rowIdx < _count)
        {
            if (f.Bs1 != null && !f.Bs1.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs2 != null && !f.Bs2.Get(_rowIdx)) { _rowIdx++; continue; }
            _ptr1.Value = ref RowEnumeratorUtil.Resolve(f.Col1, f.Shared1, _rowIdx);
            _ptr2.Value = ref RowEnumeratorUtil.Resolve(f.Col2, f.Shared2, _rowIdx);
            return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MoveNextSlow()
    {
        var matched = CollectionsMarshal.AsSpan(_query._matched);
        while (true)
        {
            _tableIdx++;
            if (_tableIdx >= matched.Length) return false;
            var t = matched[_tableIdx];
            int n = t.Count;
            if (n == 0) continue;
            if (!_hasFilter)
            {
                var c1 = (Column<T1>)t.Columns[t.IndexOf(_query._c1)]!;
                var c2 = (Column<T2>)t.Columns[t.IndexOf(_query._c2)]!;
                _ptr1.Value = ref MemoryMarshal.GetReference(c1.AsSpan());
                _ptr2.Value = ref MemoryMarshal.GetReference(c2.AsSpan());
                _count = n;
                _rowIdx = 0;
                return true;
            }
            var (col1, s1) = _query.ResolveSource<T1>(t, _query._c1);
            var (col2, s2) = _query.ResolveSource<T2>(t, _query._c2);
            if ((col1 == null && !_query._t1Optional) || (col2 == null && !_query._t2Optional)) continue;
            var f = _filter!;
            f.Col1 = col1; f.Col2 = col2;
            f.Shared1 = s1; f.Shared2 = s2;
            f.CurTable = t;
            _stride1 = (s1 < 0 && col1 != null) ? 1 : 0;
            _stride2 = (s2 < 0 && col2 != null) ? 1 : 0;
            f.Bs1 = _query.ResolveBitset(t, _query._c1, s1);
            f.Bs2 = _query.ResolveBitset(t, _query._c2, s2);
            _count = n;
            _rowIdx = 0;
            if (AdvanceFiltered()) return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2> GetEnumerator() => this;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_filter != null) FilterState<T1, T2>.Return(_filter);
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}

public ref struct RowEnumerator<T1, T2, T3>
    where T1 : struct where T2 : struct where T3 : struct
{
    private readonly Query<T1, T2, T3> _query;
    private readonly bool _hasFilter;
    private ReadonlyScope _defer;
    private int _tableIdx;
    private int _rowIdx;
    private int _count;
    private bool _disposed;
    private Ptr<T1> _ptr1;
    private Ptr<T2> _ptr2;
    private Ptr<T3> _ptr3;
    private int _stride1, _stride2, _stride3;
    private FilterState<T1, T2, T3>? _filter;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal RowEnumerator(Query<T1, T2, T3> q)
    {
        _query = q;
        _hasFilter = q._anyInheritance || q._t1Optional || q._t2Optional || q._t3Optional
            || q._world.IsCanToggleId(q._c1) || q._world.IsCanToggleId(q._c2)
            || q._world.IsCanToggleId(q._c3);
        _filter = _hasFilter ? FilterState<T1, T2, T3>.Rent() : null;
        _defer = q._world.Readonly();
        q.Rematch();
        _tableIdx = -1;
        _rowIdx = -1;
        _count = 0;
        _disposed = false;
        _stride1 = 1; _stride2 = 1; _stride3 = 1;
    }

    public RowEnumerator<T1, T2, T3> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this;
    }
    public Ptr<T1> Component1 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr1; }
    public Ptr<T2> Component2 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr2; }
    public Ptr<T3> Component3 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr3; }
    public EntityId Entity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_filter?.CurTable ?? _query._matched[_tableIdx]).Entities[_rowIdx];
    }
    public bool IsShared1 => _stride1 == 0;
    public bool IsShared2 => _stride2 == 0;
    public bool IsShared3 => _stride3 == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Deconstruct(out Ptr<T1> a, out Ptr<T2> b, out Ptr<T3> c)
    { a = _ptr1; b = _ptr2; c = _ptr3; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (++_rowIdx < _count)
        {
            if (!_hasFilter)
            {
                _ptr1.Value = ref Unsafe.Add(ref _ptr1.Value, 1);
                _ptr2.Value = ref Unsafe.Add(ref _ptr2.Value, 1);
                _ptr3.Value = ref Unsafe.Add(ref _ptr3.Value, 1);
                return true;
            }
            if (AdvanceFiltered()) return true;
        }
        return MoveNextSlow();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AdvanceFiltered()
    {
        var f = _filter!;
        while (_rowIdx < _count)
        {
            if (f.Bs1 != null && !f.Bs1.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs2 != null && !f.Bs2.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs3 != null && !f.Bs3.Get(_rowIdx)) { _rowIdx++; continue; }
            _ptr1.Value = ref RowEnumeratorUtil.Resolve(f.Col1, f.Shared1, _rowIdx);
            _ptr2.Value = ref RowEnumeratorUtil.Resolve(f.Col2, f.Shared2, _rowIdx);
            _ptr3.Value = ref RowEnumeratorUtil.Resolve(f.Col3, f.Shared3, _rowIdx);
            return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MoveNextSlow()
    {
        var matched = CollectionsMarshal.AsSpan(_query._matched);
        while (true)
        {
            _tableIdx++;
            if (_tableIdx >= matched.Length) return false;
            var t = matched[_tableIdx];
            int n = t.Count;
            if (n == 0) continue;
            if (!_hasFilter)
            {
                var c1 = (Column<T1>)t.Columns[t.IndexOf(_query._c1)]!;
                var c2 = (Column<T2>)t.Columns[t.IndexOf(_query._c2)]!;
                var c3 = (Column<T3>)t.Columns[t.IndexOf(_query._c3)]!;
                _ptr1.Value = ref MemoryMarshal.GetReference(c1.AsSpan());
                _ptr2.Value = ref MemoryMarshal.GetReference(c2.AsSpan());
                _ptr3.Value = ref MemoryMarshal.GetReference(c3.AsSpan());
                _count = n;
                _rowIdx = 0;
                return true;
            }
            var (col1, s1) = _query.ResolveSource<T1>(t, _query._c1);
            var (col2, s2) = _query.ResolveSource<T2>(t, _query._c2);
            var (col3, s3) = _query.ResolveSource<T3>(t, _query._c3);
            if ((col1 == null && !_query._t1Optional)
                || (col2 == null && !_query._t2Optional)
                || (col3 == null && !_query._t3Optional)) continue;
            var f = _filter!;
            f.Col1 = col1; f.Col2 = col2; f.Col3 = col3;
            f.Shared1 = s1; f.Shared2 = s2; f.Shared3 = s3;
            f.CurTable = t;
            _stride1 = (s1 < 0 && col1 != null) ? 1 : 0;
            _stride2 = (s2 < 0 && col2 != null) ? 1 : 0;
            _stride3 = (s3 < 0 && col3 != null) ? 1 : 0;
            f.Bs1 = _query.ResolveBitset(t, _query._c1, s1);
            f.Bs2 = _query.ResolveBitset(t, _query._c2, s2);
            f.Bs3 = _query.ResolveBitset(t, _query._c3, s3);
            _count = n;
            _rowIdx = 0;
            if (AdvanceFiltered()) return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2, T3> GetEnumerator() => this;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_filter != null) FilterState<T1, T2, T3>.Return(_filter);
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}

public ref struct RowEnumerator<T1, T2, T3, T4>
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct
{
    private readonly Query<T1, T2, T3, T4> _query;
    private readonly bool _hasFilter;
    private ReadonlyScope _defer;
    private int _tableIdx;
    private int _rowIdx;
    private int _count;
    private bool _disposed;
    private Ptr<T1> _ptr1;
    private Ptr<T2> _ptr2;
    private Ptr<T3> _ptr3;
    private Ptr<T4> _ptr4;
    private int _stride1, _stride2, _stride3, _stride4;
    private FilterState<T1, T2, T3, T4>? _filter;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal RowEnumerator(Query<T1, T2, T3, T4> q)
    {
        _query = q;
        _hasFilter = q._anyInheritance
            || q._world.IsCanToggleId(q._c1) || q._world.IsCanToggleId(q._c2)
            || q._world.IsCanToggleId(q._c3) || q._world.IsCanToggleId(q._c4);
        _filter = _hasFilter ? FilterState<T1, T2, T3, T4>.Rent() : null;
        _defer = q._world.Readonly();
        q.Rematch();
        _tableIdx = -1;
        _rowIdx = -1;
        _count = 0;
        _disposed = false;
        _stride1 = 1; _stride2 = 1; _stride3 = 1; _stride4 = 1;
    }

    public RowEnumerator<T1, T2, T3, T4> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this;
    }
    public Ptr<T1> Component1 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr1; }
    public Ptr<T2> Component2 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr2; }
    public Ptr<T3> Component3 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr3; }
    public Ptr<T4> Component4 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr4; }
    public EntityId Entity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_filter?.CurTable ?? _query._matched[_tableIdx]).Entities[_rowIdx];
    }
    public bool IsShared1 => _stride1 == 0;
    public bool IsShared2 => _stride2 == 0;
    public bool IsShared3 => _stride3 == 0;
    public bool IsShared4 => _stride4 == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Deconstruct(out Ptr<T1> a, out Ptr<T2> b, out Ptr<T3> c, out Ptr<T4> d)
    { a = _ptr1; b = _ptr2; c = _ptr3; d = _ptr4; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (++_rowIdx < _count)
        {
            if (!_hasFilter)
            {
                _ptr1.Value = ref Unsafe.Add(ref _ptr1.Value, 1);
                _ptr2.Value = ref Unsafe.Add(ref _ptr2.Value, 1);
                _ptr3.Value = ref Unsafe.Add(ref _ptr3.Value, 1);
                _ptr4.Value = ref Unsafe.Add(ref _ptr4.Value, 1);
                return true;
            }
            if (AdvanceFiltered()) return true;
        }
        return MoveNextSlow();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AdvanceFiltered()
    {
        var f = _filter!;
        while (_rowIdx < _count)
        {
            if (f.Bs1 != null && !f.Bs1.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs2 != null && !f.Bs2.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs3 != null && !f.Bs3.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs4 != null && !f.Bs4.Get(_rowIdx)) { _rowIdx++; continue; }
            _ptr1.Value = ref RowEnumeratorUtil.Resolve(f.Col1, f.Shared1, _rowIdx);
            _ptr2.Value = ref RowEnumeratorUtil.Resolve(f.Col2, f.Shared2, _rowIdx);
            _ptr3.Value = ref RowEnumeratorUtil.Resolve(f.Col3, f.Shared3, _rowIdx);
            _ptr4.Value = ref RowEnumeratorUtil.Resolve(f.Col4, f.Shared4, _rowIdx);
            return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MoveNextSlow()
    {
        var matched = CollectionsMarshal.AsSpan(_query._matched);
        while (true)
        {
            _tableIdx++;
            if (_tableIdx >= matched.Length) return false;
            var t = matched[_tableIdx];
            int n = t.Count;
            if (n == 0) continue;
            if (!_hasFilter)
            {
                var c1 = (Column<T1>)t.Columns[t.IndexOf(_query._c1)]!;
                var c2 = (Column<T2>)t.Columns[t.IndexOf(_query._c2)]!;
                var c3 = (Column<T3>)t.Columns[t.IndexOf(_query._c3)]!;
                var c4 = (Column<T4>)t.Columns[t.IndexOf(_query._c4)]!;
                _ptr1.Value = ref MemoryMarshal.GetReference(c1.AsSpan());
                _ptr2.Value = ref MemoryMarshal.GetReference(c2.AsSpan());
                _ptr3.Value = ref MemoryMarshal.GetReference(c3.AsSpan());
                _ptr4.Value = ref MemoryMarshal.GetReference(c4.AsSpan());
                _count = n;
                _rowIdx = 0;
                return true;
            }
            var (col1, s1) = _query.ResolveSource<T1>(t, _query._c1);
            var (col2, s2) = _query.ResolveSource<T2>(t, _query._c2);
            var (col3, s3) = _query.ResolveSource<T3>(t, _query._c3);
            var (col4, s4) = _query.ResolveSource<T4>(t, _query._c4);
            if (col1 == null || col2 == null || col3 == null || col4 == null) continue;
            var f = _filter!;
            f.Col1 = col1; f.Col2 = col2; f.Col3 = col3; f.Col4 = col4;
            f.Shared1 = s1; f.Shared2 = s2; f.Shared3 = s3; f.Shared4 = s4;
            f.CurTable = t;
            _stride1 = s1 < 0 ? 1 : 0;
            _stride2 = s2 < 0 ? 1 : 0;
            _stride3 = s3 < 0 ? 1 : 0;
            _stride4 = s4 < 0 ? 1 : 0;
            f.Bs1 = _query.ResolveBitset(t, _query._c1, s1);
            f.Bs2 = _query.ResolveBitset(t, _query._c2, s2);
            f.Bs3 = _query.ResolveBitset(t, _query._c3, s3);
            f.Bs4 = _query.ResolveBitset(t, _query._c4, s4);
            _count = n;
            _rowIdx = 0;
            if (AdvanceFiltered()) return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2, T3, T4> GetEnumerator() => this;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_filter != null) FilterState<T1, T2, T3, T4>.Return(_filter);
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}

public ref struct RowEnumerator<T1, T2, T3, T4, T5>
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
{
    private readonly Query<T1, T2, T3, T4, T5> _query;
    private readonly bool _hasFilter;
    private ReadonlyScope _defer;
    private int _tableIdx;
    private int _rowIdx;
    private int _count;
    private bool _disposed;
    private Ptr<T1> _ptr1; private Ptr<T2> _ptr2; private Ptr<T3> _ptr3;
    private Ptr<T4> _ptr4; private Ptr<T5> _ptr5;
    private int _stride1, _stride2, _stride3, _stride4, _stride5;
    private FilterState<T1, T2, T3, T4, T5>? _filter;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal RowEnumerator(Query<T1, T2, T3, T4, T5> q)
    {
        _query = q;
        _hasFilter = q._anyInheritance
            || q._world.IsCanToggleId(q._c1) || q._world.IsCanToggleId(q._c2)
            || q._world.IsCanToggleId(q._c3) || q._world.IsCanToggleId(q._c4)
            || q._world.IsCanToggleId(q._c5);
        _filter = _hasFilter ? FilterState<T1, T2, T3, T4, T5>.Rent() : null;
        _defer = q._world.Readonly();
        q.Rematch();
        _tableIdx = -1;
        _rowIdx = -1;
        _count = 0;
        _disposed = false;
        _stride1 = 1; _stride2 = 1; _stride3 = 1; _stride4 = 1; _stride5 = 1;
    }

    public RowEnumerator<T1, T2, T3, T4, T5> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this;
    }
    public Ptr<T1> Component1 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr1; }
    public Ptr<T2> Component2 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr2; }
    public Ptr<T3> Component3 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr3; }
    public Ptr<T4> Component4 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr4; }
    public Ptr<T5> Component5 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr5; }
    public EntityId Entity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_filter?.CurTable ?? _query._matched[_tableIdx]).Entities[_rowIdx];
    }
    public bool IsShared1 => _stride1 == 0;
    public bool IsShared2 => _stride2 == 0;
    public bool IsShared3 => _stride3 == 0;
    public bool IsShared4 => _stride4 == 0;
    public bool IsShared5 => _stride5 == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Deconstruct(out Ptr<T1> a, out Ptr<T2> b, out Ptr<T3> c,
                                     out Ptr<T4> d, out Ptr<T5> e)
    { a = _ptr1; b = _ptr2; c = _ptr3; d = _ptr4; e = _ptr5; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (++_rowIdx < _count)
        {
            if (!_hasFilter)
            {
                _ptr1.Value = ref Unsafe.Add(ref _ptr1.Value, 1);
                _ptr2.Value = ref Unsafe.Add(ref _ptr2.Value, 1);
                _ptr3.Value = ref Unsafe.Add(ref _ptr3.Value, 1);
                _ptr4.Value = ref Unsafe.Add(ref _ptr4.Value, 1);
                _ptr5.Value = ref Unsafe.Add(ref _ptr5.Value, 1);
                return true;
            }
            if (AdvanceFiltered()) return true;
        }
        return MoveNextSlow();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AdvanceFiltered()
    {
        var f = _filter!;
        while (_rowIdx < _count)
        {
            if (f.Bs1 != null && !f.Bs1.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs2 != null && !f.Bs2.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs3 != null && !f.Bs3.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs4 != null && !f.Bs4.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs5 != null && !f.Bs5.Get(_rowIdx)) { _rowIdx++; continue; }
            _ptr1.Value = ref RowEnumeratorUtil.Resolve(f.Col1, f.Shared1, _rowIdx);
            _ptr2.Value = ref RowEnumeratorUtil.Resolve(f.Col2, f.Shared2, _rowIdx);
            _ptr3.Value = ref RowEnumeratorUtil.Resolve(f.Col3, f.Shared3, _rowIdx);
            _ptr4.Value = ref RowEnumeratorUtil.Resolve(f.Col4, f.Shared4, _rowIdx);
            _ptr5.Value = ref RowEnumeratorUtil.Resolve(f.Col5, f.Shared5, _rowIdx);
            return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MoveNextSlow()
    {
        var matched = CollectionsMarshal.AsSpan(_query._matched);
        while (true)
        {
            _tableIdx++;
            if (_tableIdx >= matched.Length) return false;
            var t = matched[_tableIdx];
            int n = t.Count;
            if (n == 0) continue;
            if (!_hasFilter)
            {
                var c1 = (Column<T1>)t.Columns[t.IndexOf(_query._c1)]!;
                var c2 = (Column<T2>)t.Columns[t.IndexOf(_query._c2)]!;
                var c3 = (Column<T3>)t.Columns[t.IndexOf(_query._c3)]!;
                var c4 = (Column<T4>)t.Columns[t.IndexOf(_query._c4)]!;
                var c5 = (Column<T5>)t.Columns[t.IndexOf(_query._c5)]!;
                _ptr1.Value = ref MemoryMarshal.GetReference(c1.AsSpan());
                _ptr2.Value = ref MemoryMarshal.GetReference(c2.AsSpan());
                _ptr3.Value = ref MemoryMarshal.GetReference(c3.AsSpan());
                _ptr4.Value = ref MemoryMarshal.GetReference(c4.AsSpan());
                _ptr5.Value = ref MemoryMarshal.GetReference(c5.AsSpan());
                _count = n;
                _rowIdx = 0;
                return true;
            }
            var (col1, s1) = _query.ResolveSource<T1>(t, _query._c1);
            var (col2, s2) = _query.ResolveSource<T2>(t, _query._c2);
            var (col3, s3) = _query.ResolveSource<T3>(t, _query._c3);
            var (col4, s4) = _query.ResolveSource<T4>(t, _query._c4);
            var (col5, s5) = _query.ResolveSource<T5>(t, _query._c5);
            if (col1 == null || col2 == null || col3 == null || col4 == null || col5 == null) continue;
            var f = _filter!;
            f.Col1 = col1; f.Col2 = col2; f.Col3 = col3; f.Col4 = col4; f.Col5 = col5;
            f.Shared1 = s1; f.Shared2 = s2; f.Shared3 = s3; f.Shared4 = s4; f.Shared5 = s5;
            f.CurTable = t;
            _stride1 = s1 < 0 ? 1 : 0;
            _stride2 = s2 < 0 ? 1 : 0;
            _stride3 = s3 < 0 ? 1 : 0;
            _stride4 = s4 < 0 ? 1 : 0;
            _stride5 = s5 < 0 ? 1 : 0;
            f.Bs1 = _query.ResolveBitset(t, _query._c1, s1);
            f.Bs2 = _query.ResolveBitset(t, _query._c2, s2);
            f.Bs3 = _query.ResolveBitset(t, _query._c3, s3);
            f.Bs4 = _query.ResolveBitset(t, _query._c4, s4);
            f.Bs5 = _query.ResolveBitset(t, _query._c5, s5);
            _count = n;
            _rowIdx = 0;
            if (AdvanceFiltered()) return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2, T3, T4, T5> GetEnumerator() => this;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_filter != null) FilterState<T1, T2, T3, T4, T5>.Return(_filter);
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}

public ref struct RowEnumerator<T1, T2, T3, T4, T5, T6>
    where T1 : struct where T2 : struct where T3 : struct
    where T4 : struct where T5 : struct where T6 : struct
{
    private readonly Query<T1, T2, T3, T4, T5, T6> _query;
    private readonly bool _hasFilter;
    private ReadonlyScope _defer;
    private int _tableIdx;
    private int _rowIdx;
    private int _count;
    private bool _disposed;
    private Ptr<T1> _ptr1; private Ptr<T2> _ptr2; private Ptr<T3> _ptr3;
    private Ptr<T4> _ptr4; private Ptr<T5> _ptr5; private Ptr<T6> _ptr6;
    private int _stride1, _stride2, _stride3, _stride4, _stride5, _stride6;
    private FilterState<T1, T2, T3, T4, T5, T6>? _filter;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal RowEnumerator(Query<T1, T2, T3, T4, T5, T6> q)
    {
        _query = q;
        _hasFilter = q._anyInheritance
            || q._world.IsCanToggleId(q._c1) || q._world.IsCanToggleId(q._c2)
            || q._world.IsCanToggleId(q._c3) || q._world.IsCanToggleId(q._c4)
            || q._world.IsCanToggleId(q._c5) || q._world.IsCanToggleId(q._c6);
        _filter = _hasFilter ? FilterState<T1, T2, T3, T4, T5, T6>.Rent() : null;
        _defer = q._world.Readonly();
        q.Rematch();
        _tableIdx = -1;
        _rowIdx = -1;
        _count = 0;
        _disposed = false;
        _stride1 = 1; _stride2 = 1; _stride3 = 1; _stride4 = 1; _stride5 = 1; _stride6 = 1;
    }

    public RowEnumerator<T1, T2, T3, T4, T5, T6> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this;
    }
    public Ptr<T1> Component1 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr1; }
    public Ptr<T2> Component2 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr2; }
    public Ptr<T3> Component3 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr3; }
    public Ptr<T4> Component4 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr4; }
    public Ptr<T5> Component5 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr5; }
    public Ptr<T6> Component6 { [MethodImpl(MethodImplOptions.AggressiveInlining)] get => _ptr6; }
    public EntityId Entity
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => (_filter?.CurTable ?? _query._matched[_tableIdx]).Entities[_rowIdx];
    }
    public bool IsShared1 => _stride1 == 0;
    public bool IsShared2 => _stride2 == 0;
    public bool IsShared3 => _stride3 == 0;
    public bool IsShared4 => _stride4 == 0;
    public bool IsShared5 => _stride5 == 0;
    public bool IsShared6 => _stride6 == 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Deconstruct(out Ptr<T1> a, out Ptr<T2> b, out Ptr<T3> c,
                                     out Ptr<T4> d, out Ptr<T5> e, out Ptr<T6> f)
    { a = _ptr1; b = _ptr2; c = _ptr3; d = _ptr4; e = _ptr5; f = _ptr6; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (++_rowIdx < _count)
        {
            if (!_hasFilter)
            {
                _ptr1.Value = ref Unsafe.Add(ref _ptr1.Value, 1);
                _ptr2.Value = ref Unsafe.Add(ref _ptr2.Value, 1);
                _ptr3.Value = ref Unsafe.Add(ref _ptr3.Value, 1);
                _ptr4.Value = ref Unsafe.Add(ref _ptr4.Value, 1);
                _ptr5.Value = ref Unsafe.Add(ref _ptr5.Value, 1);
                _ptr6.Value = ref Unsafe.Add(ref _ptr6.Value, 1);
                return true;
            }
            if (AdvanceFiltered()) return true;
        }
        return MoveNextSlow();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool AdvanceFiltered()
    {
        var f = _filter!;
        while (_rowIdx < _count)
        {
            if (f.Bs1 != null && !f.Bs1.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs2 != null && !f.Bs2.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs3 != null && !f.Bs3.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs4 != null && !f.Bs4.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs5 != null && !f.Bs5.Get(_rowIdx)) { _rowIdx++; continue; }
            if (f.Bs6 != null && !f.Bs6.Get(_rowIdx)) { _rowIdx++; continue; }
            _ptr1.Value = ref RowEnumeratorUtil.Resolve(f.Col1, f.Shared1, _rowIdx);
            _ptr2.Value = ref RowEnumeratorUtil.Resolve(f.Col2, f.Shared2, _rowIdx);
            _ptr3.Value = ref RowEnumeratorUtil.Resolve(f.Col3, f.Shared3, _rowIdx);
            _ptr4.Value = ref RowEnumeratorUtil.Resolve(f.Col4, f.Shared4, _rowIdx);
            _ptr5.Value = ref RowEnumeratorUtil.Resolve(f.Col5, f.Shared5, _rowIdx);
            _ptr6.Value = ref RowEnumeratorUtil.Resolve(f.Col6, f.Shared6, _rowIdx);
            return true;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private bool MoveNextSlow()
    {
        var matched = CollectionsMarshal.AsSpan(_query._matched);
        while (true)
        {
            _tableIdx++;
            if (_tableIdx >= matched.Length) return false;
            var t = matched[_tableIdx];
            int n = t.Count;
            if (n == 0) continue;
            if (!_hasFilter)
            {
                var c1 = (Column<T1>)t.Columns[t.IndexOf(_query._c1)]!;
                var c2 = (Column<T2>)t.Columns[t.IndexOf(_query._c2)]!;
                var c3 = (Column<T3>)t.Columns[t.IndexOf(_query._c3)]!;
                var c4 = (Column<T4>)t.Columns[t.IndexOf(_query._c4)]!;
                var c5 = (Column<T5>)t.Columns[t.IndexOf(_query._c5)]!;
                var c6 = (Column<T6>)t.Columns[t.IndexOf(_query._c6)]!;
                _ptr1.Value = ref MemoryMarshal.GetReference(c1.AsSpan());
                _ptr2.Value = ref MemoryMarshal.GetReference(c2.AsSpan());
                _ptr3.Value = ref MemoryMarshal.GetReference(c3.AsSpan());
                _ptr4.Value = ref MemoryMarshal.GetReference(c4.AsSpan());
                _ptr5.Value = ref MemoryMarshal.GetReference(c5.AsSpan());
                _ptr6.Value = ref MemoryMarshal.GetReference(c6.AsSpan());
                _count = n;
                _rowIdx = 0;
                return true;
            }
            var (col1, s1) = _query.ResolveSource<T1>(t, _query._c1);
            var (col2, s2) = _query.ResolveSource<T2>(t, _query._c2);
            var (col3, s3) = _query.ResolveSource<T3>(t, _query._c3);
            var (col4, s4) = _query.ResolveSource<T4>(t, _query._c4);
            var (col5, s5) = _query.ResolveSource<T5>(t, _query._c5);
            var (col6, s6) = _query.ResolveSource<T6>(t, _query._c6);
            if (col1 == null || col2 == null || col3 == null
                || col4 == null || col5 == null || col6 == null) continue;
            var f = _filter!;
            f.Col1 = col1; f.Col2 = col2; f.Col3 = col3;
            f.Col4 = col4; f.Col5 = col5; f.Col6 = col6;
            f.Shared1 = s1; f.Shared2 = s2; f.Shared3 = s3;
            f.Shared4 = s4; f.Shared5 = s5; f.Shared6 = s6;
            f.CurTable = t;
            _stride1 = s1 < 0 ? 1 : 0;
            _stride2 = s2 < 0 ? 1 : 0;
            _stride3 = s3 < 0 ? 1 : 0;
            _stride4 = s4 < 0 ? 1 : 0;
            _stride5 = s5 < 0 ? 1 : 0;
            _stride6 = s6 < 0 ? 1 : 0;
            f.Bs1 = _query.ResolveBitset(t, _query._c1, s1);
            f.Bs2 = _query.ResolveBitset(t, _query._c2, s2);
            f.Bs3 = _query.ResolveBitset(t, _query._c3, s3);
            f.Bs4 = _query.ResolveBitset(t, _query._c4, s4);
            f.Bs5 = _query.ResolveBitset(t, _query._c5, s5);
            f.Bs6 = _query.ResolveBitset(t, _query._c6, s6);
            _count = n;
            _rowIdx = 0;
            if (AdvanceFiltered()) return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2, T3, T4, T5, T6> GetEnumerator() => this;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_filter != null) FilterState<T1, T2, T3, T4, T5, T6>.Return(_filter);
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}
