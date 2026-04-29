using System.Collections.Generic;

namespace Flecs;

// ============================================================================
// Table — archetype storage. Signature is sorted Id[]. Columns[i] is null for
// entries without data (tags / pure pairs).
// ============================================================================
public sealed class Table
{
    internal readonly int Id;
    internal readonly Id[] ComponentIds; // sorted by Id.Value ascending
    internal readonly Column?[] Columns;
    // Parallel to Columns. Slot non-null iff that component-id is marked
    // CanToggle in the world. Bit-per-row enabled flag; default 1 (enabled)
    // on AddRow. Mirrors flecs ecs_table_t::bs_columns.
    internal readonly Bitset?[] Bits;
    // True iff any slot in Bits is non-null. Cached so query hot-path can
    // branch on a single field load instead of scanning the array. Mutable
    // because MarkCanToggle can retroactively allocate bitsets in existing
    // tables — see RefreshHasAnyBitset.
    internal bool HasAnyBitset;
    private readonly Dictionary<Id, int> _idToIndex;
    internal readonly List<EntityId> Entities = new();
    // Bumped on every structural change (AddRow / RemoveRow). Used by queries
    // for change detection. Mirrors flecs ecs_table_t::dirty_state.
    internal int Version;

    // Archetype transition edges. Lazily populated:
    //   _addEdges[id]    = table reached by adding 'id' to this signature
    //   _removeEdges[id] = table reached by removing 'id' from this signature
    // Avoids rebuilding signature + SignatureKey + _tablesBySig lookup on
    // every Add/Remove. Tables are never freed → cache permanently valid.
    // Mirrors flecs ecs_table_t::node edges.
    internal readonly Dictionary<Id, Table> _addEdges = new();
    internal readonly Dictionary<Id, Table> _removeEdges = new();

    public int Count => Entities.Count;
    public int ColumnCount => Columns.Length;

    internal Table(int id, Id[] sortedIds, Column?[] columns, Bitset?[] bits)
    {
        Id = id;
        ComponentIds = sortedIds;
        Columns = columns;
        Bits = bits;
        bool any = false;
        for (int i = 0; i < bits.Length; i++) if (bits[i] != null) { any = true; break; }
        HasAnyBitset = any;
        _idToIndex = new Dictionary<Id, int>(sortedIds.Length);
        for (int i = 0; i < sortedIds.Length; i++) _idToIndex[sortedIds[i]] = i;
    }

    public bool Has(Id componentId) => _idToIndex.ContainsKey(componentId);
    internal int IndexOf(Id componentId) => _idToIndex[componentId];

    // Re-evaluate HasAnyBitset after a retroactive bitset allocation.
    internal void RefreshHasAnyBitset()
    {
        for (int i = 0; i < Bits.Length; i++)
            if (Bits[i] != null) { HasAnyBitset = true; return; }
        HasAnyBitset = false;
    }

    internal int AddRow(EntityId e)
    {
        int row = Entities.Count;
        Entities.Add(e);
        for (int i = 0; i < Columns.Length; i++) Columns[i]?.AddDefault();
        // New rows default to enabled. Migration paths overwrite via SetBit
        // after AddRow when the source row had the bit clear.
        if (HasAnyBitset)
            for (int i = 0; i < Bits.Length; i++) Bits[i]?.Add(true);
        Version++;
        return row;
    }

    // Returns entity that was swapped into 'row' (default if none).
    internal EntityId RemoveRow(int row)
    {
        int last = Entities.Count - 1;
        EntityId moved = default;
        if (row != last)
        {
            moved = Entities[last];
            Entities[row] = moved;
        }
        Entities.RemoveAt(last);
        for (int i = 0; i < Columns.Length; i++) Columns[i]?.RemoveSwapBack(row);
        if (HasAnyBitset)
            for (int i = 0; i < Bits.Length; i++) Bits[i]?.RemoveSwapBack(row);
        Version++;
        return moved;
    }
}
