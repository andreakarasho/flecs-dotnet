using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace Flecs;

// ============================================================================
// World — ECS container. Unified id space: components, tags, relations,
// entities all share EntityId. Pairs encoded as Id (bit 63).
//
// Split across partials by concern:
//   World.cs           — fields, ctor, entity lifecycle, archetype migration
//   World.Components.cs — component/tag/pair/has/add/remove/set/get + hooks
//                        + observers + defer + query factories
//   World.Features.cs   — hierarchy, naming, bulk, singleton, system, module
// ============================================================================
public sealed partial class World
{
    private const int PageSize = 1024;

    private EntityRecord[]?[] _pages;
    private uint _nextId = 1;
    private readonly Stack<uint> _recycled = new();
    private int _aliveCount;

    private readonly Dictionary<SignatureKey, Table> _tablesBySig = new();
    internal readonly List<Table?> _tablesById = new() { null }; // index 0 unused
    private readonly Table _rootTable;

    // Type → EntityId (covers components AND tags). Components have an entry
    // in _componentInfo additionally; tags do not.
    private readonly Dictionary<Type, EntityId> _typeToEntity = new();
    private readonly Dictionary<Id, ComponentInfo> _componentInfo = new();
    // Per-Id observer table. Holds tag / pair / value-less component
    // structural-event subscribers. Components that need ref to the value use
    // ComponentInfo.Hooks (TypeHooks<T>) instead.
    private readonly Dictionary<Id, IdHooks> _idHooks = new();

    // Deferred-mode command queue. Mirrors flecs defer_begin/end. Two lists
    // swapped on flush — no per-flush array alloc. Commands pooled per-type
    // (ThreadStatic) — zero alloc after warmup.
    private int _deferDepth;
    private List<Command> _commands = new();
    private List<Command> _flushing = new();

    private readonly object _lock = new();

    // Reserved entities. User entities start after these.
    // Wildcard — matches any relation/target in pair queries (EcsWildcard).
    // ChildOf  — builtin parent/child relation (EcsChildOf).
    // IsA      — builtin prefab/inheritance marker (EcsIsA). Inheritance
    //            semantics not yet implemented; the entity exists for tests
    //            and downstream wiring.
    public readonly EntityId Wildcard;
    public readonly EntityId ChildOf;
    public readonly EntityId IsA;
    // Disabled — reserved tag. Add via Disable(); queries do NOT auto-exclude
    // (matches user-controlled flecs default). Use .Without(world.Disabled) on
    // queries to skip disabled entities.
    public readonly EntityId Disabled;
    // Component-trait tags. Final blocks IsA inheritance from target.
    // Exclusive enforces single (rel, *) per entity. Mirror EcsFinal /
    // EcsExclusive.
    public readonly EntityId Final;
    public readonly EntityId Exclusive;
    // Acyclic    — relation cannot form cycles (enforced on Add).
    // Reflexive  — relation true for self (semantic flag; used by helpers).
    // Symmetric  — (R, B) on A also implies (R, A) on B. Auto-mirrored.
    // Transitive — (R, B) on A and (R, C) on B → (R, C) on A. Query-only.
    public readonly EntityId Acyclic;
    public readonly EntityId Reflexive;
    public readonly EntityId Symmetric;
    public readonly EntityId Transitive;
    // Inheritable — default. Component propagates via IsA.
    // DontInherit — opt out. IsA chain walk stops at direct presence.
    // Traversable — relation eligible for query traversal sources (Up, Cascade,
    //               Parent). Passive metadata for now (term sources NYI).
    public readonly EntityId Inheritable;
    public readonly EntityId DontInherit;
    public readonly EntityId Traversable;
    // Builtin phases. Progress() runs systems in this order. Mirror flecs
    // builtin pipeline phase ordering.
    public readonly EntityId OnLoad, PostLoad, PreUpdate, OnUpdate,
                              OnValidate, PostUpdate, PreStore, OnStore;

    // Phase-ordered list — used by Progress to dispatch systems.
    private readonly EntityId[] _phaseOrder;
    private readonly List<SystemHandle> _systems = new();

    // World-level table-creation event. Fires after a new archetype table is
    // materialized. Not fired retroactively for tables that existed before
    // subscription. No table-delete event yet (tables never freed).
    public event Action<World, Table>? OnTableCreate;

    // Imported module set. Import<TModule> idempotent.
    private readonly HashSet<Type> _imported = new();

    // Current scope — entities created while this is non-default get a
    // (ChildOf, scope) pair automatically. Set via WithScope. Used by Import
    // to namespace module entities.
    private EntityId _currentScope;
    public EntityId CurrentScope { get { lock (_lock) { return _currentScope; } } }

    // Custom-event observer table. Keyed by (event-entity-id, target-id)
    // pair. Multicast — many subscribers per (evt, target) cell.
    // User-driven: Emit() fires; no automatic dispatch.
    private readonly Dictionary<(uint evtId, Id target), Action<World, EntityId>?> _customObs = new();

    // Multi-term builtin-event observers. Trigger-keyed: lookup is by
    // (event, triggered-id). Each observer checks its remaining terms before
    // dispatch. An observer with N terms is registered N times — once per
    // term — so any of its term ids firing the event reaches the same
    // observer instance.
    private readonly Dictionary<(Event evt, Id id), List<MultiObserver>> _multiObsByTrigger = new();

    // Per-(TEvent, T...) resolved (evtId, targetId) cache. Avoids dict lookups
    // + register calls on every Emit. Entries stable once written: registration
    // creates entities exactly once, ids never change.
    private readonly Dictionary<(Type, Type), (uint evtId, Id tgt)> _emitKeyCache1 = new();
    private readonly Dictionary<(Type, Type, Type), (uint evtId, Id tgt)> _emitKeyCache2 = new();

    // Pooled BFS buffers for EmitInternal propagation. Reused across calls;
    // reentrancy guard falls back to fresh allocs if a callback re-emits.
    private List<EntityId>? _emitChainBuf;
    private HashSet<uint>? _emitVisitedBuf;
    private Queue<EntityId>? _emitQueueBuf;
    private bool _emitBufBusy;

    // Delete-policy tables. Keyed by Id (component/tag/relation entity).
    //   _onDelete[X]       — fate of holders when X itself is deleted
    //                        (X used as component, tag, or as relation in pairs)
    //   _onDeleteTarget[R] — fate of holders of (R, T) when target T is deleted
    // Default policy when key absent: Remove.
    // ChildOf is preconfigured with OnDeleteTarget = Delete in the ctor —
    // killing a parent cascades-deletes its children.
    private readonly Dictionary<Id, DeletePolicy> _onDelete = new();
    private readonly Dictionary<Id, DeletePolicy> _onDeleteTarget = new();

    // Trait fast-lookup. Synced with reserved Final/Exclusive tags but kept as
    // hash sets for hot-path checks during Add.
    private readonly HashSet<uint> _finalIds = new();
    private readonly HashSet<uint> _exclusiveRelIds = new();
    private readonly HashSet<uint> _acyclicRelIds = new();
    private readonly HashSet<uint> _reflexiveRelIds = new();
    private readonly HashSet<uint> _symmetricRelIds = new();
    private readonly HashSet<uint> _transitiveRelIds = new();
    // Component/relation ids that should NOT propagate via IsA chain.
    // Default = absent (inheritable). Add to opt out.
    private readonly HashSet<uint> _dontInheritIds = new();
    private readonly HashSet<uint> _traversableRelIds = new();

    public World()
    {
        _pages = new EntityRecord[1][];
        _pages[0] = new EntityRecord[PageSize];
        _rootTable = CreateTable(Array.Empty<Id>());
        Wildcard = CreateEntityCore();
        ChildOf = CreateEntityCore();
        IsA = CreateEntityCore();
        Disabled = CreateEntityCore();
        Final = CreateEntityCore();
        Exclusive = CreateEntityCore();
        Acyclic = CreateEntityCore();
        Reflexive = CreateEntityCore();
        Symmetric = CreateEntityCore();
        Transitive = CreateEntityCore();
        Inheritable = CreateEntityCore();
        DontInherit = CreateEntityCore();
        Traversable = CreateEntityCore();
        OnLoad = CreateEntityCore();
        PostLoad = CreateEntityCore();
        PreUpdate = CreateEntityCore();
        OnUpdate = CreateEntityCore();
        OnValidate = CreateEntityCore();
        PostUpdate = CreateEntityCore();
        PreStore = CreateEntityCore();
        OnStore = CreateEntityCore();
        _phaseOrder = new[]
        {
            OnLoad, PostLoad, PreUpdate, OnUpdate,
            OnValidate, PostUpdate, PreStore, OnStore,
        };
        // Builtin: ChildOf cascades on parent delete.
        _onDeleteTarget[(Id)ChildOf] = DeletePolicy.Delete;
        // Builtin: ChildOf is exclusive — entity has at most one parent.
        _exclusiveRelIds.Add(ChildOf.Id);
        // Builtin: ChildOf is acyclic — parent loops forbidden.
        _acyclicRelIds.Add(ChildOf.Id);
        // Builtin: ChildOf and IsA both Traversable — eligible for query
        // traversal sources (when implemented).
        _traversableRelIds.Add(ChildOf.Id);
        _traversableRelIds.Add(IsA.Id);
    }

    public int AliveCount => _aliveCount;
    public int TableCount => _tablesById.Count - 1;
    public int ComponentCount { get { lock (_lock) { return _componentInfo.Count; } } }
    public bool IsDeferred { get { lock (_lock) { return _deferDepth > 0; } } }

    // ========== Entity lifecycle ==========

    public EntityId CreateEntity()
    {
        lock (_lock)
        {
            var e = CreateEntityCore();
            ApplyScopeLocked(e);
            return e;
        }
    }

    // Auto-parent newly-created entity to current scope, if any.
    private void ApplyScopeLocked(EntityId e)
    {
        if (!_currentScope.IsValid) return;
        // Don't scope the scope itself or the reserved entities (those are
        // created during ctor when _currentScope is default).
        if (e.Id == _currentScope.Id) return;
        EnsureHasIdLocked(e, Id.MakePair(ChildOf, _currentScope));
    }

    private EntityId CreateEntityCore()
    {
        uint id;
        ushort gen;
        if (_recycled.Count > 0)
        {
            id = _recycled.Pop();
            ref var slot = ref GetSlot(id);
            gen = slot.Generation;
            slot.Alive = true;
            slot.TableId = _rootTable.Id;
            slot.Row = _rootTable.AddRow(new EntityId(id, gen));
        }
        else
        {
            id = _nextId++;
            ref var slot = ref GetSlot(id);
            gen = 1;
            slot.Generation = gen;
            slot.Alive = true;
            slot.TableId = _rootTable.Id;
            slot.Row = _rootTable.AddRow(new EntityId(id, gen));
        }
        _aliveCount++;
        return new EntityId(id, gen);
    }

    public void Delete(EntityId entity)
    {
        if (entity.Id == 0) return;
        lock (_lock)
        {
            if (_deferDepth > 0)
            {
                _commands.Add(DeleteCmd.Rent(entity));
                return;
            }
            if (!IsAliveCore(entity)) return;

            // Fast path: most deletes don't cascade. Apply policies first; if
            // no descendants got enqueued, just tear down the single entity.
            // Lazy cascade queue: ApplyDeletePoliciesLocked allocates only on
            // first Delete-policy hit.
            Queue<EntityId>? cascade = null;
            ApplyDeletePoliciesLocked(entity, ref cascade);
            if (!IsAliveCore(entity)) return; // policy may have killed it (Panic throw)
            DeleteSingleLocked(entity);

            if (cascade == null) return;

            while (cascade.Count > 0)
            {
                var cur = cascade.Dequeue();
                if (!IsAliveCore(cur)) continue;
                ApplyDeletePoliciesLocked(cur, ref cascade);
                if (!IsAliveCore(cur)) continue;
                DeleteSingleLocked(cur);
            }
        }
    }

    public bool IsAlive(EntityId entity)
    {
        if (entity.Id == 0) return false;
        ref var slot = ref GetSlot(entity.Id);
        return slot.Alive && slot.Generation == entity.Generation;
    }

    // Lock-free alive check — caller holds _lock.
    private bool IsAliveCore(EntityId entity)
    {
        if (entity.Id == 0) return false;
        ref var slot = ref GetSlot(entity.Id);
        return slot.Alive && slot.Generation == entity.Generation;
    }

    // Single-entity teardown — fires OnRemove/Dtor on every component, swaps
    // out of its table, releases the slot. Caller holds _lock and has
    // verified entity is alive.
    private void DeleteSingleLocked(EntityId entity)
    {
        ref var rec = ref GetSlot(entity.Id);
        var table = _tablesById[rec.TableId]!;
        for (int i = 0; i < table.Columns.Length; i++)
        {
            var col = table.Columns[i];
            if (col != null)
            {
                col.InvokeOnRemove(this, entity, rec.Row);
                col.InvokeDtor(this, entity, rec.Row);
            }
            var compIdAtI = table.ComponentIds[i];
            GetIdHooks(compIdAtI)?.OnRemove?.Invoke(this, entity);
            DispatchMultiObsLocked(Event.OnRemove, entity, compIdAtI);
        }
        var moved = table.RemoveRow(rec.Row);
        if (moved.Id != 0)
        {
            ref var movedRec = ref GetSlot(moved.Id);
            movedRec.Row = rec.Row;
        }
        rec.Alive = false;
        rec.TableId = 0;
        rec.Row = 0;
        rec.Generation++;
        _recycled.Push(entity.Id);
        _aliveCount--;
    }

    // Find all ids in any table that reference 'deleted' (as component, as
    // pair relation, or as pair target) and apply the corresponding
    // OnDelete / OnDeleteTarget policy. Cascade-Delete enqueues holders;
    // Remove drops the id from holders; Panic throws.
    //
    // cascade is lazy-allocated only on first Delete-policy hit.
    private void ApplyDeletePoliciesLocked(EntityId deleted, ref Queue<EntityId>? cascade)
    {
        uint deletedId = deleted.Id;
        // Collect (table, id, policy) before mutating — applying policies
        // restructures tables, but we have a snapshot of work to do.
        List<(Table t, Id id, DeletePolicy policy)>? actions = null;
        for (int ti = 1; ti < _tablesById.Count; ti++)
        {
            var t = _tablesById[ti];
            if (t == null || t.Count == 0) continue;
            for (int i = 0; i < t.ComponentIds.Length; i++)
            {
                var id = t.ComponentIds[i];
                DeletePolicy? policy = null;
                if (id.IsPair)
                {
                    if (id.Target == deletedId)
                    {
                        // (R, deleted) — OnDeleteTarget(R)
                        var relIdAsId = new Id((ulong)id.Relation);
                        policy = _onDeleteTarget.TryGetValue(relIdAsId, out var p)
                            ? p : DeletePolicy.Remove;
                    }
                    else if (id.Relation == deletedId)
                    {
                        // (deleted, T) — OnDelete(deleted)
                        policy = _onDelete.TryGetValue((Id)deleted, out var p)
                            ? p : DeletePolicy.Remove;
                    }
                }
                else if (id.Component == deletedId)
                {
                    // Component / tag id = deleted entity itself.
                    policy = _onDelete.TryGetValue((Id)deleted, out var p)
                        ? p : DeletePolicy.Remove;
                }
                if (policy.HasValue)
                    (actions ??= new()).Add((t, id, policy.Value));
            }
        }
        if (actions == null) return;

        for (int ai = 0; ai < actions.Count; ai++)
        {
            var (t, id, policy) = actions[ai];
            // Snapshot — RemoveIdLocked / cascade-delete will mutate table.
            using var holders = new PooledList<EntityId>(t.Entities.Count);
            var ents = t.Entities;
            for (int i = 0; i < ents.Count; i++) holders.Add(ents[i]);
            var hSpan = holders.AsSpan;
            switch (policy)
            {
                case DeletePolicy.Remove:
                    for (int i = 0; i < hSpan.Length; i++)
                        if (IsAliveCore(hSpan[i])) RemoveIdLocked(hSpan[i], id);
                    break;
                case DeletePolicy.Delete:
                    cascade ??= new Queue<EntityId>();
                    for (int i = 0; i < hSpan.Length; i++)
                        if (IsAliveCore(hSpan[i])) cascade.Enqueue(hSpan[i]);
                    break;
                case DeletePolicy.Panic:
                    throw new InvalidOperationException(
                        $"DeletePolicy.Panic: deleting #{deletedId} would orphan id {id} on " +
                        $"#{(hSpan.Length > 0 ? hSpan[0].Id : 0u)} (and possibly others).");
            }
        }
    }

    // ========== Delete-policy configuration ==========

    // OnDelete: policy when 'id' (the entity) is deleted; affects entities
    // holding it as component, as relation in any pair, or as plain id.
    public void SetOnDelete(EntityId id, DeletePolicy policy)
    {
        lock (_lock) { _onDelete[(Id)id] = policy; }
    }

    // OnDeleteTarget: policy when target T is deleted, applied to entities
    // holding (relation, T). Keyed by relation entity. Default is Remove —
    // pair drops from holder. ChildOf preconfigured to Delete (cascade).
    public void SetOnDeleteTarget(EntityId relation, DeletePolicy policy)
    {
        lock (_lock) { _onDeleteTarget[(Id)relation] = policy; }
    }

    // ========== Internal registration ==========

    private EntityId GetOrRegisterComponentLocked<T>() where T : struct
    {
        if (_typeToEntity.TryGetValue(typeof(T), out var ent))
        {
            if (!_componentInfo.ContainsKey((Id)ent))
                throw new InvalidOperationException(
                    $"Type '{typeof(T).Name}' already registered as tag, not component.");
            return ent;
        }
        ent = CreateEntityCore();
        _typeToEntity[typeof(T)] = ent;
        var info = new ComponentInfo(typeof(T), Unsafe.SizeOf<T>(), typeof(T).Name);
        info.Factory = () => new Column<T>(info);
        _componentInfo[(Id)ent] = info;
        ApplyModuleScopeLocked(ent, typeof(T).Name);
        return ent;
    }

    private EntityId GetOrRegisterTagLocked<T>() where T : struct
    {
        if (_typeToEntity.TryGetValue(typeof(T), out var ent))
        {
            if (_componentInfo.ContainsKey((Id)ent))
                throw new InvalidOperationException(
                    $"Type '{typeof(T).Name}' already registered as component, not tag.");
            return ent;
        }
        ent = CreateEntityCore();
        _typeToEntity[typeof(T)] = ent;
        ApplyModuleScopeLocked(ent, typeof(T).Name);
        return ent;
    }

    // Tolerant register — accepts T as either tag or component (whichever it
    // was first registered as). For pair-construction or type-erased Add.
    private EntityId GetOrRegisterAnyLocked<T>() where T : struct
    {
        if (_typeToEntity.TryGetValue(typeof(T), out var ent)) return ent;
        ent = CreateEntityCore();
        _typeToEntity[typeof(T)] = ent;
        // No ComponentInfo → defaults to tag. Caller can promote via Component<T>().
        ApplyModuleScopeLocked(ent, typeof(T).Name);
        return ent;
    }

    // Auto-name + scope a freshly registered component/tag, but ONLY inside
    // a module scope. Default world ops stay clean (don't pull in EntityName).
    private void ApplyModuleScopeLocked(EntityId ent, string name)
    {
        if (!_currentScope.IsValid) return;
        SetNameLocked(ent, name);
        ApplyScopeLocked(ent);
    }

    // Internal SetName variant that runs inside an existing lock — avoids
    // re-entry on _lock. Used during component registration.
    private void SetNameLocked(EntityId entity, string name)
    {
        // Inline the Set path without re-lock: ensure EntityName
        // registered, then write.
        if (!_typeToEntity.TryGetValue(typeof(EntityName), out var nameComp))
        {
            // Register EntityName lazily (recursively safe — typeof check
            // prevents infinite loop because next call hits early return).
            nameComp = CreateEntityCore();
            _typeToEntity[typeof(EntityName)] = nameComp;
            var info = new ComponentInfo(typeof(EntityName), Unsafe.SizeOf<EntityName>(), nameof(EntityName));
            info.Factory = () => new Column<EntityName>(info);
            _componentInfo[(Id)nameComp] = info;
        }
        var compId = (Id)nameComp;
        EnsureHasIdLocked(entity, compId);
        ref var rec = ref GetSlot(entity.Id);
        var table = _tablesById[rec.TableId]!;
        int colIdx = table.IndexOf(compId);
        ((Column<EntityName>)table.Columns[colIdx]!).Set(rec.Row, new EntityName(name));
    }

    // ========== Archetype migration ==========

    private void EnsureHasIdLocked(EntityId entity, Id compId)
    {
        ref var rec = ref GetSlot(entity.Id);
        if (!rec.Alive || rec.Generation != entity.Generation)
            throw new InvalidOperationException("Entity is dead.");
        var src = _tablesById[rec.TableId]!;
        if (src.Has(compId)) return;
        // Trait enforcement on pairs.
        if (compId.IsPair)
        {
            uint relUint = compId.Relation;
            uint tgtUint = compId.Target;
            // Final: cannot IsA-target an entity marked Final.
            if (relUint == IsA.Id && _finalIds.Contains(tgtUint))
                throw new InvalidOperationException(
                    $"Cannot IsA to entity #{tgtUint} marked Final.");
            // Acyclic: target's chain must not include entity (and target
            // must not be entity itself).
            if (_acyclicRelIds.Contains(relUint))
            {
                if (tgtUint == entity.Id)
                    throw new InvalidOperationException(
                        $"Acyclic relation #{relUint}: self-reference on #{entity.Id} forbidden.");
                if (RelChainReachesLocked(tgtUint, entity.Id, relUint))
                    throw new InvalidOperationException(
                        $"Acyclic relation #{relUint}: cycle would form (#{entity.Id} → #{tgtUint}).");
            }
            // Exclusive: drop any existing (relUint, *) pair before adding.
            if (_exclusiveRelIds.Contains(relUint))
            {
                Id? toRemove = null;
                for (int i = 0; i < src.ComponentIds.Length; i++)
                {
                    var existing = src.ComponentIds[i];
                    if (existing.IsPair && existing.Relation == relUint && existing != compId)
                    { toRemove = existing; break; }
                }
                if (toRemove.HasValue)
                {
                    RemoveIdLocked(entity, toRemove.Value);
                    src = _tablesById[rec.TableId]!; // table changed
                }
            }
        }
        // Edge-cache hit skips ArrayWith + SignatureKey + _tablesBySig lookup.
        if (!src._addEdges.TryGetValue(compId, out var dst))
        {
            var newIds = ArrayWith(src.ComponentIds, compId);
            dst = GetOrCreateTable(newIds);
            src._addEdges[compId] = dst;
            dst._removeEdges[compId] = src;
        }
        MoveEntity(entity, ref rec, src, dst);
        // Newly-added column at dst has default-initialized slot. Fire Ctor
        // (user init) then OnAdd (structural event).
        int idx = dst.IndexOf(compId);
        var col = dst.Columns[idx];
        if (col != null)
        {
            col.InvokeCtor(this, entity, rec.Row);
            col.InvokeOnAdd(this, entity, rec.Row);
        }
        GetIdHooks(compId)?.OnAdd?.Invoke(this, entity);
        DispatchMultiObsLocked(Event.OnAdd, entity, compId);

        // Symmetric: mirror (R, target) on entity with (R, entity) on target.
        // Recursion terminates because second call hits early-out via
        // src.Has(reverse).
        if (compId.IsPair && _symmetricRelIds.Contains(compId.Relation))
        {
            uint tgtUint = compId.Target;
            ref var tgtSlot = ref GetSlot(tgtUint);
            if (tgtSlot.Alive)
            {
                var tgtEnt = new EntityId(tgtUint, tgtSlot.Generation);
                ref var relSlot = ref GetSlot(compId.Relation);
                var relEnt = new EntityId(compId.Relation, relSlot.Generation);
                EnsureHasIdLocked(tgtEnt, Id.MakePair(relEnt, entity));
            }
        }
    }

    private void RemoveIdLocked(EntityId entity, Id compId)
    {
        ref var rec = ref GetSlot(entity.Id);
        if (!rec.Alive || rec.Generation != entity.Generation) return;
        var src = _tablesById[rec.TableId]!;
        if (!src.Has(compId)) return;
        // Fire OnRemove + Dtor while data still in src (user can read it).
        int srcIdx = src.IndexOf(compId);
        var srcCol = src.Columns[srcIdx];
        if (srcCol != null)
        {
            srcCol.InvokeOnRemove(this, entity, rec.Row);
            srcCol.InvokeDtor(this, entity, rec.Row);
        }
        GetIdHooks(compId)?.OnRemove?.Invoke(this, entity);
        DispatchMultiObsLocked(Event.OnRemove, entity, compId);
        if (!src._removeEdges.TryGetValue(compId, out var dst))
        {
            var newIds = ArrayWithout(src.ComponentIds, compId);
            dst = GetOrCreateTable(newIds);
            src._removeEdges[compId] = dst;
            dst._addEdges[compId] = src;
        }
        MoveEntity(entity, ref rec, src, dst);

        // Symmetric: mirror removal on the reverse pair. Terminates because
        // second call hits early-out (target no longer has reverse).
        if (compId.IsPair && _symmetricRelIds.Contains(compId.Relation))
        {
            uint tgtUint = compId.Target;
            ref var tgtSlot = ref GetSlot(tgtUint);
            if (tgtSlot.Alive)
            {
                var tgtEnt = new EntityId(tgtUint, tgtSlot.Generation);
                ref var relSlot = ref GetSlot(compId.Relation);
                var relEnt = new EntityId(compId.Relation, relSlot.Generation);
                RemoveIdLocked(tgtEnt, Id.MakePair(relEnt, entity));
            }
        }
    }

    // BFS along (relUint, *) pairs from 'startId'. Returns true if 'targetId'
    // is reachable. Caller holds _lock. Uses pooled scratch buffers.
    private bool RelChainReachesLocked(uint startId, uint targetId, uint relUint)
    {
        if (startId == targetId) return true;
        var (visited, queue) = RentBfsScratchU32();
        try
        {
            visited.Add(startId);
            queue.Enqueue(startId);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                ref var rec = ref GetSlot(cur);
                if (!rec.Alive) continue;
                var t = _tablesById[rec.TableId]!;
                for (int i = 0; i < t.ComponentIds.Length; i++)
                {
                    var id = t.ComponentIds[i];
                    if (!id.IsPair || id.Relation != relUint) continue;
                    uint nxt = id.Target;
                    if (nxt == targetId) return true;
                    if (visited.Add(nxt)) queue.Enqueue(nxt);
                }
            }
            return false;
        }
        finally
        {
            ReturnBfsScratchU32(visited, queue);
        }
    }

    private void MoveEntity(EntityId entity, ref EntityRecord rec, Table src, Table dst)
    {
        int newRow = dst.AddRow(entity);

        int i = 0, j = 0;
        while (i < src.ComponentIds.Length && j < dst.ComponentIds.Length)
        {
            var a = src.ComponentIds[i];
            var b = dst.ComponentIds[j];
            if (a == b)
            {
                var sc = src.Columns[i];
                var dc = dst.Columns[j];
                if (sc != null && dc != null) sc.MoveTo(this, entity, rec.Row, dc, newRow);
                i++; j++;
            }
            else if (a.Value < b.Value) i++;
            else j++;
        }

        var moved = src.RemoveRow(rec.Row);
        if (moved.Id != 0 && moved.Id != entity.Id)
        {
            ref var movedRec = ref GetSlot(moved.Id);
            movedRec.Row = rec.Row;
        }

        rec.TableId = dst.Id;
        rec.Row = newRow;
    }

    private Table GetOrCreateTable(Id[] sortedIds)
    {
        if (_tablesBySig.TryGetValue(new SignatureKey(sortedIds), out var t)) return t;
        return CreateTable(sortedIds);
    }

    private Table CreateTable(Id[] sortedIds)
    {
        var cols = new Column?[sortedIds.Length];
        for (int i = 0; i < sortedIds.Length; i++)
        {
            if (_componentInfo.TryGetValue(sortedIds[i], out var info))
                cols[i] = info.Factory();
            // tag / pair-without-data → null column
        }
        int id = _tablesById.Count;
        var table = new Table(id, sortedIds, cols);
        _tablesById.Add(table);
        _tablesBySig[new SignatureKey(sortedIds)] = table;
        OnTableCreate?.Invoke(this, table);
        return table;
    }

    private ref EntityRecord GetSlot(uint id)
    {
        int page = (int)(id / PageSize);
        int idx = (int)(id % PageSize);
        if (page >= _pages.Length) Array.Resize(ref _pages, page + 1);
        var p = _pages[page] ??= new EntityRecord[PageSize];
        return ref p[idx];
    }

    // ========== BFS scratch pool ==========
    //
    // Reentrancy-safe ThreadStatic pool of (HashSet, Queue) pairs. Rent
    // clears the slot so a nested call gets a fresh instance; Return parks
    // the buffer back. Used by FindInChain / RelChainReachesLocked to avoid
    // per-call allocations on inheritance + acyclic + transitive walks.

    [ThreadStatic] private static HashSet<uint>? _bfsVisitedU32Slot;
    [ThreadStatic] private static Queue<uint>? _bfsQueueU32Slot;
    [ThreadStatic] private static Queue<EntityId>? _bfsQueueEntitySlot;

    private static (HashSet<uint> visited, Queue<uint> queue) RentBfsScratchU32()
    {
        var v = _bfsVisitedU32Slot;
        var q = _bfsQueueU32Slot;
        _bfsVisitedU32Slot = null; // mark in-use; nested rent gets fresh
        _bfsQueueU32Slot = null;
        if (v == null) v = new HashSet<uint>();
        else v.Clear();
        if (q == null) q = new Queue<uint>();
        else q.Clear();
        return (v, q);
    }

    private static void ReturnBfsScratchU32(HashSet<uint> v, Queue<uint> q)
    {
        // Park back; ignore if slot already filled by a nested return.
        if (_bfsVisitedU32Slot == null) _bfsVisitedU32Slot = v;
        if (_bfsQueueU32Slot == null) _bfsQueueU32Slot = q;
    }

    private static (HashSet<uint> visited, Queue<EntityId> queue) RentBfsScratchEntity()
    {
        var v = _bfsVisitedU32Slot;
        var q = _bfsQueueEntitySlot;
        _bfsVisitedU32Slot = null;
        _bfsQueueEntitySlot = null;
        if (v == null) v = new HashSet<uint>();
        else v.Clear();
        if (q == null) q = new Queue<EntityId>();
        else q.Clear();
        return (v, q);
    }

    private static void ReturnBfsScratchEntity(HashSet<uint> v, Queue<EntityId> q)
    {
        if (_bfsVisitedU32Slot == null) _bfsVisitedU32Slot = v;
        if (_bfsQueueEntitySlot == null) _bfsQueueEntitySlot = q;
    }

    // ========== Set algebra on sorted Id[] ==========

    private static Id[] ArrayWith(Id[] src, Id id)
    {
        int n = src.Length;
        int insert = 0;
        while (insert < n && src[insert].Value < id.Value) insert++;
        if (insert < n && src[insert] == id) return src;
        var dst = new Id[n + 1];
        Array.Copy(src, 0, dst, 0, insert);
        dst[insert] = id;
        if (insert < n) Array.Copy(src, insert, dst, insert + 1, n - insert);
        return dst;
    }

    private static Id[] ArrayWithout(Id[] src, Id id)
    {
        int idx = Array.IndexOf(src, id);
        if (idx < 0) return src;
        var dst = new Id[src.Length - 1];
        Array.Copy(src, 0, dst, 0, idx);
        Array.Copy(src, idx + 1, dst, idx, src.Length - idx - 1);
        return dst;
    }
}
