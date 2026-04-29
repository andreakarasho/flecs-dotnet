using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Flecs;

// ============================================================================
// Bitset — packed 1-bit-per-row enabled flag column. Used for non-fragmenting
// component toggle (CanToggle trait). Parallel to Column<T>: shares row index
// with the data column. Mirrors flecs ecs_bitset_t.
// ============================================================================
internal sealed class Bitset
{
    private ulong[] _bits = Array.Empty<ulong>();
    private int _count;

    public int Count => _count;

    public void Add(bool value)
    {
        EnsureCapacity(_count + 1);
        if (value) _bits[_count >> 6] |= 1UL << (_count & 63);
        else _bits[_count >> 6] &= ~(1UL << (_count & 63));
        _count++;
    }

    public void RemoveSwapBack(int index)
    {
        int last = _count - 1;
        if (index != last) Set(index, Get(last));
        Set(last, false);
        _count--;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool Get(int index) => ((_bits[index >> 6] >> (index & 63)) & 1UL) != 0UL;

    public void Set(int index, bool value)
    {
        if (value) _bits[index >> 6] |= 1UL << (index & 63);
        else _bits[index >> 6] &= ~(1UL << (index & 63));
    }

    private void EnsureCapacity(int n)
    {
        int need = (n + 63) >> 6;
        if (need > _bits.Length)
        {
            int newCap = _bits.Length == 0 ? 1 : _bits.Length * 2;
            while (newCap < need) newCap *= 2;
            Array.Resize(ref _bits, newCap);
        }
    }
}

// ============================================================================
// ComponentInfo — per-world metadata for a data-bearing component. Tags and
// pairs (without data) have no entry.
// ============================================================================
internal sealed class ComponentInfo
{
    public readonly Type ElementType;
    public readonly int Size;
    public Func<Column> Factory = null!; // assigned after construction (closes over self)
    public readonly string Name;
    // Type-erased TypeHooks<T> for the component's element type. Null if no
    // hooks ever set. Mirrors ecs_type_hooks_t.
    public object? Hooks;

    public ComponentInfo(Type type, int size, string name)
    {
        ElementType = type; Size = size; Name = name;
    }
}

// ============================================================================
// Column — type-erased base + typed implementation. Mirrors ecs_column_t.
// ============================================================================
internal abstract class Column
{
    public abstract int Count { get; }
    public abstract void AddDefault();
    public abstract void RemoveSwapBack(int index);
    // Move semantics — invoked during archetype migration. Hook order: user
    // Move callback if defined, else plain field copy.
    public abstract void MoveTo(World w, EntityId e, int srcIndex, Column dst, int dstIndex);
    // Copy semantics — invoked when both src and dst remain live (Clone).
    // User Copy callback if defined, else plain field copy.
    public abstract void CopyTo(World w, EntityId e, int srcIndex, Column dst, int dstIndex);

    // Type-erased hook dispatch. Default no-op; Column<T> overrides and reads
    // TypeHooks<T> from its ComponentInfo. Caller pays only a vcall + null
    // check when no hooks set.
    public virtual void InvokeCtor(World w, EntityId e, int row) { }
    public virtual void InvokeDtor(World w, EntityId e, int row) { }
    public virtual void InvokeOnAdd(World w, EntityId e, int row) { }
    public virtual void InvokeOnRemove(World w, EntityId e, int row) { }
    public virtual void InvokeOnSet(World w, EntityId e, int row) { }
}

internal sealed class Column<T> : Column where T : struct
{
    private readonly ComponentInfo _info;
    private T[] _data = Array.Empty<T>();
    private int _count;

    public Column(ComponentInfo info) { _info = info; }

    public override int Count => _count;
    public ref T GetRef(int index) => ref _data[index];
    public void Set(int index, T value) => _data[index] = value;
    // Span over the live portion of the column. For Run/Iter bulk access.
    public Span<T> AsSpan() => _data.AsSpan(0, _count);

    public override void AddDefault()
    {
        if (_count == _data.Length)
        {
            int newCap = _data.Length == 0 ? 8 : _data.Length * 2;
            Array.Resize(ref _data, newCap);
        }
        _data[_count++] = default;
    }

    public override void RemoveSwapBack(int index)
    {
        int last = _count - 1;
        if (index != last) _data[index] = _data[last];
        _data[last] = default; // release refs
        _count--;
    }

    public override void MoveTo(World w, EntityId e, int srcIndex, Column dst, int dstIndex)
    {
        var dstCol = (Column<T>)dst;
        var h = (TypeHooks<T>?)_info.Hooks;
        if (h?.Move != null)
            h.Move(w, e, ref _data[srcIndex], ref dstCol._data[dstIndex]);
        else
            dstCol._data[dstIndex] = _data[srcIndex];
    }

    public override void CopyTo(World w, EntityId e, int srcIndex, Column dst, int dstIndex)
    {
        var dstCol = (Column<T>)dst;
        var h = (TypeHooks<T>?)_info.Hooks;
        if (h?.Copy != null)
            h.Copy(w, e, ref _data[srcIndex], ref dstCol._data[dstIndex]);
        else
            dstCol._data[dstIndex] = _data[srcIndex];
    }

    public override void InvokeCtor(World w, EntityId e, int row)
    {
        var h = (TypeHooks<T>?)_info.Hooks;
        if (h?.Ctor != null) h.Ctor(w, e, ref _data[row]);
    }
    public override void InvokeDtor(World w, EntityId e, int row)
    {
        var h = (TypeHooks<T>?)_info.Hooks;
        if (h?.Dtor != null) h.Dtor(w, e, ref _data[row]);
    }
    public override void InvokeOnAdd(World w, EntityId e, int row)
    {
        var h = (TypeHooks<T>?)_info.Hooks;
        if (h?.OnAdd != null) h.OnAdd(w, e, ref _data[row]);
    }
    public override void InvokeOnRemove(World w, EntityId e, int row)
    {
        var h = (TypeHooks<T>?)_info.Hooks;
        if (h?.OnRemove != null) h.OnRemove(w, e, ref _data[row]);
    }
    public override void InvokeOnSet(World w, EntityId e, int row)
    {
        var h = (TypeHooks<T>?)_info.Hooks;
        if (h?.OnSet != null) h.OnSet(w, e, ref _data[row]);
    }
}

// ============================================================================
// SignatureKey — value-equality key over sorted Id[].
// ============================================================================
internal readonly struct SignatureKey : IEquatable<SignatureKey>
{
    public readonly Id[] Ids;
    private readonly int _hash;

    public SignatureKey(Id[] ids)
    {
        Ids = ids;
        int h = 17;
        for (int i = 0; i < ids.Length; i++) h = h * 31 + ids[i].GetHashCode();
        _hash = h;
    }

    public bool Equals(SignatureKey other)
    {
        if (Ids.Length != other.Ids.Length) return false;
        for (int i = 0; i < Ids.Length; i++)
            if (Ids[i] != other.Ids[i]) return false;
        return true;
    }

    public override bool Equals(object? obj) => obj is SignatureKey s && Equals(s);
    public override int GetHashCode() => _hash;
}

internal struct EntityRecord
{
    public ushort Generation;
    public bool Alive;
    public int TableId;
    public int Row;
}
