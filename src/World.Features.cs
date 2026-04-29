using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Flecs;

// World partial — singletons, systems, modules, bulk utilities, hierarchy
// (ChildOf), naming + path lookup.
public sealed partial class World
{
    // ========== Singletons ==========
    // Flecs pattern: component-on-self. T's component-entity holds an
    // instance of T. world-scoped global. Mirrors world.set<T>() / get<T>().

    public void SetSingleton<T>(T value) where T : struct
    {
        var ent = Component<T>();
        Set(ent, value);
    }

    public ref T GetSingleton<T>() where T : struct
    {
        var ent = Component<T>();
        return ref Get<T>(ent);
    }

    public bool HasSingleton<T>() where T : struct
    {
        if (!_typeToEntity.TryGetValue(typeof(T), out var ent)) return false;
        return Has<T>(ent);
    }

    public void RemoveSingleton<T>() where T : struct
    {
        if (!_typeToEntity.TryGetValue(typeof(T), out var ent)) return;
        Remove<T>(ent);
    }

    // ========== Systems / Progress ==========

    // Register a system. Action runs once per Progress() in the given phase.
    // Phase must be one of the builtin phase entities (OnLoad..OnStore) for
    // Progress to schedule it.
    public SystemHandle System(string name, EntityId phase, SystemAction action)
    {
        var h = new SystemHandle(name, phase, action);
        lock (_lock) { _systems.Add(h); _pipelineDirty = true; }
        return h;
    }

    // Typed query-system sugar — Each callback wrapped in a system. Auto-
    // populates the handle's r/w sets from the query (every term defaults to
    // write; mark explicit reads via the .Read<T>() builder before calling
    // System<...>). Sets ParallelSafe = true since the action is a pure
    // Each invocation with no untracked side effects.
    public SystemHandle System<T1>(string name, EntityId phase, EachAction<T1> each)
        where T1 : struct
    {
        var q = Query<T1>();
        var h = System(name, phase, (w, dt) => q.Each(each));
        return AttachQueryAccess(h, q);
    }

    public SystemHandle System<T1, T2>(string name, EntityId phase, EachAction<T1, T2> each)
        where T1 : struct where T2 : struct
    {
        var q = Query<T1, T2>();
        var h = System(name, phase, (w, dt) => q.Each(each));
        return AttachQueryAccess(h, q);
    }

    public SystemHandle System<T1, T2, T3>(string name, EntityId phase, EachAction<T1, T2, T3> each)
        where T1 : struct where T2 : struct where T3 : struct
    {
        var q = Query<T1, T2, T3>();
        var h = System(name, phase, (w, dt) => q.Each(each));
        return AttachQueryAccess(h, q);
    }

    // Pre-built query overload — caller controls .Read<T>() before passing in.
    public SystemHandle System<T1>(string name, EntityId phase, Query<T1> q, EachAction<T1> each)
        where T1 : struct
    {
        var h = System(name, phase, (w, dt) => q.Each(each));
        return AttachQueryAccess(h, q);
    }
    public SystemHandle System<T1, T2>(string name, EntityId phase, Query<T1, T2> q, EachAction<T1, T2> each)
        where T1 : struct where T2 : struct
    {
        var h = System(name, phase, (w, dt) => q.Each(each));
        return AttachQueryAccess(h, q);
    }
    public SystemHandle System<T1, T2, T3>(string name, EntityId phase, Query<T1, T2, T3> q, EachAction<T1, T2, T3> each)
        where T1 : struct where T2 : struct where T3 : struct
    {
        var h = System(name, phase, (w, dt) => q.Each(each));
        return AttachQueryAccess(h, q);
    }

    private static SystemHandle AttachQueryAccess(SystemHandle h, QueryBase q)
    {
        h.SetReads(q.ReadIds);
        h.SetWrites(q.WriteIds);
        h.SetParallelSafe(true);
        return h;
    }

    // Reusable buffer for Progress snapshot — avoids per-frame List alloc.
    private readonly List<SystemHandle> _systemsSnapshot = new();
    // Worker pool for parallel wave execution. _workerCount == 0 → sequential
    // (current default). UseWorkers spins up Stage objects + ThreadPool task
    // dispatch. Mirrors flecs ecs_set_threads.
    private int _workerCount;
    private Stage[]? _stages;
    // Cached per-phase wave grouping. Rebuilt on demand when _pipelineDirty
    // is set (System() registration, Enable/Disable can leave this stale —
    // callers that toggle Enabled mid-Progress must accept the prior wave
    // structure for that frame).
    private readonly Dictionary<EntityId, List<List<SystemHandle>>> _phaseWaves = new();
    private bool _pipelineDirty = true;

    // Build waves per phase via greedy r/w-conflict packing. Systems with
    // ParallelSafe == false serialize (one per wave). ParallelSafe systems
    // pack into the earliest wave whose existing members do not conflict.
    // Mirrors flecs pipeline_build_dependency_graph.
    private void RebuildPipelineLocked()
    {
        _phaseWaves.Clear();
        for (int p = 0; p < _phaseOrder.Length; p++)
        {
            var phase = _phaseOrder[p];
            List<List<SystemHandle>>? waves = null;
            for (int i = 0; i < _systems.Count; i++)
            {
                var s = _systems[i];
                if (s.Phase != phase) continue;
                waves ??= new List<List<SystemHandle>>();
                if (!s.ParallelSafe)
                {
                    waves.Add(new List<SystemHandle> { s });
                    continue;
                }
                bool placed = false;
                for (int w = 0; w < waves.Count; w++)
                {
                    var wave = waves[w];
                    bool conflict = false;
                    for (int j = 0; j < wave.Count; j++)
                    {
                        var other = wave[j];
                        if (!other.ParallelSafe || s.ConflictsWith(other))
                        { conflict = true; break; }
                    }
                    if (!conflict) { wave.Add(s); placed = true; break; }
                }
                if (!placed) waves.Add(new List<SystemHandle> { s });
            }
            if (waves != null) _phaseWaves[phase] = waves;
        }
        _pipelineDirty = false;
    }

    // Inspect computed waves for the given phase. Rebuilds if dirty. Returns
    // empty when phase has no systems. Read-only snapshot — caller must not
    // mutate the lists.
    public IReadOnlyList<IReadOnlyList<SystemHandle>> GetPhaseWaves(EntityId phase)
    {
        lock (_lock)
        {
            if (_pipelineDirty) RebuildPipelineLocked();
            return _phaseWaves.TryGetValue(phase, out var w)
                ? (IReadOnlyList<IReadOnlyList<SystemHandle>>)w
                : Array.Empty<IReadOnlyList<SystemHandle>>();
        }
    }

    // Run all enabled systems in builtin-phase order. Within a phase, systems
    // are grouped into waves by r/w conflict (pipeline DAG). Waves run in
    // sequence; systems within a wave run sequentially today (parallelization
    // gated on UseWorkers).
    public void Progress(float deltaTime)
    {
        lock (_lock)
        {
            if (_pipelineDirty) RebuildPipelineLocked();
        }
        for (int p = 0; p < _phaseOrder.Length; p++)
        {
            var phase = _phaseOrder[p];
            if (!_phaseWaves.TryGetValue(phase, out var waves)) continue;
            for (int w = 0; w < waves.Count; w++)
            {
                var wave = waves[w];
                RunWave(wave, deltaTime);
            }
        }
    }

    // Run one wave. Sequential today; UseWorkers swaps to parallel exec.
    private void RunWave(List<SystemHandle> wave, float deltaTime)
    {
        if (_workerCount > 0 && wave.Count > 1)
        {
            RunWaveParallel(wave, deltaTime);
            return;
        }
        for (int i = 0; i < wave.Count; i++)
        {
            var s = wave[i];
            if (!s.Enabled) continue;
            s.Action(this, deltaTime);
        }
    }

    // Configure worker pool size. 0 → sequential (default). N > 0 → spawn N
    // Stage instances; parallel waves dispatch via ThreadPool.QueueUserWorkItem
    // and barrier on completion. Stages are reused across Progress calls.
    public void UseWorkers(int count)
    {
        if (count < 0) ThrowHelper.NegativeCount(nameof(count));
        lock (_lock)
        {
            _workerCount = count;
            if (count == 0) { _stages = null; return; }
            _stages = new Stage[count];
            for (int i = 0; i < count; i++) _stages[i] = new Stage(this, i);
        }
    }

    // Parallel wave dispatch. One task per system; each task runs its action
    // with a thread-local Stage active so mutations queue per-stage rather
    // than racing on the world. After all tasks barrier, stages flush in
    // registration order to preserve semantics.
    private void RunWaveParallel(List<SystemHandle> wave, float deltaTime)
    {
        var stages = _stages!;
        // Enter readonly window for the whole wave so reads are coherent and
        // any unstaged-mutation attempts go through the world defer queue.
        using var _ = Readonly();
        // Distribute systems across stages round-robin. More systems than
        // stages → some stages run multiple sequentially.
        int n = wave.Count;
        var tasks = new Task[n];
        for (int i = 0; i < n; i++)
        {
            var s = wave[i];
            var stage = stages[i % stages.Length];
            int idx = i;
            tasks[idx] = Task.Run(() =>
            {
                if (!s.Enabled) return;
                Stage.SetCurrent(stage);
                try { s.Action(this, deltaTime); }
                finally { Stage.ClearCurrent(); }
            });
        }
        Task.WaitAll(tasks);
        // Merge stages in registration order — preserves command sequencing
        // when multiple systems on the same stage queued mutations.
        for (int i = 0; i < stages.Length; i++) stages[i].Flush();
    }

    // ========== Modules ==========

    // Import a module. Idempotent — TModule.Build runs at most once per world.
    // Creates a module entity (named after TModule) and scopes Build's
    // registrations as its children. Components/entities created inside Build
    // become reachable via path lookup, e.g. world.Lookup("MyModule.Position").
    public void Import<TModule>() where TModule : IModule, new()
    {
        lock (_lock) { if (!_imported.Add(typeof(TModule))) return; }
        var moduleEnt = CreateEntity();
        SetName(moduleEnt, typeof(TModule).Name);
        using (WithScope(moduleEnt))
        {
            new TModule().Build(this);
        }
    }

    // Push a scope. New entities (CreateEntity, Component<T>, Tag<T>, etc.)
    // created until Dispose() get (ChildOf, scope) auto-added.
    public ScopeHandle WithScope(EntityId scope)
    {
        EntityId prev;
        lock (_lock)
        {
            prev = _currentScope;
            _currentScope = scope;
        }
        return new ScopeHandle(this, prev);
    }

    internal void RestoreScope(EntityId prev)
    {
        lock (_lock) { _currentScope = prev; }
    }

    // ========== Bulk / utility ==========

    // BulkNew<T>(count) — create count entities, each in T's archetype (so
    // Has<T> immediately true). Faster than CreateEntity+Set loop: single
    // archetype migration per entity, no command queue overhead. Returns the
    // entity handles. Hooks fire per entity.
    public EntityId[] BulkNew<T>(int count) where T : struct
    {
        if (count < 0) ThrowHelper.NegativeCount(nameof(count));
        var result = new EntityId[count];
        if (count == 0) return result;
        lock (_lock)
        {
            var compEnt = GetOrRegisterComponentLocked<T>();
            var compId = (Id)compEnt;
            var dst = GetOrCreateTable(new[] { compId });
            for (int i = 0; i < count; i++)
            {
                var e = CreateEntityCore();
                ref var rec = ref GetSlot(e.Id);
                MoveEntity(e, ref rec, _rootTable, dst);
                int idx = dst.IndexOf(compId);
                var col = dst.Columns[idx];
                if (col != null)
                {
                    col.InvokeCtor(this, e, rec.Row);
                    col.InvokeOnAdd(this, e, rec.Row);
                }
                GetIdHooks(compId)?.OnAdd?.Invoke(this, e);
                DispatchMultiObsLocked(Event.OnAdd, e, (Id)compId);
                result[i] = e;
            }
        }
        return result;
    }

    // Count entities holding component / tag T across all tables. O(tables).
    public int Count<T>() where T : struct
    {
        if (!_typeToEntity.TryGetValue(typeof(T), out var ent)) return 0;
        return Count((Id)ent);
    }

    public int Count(Id id)
    {
        int total = 0;
        lock (_lock)
        {
            for (int i = 1; i < _tablesById.Count; i++)
            {
                var t = _tablesById[i];
                if (t != null && t.Has(id)) total += t.Count;
            }
        }
        return total;
    }

    // Clone entity: new entity with the same archetype and component values.
    // ChildOf relations included (clone has same parent). For deep clone of
    // children, recurse with Children().
    public EntityId Clone(EntityId source)
    {
        if (!IsAlive(source))
            ThrowHelper.CannotCloneDeadEntity();
        lock (_lock)
        {
            var dst = CreateEntityCore();
            ref var srcRec = ref GetSlot(source.Id);
            var srcTable = _tablesById[srcRec.TableId]!;
            if (srcTable == _rootTable) return dst;

            // Move dst into srcTable (default-init across all cols).
            ref var dstRec = ref GetSlot(dst.Id);
            MoveEntity(dst, ref dstRec, _rootTable, srcTable);
            // Now copy each column's source row data into dst row.
            int sRow = srcRec.Row;
            int dRow = dstRec.Row;
            for (int i = 0; i < srcTable.Columns.Length; i++)
            {
                var col = srcTable.Columns[i];
                if (col == null) continue;
                col.CopyTo(this, dst, sRow, col, dRow);
                // Fire OnSet so observers see the cloned value.
                col.InvokeOnSet(this, dst, dRow);
                var compIdAtI = srcTable.ComponentIds[i];
                GetIdHooks(compIdAtI)?.OnSet?.Invoke(this, dst);
                DispatchMultiObsLocked(Event.OnSet, dst, compIdAtI);
            }
            return dst;
        }
    }

    // Disable / enable entity via Disabled tag. Queries do NOT auto-skip;
    // pair with .Without(world.Disabled).
    public void Disable(EntityId entity) => Add(entity, (Id)Disabled);
    public void Enable(EntityId entity) => Remove(entity, (Id)Disabled);
    public bool IsEnabled(EntityId entity) => !Has(entity, (Id)Disabled);

    // ========== Naming / lookup ==========

    // Set entity's name. Stored as EntityName component.
    public void SetName(EntityId entity, string name)
        => Set(entity, new EntityName(name));

    // Read entity's name. Returns null if no EntityName component.
    public string? GetName(EntityId entity)
    {
        if (!Has<EntityName>(entity)) return null;
        return Get<EntityName>(entity).Value;
    }

    // Lookup entity by name or dotted path "a.b.c". Each segment matched by
    // EntityName under appropriate parent (root for first segment, ChildOf
    // parent for subsequent). Returns default on miss. O(n) scan.
    public EntityId Lookup(string path)
    {
        if (string.IsNullOrEmpty(path)) return default;
        var parts = path.Split('.');
        EntityId parent = default; // default = no parent (root scope)
        for (int i = 0; i < parts.Length; i++)
        {
            var seg = parts[i];
            EntityId found = FindNamedChild(parent, seg);
            if (!found.IsValid) return default;
            parent = found;
        }
        return parent;
    }

    // Find entity with EntityName == name and parent == parent. parent default
    // means "no ChildOf relation".
    private EntityId FindNamedChild(EntityId parent, string name)
    {
        if (!_typeToEntity.TryGetValue(typeof(EntityName), out var nameComp))
            return default;
        var nameId = (Id)nameComp;
        uint chOfId = ChildOf.Id;
        uint parentId = parent.Id;
        lock (_lock)
        {
            for (int ti = 1; ti < _tablesById.Count; ti++)
            {
                var t = _tablesById[ti];
                if (t == null || t.Count == 0 || !t.Has(nameId)) continue;
                // Parent gate: when parentId==0, table must NOT have any
                // ChildOf pair. Otherwise table must have (ChildOf, parent).
                bool parentMatch;
                if (parentId == 0)
                {
                    parentMatch = true;
                    for (int k = 0; k < t.ComponentIds.Length; k++)
                    {
                        var id = t.ComponentIds[k];
                        if (id.IsPair && id.Relation == chOfId) { parentMatch = false; break; }
                    }
                }
                else
                {
                    parentMatch = t.Has(Id.MakePair(ChildOf, parent));
                }
                if (!parentMatch) continue;
                var col = (Column<EntityName>)t.Columns[t.IndexOf(nameId)]!;
                var span = col.AsSpan();
                for (int r = 0; r < span.Length; r++)
                {
                    if (span[r].Value == name) return t.Entities[r];
                }
            }
        }
        return default;
    }

    // ========== Component traits ==========
    //
    // Final     — entity cannot be used as IsA target (no inheritance from it).
    // Exclusive — relation enforces single (rel, *) per entity. Adding a new
    //             pair removes any existing pair with the same relation.
    //
    // ChildOf is preconfigured Exclusive. IsA is NOT exclusive (multi-parent
    // prefab inheritance allowed).

    public void MarkFinal(EntityId entity)
    {
        lock (_lock)
        {
            _finalIds.Add(entity.Id);
            // Also add the Final tag so users can discover via Has<>.
            EnsureHasIdLocked(entity, (Id)Final);
        }
    }

    public void UnmarkFinal(EntityId entity)
    {
        lock (_lock)
        {
            _finalIds.Remove(entity.Id);
            RemoveIdLocked(entity, (Id)Final);
        }
    }

    public bool IsFinal(EntityId entity) => _finalIds.Contains(entity.Id);

    public void MarkExclusive(EntityId relation)
    {
        lock (_lock)
        {
            _exclusiveRelIds.Add(relation.Id);
            EnsureHasIdLocked(relation, (Id)Exclusive);
        }
    }

    public void UnmarkExclusive(EntityId relation)
    {
        lock (_lock)
        {
            _exclusiveRelIds.Remove(relation.Id);
            RemoveIdLocked(relation, (Id)Exclusive);
        }
    }

    public bool IsExclusive(EntityId relation) => _exclusiveRelIds.Contains(relation.Id);

    // Acyclic — relation cannot form cycles. Enforced on Add (throws).
    public void MarkAcyclic(EntityId relation)
    {
        lock (_lock)
        {
            _acyclicRelIds.Add(relation.Id);
            EnsureHasIdLocked(relation, (Id)Acyclic);
        }
    }
    public void UnmarkAcyclic(EntityId relation)
    {
        lock (_lock)
        {
            _acyclicRelIds.Remove(relation.Id);
            RemoveIdLocked(relation, (Id)Acyclic);
        }
    }
    public bool IsAcyclic(EntityId relation) => _acyclicRelIds.Contains(relation.Id);

    // Reflexive — relation is true for self. Used by HasReflexive helper;
    // does not affect physical storage.
    public void MarkReflexive(EntityId relation)
    {
        lock (_lock)
        {
            _reflexiveRelIds.Add(relation.Id);
            EnsureHasIdLocked(relation, (Id)Reflexive);
        }
    }
    public void UnmarkReflexive(EntityId relation)
    {
        lock (_lock)
        {
            _reflexiveRelIds.Remove(relation.Id);
            RemoveIdLocked(relation, (Id)Reflexive);
        }
    }
    public bool IsReflexive(EntityId relation) => _reflexiveRelIds.Contains(relation.Id);

    // Symmetric — Add(A, R, B) auto-adds (R, A) on B; Remove mirrors.
    public void MarkSymmetric(EntityId relation)
    {
        lock (_lock)
        {
            _symmetricRelIds.Add(relation.Id);
            EnsureHasIdLocked(relation, (Id)Symmetric);
        }
    }
    public void UnmarkSymmetric(EntityId relation)
    {
        lock (_lock)
        {
            _symmetricRelIds.Remove(relation.Id);
            RemoveIdLocked(relation, (Id)Symmetric);
        }
    }
    public bool IsSymmetric(EntityId relation) => _symmetricRelIds.Contains(relation.Id);

    // Transitive — chain query helper. (R, B) on A and (R, C) on B → A has
    // transitive (R, C). Use HasTransitive to check.
    public void MarkTransitive(EntityId relation)
    {
        lock (_lock)
        {
            _transitiveRelIds.Add(relation.Id);
            EnsureHasIdLocked(relation, (Id)Transitive);
        }
    }
    public void UnmarkTransitive(EntityId relation)
    {
        lock (_lock)
        {
            _transitiveRelIds.Remove(relation.Id);
            RemoveIdLocked(relation, (Id)Transitive);
        }
    }
    public bool IsTransitive(EntityId relation) => _transitiveRelIds.Contains(relation.Id);

    // Reflexive-aware Has. If R is Reflexive and entity == target, returns
    // true even without explicit pair.
    public bool HasReflexive(EntityId entity, EntityId relation, EntityId target)
    {
        if (_reflexiveRelIds.Contains(relation.Id)
            && entity.Id == target.Id && entity.Generation == target.Generation
            && IsAlive(entity))
            return true;
        return Has(entity, Id.MakePair(relation, target));
    }

    // Transitive walk. Returns true if entity reaches target via any chain
    // of (relation, *) pairs. Direct (relation, target) also returns true.
    // Reflexive: returns true for self if relation Reflexive.
    public bool HasTransitive(EntityId entity, EntityId relation, EntityId target)
    {
        if (HasReflexive(entity, relation, target)) return true;
        if (!IsAlive(entity)) return false;
        lock (_lock)
        {
            return RelChainReachesLocked(entity.Id, target.Id, relation.Id);
        }
    }

    public bool HasTransitive<TR>(EntityId entity, EntityId target) where TR : struct
    {
        if (!_typeToEntity.TryGetValue(typeof(TR), out var rel)) return false;
        return HasTransitive(entity, rel, target);
    }

    // Inheritable / DontInherit — gates IsA propagation. Default is
    // inheritable (absent from set). Mark DontInherit on a component-entity
    // to prevent IsA chain walk from finding it on ancestors.
    public void MarkInheritable(EntityId id)
    {
        lock (_lock)
        {
            _dontInheritIds.Remove(id.Id);
            // Tag the component entity with Inheritable for discoverability.
            EnsureHasIdLocked(id, (Id)Inheritable);
            // Drop DontInherit tag if present.
            if (Has(id, (Id)DontInherit)) RemoveIdLocked(id, (Id)DontInherit);
        }
    }
    public void MarkDontInherit(EntityId id)
    {
        lock (_lock)
        {
            _dontInheritIds.Add(id.Id);
            EnsureHasIdLocked(id, (Id)DontInherit);
            if (Has(id, (Id)Inheritable)) RemoveIdLocked(id, (Id)Inheritable);
        }
    }
    public bool IsInheritable(EntityId id) => !_dontInheritIds.Contains(id.Id);
    public bool IsDontInherit(EntityId id) => _dontInheritIds.Contains(id.Id);

    // Traversable — relation eligible for query traversal sources. Passive
    // metadata for now. ChildOf and IsA preconfigured Traversable.
    public void MarkTraversable(EntityId relation)
    {
        lock (_lock)
        {
            _traversableRelIds.Add(relation.Id);
            EnsureHasIdLocked(relation, (Id)Traversable);
        }
    }
    public void UnmarkTraversable(EntityId relation)
    {
        lock (_lock)
        {
            _traversableRelIds.Remove(relation.Id);
            RemoveIdLocked(relation, (Id)Traversable);
        }
    }
    public bool IsTraversable(EntityId relation) => _traversableRelIds.Contains(relation.Id);

    // CanToggle — opt component (or relation) into non-fragmenting toggle.
    // Tables containing the id allocate parallel Bitset columns. Bits default
    // to enabled (true) on row creation. Toggle/SetEnabled flip bits in place
    // — no archetype migration. Iteration honors bits via row skip.
    //
    // Retroactive: existing tables containing the id get bitsets allocated and
    // populated with all-enabled bits matching current row count.
    public void MarkCanToggle<T>() where T : struct
    {
        lock (_lock)
        {
            var ent = GetOrRegisterAnyLocked<T>();
            MarkCanToggleLocked(ent);
        }
    }

    public void MarkCanToggle(EntityId id)
    {
        lock (_lock) { MarkCanToggleLocked(id); }
    }

    private void MarkCanToggleLocked(EntityId id)
    {
        if (!_canToggleIds.Add(id.Id)) return;
        EnsureHasIdLocked(id, (Id)CanToggle);
        // Retroactively allocate bitsets in existing tables that hold this id
        // (or any pair where this id is the relation). Existing rows: enabled.
        for (int ti = 1; ti < _tablesById.Count; ti++)
        {
            var t = _tablesById[ti];
            if (t == null) continue;
            for (int i = 0; i < t.ComponentIds.Length; i++)
            {
                var cid = t.ComponentIds[i];
                uint key = cid.IsPair ? cid.Relation : cid.Component;
                if (key != id.Id) continue;
                if (t.Bits[i] != null) continue;
                var bs = new Bitset();
                for (int r = 0; r < t.Count; r++) bs.Add(true);
                t.Bits[i] = bs;
            }
        }
        // Refresh HasAnyBitset cache by recreating tables — but readonly field;
        // instead, rely on slot scan in Table loops. The Add path above leaves
        // HasAnyBitset stale (false) for tables that previously had no bits,
        // so we need to flip it. Walk tables and update via internal setter.
        for (int ti = 1; ti < _tablesById.Count; ti++)
        {
            var t = _tablesById[ti];
            if (t == null) continue;
            t.RefreshHasAnyBitset();
        }
    }

    public bool IsCanToggle(EntityId id) => _canToggleIds.Contains(id.Id);
    public bool IsCanToggle<T>() where T : struct
    {
        if (!_typeToEntity.TryGetValue(typeof(T), out var ent)) return false;
        return _canToggleIds.Contains(ent.Id);
    }

    // True if the id (component or pair) blocks IsA propagation. For pairs,
    // checks the relation entity.
    private bool IsIdDontInherit(Id id)
        => _dontInheritIds.Contains(id.IsPair ? id.Relation : id.Component);

    // ========== Inheritance (IsA) ==========
    //
    // (IsA, prefab) makes 'entity' inherit from 'prefab'. Inherited component
    // lookup walks the chain breadth-first; first table holding the id wins.
    // Multi-IsA allowed (multi-inheritance). Cycles guarded by visited set.
    //
    // Mutability: GetInherited<T> returns a ref into the ancestor's column —
    // mutating it changes shared state. To "override" on the descendant, use
    // Set<T> which adds a private direct copy.
    //
    // Direct accessors (Has<T> / Get<T>) stay literal — they do NOT
    // walk IsA. Use the *Inherited variants to opt in.

    public void SetIsA(EntityId entity, EntityId prefab)
        => Add(entity, IsA, prefab);

    public bool HasIsA(EntityId entity, EntityId prefab)
        => Has(entity, Id.MakePair(IsA, prefab));

    // Returns ref to T's value on entity itself or first IsA ancestor holding it.
    // Throws if not found anywhere in the chain.
    public ref T GetInherited<T>(EntityId entity) where T : struct
    {
        if (!IsAlive(entity)) ThrowHelper.EntityDead();
        if (!_typeToEntity.TryGetValue(typeof(T), out var compEnt)) ThrowHelper.ComponentNotRegistered(typeof(T));
        var compId = (Id)compEnt;
        if (!_componentInfo.ContainsKey(compId)) ThrowHelper.IsTagNotComponent(typeof(T));

        var (found, table, row) = FindInIsAChain(entity, compId);
        if (!found) ThrowHelper.NotFoundInIsAChain(typeof(T), entity.Id);
        return ref ((Column<T>)table!.Columns[table.IndexOf(compId)]!).GetRef(row);
    }

    // Soft variant. Returns false if not found (no throw). value is by-value
    // copy at call time — does not track ancestor mutations.
    public bool TryGetInherited<T>(EntityId entity, out T value) where T : struct
    {
        if (!IsAlive(entity)) { value = default; return false; }
        if (!_typeToEntity.TryGetValue(typeof(T), out var compEnt)) { value = default; return false; }
        var compId = (Id)compEnt;
        if (!_componentInfo.ContainsKey(compId)) { value = default; return false; }
        var (found, table, row) = FindInIsAChain(entity, compId);
        if (!found) { value = default; return false; }
        value = ((Column<T>)table!.Columns[table.IndexOf(compId)]!).GetRef(row);
        return true;
    }

    public bool HasInherited<T>(EntityId entity) where T : struct
    {
        if (!IsAlive(entity)) return false;
        if (!_typeToEntity.TryGetValue(typeof(T), out var compEnt)) return false;
        return HasInherited(entity, (Id)compEnt);
    }

    public bool HasInherited<TR, TT>(EntityId entity) where TR : struct where TT : struct
    {
        if (!IsAlive(entity)) return false;
        if (!_typeToEntity.TryGetValue(typeof(TR), out var rel)) return false;
        if (!_typeToEntity.TryGetValue(typeof(TT), out var tgt)) return false;
        return HasInherited(entity, Id.MakePair(rel, tgt));
    }

    public bool HasInherited(EntityId entity, Id id)
    {
        if (!IsAlive(entity)) return false;
        var (found, _, _) = FindInIsAChain(entity, id);
        return found;
    }

    // Term-source variants — walk any user-chosen relation chain (Up(rel) in
    // flecs query terms). Mirrors `Self+Up(rel)` traversal. relation must be
    // alive; behavior undefined otherwise.
    public ref T GetInheritedVia<T>(EntityId entity, EntityId relation) where T : struct
    {
        if (!IsAlive(entity)) ThrowHelper.EntityDead();
        if (!_typeToEntity.TryGetValue(typeof(T), out var compEnt)) ThrowHelper.ComponentNotRegistered(typeof(T));
        var compId = (Id)compEnt;
        if (!_componentInfo.ContainsKey(compId)) ThrowHelper.IsTagNotComponent(typeof(T));
        var (found, table, row) = FindInChain(entity, compId, relation.Id, blockable: false);
        if (!found) ThrowHelper.NotFoundInRelationChain(typeof(T), relation.Id, entity.Id);
        return ref ((Column<T>)table!.Columns[table.IndexOf(compId)]!).GetRef(row);
    }

    public bool TryGetInheritedVia<T>(EntityId entity, EntityId relation, out T value) where T : struct
    {
        if (!IsAlive(entity)) { value = default; return false; }
        if (!_typeToEntity.TryGetValue(typeof(T), out var compEnt)) { value = default; return false; }
        var compId = (Id)compEnt;
        if (!_componentInfo.ContainsKey(compId)) { value = default; return false; }
        var (found, table, row) = FindInChain(entity, compId, relation.Id, blockable: false);
        if (!found) { value = default; return false; }
        value = ((Column<T>)table!.Columns[table.IndexOf(compId)]!).GetRef(row);
        return true;
    }

    public bool HasInheritedVia<T>(EntityId entity, EntityId relation) where T : struct
    {
        if (!IsAlive(entity)) return false;
        if (!_typeToEntity.TryGetValue(typeof(T), out var compEnt)) return false;
        var (found, _, _) = FindInChain(entity, (Id)compEnt, relation.Id, blockable: false);
        return found;
    }

    public bool HasInheritedVia(EntityId entity, Id id, EntityId relation)
    {
        if (!IsAlive(entity)) return false;
        var (found, _, _) = FindInChain(entity, id, relation.Id, blockable: false);
        return found;
    }

    // BFS chain walk. Returns (found, table, row) on first hit.
    internal (bool found, Table? table, int row) FindInIsAChain(EntityId start, Id id)
        => FindInChain(start, id, IsA.Id, blockable: true);

    // Walk the first (rel, *) pair at each level. Returns the number of hops
    // from 'start' to its furthest reachable ancestor via 'relUint'. Used by
    // Cascade query ordering. Tree assumption — for DAGs picks the first
    // target. Cycle-safe via 1024 step cap.
    internal int RelationDepth(EntityId start, uint relUint)
    {
        var cur = start;
        int depth = 0;
        const int safety = 1024;
        while (depth < safety)
        {
            if (!IsAliveCore(cur)) return depth;
            ref var rec = ref GetSlot(cur.Id);
            var t = _tablesById[rec.TableId]!;
            bool advanced = false;
            for (int i = 0; i < t.ComponentIds.Length; i++)
            {
                var cid = t.ComponentIds[i];
                if (!cid.IsPair || cid.Relation != relUint) continue;
                uint tgt = cid.Target;
                if (tgt == 0) continue;
                ref var ts = ref GetSlot(tgt);
                if (!ts.Alive) continue;
                cur = new EntityId(tgt, ts.Generation);
                depth++;
                advanced = true;
                break;
            }
            if (!advanced) break;
        }
        return depth;
    }

    // Generalized chain walk via arbitrary relation. blockable=true honors
    // DontInherit (short-circuits ancestor expansion). maxDepth caps the
    // BFS — 0 = self-only, 1 = direct neighbors, -1 = unlimited. Uses
    // pooled scratch.
    internal (bool found, Table? table, int row) FindInChain(EntityId start, Id id, uint relUint, bool blockable, int maxDepth = -1)
    {
        bool block = blockable && IsIdDontInherit(id);

        var (visited, queue) = RentBfsScratchEntity();
        try
        {
            visited.Add(start.Id);
            queue.Enqueue(start);
            int depth = 0;
            while (queue.Count > 0)
            {
                int levelSize = queue.Count;
                for (int li = 0; li < levelSize; li++)
                {
                    var cur = queue.Dequeue();
                    if (!IsAlive(cur)) continue;
                    ref var rec = ref GetSlot(cur.Id);
                    var t = _tablesById[rec.TableId]!;
                    if (t.Has(id)) return (true, t, rec.Row);
                    if (block) continue;
                    if (maxDepth >= 0 && depth >= maxDepth) continue;
                    for (int i = 0; i < t.ComponentIds.Length; i++)
                    {
                        var cid = t.ComponentIds[i];
                        if (!cid.IsPair || cid.Relation != relUint) continue;
                        uint tgt = cid.Target;
                        if (tgt == 0 || !visited.Add(tgt)) continue;
                        ref var ts = ref GetSlot(tgt);
                        queue.Enqueue(new EntityId(tgt, ts.Generation));
                    }
                }
                depth++;
            }
            return (false, null, 0);
        }
        finally
        {
            ReturnBfsScratchEntity(visited, queue);
        }
    }

    // ========== Hierarchy (ChildOf) ==========

    // Set 'child's parent to 'parent'. Adds (ChildOf, parent) pair to child.
    // Existing parent (if any) is NOT removed — flecs allows multiple ChildOf
    // targets, but typical use is single. Use ClearParent first if you want
    // exclusive parenting.
    public void SetParent(EntityId child, EntityId parent)
        => Add(child, ChildOf, parent);

    // Remove all ChildOf relations from entity.
    public void ClearParent(EntityId entity)
    {
        if (!IsAlive(entity)) return;
        lock (_lock)
        {
            ref var rec = ref GetSlot(entity.Id);
            var t = _tablesById[rec.TableId]!;
            // Collect targets first — RemoveIdLocked mutates table.
            // default: lazy — no rent until first match.
            var toRemove = default(PooledList<Id>);
            try
            {
                for (int i = 0; i < t.ComponentIds.Length; i++)
                {
                    var id = t.ComponentIds[i];
                    if (id.IsPair && id.Relation == ChildOf.Id)
                        toRemove.Add(id);
                }
                var span = toRemove.AsSpan;
                for (int i = 0; i < span.Length; i++) RemoveIdLocked(entity, span[i]);
            }
            finally { toRemove.Dispose(); }
        }
    }

    // First (ChildOf, *) target. Returns default if no parent. Generation
    // recovered from current slot — if target was deleted+recycled, you may
    // get a stale handle; check IsAlive on result.
    public EntityId GetParent(EntityId entity)
    {
        if (!IsAlive(entity)) return default;
        ref var rec = ref GetSlot(entity.Id);
        var t = _tablesById[rec.TableId]!;
        for (int i = 0; i < t.ComponentIds.Length; i++)
        {
            var id = t.ComponentIds[i];
            if (id.IsPair && id.Relation == ChildOf.Id)
            {
                uint pid = id.Target;
                if (pid == 0) return default;
                ref var ps = ref GetSlot(pid);
                return new EntityId(pid, ps.Generation);
            }
        }
        return default;
    }

    public bool HasParent(EntityId entity, EntityId parent)
        => Has(entity, Id.MakePair(ChildOf, parent));

    // Walk ChildOf chain checking ancestor-of relationship.
    public bool IsAncestor(EntityId ancestor, EntityId entity)
    {
        var cur = GetParent(entity);
        while (cur.IsValid)
        {
            if (cur.Id == ancestor.Id && cur.Generation == ancestor.Generation) return true;
            cur = GetParent(cur);
        }
        return false;
    }

    // Enumerate direct children of 'parent'. Scans tables matching
    // (ChildOf, parent). O(tables) per call — fine for occasional use; for hot
    // paths cache via a Query.
    public IEnumerable<EntityId> Children(EntityId parent)
    {
        var pair = Id.MakePair(ChildOf, parent);
        // Snapshot table list to avoid mutation-during-iteration issues if
        // caller adds/removes children mid-enumeration.
        var snapshot = new List<Table>();
        lock (_lock)
        {
            for (int i = 1; i < _tablesById.Count; i++)
            {
                var t = _tablesById[i];
                if (t != null && t.Has(pair)) snapshot.Add(t);
            }
        }
        foreach (var t in snapshot)
        {
            // Take a copy to be safe — Entities mutates with structural ops.
            EntityId[] copy;
            lock (_lock) { copy = t.Entities.ToArray(); }
            foreach (var e in copy) yield return e;
        }
    }
}
