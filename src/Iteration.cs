using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Flecs;

// ============================================================================
// Ptr<T> + RowEnumeratorUtil — query iteration helpers shared by all arities.
//
// All arity-specific types (RowEnumerator<T1..TN> + FilterState<T1..TN> +
// Query<T1..TN>) are auto-generated in Query.Arity.cs. Edit the template in
// gen_arity.py and regenerate.
//
//   foreach (var (pos, vel) in world.Query<Position, Velocity>())
//       pos.Value.X *= vel.Value.Dx;
//
// Two internal modes selected at RowEnumerator ctor:
//   • Fast path  — own-only, no Optional, no CanToggle/Sparse/Union. Constant
//                  +1 pointer-stride. RowEnumerator stays slim so per-iter
//                  Current copy doesn't dominate.
//   • Filter path — any of (Inherited, Optional, CanToggle, Sparse, Union).
//                  Per-table state lives on a heap-pooled FilterState<...>
//                  ([ThreadStatic] stack). AdvanceFiltered skips bitset-
//                  disabled rows, sparse-missing rows, union-mismatched rows,
//                  and yields Unsafe.NullRef for absent Optional slots.
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

