using System;
using System.Runtime.CompilerServices;

namespace Flecs;

// World partial — component / tag / pair registration, has/add/remove/set/get,
// hooks accessor, observers, defer mode, and Query factories.
public sealed partial class World
{
    // ========== Deferred mode ==========

    public void BeginDefer()
    {
        lock (_lock) { _deferDepth++; }
    }

    public void EndDefer()
    {
        bool flush = false;
        lock (_lock)
        {
            if (_deferDepth == 0)
                ThrowHelper.EndDeferWithoutBegin();
            _deferDepth--;
            // Flush only when no defer AND no readonly remain — otherwise the
            // queue is still owned by an outer iteration window.
            flush = _deferDepth == 0 && _readonlyDepth == 0;
        }
        if (flush) Flush();
    }

    public DeferScope Defer()
    {
        BeginDefer();
        return new DeferScope(this);
    }

    // ========== Readonly mode ==========

    // Bumps the readonly counter. Structural mutations route through the
    // command queue while readonly > 0, identical to Defer. Distinct flag so
    // tests / observers can see "we are inside a query iter" without confusing
    // it with explicit user defer. Iteration entry points (Each / Run / Rows /
    // TableEnumerator) wrap their bodies in this scope.
    public void BeginReadonly()
    {
        lock (_lock) { _readonlyDepth++; }
    }

    public void EndReadonly()
    {
        bool flush = false;
        lock (_lock)
        {
            if (_readonlyDepth == 0)
                ThrowHelper.EndReadonlyWithoutBegin();
            _readonlyDepth--;
            flush = _readonlyDepth == 0 && _deferDepth == 0;
        }
        if (flush) Flush();
    }

    public ReadonlyScope Readonly()
    {
        BeginReadonly();
        return new ReadonlyScope(this);
    }

    private void Flush()
    {
        // Loop drains commands queued during apply (e.g. nested Defer).
        while (true)
        {
            lock (_lock)
            {
                if (_commands.Count == 0) return;
                (_commands, _flushing) = (_flushing, _commands);
            }
            for (int i = 0; i < _flushing.Count; i++)
            {
                var c = _flushing[i];
                c.Apply(this);
                c.Recycle();
            }
            _flushing.Clear();
        }
    }

    // ========== Component / tag / pair registration ==========

    // Register T as a data-bearing component. Throws if already a tag.
    public EntityId Component<T>() where T : struct
    {
        lock (_lock) { return GetOrRegisterComponentLocked<T>(); }
    }

    // Register T as a tag (no data). Throws if already a component.
    public EntityId Tag<T>() where T : struct
    {
        lock (_lock) { return GetOrRegisterTagLocked<T>(); }
    }

    public Id Pair<TR, TT>() where TR : struct where TT : struct
    {
        lock (_lock)
        {
            var rel = GetOrRegisterAnyLocked<TR>();
            var tgt = GetOrRegisterAnyLocked<TT>();
            return Id.MakePair(rel, tgt);
        }
    }

    public Id Pair(EntityId relation, EntityId target) => Id.MakePair(relation, target);

    // Get/init mutable TypeHooks<T> for component T. Auto-registers as
    // component if not yet registered. Mutate fields directly:
    //   world.Hooks<Position>().OnAdd = (w, e, ref Position p) => ...;
    public TypeHooks<T> Hooks<T>() where T : struct
    {
        lock (_lock)
        {
            var ent = GetOrRegisterComponentLocked<T>();
            var info = _componentInfo[(Id)ent];
            var h = info.Hooks as TypeHooks<T>;
            if (h == null) { h = new TypeHooks<T>(); info.Hooks = h; }
            return h;
        }
    }

    // ========== Observers ==========
    //
    // yieldExisting: when true, the action fires retroactively for every live
    // entity already holding the id at registration time. Only meaningful for
    // OnAdd / OnSet — OnRemove on yield-existing is a no-op (the entity hasn't
    // lost the id). Mirrors flecs ecs_observer_desc_t::yield_existing.

    // Typed observer — receives ref to component value. T must be a data
    // component (not a tag). Stacks via multicast on TypeHooks<T>.
    public void Observer<T>(Event evt, TypeHookAction<T> action, bool yieldExisting = false) where T : struct
    {
        var h = Hooks<T>();
        switch (evt)
        {
            case Event.OnAdd: h.OnAdd += action; break;
            case Event.OnRemove: h.OnRemove += action; break;
            case Event.OnSet: h.OnSet += action; break;
        }
        if (yieldExisting && evt != Event.OnRemove) YieldExistingTypedLocked<T>(action);
    }

    // Tag-style observer (no value ref) for a typed tag/component.
    public void Observer<T>(Event evt, Action<World, EntityId> action, bool yieldExisting = false) where T : struct
    {
        Id id;
        lock (_lock)
        {
            var ent = GetOrRegisterAnyLocked<T>();
            id = (Id)ent;
            AddIdObserverLocked(id, evt, action);
        }
        if (yieldExisting && evt != Event.OnRemove) YieldExistingIdLocked(id, action);
    }

    // Pair observer (TR, TT). Both auto-registered.
    public void Observer<TR, TT>(Event evt, Action<World, EntityId> action, bool yieldExisting = false)
        where TR : struct where TT : struct
    {
        Id pair;
        lock (_lock)
        {
            var rel = GetOrRegisterAnyLocked<TR>();
            var tgt = GetOrRegisterAnyLocked<TT>();
            pair = Id.MakePair(rel, tgt);
            AddIdObserverLocked(pair, evt, action);
        }
        if (yieldExisting && evt != Event.OnRemove) YieldExistingIdLocked(pair, action);
    }

    // Generic Id-keyed observer (covers runtime pairs).
    public void Observer(Id id, Event evt, Action<World, EntityId> action, bool yieldExisting = false)
    {
        lock (_lock) { AddIdObserverLocked(id, evt, action); }
        if (yieldExisting && evt != Event.OnRemove) YieldExistingIdLocked(id, action);
    }

    // Snapshot tables holding 'id', invoke action for every entity. Done
    // outside the world lock so the callback can mutate freely (deferred mode
    // applies if active). Mirrors flecs yield_existing pass.
    private void YieldExistingIdLocked(Id id, Action<World, EntityId> action)
    {
        using var holders = new PooledList<EntityId>(16);
        lock (_lock)
        {
            for (int ti = 1; ti < _tablesById.Count; ti++)
            {
                var t = _tablesById[ti];
                if (t == null || t.Count == 0 || !t.Has(id)) continue;
                var ents = t.Entities;
                for (int i = 0; i < ents.Count; i++) holders.Add(ents[i]);
            }
        }
        var span = holders.AsSpan;
        for (int i = 0; i < span.Length; i++) action(this, span[i]);
    }

    // Typed yield — invokes action with ref to current value of T on every
    // live holder. T must be a data component.
    private void YieldExistingTypedLocked<T>(TypeHookAction<T> action) where T : struct
    {
        Id compId;
        lock (_lock)
        {
            if (!_typeToEntity.TryGetValue(typeof(T), out var ent)) return;
            compId = (Id)ent;
        }
        // Collect (table, row, entity) snapshots and dispatch outside lock.
        using var snap = new PooledList<(Table t, int row, EntityId e)>(16);
        lock (_lock)
        {
            for (int ti = 1; ti < _tablesById.Count; ti++)
            {
                var t = _tablesById[ti];
                if (t == null || t.Count == 0 || !t.Has(compId)) continue;
                for (int r = 0; r < t.Entities.Count; r++) snap.Add((t, r, t.Entities[r]));
            }
        }
        var span = snap.AsSpan;
        for (int i = 0; i < span.Length; i++)
        {
            var e = span[i].e;
            // Re-resolve ref each iteration — table may have shifted rows due
            // to side effects of prior callbacks.
            if (!IsAlive(e)) continue;
            ref var rec = ref GetSlot(e.Id);
            var live = _tablesById[rec.TableId]!;
            if (!live.Has(compId)) continue;
            var col = (Column<T>)live.Columns[live.IndexOf(compId)]!;
            action(this, e, ref col.GetRef(rec.Row));
        }
    }

    private void AddIdObserverLocked(Id id, Event evt, Action<World, EntityId> action)
    {
        if (!_idHooks.TryGetValue(id, out var ih)) { ih = new IdHooks(); _idHooks[id] = ih; }
        switch (evt)
        {
            case Event.OnAdd: ih.OnAdd += action; break;
            case Event.OnRemove: ih.OnRemove += action; break;
            case Event.OnSet: ih.OnSet += action; break;
        }
    }

    // Lookup helper — return null if no subscribers (hot path optimization).
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private IdHooks? GetIdHooks(Id id)
    {
        if (_idHooks.Count == 0) return null;
        return _idHooks.TryGetValue(id, out var ih) ? ih : null;
    }

    // ===== Multi-term observers (filter-style) =====

    // Register an observer that fires when 'evt' hits T1 or T2 on an entity
    // AND that entity has the other term. Refs handed to the callback may be
    // shared (inherited via IsA) — same semantics as Get<T>. Mirrors flecs
    // filter-style observer with two terms.
    public void Observer<T1, T2>(Event evt, MultiObserverAction<T1, T2> action)
        where T1 : struct where T2 : struct
    {
        Id id1, id2;
        lock (_lock)
        {
            // Refs handed to the callback presume data components.
            id1 = (Id)GetOrRegisterComponentLocked<T1>();
            id2 = (Id)GetOrRegisterComponentLocked<T2>();
            // Capture-by-value of typed ids; dispatch closes over typed action
            // so the per-event hot path stays untyped.
            void Dispatch(World w, EntityId e)
            {
                ref var c1 = ref w.TryGetRef<T1>(e);
                ref var c2 = ref w.TryGetRef<T2>(e);
                if (Unsafe.IsNullRef(ref c1) || Unsafe.IsNullRef(ref c2)) return;
                action(w, e, ref c1, ref c2);
            }
            var obs = new MultiObserver(new[] { id1, id2 }, Dispatch);
            AddMultiObsTriggerLocked(evt, id1, obs);
            AddMultiObsTriggerLocked(evt, id2, obs);
        }
    }

    private void AddMultiObsTriggerLocked(Event evt, Id triggerId, MultiObserver obs)
    {
        var key = (evt, triggerId);
        if (!_multiObsByTrigger.TryGetValue(key, out var list))
        {
            list = new List<MultiObserver>();
            _multiObsByTrigger[key] = list;
        }
        list.Add(obs);
    }

    // Called from event-firing sites after single-id hooks. Walks the trigger
    // observer list, checks remaining terms via Has (Self+Up), dispatches.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void DispatchMultiObsLocked(Event evt, EntityId entity, Id triggerId)
    {
        // Hot-path bypass: when no multi-observers are registered, skip the
        // dict hash entirely. Set/Add/Remove fire this on every component
        // event; the dict.TryGetValue cost was measurable during entity
        // setup-heavy workloads.
        if (_multiObsByTrigger.Count == 0) return;
        if (!_multiObsByTrigger.TryGetValue((evt, triggerId), out var list)) return;
        for (int i = 0; i < list.Count; i++)
        {
            var obs = list[i];
            bool allHave = true;
            for (int j = 0; j < obs.Ids.Length; j++)
            {
                var id = obs.Ids[j];
                if (id == triggerId) continue;
                if (!HasIdSelfOrIsA(entity, id)) { allHave = false; break; }
            }
            if (allHave) obs.Dispatch(this, entity);
        }
    }

    // Self+Up id check used by multi-observer dispatch. Mirrors Has(EntityId, Id)
    // logic without re-validating IsAlive (caller already in event flow).
    private bool HasIdSelfOrIsA(EntityId entity, Id id)
    {
        ref var rec = ref GetSlot(entity.Id);
        if (_tablesById[rec.TableId]!.Has(id)) return true;
        var (found, _, _) = FindInIsAChain(entity, id);
        return found;
    }

    // ========== Custom events ==========
    //
    // Events are user-defined types. First use auto-registers the type as a
    // tag-style entity (same path as Tag<T>) — no CreateEvent ceremony.
    // Observers subscribe to (TEvent, target-id) cells; Emit fires.
    // Builtin OnAdd/OnRemove/OnSet stay on the Event enum and dispatch
    // automatically; custom events are user-driven via Emit.
    //
    //   public struct OnHit { }
    //   world.Observer<OnHit, Health>((w, e) => ...);
    //   world.Emit<OnHit, Health>(target);
    //   world.Emit<OnHit, Health>(target, world.ChildOf);  // bubble up

    // Subscribe to (TEvent, T) — typed component or tag target.
    public void Observer<TEvent, T>(Action<World, EntityId> action, bool yieldExisting = false)
        where TEvent : struct where T : struct
    {
        EntityId evt;
        Id target;
        lock (_lock)
        {
            evt = GetOrRegisterAnyLocked<TEvent>();
            target = (Id)GetOrRegisterAnyLocked<T>();
            AddCustomObsLocked(evt, target, action);
        }
        if (yieldExisting) YieldExistingIdLocked(target, action);
    }

    // Subscribe to (TEvent, (TR, TT)) — pair target.
    public void Observer<TEvent, TR, TT>(Action<World, EntityId> action, bool yieldExisting = false)
        where TEvent : struct where TR : struct where TT : struct
    {
        EntityId evt;
        Id pair;
        lock (_lock)
        {
            evt = GetOrRegisterAnyLocked<TEvent>();
            var rel = GetOrRegisterAnyLocked<TR>();
            var tgt = GetOrRegisterAnyLocked<TT>();
            pair = Id.MakePair(rel, tgt);
            AddCustomObsLocked(evt, pair, action);
        }
        if (yieldExisting) YieldExistingIdLocked(pair, action);
    }

    private void AddCustomObsLocked(EntityId evt, Id target, Action<World, EntityId> action)
    {
        var key = (evt.Id, target);
        _customObs.TryGetValue(key, out var existing);
        _customObs[key] = existing + action;
    }

    // Emit (TEvent, T) on entity. Optional propagateRel bubbles the event up
    // a relation chain (ChildOf, IsA, custom). Callback fires once per chain
    // step (self → ancestor → ...).
    public void Emit<TEvent, T>(EntityId entity, EntityId propagateRel = default)
        where TEvent : struct where T : struct
    {
        var key = (typeof(TEvent), typeof(T));
        uint evtId;
        Id id;
        lock (_lock)
        {
            if (_emitKeyCache1.TryGetValue(key, out var cached))
            {
                evtId = cached.evtId;
                id = cached.tgt;
            }
            else
            {
                evtId = GetOrRegisterAnyLocked<TEvent>().Id;
                id = (Id)GetOrRegisterAnyLocked<T>();
                _emitKeyCache1[key] = (evtId, id);
            }
        }
        EmitInternal(evtId, entity, id, propagateRel);
    }

    public void Emit<TEvent, TR, TT>(EntityId entity, EntityId propagateRel = default)
        where TEvent : struct where TR : struct where TT : struct
    {
        var key = (typeof(TEvent), typeof(TR), typeof(TT));
        uint evtId;
        Id pair;
        lock (_lock)
        {
            if (_emitKeyCache2.TryGetValue(key, out var cached))
            {
                evtId = cached.evtId;
                pair = cached.tgt;
            }
            else
            {
                evtId = GetOrRegisterAnyLocked<TEvent>().Id;
                var rel = GetOrRegisterAnyLocked<TR>();
                var tgt = GetOrRegisterAnyLocked<TT>();
                pair = Id.MakePair(rel, tgt);
                _emitKeyCache2[key] = (evtId, pair);
            }
        }
        EmitInternal(evtId, entity, pair, propagateRel);
    }

    private void EmitInternal(uint evtId, EntityId entity, Id id, EntityId propagateRel)
    {
        Action<World, EntityId>? a;
        lock (_lock) { _customObs.TryGetValue((evtId, id), out a); }
        if (a == null) return;

        if (!propagateRel.IsValid)
        {
            a(this, entity);
            return;
        }

        // Collect BFS chain under lock so callbacks fire outside lock.
        // Allows callbacks to mutate world freely (Defer still respected).
        // Buffers pooled per-World; reentrancy (callback re-emits) falls back
        // to fresh allocs to avoid mid-iteration mutation.
        List<EntityId> chain;
        HashSet<uint> visited;
        Queue<EntityId> queue;
        bool pooled;
        lock (_lock)
        {
            if (!_emitBufBusy)
            {
                _emitBufBusy = true;
                chain = _emitChainBuf ??= new List<EntityId>();
                visited = _emitVisitedBuf ??= new HashSet<uint>();
                queue = _emitQueueBuf ??= new Queue<EntityId>();
                chain.Clear();
                visited.Clear();
                queue.Clear();
                pooled = true;
            }
            else
            {
                chain = new List<EntityId>();
                visited = new HashSet<uint>();
                queue = new Queue<EntityId>();
                pooled = false;
            }

            chain.Add(entity);
            visited.Add(entity.Id);
            queue.Enqueue(entity);
            uint relId = propagateRel.Id;
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                if (!IsAliveCore(cur)) continue;
                ref var rec = ref GetSlot(cur.Id);
                var t = _tablesById[rec.TableId]!;
                for (int i = 0; i < t.ComponentIds.Length; i++)
                {
                    var cid = t.ComponentIds[i];
                    if (!cid.IsPair || cid.Relation != relId) continue;
                    uint tgtId = cid.Target;
                    if (tgtId == 0 || !visited.Add(tgtId)) continue;
                    ref var ts = ref GetSlot(tgtId);
                    var anc = new EntityId(tgtId, ts.Generation);
                    chain.Add(anc);
                    queue.Enqueue(anc);
                }
            }
        }
        try
        {
            foreach (var e in chain) a(this, e);
        }
        finally
        {
            if (pooled) lock (_lock) { _emitBufBusy = false; }
        }
    }

    // Pair (TR, *) — relation typed, target wildcard. For query matches.
    public Id PairWildcard<TR>() where TR : struct
    {
        lock (_lock) { return Id.MakePair(GetOrRegisterAnyLocked<TR>(), Wildcard); }
    }

    // Tolerant id resolver — works for components or tags. Used by queries.
    public Id IdOf<T>() where T : struct
    {
        lock (_lock) { return (Id)GetOrRegisterAnyLocked<T>(); }
    }

    // ========== Query factories ==========
    public Query<T1> Query<T1>() where T1 : struct => new(this);
    public Query<T1, T2> Query<T1, T2>() where T1 : struct where T2 : struct => new(this);
    public Query<T1, T2, T3> Query<T1, T2, T3>() where T1 : struct where T2 : struct where T3 : struct => new(this);
    public Query<T1, T2, T3, T4> Query<T1, T2, T3, T4>() where T1 : struct where T2 : struct where T3 : struct where T4 : struct => new(this);
    public Query<T1, T2, T3, T4, T5> Query<T1, T2, T3, T4, T5>() where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct => new(this);
    public Query<T1, T2, T3, T4, T5, T6> Query<T1, T2, T3, T4, T5, T6>() where T1 : struct where T2 : struct where T3 : struct where T4 : struct where T5 : struct where T6 : struct => new(this);

    // ========== Component data ops ==========

    public void Set<T>(EntityId entity, T value) where T : struct
    {
        lock (_lock)
        {
            if (ShouldQueueLocked())
            {
                _commands.Add(SetCmd<T>.Rent(entity, value));
                return;
            }
            var compId = GetOrRegisterComponentLocked<T>();
            EnsureHasIdLocked(entity, compId);
            ref var rec = ref GetSlot(entity.Id);
            var table = _tablesById[rec.TableId]!;
            int colIdx = table.IndexOf((Id)compId);
            var col = (Column<T>)table.Columns[colIdx]!;
            col.Set(rec.Row, value);
            col.InvokeOnSet(this, entity, rec.Row);
            GetIdHooks((Id)compId)?.OnSet?.Invoke(this, entity);
            DispatchMultiObsLocked(Event.OnSet, entity, (Id)compId);
        }
    }

    // Get<T>: returns ref to T on entity itself, or via IsA chain (shared ref
    // from prefab archetype). Mirrors flecs ecs_get_id semantics. Mutating a
    // shared (inherited) ref affects all instances pointing to the prefab.
    // Use Owns<T> to check literal ownership before mutating.
    public ref T Get<T>(EntityId entity) where T : struct
    {
        ref var rec = ref GetSlot(entity.Id);
        if (!rec.Alive || rec.Generation != entity.Generation) ThrowHelper.EntityDead();
        if (!_typeToEntity.TryGetValue(typeof(T), out var compEnt)) ThrowHelper.ComponentNotRegistered(typeof(T));
        var compId = (Id)compEnt;
        if (!_componentInfo.ContainsKey(compId)) ThrowHelper.IsTagNotComponent(typeof(T));
        var table = _tablesById[rec.TableId]!;
        if (table.Has(compId))
            return ref ((Column<T>)table.Columns[table.IndexOf(compId)]!).GetRef(rec.Row);
        var (found, t, row) = FindInIsAChain(entity, compId);
        if (!found) ThrowHelper.EntityMissingComponent(typeof(T));
        return ref ((Column<T>)t!.Columns[t.IndexOf(compId)]!).GetRef(row);
    }

    // By-value optional accessor. Use inside Query.Each callbacks for the
    // "optional term" pattern: check + grab in one call. Walks IsA chain.
    public bool TryGetComponent<T>(EntityId entity, out T value) where T : struct
    {
        ref var rec = ref GetSlot(entity.Id);
        if (!rec.Alive || rec.Generation != entity.Generation
            || !_typeToEntity.TryGetValue(typeof(T), out var compEnt))
        { value = default; return false; }
        var compId = (Id)compEnt;
        if (!_componentInfo.ContainsKey(compId)) { value = default; return false; }
        var table = _tablesById[rec.TableId]!;
        if (table.Has(compId))
        {
            value = ((Column<T>)table.Columns[table.IndexOf(compId)]!).GetRef(rec.Row);
            return true;
        }
        var (found, t, row) = FindInIsAChain(entity, compId);
        if (!found) { value = default; return false; }
        value = ((Column<T>)t!.Columns[t.IndexOf(compId)]!).GetRef(row);
        return true;
    }

    // Mutating optional accessor. Returns a writable ref to the real column
    // slot when present (own or shared via IsA), a NullRef when absent.
    // Caller checks via `Unsafe.IsNullRef(ref v)`.
    //
    //   ref var v = ref world.TryGetRef<Velocity>(e);
    //   if (!Unsafe.IsNullRef(ref v)) v.Dx *= 0.5f;
    public ref T TryGetRef<T>(EntityId entity) where T : struct
    {
        ref var rec = ref GetSlot(entity.Id);
        if (!rec.Alive || rec.Generation != entity.Generation
            || !_typeToEntity.TryGetValue(typeof(T), out var compEnt))
            return ref Unsafe.NullRef<T>();
        var compId = (Id)compEnt;
        if (!_componentInfo.ContainsKey(compId)) return ref Unsafe.NullRef<T>();
        var table = _tablesById[rec.TableId]!;
        if (table.Has(compId))
            return ref ((Column<T>)table.Columns[table.IndexOf(compId)]!).GetRef(rec.Row);
        var (found, t, row) = FindInIsAChain(entity, compId);
        if (!found) return ref Unsafe.NullRef<T>();
        return ref ((Column<T>)t!.Columns[t.IndexOf(compId)]!).GetRef(row);
    }

    // ========== Has / Owns / Add / Remove (unified for component, tag, pair) ==========
    //
    // Has<T>:  walks IsA chain (Self+Up). Mirrors flecs ecs_has_id.
    // Owns<T>: literal check, ignores IsA. Mirrors flecs ecs_owns_id.

    public bool Has<T>(EntityId entity) where T : struct
    {
        if (!IsAlive(entity)) return false;
        if (!_typeToEntity.TryGetValue(typeof(T), out var compEnt)) return false;
        ref var rec = ref GetSlot(entity.Id);
        var compId = (Id)compEnt;
        if (_tablesById[rec.TableId]!.Has(compId)) return true;
        var (found, _, _) = FindInIsAChain(entity, compId);
        return found;
    }

    public bool Has<TR, TT>(EntityId entity) where TR : struct where TT : struct
    {
        if (!IsAlive(entity)) return false;
        if (!_typeToEntity.TryGetValue(typeof(TR), out var rel)) return false;
        if (!_typeToEntity.TryGetValue(typeof(TT), out var tgt)) return false;
        ref var rec = ref GetSlot(entity.Id);
        var pair = Id.MakePair(rel, tgt);
        if (_tablesById[rec.TableId]!.Has(pair)) return true;
        var (found, _, _) = FindInIsAChain(entity, pair);
        return found;
    }

    public bool Has(EntityId entity, Id componentId)
    {
        if (!IsAlive(entity)) return false;
        ref var rec = ref GetSlot(entity.Id);
        if (_tablesById[rec.TableId]!.Has(componentId)) return true;
        var (found, _, _) = FindInIsAChain(entity, componentId);
        return found;
    }

    // Literal-only ownership check. Does NOT walk IsA. Use when you need to
    // distinguish own components from inherited ones (e.g. before a write-in-
    // place mutation that would otherwise affect a shared prefab value).
    public bool Owns<T>(EntityId entity) where T : struct
    {
        if (!IsAlive(entity)) return false;
        if (!_typeToEntity.TryGetValue(typeof(T), out var compEnt)) return false;
        ref var rec = ref GetSlot(entity.Id);
        return _tablesById[rec.TableId]!.Has((Id)compEnt);
    }

    public bool Owns<TR, TT>(EntityId entity) where TR : struct where TT : struct
    {
        if (!IsAlive(entity)) return false;
        if (!_typeToEntity.TryGetValue(typeof(TR), out var rel)) return false;
        if (!_typeToEntity.TryGetValue(typeof(TT), out var tgt)) return false;
        ref var rec = ref GetSlot(entity.Id);
        return _tablesById[rec.TableId]!.Has(Id.MakePair(rel, tgt));
    }

    public bool Owns(EntityId entity, Id componentId)
    {
        if (!IsAlive(entity)) return false;
        ref var rec = ref GetSlot(entity.Id);
        return _tablesById[rec.TableId]!.Has(componentId);
    }

    // Add T as tag/component (no value set; component slot defaults).
    public void Add<T>(EntityId entity) where T : struct
    {
        lock (_lock)
        {
            var ent = GetOrRegisterAnyLocked<T>();
            if (ShouldQueueLocked()) { _commands.Add(AddIdCmd.Rent(entity, (Id)ent)); return; }
            EnsureHasIdLocked(entity, ent);
        }
    }

    // Add pair (TR, TT). Both auto-registered as tags if new.
    public void Add<TR, TT>(EntityId entity) where TR : struct where TT : struct
    {
        lock (_lock)
        {
            var rel = GetOrRegisterAnyLocked<TR>();
            var tgt = GetOrRegisterAnyLocked<TT>();
            var pair = Id.MakePair(rel, tgt);
            if (ShouldQueueLocked()) { _commands.Add(AddIdCmd.Rent(entity, pair)); return; }
            EnsureHasIdLocked(entity, pair);
        }
    }

    // Type-erased add — relation/target can be any entity.
    public void Add(EntityId entity, EntityId relation, EntityId target)
    {
        lock (_lock)
        {
            var pair = Id.MakePair(relation, target);
            if (ShouldQueueLocked()) { _commands.Add(AddIdCmd.Rent(entity, pair)); return; }
            EnsureHasIdLocked(entity, pair);
        }
    }

    public void Add(EntityId entity, Id componentId)
    {
        lock (_lock)
        {
            if (ShouldQueueLocked()) { _commands.Add(AddIdCmd.Rent(entity, componentId)); return; }
            EnsureHasIdLocked(entity, componentId);
        }
    }

    public void Remove<T>(EntityId entity) where T : struct
    {
        lock (_lock)
        {
            if (!_typeToEntity.TryGetValue(typeof(T), out var compEnt)) return;
            if (ShouldQueueLocked()) { _commands.Add(RemoveIdCmd.Rent(entity, (Id)compEnt)); return; }
            RemoveIdLocked(entity, (Id)compEnt);
        }
    }

    public void Remove<TR, TT>(EntityId entity) where TR : struct where TT : struct
    {
        lock (_lock)
        {
            if (!_typeToEntity.TryGetValue(typeof(TR), out var rel)) return;
            if (!_typeToEntity.TryGetValue(typeof(TT), out var tgt)) return;
            var pair = Id.MakePair(rel, tgt);
            if (ShouldQueueLocked()) { _commands.Add(RemoveIdCmd.Rent(entity, pair)); return; }
            RemoveIdLocked(entity, pair);
        }
    }

    public void Remove(EntityId entity, Id componentId)
    {
        lock (_lock)
        {
            if (ShouldQueueLocked()) { _commands.Add(RemoveIdCmd.Rent(entity, componentId)); return; }
            RemoveIdLocked(entity, componentId);
        }
    }

    // Toggle / SetEnabled / IsEnabled
    //
    // Two semantics, dispatched by CanToggle trait on the id:
    //
    //   Without CanToggle (default): legacy Add/Remove. Each call triggers an
    //   archetype migration. Use for tags or rarely-toggled state.
    //
    //   With CanToggle (MarkCanToggle<T>()): non-fragmenting bitset. Toggle
    //   flips the parallel-bit; the component stays present in the table, its
    //   value is preserved across disable→enable, and queries skip rows whose
    //   required terms are disabled. No archetype migration.
    //
    // For CanToggle ids, the component must already be Add'd before flipping
    // — Toggle / SetEnabled on a missing component first Adds it (entering
    // enabled state), so a subsequent SetEnabled(false) lands in 'present but
    // disabled'. Mirrors flecs ecs_enable_id / ecs_is_enabled_id.
    public void Toggle<T>(EntityId entity) where T : struct
    {
        lock (_lock)
        {
            if (_typeToEntity.TryGetValue(typeof(T), out var compEnt)
                && _canToggleIds.Contains(compEnt.Id))
            {
                ToggleBitLocked(entity, compEnt, (Id)compEnt);
                return;
            }
        }
        if (Has<T>(entity)) Remove<T>(entity);
        else Add<T>(entity);
    }

    public void SetEnabled<T>(EntityId entity, bool enabled) where T : struct
    {
        lock (_lock)
        {
            if (_typeToEntity.TryGetValue(typeof(T), out var compEnt)
                && _canToggleIds.Contains(compEnt.Id))
            {
                SetBitLocked(entity, compEnt, (Id)compEnt, enabled);
                return;
            }
        }
        if (enabled) { if (!Has<T>(entity)) Add<T>(entity); }
        else { if (Has<T>(entity)) Remove<T>(entity); }
    }

    public bool IsEnabled<T>(EntityId entity) where T : struct
    {
        if (!IsAlive(entity)) return false;
        if (!_typeToEntity.TryGetValue(typeof(T), out var compEnt)) return false;
        var compId = (Id)compEnt;
        ref var rec = ref GetSlot(entity.Id);
        var t = _tablesById[rec.TableId]!;
        if (!t.Has(compId)) return false;
        // Non-CanToggle: presence == enabled.
        if (!_canToggleIds.Contains(compEnt.Id)) return true;
        int idx = t.IndexOf(compId);
        var bs = t.Bits[idx];
        return bs == null || bs.Get(rec.Row);
    }

    private void ToggleBitLocked(EntityId entity, EntityId compEnt, Id compId)
    {
        EnsureHasIdLocked(entity, compId);
        ref var rec = ref GetSlot(entity.Id);
        var t = _tablesById[rec.TableId]!;
        int idx = t.IndexOf(compId);
        var bs = t.Bits[idx]!;
        bs.Set(rec.Row, !bs.Get(rec.Row));
        t.Version++;
    }

    private void SetBitLocked(EntityId entity, EntityId compEnt, Id compId, bool enabled)
    {
        EnsureHasIdLocked(entity, compId);
        ref var rec = ref GetSlot(entity.Id);
        var t = _tablesById[rec.TableId]!;
        int idx = t.IndexOf(compId);
        var bs = t.Bits[idx]!;
        if (bs.Get(rec.Row) == enabled) return;
        bs.Set(rec.Row, enabled);
        t.Version++;
    }
}
