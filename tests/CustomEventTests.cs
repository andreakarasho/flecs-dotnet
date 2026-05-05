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

    public struct Click { }

    [Fact]
    public void Observer_TargetLess_FiresOnEmit()
    {
        var w = new World();
        EntityId seen = default;
        w.Observer<Click>(it => seen = it.Entity);
        var widget = w.CreateEntity();
        w.Emit<Click>(widget);
        Assert.Equal(widget.Id, seen.Id);
    }

    [Fact]
    public void Observer_TargetLess_PropagatesUpChain()
    {
        var w = new World();
        var seen = new List<uint>();
        w.Observer<Click>(it => seen.Add(it.Entity.Id));
        var p = w.CreateEntity();
        var c = w.CreateEntity();
        w.SetParent(c, p);
        w.Emit<Click>(c, w.Relations.ChildOf);
        Assert.Equal(new[] { c.Id, p.Id }, seen);
    }

    [Fact]
    public void Observer_TargetLess_DoesNotFireOnTargetedEmit()
    {
        var w = new World();
        int hits = 0;
        w.Observer<Click>(it => hits++);                 // target-less
        w.Emit<Click, Position>(w.CreateEntity());       // targeted — different key
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

    // ===== Emit interaction edges =====

    [Fact]
    public void Observer_ManySubscribers_AllFire()
    {
        var w = new World();
        int total = 0;
        for (int i = 0; i < 5; i++) w.Observer<OnHit, Position>(it => total++);
        w.Emit<OnHit, Position>(w.CreateEntity());
        Assert.Equal(5, total);
    }

    [Fact]
    public void Observer_DistinctTargetsTracked()
    {
        var w = new World();
        int posHits = 0, velHits = 0;
        w.Observer<OnHit, Position>(it => posHits++);
        w.Observer<OnHit, Velocity>(it => velHits++);
        var e = w.CreateEntity();
        w.Emit<OnHit, Position>(e);
        Assert.Equal(1, posHits);
        Assert.Equal(0, velHits);
        w.Emit<OnHit, Velocity>(e);
        Assert.Equal(1, posHits);
        Assert.Equal(1, velHits);
    }

    [Fact]
    public void Observer_TargetLess_ManyEmits()
    {
        var w = new World();
        int hits = 0;
        w.Observer<Click>(it => hits++);
        var e = w.CreateEntity();
        for (int i = 0; i < 10; i++) w.Emit<Click>(e);
        Assert.Equal(10, hits);
    }

    [Fact]
    public void Observer_PairEvent_DistinctPairsDoNotCross()
    {
        var w = new World();
        int apple = 0, orange = 0;
        w.Observer<OnHit, Likes, Apple>(it => apple++);
        w.Observer<OnHit, Likes, Orange>(it => orange++);
        var e = w.CreateEntity();
        w.Emit<OnHit, Likes, Apple>(e);
        Assert.Equal(1, apple);
        Assert.Equal(0, orange);
    }

    // ===== Payload events (cpp `world.event<E>().ctx(&p).emit()` parity) =====

    public struct Hello { public int N; }
    public struct Resize { public double W, H; }

    [Fact]
    public void PayloadEmit_World_FiresPayloadObserver()
    {
        var w = new World();
        int seen = 0;
        w.Observer<Hello>((EventIter it, in Hello h) => seen = h.N);
        w.Emit(new Hello { N = 42 });
        Assert.Equal(42, seen);
    }

    [Fact]
    public void PayloadEmit_World_NoTarget_StillFires()
    {
        var w = new World();
        EntityId target = default;
        w.Observer<Hello>((EventIter it, in Hello h) => target = it.Entity);
        w.Emit(new Hello());
        Assert.False(target.IsValid);
    }

    [Fact]
    public void PayloadEmit_NoSubs_NoOp()
    {
        var w = new World();
        w.Emit(new Hello { N = 1 }); // no observers, must not throw
    }

    [Fact]
    public void PayloadEmit_TargetedFiresWorldSubs()
    {
        var w = new World();
        int n = 0;
        w.Observer<Hello>((EventIter it, in Hello h) => n += h.N);
        var e = w.CreateEntity();
        w.Emit(e, new Hello { N = 5 });
        Assert.Equal(5, n);
    }

    [Fact]
    public void EntityEmit_Tag_FiresWorldObserver()
    {
        var w = new World();
        EntityId seen = default;
        w.Observer<Click>(it => seen = it.Entity);
        var widget = w.Entity();
        widget.Emit<Click>();
        Assert.Equal(widget.Id.Id, seen.Id);
    }

    [Fact]
    public void EntityEmit_Payload_FiresWorldPayloadObserver()
    {
        var w = new World();
        Resize seen = default;
        w.Observer<Resize>((EventIter it, in Resize p) => seen = p);
        var widget = w.Entity();
        widget.Emit(new Resize { W = 100, H = 200 });
        Assert.Equal(100, seen.W);
        Assert.Equal(200, seen.H);
    }

    [Fact]
    public void EntityObserve_Tag_OnlyFiresForSelf()
    {
        var w = new World();
        var a = w.Entity();
        var b = w.Entity();
        int aHits = 0;
        a.Observe<Click>(it => aHits++);
        b.Emit<Click>();
        Assert.Equal(0, aHits);
        a.Emit<Click>();
        Assert.Equal(1, aHits);
    }

    [Fact]
    public void EntityObserve_Payload_OnlyFiresForSelf()
    {
        var w = new World();
        var a = w.Entity();
        var b = w.Entity();
        Resize seen = default;
        int aHits = 0;
        a.Observe<Resize>((EventIter it, in Resize p) => { aHits++; seen = p; });
        b.Emit(new Resize { W = 1, H = 2 });
        Assert.Equal(0, aHits);
        a.Emit(new Resize { W = 3, H = 4 });
        Assert.Equal(1, aHits);
        Assert.Equal(3, seen.W);
        Assert.Equal(4, seen.H);
    }

    [Fact]
    public void EntityObserve_Payload_WorldEmitNoTarget_DoesNotFire()
    {
        var w = new World();
        var widget = w.Entity();
        int hits = 0;
        widget.Observe<Hello>((EventIter _, in Hello _) => hits++);
        w.Emit(new Hello { N = 1 }); // no target — entity-scoped sub must not fire
        Assert.Equal(0, hits);
    }

    [Fact]
    public void DisabledHandle_StopsPayloadDispatch()
    {
        var w = new World();
        int hits = 0;
        var h = w.Observer<Hello>((EventIter it, in Hello _) => hits++);
        w.Emit(new Hello());
        h.SetEnabled(false);
        w.Emit(new Hello());
        Assert.Equal(1, hits);
    }
}
