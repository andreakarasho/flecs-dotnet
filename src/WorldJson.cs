using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace Flecs;

// ============================================================================
// JSON snapshot — opt-in per component via RegisterJson<T>(JsonTypeInfo<T>).
// Source-gen JsonSerializerContext required; no runtime reflection (project
// rule). Library walks alive entities, dumps registered components keyed by
// component-entity name. Pairs / sparse / inherited refs deferred to v2.
//
// Format:
//   {
//     "entities": [
//       { "id": 42, "Position": {"X":1,"Y":2}, "Velocity": {"Dx":0,"Dy":0} },
//       ...
//     ]
//   }
//
// Round-trip semantics:
//   • Serialize: snapshot of every alive entity + its own dense data
//     components that have a JsonTypeInfo registered. Tags / unregistered
//     components silently skipped. Component name = world.NameOf(component).
//   • Deserialize: creates fresh entities (new IDs); returns Dictionary
//     mapping serialized-id → new EntityId so callers can fix any external
//     references. Pair components reference other entities by id — caller
//     must rewrite via the map (NYI in v2).
// ============================================================================

public delegate void JsonComponentWrite(World world, EntityId entity, Utf8JsonWriter writer);
public delegate void JsonComponentRead(World world, EntityId entity, ref Utf8JsonReader reader);

public sealed partial class World
{
    // Component-name → typed write/read pair. Indexed by name (the
    // component-entity Name) so deserializer can dispatch from JSON property.
    private readonly Dictionary<string, (Id Id, JsonComponentWrite Write, JsonComponentRead Read)>
        _jsonByName = new();
    // Component-id → write/read for serialize-side fast lookup during entity walk.
    private readonly Dictionary<Id, (string Name, JsonComponentWrite Write, JsonComponentRead Read)>
        _jsonById = new();

    // Register a typed JSON serializer for component T. JsonTypeInfo comes
    // from the caller's source-generated JsonSerializerContext. T must be a
    // value component (Component<T>() registered).
    public void RegisterJson<T>(JsonTypeInfo<T> typeInfo) where T : struct
    {
        lock (_lock)
        {
            var compEnt = GetOrRegisterComponentLocked<T>();
            var compId = (Id)compEnt;
            if (!_componentInfo.ContainsKey(compId)) ThrowHelper.IsTagNotComponent(typeof(T));
            string name = GetName(compEnt) ?? typeof(T).Name;
            JsonComponentWrite write = (w, e, writer) =>
            {
                ref var v = ref w.Get<T>(e);
                JsonSerializer.Serialize(writer, v, typeInfo);
            };
            JsonComponentRead read = (World w, EntityId e, ref Utf8JsonReader r) =>
            {
                var v = JsonSerializer.Deserialize(ref r, typeInfo);
                w.Set(e, v);
            };
            _jsonByName[name] = (compId, write, read);
            _jsonById[compId] = (name, write, read);
        }
    }

    // Snapshot all alive entities. Components without RegisterJson<T> are
    // skipped silently. Caller owns the writer / stream.
    public void SerializeJson(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteStartArray("entities");
        // Snapshot the alive set under the lock to avoid mid-dump mutation.
        List<EntityId> alive;
        lock (_lock)
        {
            alive = new List<EntityId>(_aliveCount);
            for (uint id = 1; id < _nextId; id++)
            {
                ref var slot = ref GetSlot(id);
                if (slot.Alive) alive.Add(new EntityId(id, slot.Generation));
            }
        }
        foreach (var e in alive)
        {
            ref var rec = ref GetSlot(e.Id);
            if (!rec.Alive) continue;
            var table = _tablesById[rec.TableId]!;
            // Skip entities that hold no JSON-registered component (mostly the
            // builtin reserved entities — Wildcard, ChildOf, IsA, phases, etc).
            bool any = false;
            for (int i = 0; i < table.ComponentIds.Length; i++)
                if (_jsonById.ContainsKey(table.ComponentIds[i])) { any = true; break; }
            if (!any) continue;
            writer.WriteStartObject();
            writer.WriteNumber("id", e.Id);
            for (int i = 0; i < table.ComponentIds.Length; i++)
            {
                var cid = table.ComponentIds[i];
                if (!_jsonById.TryGetValue(cid, out var entry)) continue;
                writer.WritePropertyName(entry.Name);
                entry.Write(this, e, writer);
            }
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    // Convenience: dump to a UTF-8 stream with default writer options.
    public void SerializeJson(Stream stream, JsonWriterOptions options = default)
    {
        using var writer = new Utf8JsonWriter(stream, options);
        SerializeJson(writer);
    }

    // Read a JSON snapshot. Creates fresh entities (new IDs). Returns map
    // serialized-id → new-EntityId; caller can rewrite external references.
    // Components keyed by an unknown name are skipped (caller should ensure
    // RegisterJson<T> matches the writer's component set).
    public Dictionary<uint, EntityId> DeserializeJson(ref Utf8JsonReader reader)
    {
        var idMap = new Dictionary<uint, EntityId>();
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            ThrowHelper.JsonExpected("StartObject");
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName)
                ThrowHelper.JsonExpected("PropertyName");
            var prop = reader.GetString()!;
            if (prop != "entities") { reader.Skip(); continue; }
            if (!reader.Read() || reader.TokenType != JsonTokenType.StartArray)
                ThrowHelper.JsonExpected("StartArray entities");
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.StartObject)
                    ThrowHelper.JsonExpected("StartObject entity");
                ReadEntity(ref reader, idMap);
            }
        }
        return idMap;
    }

    public Dictionary<uint, EntityId> DeserializeJson(Stream stream)
    {
        var buf = new byte[stream.Length - stream.Position];
        int read = stream.Read(buf, 0, buf.Length);
        var reader = new Utf8JsonReader(buf.AsSpan(0, read));
        return DeserializeJson(ref reader);
    }

    private void ReadEntity(ref Utf8JsonReader reader, Dictionary<uint, EntityId> idMap)
    {
        uint serializedId = 0;
        EntityId? created = null;
        while (reader.Read())
        {
            if (reader.TokenType == JsonTokenType.EndObject) return;
            if (reader.TokenType != JsonTokenType.PropertyName)
                ThrowHelper.JsonExpected("PropertyName entity-prop");
            var prop = reader.GetString()!;
            if (prop == "id")
            {
                reader.Read();
                serializedId = reader.GetUInt32();
                created = CreateEntity();
                idMap[serializedId] = created.Value;
                continue;
            }
            if (created == null)
            {
                created = CreateEntity();
                idMap[serializedId] = created.Value;
            }
            if (!_jsonByName.TryGetValue(prop, out var entry)) { reader.Read(); reader.Skip(); continue; }
            reader.Read();
            entry.Read(this, created.Value, ref reader);
        }
    }
}
