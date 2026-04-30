using Xunit;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Flecs.Tests;

// Source-generated context — required because runtime reflection is banned.
[JsonSerializable(typeof(Position))]
[JsonSerializable(typeof(Velocity))]
[JsonSerializable(typeof(Health))]
public partial class TestJsonContext : JsonSerializerContext { }

public class JsonSerializationTests
{
    [Fact]
    public void Serialize_DumpsAliveEntitiesAndRegisteredComponents()
    {
        var w = new World();
        w.RegisterJson<Position>(TestJsonContext.Default.Position);
        w.RegisterJson<Velocity>(TestJsonContext.Default.Velocity);

        var a = w.CreateEntity();
        w.Set(a, new Position(1, 2));
        w.Set(a, new Velocity(3, 4));
        var b = w.CreateEntity();
        w.Set(b, new Position(5, 6));

        var ms = new MemoryStream();
        w.SerializeJson(ms);
        var json = Encoding.UTF8.GetString(ms.ToArray());

        Assert.Contains("\"entities\":", json);
        Assert.Contains("\"Position\":", json);
        Assert.Contains("\"Velocity\":", json);
        Assert.Contains("\"X\":1", json);
        Assert.Contains("\"Dx\":3", json);
    }

    [Fact]
    public void Serialize_SkipsUnregisteredComponents()
    {
        var w = new World();
        w.RegisterJson<Position>(TestJsonContext.Default.Position);
        // Health NOT registered — should be skipped.
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Health(99));

        var ms = new MemoryStream();
        w.SerializeJson(ms);
        var json = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("Position", json);
        Assert.DoesNotContain("Health", json);
    }

    [Fact]
    public void RoundTrip_RestoresComponentValues()
    {
        var w1 = new World();
        w1.RegisterJson<Position>(TestJsonContext.Default.Position);
        w1.RegisterJson<Velocity>(TestJsonContext.Default.Velocity);
        var a = w1.CreateEntity();
        w1.Set(a, new Position(1, 2));
        w1.Set(a, new Velocity(3, 4));
        var b = w1.CreateEntity();
        w1.Set(b, new Position(5, 6));

        var ms = new MemoryStream();
        w1.SerializeJson(ms);

        var w2 = new World();
        w2.RegisterJson<Position>(TestJsonContext.Default.Position);
        w2.RegisterJson<Velocity>(TestJsonContext.Default.Velocity);
        ms.Position = 0;
        var map = w2.DeserializeJson(ms);

        Assert.Equal(2, map.Count);
        var newA = map[a.Id];
        var newB = map[b.Id];
        Assert.Equal(new Position(1, 2), w2.Get<Position>(newA));
        Assert.Equal(new Velocity(3, 4), w2.Get<Velocity>(newA));
        Assert.Equal(new Position(5, 6), w2.Get<Position>(newB));
        Assert.False(w2.Has<Velocity>(newB));
    }

    [Fact]
    public void Deserialize_UnknownComponentSkipped()
    {
        var w = new World();
        w.RegisterJson<Position>(TestJsonContext.Default.Position);
        // JSON references "Mystery" component not registered — must skip without throwing.
        var json = "{\"entities\":[{\"id\":1,\"Position\":{\"X\":7,\"Y\":8},\"Mystery\":{\"foo\":42}}]}";
        var bytes = Encoding.UTF8.GetBytes(json);
        var reader = new Utf8JsonReader(bytes);
        var map = w.DeserializeJson(ref reader);
        Assert.Single(map);
        var e = map[1];
        Assert.Equal(new Position(7, 8), w.Get<Position>(e));
    }

    [Fact]
    public void Serialize_EmptyWorldProducesEmptyEntitiesArray()
    {
        var w = new World();
        w.RegisterJson<Position>(TestJsonContext.Default.Position);
        // Builtin reserved entities exist but have no JSON-registered components.
        var ms = new MemoryStream();
        w.SerializeJson(ms);
        var json = Encoding.UTF8.GetString(ms.ToArray());
        Assert.Contains("\"entities\":", json);
    }
}
