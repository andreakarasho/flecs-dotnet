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

    internal Table(int id, Id[] sortedIds, Column?[] columns)
    {
        Id = id;
        ComponentIds = sortedIds;
        Columns = columns;
        _idToIndex = new Dictionary<Id, int>(sortedIds.Length);
        for (int i = 0; i < sortedIds.Length; i++) _idToIndex[sortedIds[i]] = i;
    }

    public bool Has(Id componentId) => _idToIndex.ContainsKey(componentId);
    internal int IndexOf(Id componentId) => _idToIndex[componentId];

    internal int AddRow(EntityId e)
    {
        int row = Entities.Count;
        Entities.Add(e);
        for (int i = 0; i < Columns.Length; i++) Columns[i]?.AddDefault();
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
        Version++;
        return moved;
    }
}
