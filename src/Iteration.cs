using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Flecs;

// ============================================================================
// Ptr<T> + Row<T...> + RowEnumerator<T...> — per-row foreach with ref struct
// pointers. Ergonomic but ~10x slower than Run (JIT can't inline the Current
// getter struct copy + ref construction tightly enough).
//
//   foreach (var (pos, vel) in world.Query<Position, Velocity>().Rows())
//   {
//       pos.Value.X *= vel.Value.Dx;
//       pos.Value.Y *= vel.Value.Dy;
//   }
//
// For perf-critical loops, prefer Run or per-table foreach.
// ============================================================================

public ref struct Ptr<T> where T : struct
{
    public ref T Value;
}

// RowEnumerator is the per-row foreach path. It uses constant +1 pointer
// advances so the JIT bakes the stride into a literal, which is essential
// for the tight inner loop. As a consequence it does NOT support inherited
// (shared) terms — those would need a variable stride and cost ~2x.
// For inherited queries use Each / Run / TableEnumerator instead;
// inherited-only tables are silently skipped here.
public ref struct RowEnumerator<T1> where T1 : struct
{
    private readonly Query<T1> _query;
    private DeferScope _defer;
    private int _tableIdx;
    private int _rowIdx;
    private int _count;
    private bool _disposed;
    private Ptr<T1> _ptr1;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal RowEnumerator(Query<T1> q)
    {
        _query = q;
        _defer = q._world.Defer();
        q.Rematch();
        _tableIdx = -1;
        _rowIdx = -1;
        _count = 0;
        _disposed = false;
    }

    public Ptr<T1> Component1
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _ptr1;
    }

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
            _ptr1.Value = ref Unsafe.Add(ref _ptr1.Value, 1);
            return true;
        }
        return MoveNextSlow();
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
            if (_query._anyInheritance && !t.Has(_query._c1)) continue;
            var col1 = (Column<T1>)t.Columns[t.IndexOf(_query._c1)]!;
            _ptr1.Value = ref MemoryMarshal.GetReference(col1.AsSpan());
            _count = n;
            _rowIdx = 0;
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1> GetEnumerator() => this;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}

public ref struct RowEnumerator<T1, T2>
    where T1 : struct where T2 : struct
{
    private readonly Query<T1, T2> _query;
    private DeferScope _defer;
    private int _tableIdx;
    private int _rowIdx;
    private int _count;
    private bool _disposed;
    private Ptr<T1> _ptr1;
    private Ptr<T2> _ptr2;


    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal RowEnumerator(Query<T1, T2> q)
    {
        _query = q;
        _defer = q._world.Defer();
        q.Rematch();
        _tableIdx = -1;
        _rowIdx = -1;
        _count = 0;
        _disposed = false;
    }


    public RowEnumerator<T1, T2> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Deconstruct(out Ptr<T1> a, out Ptr<T2> b) { a = _ptr1; b = _ptr2; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (++_rowIdx < _count)
        {
            _ptr1.Value = ref Unsafe.Add(ref _ptr1.Value, 1);
            _ptr2.Value = ref Unsafe.Add(ref _ptr2.Value, 1);
            return true;
        }
        return MoveNextSlow();
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
            if (_query._anyInheritance && (!t.Has(_query._c1) || !t.Has(_query._c2))) continue;
            var col1 = (Column<T1>)t.Columns[t.IndexOf(_query._c1)]!;
            var col2 = (Column<T2>)t.Columns[t.IndexOf(_query._c2)]!;
            _ptr1.Value = ref MemoryMarshal.GetReference(col1.AsSpan());
            _ptr2.Value = ref MemoryMarshal.GetReference(col2.AsSpan());
            _count = n;
            _rowIdx = 0;
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2> GetEnumerator() => this;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}

public ref struct RowEnumerator<T1, T2, T3>
    where T1 : struct where T2 : struct where T3 : struct
{
    private readonly Query<T1, T2, T3> _query;
    private DeferScope _defer;
    private int _tableIdx;
    private int _rowIdx;
    private int _count;
    private bool _disposed;
    private Ptr<T1> _ptr1;
    private Ptr<T2> _ptr2;
    private Ptr<T3> _ptr3;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal RowEnumerator(Query<T1, T2, T3> q)
    {
        _query = q;
        _defer = q._world.Defer();
        q.Rematch();
        _tableIdx = -1;
        _rowIdx = -1;
        _count = 0;
        _disposed = false;
    }

    public RowEnumerator<T1, T2, T3> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => this;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public readonly void Deconstruct(out Ptr<T1> a, out Ptr<T2> b, out Ptr<T3> c)
    { a = _ptr1; b = _ptr2; c = _ptr3; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        if (++_rowIdx < _count)
        {
            _ptr1.Value = ref Unsafe.Add(ref _ptr1.Value, 1);
            _ptr2.Value = ref Unsafe.Add(ref _ptr2.Value, 1);
            _ptr3.Value = ref Unsafe.Add(ref _ptr3.Value, 1);
            return true;
        }
        return MoveNextSlow();
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
            if (_query._anyInheritance
                && (!t.Has(_query._c1) || !t.Has(_query._c2) || !t.Has(_query._c3))) continue;
            var col1 = (Column<T1>)t.Columns[t.IndexOf(_query._c1)]!;
            var col2 = (Column<T2>)t.Columns[t.IndexOf(_query._c2)]!;
            var col3 = (Column<T3>)t.Columns[t.IndexOf(_query._c3)]!;
            _ptr1.Value = ref MemoryMarshal.GetReference(col1.AsSpan());
            _ptr2.Value = ref MemoryMarshal.GetReference(col2.AsSpan());
            _ptr3.Value = ref MemoryMarshal.GetReference(col3.AsSpan());
            _count = n;
            _rowIdx = 0;
            return true;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public RowEnumerator<T1, T2, T3> GetEnumerator() => this;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}

// ============================================================================
// TableEnumerator — yields one Iter<T...> per matched non-empty table. Inner
// loop is the user's; cost equivalent to Run (Span access, no delegate). The
// only foreach-shaped iteration we provide; per-row foreach with ref-struct
// destructuring carries unavoidable struct-copy / property-call overhead and
// loses ~10x vs Run, so it's not exposed.
//
// Usage:
//
//   foreach (var it in world.Query<Position, Velocity>())
//   {
//       var ps = it.Field1();
//       var vs = it.Field2();
//       for (int r = 0; r < it.Count; r++)
//       {
//           ps[r].X *= vs[r].Dx;
//           ps[r].Y *= vs[r].Dy;
//       }
//   }
// ============================================================================

public ref struct TableEnumerator<T1> where T1 : struct
{
    private readonly Query<T1> _query;
    private DeferScope _defer;
    private int _tableIdx;
    private Iter<T1> _current;
    private bool _disposed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TableEnumerator(Query<T1> q)
    {
        _query = q;
        _defer = q._world.Defer();
        q.Rematch();
        _tableIdx = -1;
        _current = default;
        _disposed = false;
    }

    public Iter<T1> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        var matched = _query._matched;
        while (true)
        {
            _tableIdx++;
            if (_tableIdx >= matched.Count) return false;
            var t = matched[_tableIdx];
            if (t.Count == 0) continue;
            var (col1, s1) = _query.ResolveSource<T1>(t, _query._c1);
            if (col1 == null) continue;
            _current = new Iter<T1>(_query._world, t, col1, s1);
            return true;
        }
    }

    public TableEnumerator<T1> GetEnumerator() => this;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}

public ref struct TableEnumerator<T1, T2>
    where T1 : struct where T2 : struct
{
    private readonly Query<T1, T2> _query;
    private DeferScope _defer;
    private int _tableIdx;
    private Iter<T1, T2> _current;
    public Iter<T1, T2> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current;
    }
    private bool _disposed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TableEnumerator(Query<T1, T2> q)
    {
        _query = q;
        _defer = q._world.Defer();
        q.Rematch();
        _tableIdx = -1;
        _current = default;
        _disposed = false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        var matched = _query._matched;
        while (true)
        {
            _tableIdx++;
            if (_tableIdx >= matched.Count) return false;
            var t = matched[_tableIdx];
            if (t.Count == 0) continue;
            var (col1, s1) = _query.ResolveSource<T1>(t, _query._c1);
            var (col2, s2) = _query.ResolveSource<T2>(t, _query._c2);
            if (col1 == null || col2 == null) continue;
            _current = new Iter<T1, T2>(_query._world, t, col1, s1, col2, s2);
            return true;
        }
    }

    public TableEnumerator<T1, T2> GetEnumerator() => this;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}

public ref struct TableEnumerator<T1, T2, T3>
    where T1 : struct where T2 : struct where T3 : struct
{
    private readonly Query<T1, T2, T3> _query;
    private DeferScope _defer;
    private int _tableIdx;
    private Iter<T1, T2, T3> _current;
    private bool _disposed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TableEnumerator(Query<T1, T2, T3> q)
    {
        _query = q;
        _defer = q._world.Defer();
        q.Rematch();
        _tableIdx = -1;
        _current = default;
        _disposed = false;
    }

    public Iter<T1, T2, T3> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        var matched = _query._matched;
        while (true)
        {
            _tableIdx++;
            if (_tableIdx >= matched.Count) return false;
            var t = matched[_tableIdx];
            if (t.Count == 0) continue;
            var (col1, s1) = _query.ResolveSource<T1>(t, _query._c1);
            var (col2, s2) = _query.ResolveSource<T2>(t, _query._c2);
            var (col3, s3) = _query.ResolveSource<T3>(t, _query._c3);
            if (col1 == null || col2 == null || col3 == null) continue;
            _current = new Iter<T1, T2, T3>(_query._world, t, col1, s1, col2, s2, col3, s3);
            return true;
        }
    }

    public TableEnumerator<T1, T2, T3> GetEnumerator() => this;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}

public ref struct TableEnumerator<T1, T2, T3, T4>
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct
{
    private readonly Query<T1, T2, T3, T4> _query;
    private DeferScope _defer;
    private int _tableIdx;
    private Iter<T1, T2, T3, T4> _current;
    private bool _disposed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TableEnumerator(Query<T1, T2, T3, T4> q)
    {
        _query = q;
        _defer = q._world.Defer();
        q.Rematch();
        _tableIdx = -1;
        _current = default;
        _disposed = false;
    }

    public Iter<T1, T2, T3, T4> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        var matched = _query._matched;
        while (true)
        {
            _tableIdx++;
            if (_tableIdx >= matched.Count) return false;
            var t = matched[_tableIdx];
            if (t.Count == 0) continue;
            var (col1, s1) = _query.ResolveSource<T1>(t, _query._c1);
            var (col2, s2) = _query.ResolveSource<T2>(t, _query._c2);
            var (col3, s3) = _query.ResolveSource<T3>(t, _query._c3);
            var (col4, s4) = _query.ResolveSource<T4>(t, _query._c4);
            if (col1 == null || col2 == null || col3 == null || col4 == null) continue;
            _current = new Iter<T1, T2, T3, T4>(_query._world, t,
                col1, s1, col2, s2, col3, s3, col4, s4);
            return true;
        }
    }

    public TableEnumerator<T1, T2, T3, T4> GetEnumerator() => this;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}

public ref struct TableEnumerator<T1, T2, T3, T4, T5>
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct
{
    private readonly Query<T1, T2, T3, T4, T5> _query;
    private DeferScope _defer;
    private int _tableIdx;
    private Iter<T1, T2, T3, T4, T5> _current;
    private bool _disposed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TableEnumerator(Query<T1, T2, T3, T4, T5> q)
    {
        _query = q;
        _defer = q._world.Defer();
        q.Rematch();
        _tableIdx = -1;
        _current = default;
        _disposed = false;
    }

    public Iter<T1, T2, T3, T4, T5> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        var matched = _query._matched;
        while (true)
        {
            _tableIdx++;
            if (_tableIdx >= matched.Count) return false;
            var t = matched[_tableIdx];
            if (t.Count == 0) continue;
            var (col1, s1) = _query.ResolveSource<T1>(t, _query._c1);
            var (col2, s2) = _query.ResolveSource<T2>(t, _query._c2);
            var (col3, s3) = _query.ResolveSource<T3>(t, _query._c3);
            var (col4, s4) = _query.ResolveSource<T4>(t, _query._c4);
            var (col5, s5) = _query.ResolveSource<T5>(t, _query._c5);
            if (col1 == null || col2 == null || col3 == null || col4 == null || col5 == null) continue;
            _current = new Iter<T1, T2, T3, T4, T5>(_query._world, t,
                col1, s1, col2, s2, col3, s3, col4, s4, col5, s5);
            return true;
        }
    }

    public TableEnumerator<T1, T2, T3, T4, T5> GetEnumerator() => this;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}

public ref struct TableEnumerator<T1, T2, T3, T4, T5, T6>
    where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct
{
    private readonly Query<T1, T2, T3, T4, T5, T6> _query;
    private DeferScope _defer;
    private int _tableIdx;
    private Iter<T1, T2, T3, T4, T5, T6> _current;
    private bool _disposed;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal TableEnumerator(Query<T1, T2, T3, T4, T5, T6> q)
    {
        _query = q;
        _defer = q._world.Defer();
        q.Rematch();
        _tableIdx = -1;
        _current = default;
        _disposed = false;
    }

    public Iter<T1, T2, T3, T4, T5, T6> Current
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _current;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool MoveNext()
    {
        var matched = _query._matched;
        while (true)
        {
            _tableIdx++;
            if (_tableIdx >= matched.Count) return false;
            var t = matched[_tableIdx];
            if (t.Count == 0) continue;
            var (col1, s1) = _query.ResolveSource<T1>(t, _query._c1);
            var (col2, s2) = _query.ResolveSource<T2>(t, _query._c2);
            var (col3, s3) = _query.ResolveSource<T3>(t, _query._c3);
            var (col4, s4) = _query.ResolveSource<T4>(t, _query._c4);
            var (col5, s5) = _query.ResolveSource<T5>(t, _query._c5);
            var (col6, s6) = _query.ResolveSource<T6>(t, _query._c6);
            if (col1 == null || col2 == null || col3 == null
                || col4 == null || col5 == null || col6 == null) continue;
            _current = new Iter<T1, T2, T3, T4, T5, T6>(_query._world, t,
                col1, s1, col2, s2, col3, s3, col4, s4, col5, s5, col6, s6);
            return true;
        }
    }

    public TableEnumerator<T1, T2, T3, T4, T5, T6> GetEnumerator() => this;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _query.MarkObservedInternal();
        _defer.Dispose();
    }
}
