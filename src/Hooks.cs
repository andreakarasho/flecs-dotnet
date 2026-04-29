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
    public TypeHookAction<T>? Ctor;
    public TypeHookAction<T>? Dtor;
    public TypeHookAction<T>? OnAdd;
    public TypeHookAction<T>? OnRemove;
    public TypeHookAction<T>? OnSet;
    public TypeHookCopy<T>? Copy;
    public TypeHookCopy<T>? Move;
}

// ============================================================================
// Observer events. OnSet meaningful only for data components.
// ============================================================================
public enum Event { OnAdd, OnRemove, OnSet }

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

// Multi-term observer. Fires when an event hits any of its term ids AND the
// entity satisfies all other terms (via Has — Self+Up). Mirrors flecs
// filter-style observers for the common "react when shape forms" pattern.
public delegate void MultiObserverAction<T1, T2>(World world, EntityId entity, ref T1 c1, ref T2 c2)
    where T1 : struct where T2 : struct;

internal sealed class MultiObserver
{
    public readonly Id[] Ids;
    public readonly Action<World, EntityId> Dispatch;
    public MultiObserver(Id[] ids, Action<World, EntityId> dispatch)
    { Ids = ids; Dispatch = dispatch; }
}
