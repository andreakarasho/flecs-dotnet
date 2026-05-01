using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

// Event marker types for tests.
public struct OnHit { }
public struct OnHeal { }
public struct OnSpawn { }

public class CustomEventTests
{
    [Fact]
    public void Observer_FiresOnEmit_TypedComponent()
    {
        var w = new World();
        int hits = 0;
        EntityId seen = default;
        w.Observer<OnHit, Position>(it => { hits++; seen = it.Entity; });
        var target = w.CreateEntity();
        w.Emit<OnHit, Position>(target);
        Assert.Equal(1, hits);
        Assert.Equal(target.Id, seen.Id);
    }

    [Fact]
    public void Observer_NoFireWhenNotEmitted()
    {
        var w = new World();
        int hits = 0;
        w.Observer<OnHit, Position>(it => hits++);
        Assert.Equal(0, hits);
    }

    [Fact]
    public void Observer_NoFireForDifferentEvent()
    {
        var w = new World();
        int hitCount = 0, healCount = 0;
        w.Observer<OnHit, Position>(it => hitCount++);
        w.Observer<OnHeal, Position>(it => healCount++);
        var target = w.CreateEntity();
        w.Emit<OnHit, Position>(target);
        Assert.Equal(1, hitCount);
        Assert.Equal(0, healCount);
    }

    [Fact]
    public void Observer_NoFireForDifferentTarget()
    {
        var w = new World();
        int hits = 0;
        w.Observer<OnHit, Position>(it => hits++);
        var target = w.CreateEntity();
        w.Emit<OnHit, Velocity>(target); // different target type
        Assert.Equal(0, hits);
    }

    [Fact]
    public void Observer_Multicast()
    {
        var w = new World();
        int a = 0, b = 0;
        w.Observer<OnHit, Position>(it => a++);
        w.Observer<OnHit, Position>(it => b++);
        w.Emit<OnHit, Position>(w.CreateEntity());
        Assert.Equal(1, a);
        Assert.Equal(1, b);
    }

    [Fact]
    public void Observer_PairEvent()
    {
        var w = new World();
        int hits = 0;
        w.Observer<OnHit, Likes, Apple>(it => hits++);
        var target = w.CreateEntity();
        w.Emit<OnHit, Likes, Apple>(target);
        Assert.Equal(1, hits);
    }

    [Fact]
    public void Emit_NoOpWhenNoSubscribers()
    {
        var w = new World();
        // No throw, no observers.
        w.Emit<OnHit, Position>(w.CreateEntity());
    }

    [Fact]
    public void Observer_YieldExisting_FiresForCurrentHolders()
    {
        var w = new World();
        var a = w.CreateEntity();
        var b = w.CreateEntity();
        w.Set(a, new Position(0, 0));
        w.Set(b, new Position(1, 1));
        var seen = new HashSet<uint>();
        w.Observer<OnHit, Position>(it => seen.Add(it.Entity.Id), yieldExisting: true);
        Assert.Equal(2, seen.Count);
    }

    [Fact]
    public void CustomEvent_DoesNotFireOnBuiltinOps()
    {
        var w = new World();
        w.Component<Position>();
        int hits = 0;
        w.Observer<OnHit, Position>(it => hits++);
        // Builtin OnAdd does NOT fire custom OnHit.
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        Assert.Equal(0, hits);
    }

    [Fact]
    public void EventType_AutoRegistersAsTagStyleEntity()
    {
        var w = new World();
        w.Observer<OnSpawn, Position>(it => { });
        // OnSpawn registered as an entity (tag-style).
        Assert.True(w.IdOf<OnSpawn>().Component != 0u);
    }
}
