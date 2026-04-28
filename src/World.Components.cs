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
        lock (_lock)
        {
            if (_deferDepth == 0)
                throw new InvalidOperationException("EndDefer without matching BeginDefer.");
            _deferDepth--;
            if (_deferDepth > 0) return;
        }
        Flush();
    }

    public DeferScope Defer()
    {
        BeginDefer();
        return new DeferScope(this);
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
        EntityId[] holders;
        lock (_lock)
        {
            var list = new List<EntityId>();
            for (int ti = 1; ti < _tablesById.Count; ti++)
            {
                var t = _tablesById[ti];
                if (t == null || t.Count == 0 || !t.Has(id)) continue;
                list.AddRange(t.Entities);
            }
            holders = list.ToArray();
        }
        foreach (var e in holders) action(this, e);
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
        var snap = new List<(Table t, int row, EntityId e)>();
        lock (_lock)
        {
            for (int ti = 1; ti < _tablesById.Count; ti++)
            {
                var t = _tablesById[ti];
                if (t == null || t.Count == 0 || !t.Has(compId)) continue;
                for (int r = 0; r < t.Entities.Count; r++) snap.Add((t, r, t.Entities[r]));
            }
        }
        foreach (var (t, row, e) in snap)
        {
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
    private IdHooks? GetIdHooks(Id id)
        => _idHooks.TryGetValue(id, out var ih) ? ih : null;

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
        EntityId evt;
        Id id;
        lock (_lock)
        {
            evt = GetOrRegisterAnyLocked<TEvent>();
            id = (Id)GetOrRegisterAnyLocked<T>();
        }
        EmitInternal(evt, entity, id, propagateRel);
    }

    public void Emit<TEvent, TR, TT>(EntityId entity, EntityId propagateRel = default)
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
        }
        EmitInternal(evt, entity, pair, propagateRel);
    }

    private void EmitInternal(EntityId evt, EntityId entity, Id id, EntityId propagateRel)
    {
        Action<World, EntityId>? a;
        lock (_lock) { _customObs.TryGetValue((evt.Id, id), out a); }
        if (a == null) return;

        if (!propagateRel.IsValid)
        {
            a(this, entity);
            return;
        }

        // Collect BFS chain under lock so callbacks fire outside lock.
        // Allows callbacks to mutate world freely (Defer still respected).
        List<EntityId> chain;
        lock (_lock)
        {
            chain = new List<EntityId> { entity };
            var visited = new HashSet<uint> { entity.Id };
            var queue = new Queue<EntityId>();
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
        foreach (var e in chain) a(this, e);
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
            if (_deferDepth > 0)
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
        }
    }

    public ref T Get<T>(EntityId entity) where T : struct
    {
        ref var rec = ref GetSlot(entity.Id);
        if (!rec.Alive || rec.Generation != entity.Generation)
            throw new InvalidOperationException("Entity is dead.");
        if (!_typeToEntity.TryGetValue(typeof(T), out var compEnt))
            throw new InvalidOperationException($"Component '{typeof(T).Name}' not registered.");
        var compId = (Id)compEnt;
        if (!_componentInfo.ContainsKey(compId))
            throw new InvalidOperationException($"'{typeof(T).Name}' is a tag, has no data.");
        var table = _tablesById[rec.TableId]!;
        if (!table.Has(compId))
            throw new InvalidOperationException($"Entity does not have component '{typeof(T).Name}'.");
        return ref ((Column<T>)table.Columns[table.IndexOf(compId)]!).GetRef(rec.Row);
    }

    // By-value optional accessor. Use inside Query.Each callbacks for the
    // "optional term" pattern: check + grab in one call.
    public bool TryGetComponent<T>(EntityId entity, out T value) where T : struct
    {
        ref var rec = ref GetSlot(entity.Id);
        if (!rec.Alive || rec.Generation != entity.Generation
            || !_typeToEntity.TryGetValue(typeof(T), out var compEnt))
        { value = default; return false; }
        var compId = (Id)compEnt;
        if (!_componentInfo.ContainsKey(compId)) { value = default; return false; }
        var table = _tablesById[rec.TableId]!;
        if (!table.Has(compId)) { value = default; return false; }
        value = ((Column<T>)table.Columns[table.IndexOf(compId)]!).GetRef(rec.Row);
        return true;
    }

    // Mutating optional accessor. Returns a writable ref to the real column
    // slot when present, a NullRef when absent. Caller checks via
    // `Unsafe.IsNullRef(ref v)`. Use inside Query.Each callbacks.
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
        if (!table.Has(compId)) return ref Unsafe.NullRef<T>();
        var col = (Column<T>)table.Columns[table.IndexOf(compId)]!;
        return ref col.GetRef(rec.Row);
    }

    // ========== Has / Add / Remove (unified for component, tag, pair) ==========

    public bool Has<T>(EntityId entity) where T : struct
    {
        if (!IsAlive(entity)) return false;
        if (!_typeToEntity.TryGetValue(typeof(T), out var compEnt)) return false;
        ref var rec = ref GetSlot(entity.Id);
        return _tablesById[rec.TableId]!.Has((Id)compEnt);
    }

    public bool Has<TR, TT>(EntityId entity) where TR : struct where TT : struct
    {
        if (!IsAlive(entity)) return false;
        if (!_typeToEntity.TryGetValue(typeof(TR), out var rel)) return false;
        if (!_typeToEntity.TryGetValue(typeof(TT), out var tgt)) return false;
        ref var rec = ref GetSlot(entity.Id);
        return _tablesById[rec.TableId]!.Has(Id.MakePair(rel, tgt));
    }

    public bool Has(EntityId entity, Id componentId)
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
            if (_deferDepth > 0) { _commands.Add(AddIdCmd.Rent(entity, (Id)ent)); return; }
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
            if (_deferDepth > 0) { _commands.Add(AddIdCmd.Rent(entity, pair)); return; }
            EnsureHasIdLocked(entity, pair);
        }
    }

    // Type-erased add — relation/target can be any entity.
    public void Add(EntityId entity, EntityId relation, EntityId target)
    {
        lock (_lock)
        {
            var pair = Id.MakePair(relation, target);
            if (_deferDepth > 0) { _commands.Add(AddIdCmd.Rent(entity, pair)); return; }
            EnsureHasIdLocked(entity, pair);
        }
    }

    public void Add(EntityId entity, Id componentId)
    {
        lock (_lock)
        {
            if (_deferDepth > 0) { _commands.Add(AddIdCmd.Rent(entity, componentId)); return; }
            EnsureHasIdLocked(entity, componentId);
        }
    }

    public void Remove<T>(EntityId entity) where T : struct
    {
        lock (_lock)
        {
            if (!_typeToEntity.TryGetValue(typeof(T), out var compEnt)) return;
            if (_deferDepth > 0) { _commands.Add(RemoveIdCmd.Rent(entity, (Id)compEnt)); return; }
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
            if (_deferDepth > 0) { _commands.Add(RemoveIdCmd.Rent(entity, pair)); return; }
            RemoveIdLocked(entity, pair);
        }
    }

    public void Remove(EntityId entity, Id componentId)
    {
        lock (_lock)
        {
            if (_deferDepth > 0) { _commands.Add(RemoveIdCmd.Rent(entity, componentId)); return; }
            RemoveIdLocked(entity, componentId);
        }
    }

    // Toggle helpers — convenience over Add/Remove. NOT a true bitset: each
    // call still triggers archetype migration, so toggles fragment archetypes.
    // True non-fragmenting bitset storage NYI.
    public void Toggle<T>(EntityId entity) where T : struct
    {
        if (Has<T>(entity)) Remove<T>(entity);
        else Add<T>(entity);
    }
    public void SetEnabled<T>(EntityId entity, bool enabled) where T : struct
    {
        if (enabled) { if (!Has<T>(entity)) Add<T>(entity); }
        else { if (Has<T>(entity)) Remove<T>(entity); }
    }
}
