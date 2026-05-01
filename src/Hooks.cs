using System;

namespace Flecs;

// ============================================================================
// Lifecycle hooks. Per-component callback set. Mirrors ecs_type_hooks_t.
//   Ctor      — invoked after row default-init (before OnAdd)
//   Dtor      — invoked before row destroyed (after OnRemove)
//   OnAdd     — entity gained component (post-migration, post-Ctor)
//   OnRemove  — entity losing component (pre-migration, pre-Dtor)
//   OnSet     — value written via Set / SetCmd flush
//   Copy      — src→dst archetype migration (default = field copy)
//   Move      — same, ownership-transfer semantics
// ============================================================================
public delegate void TypeHookAction<T>(World world, EntityId entity, ref T value) where T : struct;
public delegate void TypeHookCopy<T>(World world, EntityId entity, ref T src, ref T dst) where T : struct;

public sealed class TypeHooks<T> where T : struct
{
    // Read-only to user; mutated via fluent Set* methods or internal multicast
    // (`h.OnAdd += x`) from World observer registration paths.
    public TypeHookAction<T>? Ctor { get; internal set; }
    public TypeHookAction<T>? Dtor { get; internal set; }
    public TypeHookAction<T>? OnAdd { get; internal set; }
    public TypeHookAction<T>? OnRemove { get; internal set; }
    public TypeHookAction<T>? OnSet { get; internal set; }
    public TypeHookCopy<T>? Copy { get; internal set; }
    public TypeHookCopy<T>? Move { get; internal set; }

    // Fluent setters — replace style. For multicast, register multiple
    // observers via World.Observe<T> instead of stacking via this entry.
    public TypeHooks<T> SetCtor(TypeHookAction<T>? cb) { Ctor = cb; return this; }
    public TypeHooks<T> SetDtor(TypeHookAction<T>? cb) { Dtor = cb; return this; }
    public TypeHooks<T> SetOnAdd(TypeHookAction<T>? cb) { OnAdd = cb; return this; }
    public TypeHooks<T> SetOnRemove(TypeHookAction<T>? cb) { OnRemove = cb; return this; }
    public TypeHooks<T> SetOnSet(TypeHookAction<T>? cb) { OnSet = cb; return this; }
    public TypeHooks<T> SetCopy(TypeHookCopy<T>? cb) { Copy = cb; return this; }
    public TypeHooks<T> SetMove(TypeHookCopy<T>? cb) { Move = cb; return this; }
}

// ============================================================================
// Observer events. OnSet meaningful only for data components.
// ============================================================================
public enum Event { OnAdd, OnRemove, OnSet }

// Per-dispatch context for an observer. Mirrors the system-side `Iter`
// shape: world + handle + entity + event tag. Carries the ObserverHandle
// so user code can read its Ctx via `it.Ctx<T>()`. Stack-only ref struct;
// constructed by dispatch wrappers and handed to user delegates.
public readonly ref struct EventIter
{
    public World World { get; }
    public ObserverHandle Observer { get; }
    public EntityId Entity { get; }
    public Event Event { get; }

    internal EventIter(World world, ObserverHandle observer, EntityId entity, Event evt)
    {
        World = world;
        Observer = observer;
        Entity = entity;
        Event = evt;
    }

    public T Ctx<T>()
    {
        if (Observer.Ctx is T t) return t;
        ThrowHelper.SystemCtxWrongType(typeof(T));
        return default!;
    }
}

// Observer body delegates. Mirror flecs.NET observer callback shape.
public delegate void EventAction(EventIter it);
public delegate void EventAction<T1>(EventIter it, ref T1 c1) where T1 : struct;
public delegate void EventAction<T1, T2>(EventIter it, ref T1 c1, ref T2 c2)
    where T1 : struct where T2 : struct;

// Observer registration handle. Counterpart to SystemHandle: carries a Ctx
// slot readable inside the body via EventIter.Ctx<T>(). Returned by every
// World.Observer overload; configure via the fluent Set* methods.
public sealed class ObserverHandle
{
    // Event filter the observer listens on. For custom-event observers
    // (Observer<TEvent, ...>) this slot stays at OnAdd as a placeholder; the
    // actual event is the TEvent type, dispatched via Emit.
    public Event Event { get; }
    public bool Enabled { get; private set; } = true;
    public object? Ctx { get; private set; }

    internal ObserverHandle(Event evt) { Event = evt; }

    public ObserverHandle SetCtx(object? ctx) { Ctx = ctx; return this; }
    public ObserverHandle SetEnabled(bool v) { Enabled = v; return this; }
}

// ============================================================================
// Delete policy — fate of holders when a referenced id is deleted.
//   Remove — drop the id from holders (default for components, OnDelete).
//   Delete — cascade-delete holders (default for ChildOf OnDeleteTarget).
//   Panic  — throw on attempted delete (used to enforce invariants).
// Mirrors flecs ECS_REMOVE / ECS_DELETE / ECS_PANIC.
// ============================================================================
public enum DeletePolicy { Remove, Delete, Panic }

// Per-Id structural-event subscribers. For tags / pure pairs (no data) and for
// component observers that don't need the value reference. Multicast.
internal sealed class IdHooks
{
    public Action<World, EntityId>? OnAdd;
    public Action<World, EntityId>? OnRemove;
    public Action<World, EntityId>? OnSet;
}

// Multi-term observer carrier. Fires when an event hits any of its term ids
// AND the entity satisfies all other terms (via Has — Self+Up). Mirrors
// flecs filter-style observers for the "react when shape forms" pattern.
internal sealed class MultiObserver
{
    public readonly Id[] Ids;
    public readonly Action<World, EntityId> Dispatch;
    public MultiObserver(Id[] ids, Action<World, EntityId> dispatch)
    { Ids = ids; Dispatch = dispatch; }
}
