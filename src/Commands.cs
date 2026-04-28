using System;
using System.Collections.Generic;

namespace Flecs;

// ============================================================================
// Commands — queued during deferred mode, replayed on flush. Generic-static
// pool (Pool<T>) yields one ThreadStatic Stack<T> per (concrete T, thread).
// Zero alloc after warmup. Mirrors ecs_cmd_t.
//
// Typed Add<T> / Remove<T> / Add<TR,TT> resolve T → Id at queue-time and share
// AddIdCmd / RemoveIdCmd — no per-T command class needed. SetCmd<T> remains
// generic because the value cannot be type-erased.
// ============================================================================
internal abstract class Command
{
    internal abstract void Apply(World w);
    internal abstract void Recycle();

    protected static T Rent<T>() where T : Command, new()
    {
        var s = Pool<T>.Stack ??= new Stack<T>();
        return s.Count > 0 ? s.Pop() : new T();
    }

    protected static void Return<T>(T cmd) where T : Command, new()
        => (Pool<T>.Stack ??= new Stack<T>()).Push(cmd);

    // Per-(concrete-type, thread) pool. Generic-static gives JIT one
    // ThreadStatic field per closed T.
    private static class Pool<T> where T : Command
    {
        [ThreadStatic] public static Stack<T>? Stack;
    }
}

internal sealed class AddIdCmd : Command
{
    public EntityId Entity;
    public Id Id;
    public static AddIdCmd Rent(EntityId e, Id id)
    {
        var c = Rent<AddIdCmd>();
        c.Entity = e; c.Id = id; return c;
    }
    internal override void Apply(World w) => w.Add(Entity, Id);
    internal override void Recycle() => Return(this);
}

internal sealed class RemoveIdCmd : Command
{
    public EntityId Entity;
    public Id Id;
    public static RemoveIdCmd Rent(EntityId e, Id id)
    {
        var c = Rent<RemoveIdCmd>();
        c.Entity = e; c.Id = id; return c;
    }
    internal override void Apply(World w) => w.Remove(Entity, Id);
    internal override void Recycle() => Return(this);
}

internal sealed class DeleteCmd : Command
{
    public EntityId Entity;
    public static DeleteCmd Rent(EntityId e)
    {
        var c = Rent<DeleteCmd>();
        c.Entity = e; return c;
    }
    internal override void Apply(World w) => w.Delete(Entity);
    internal override void Recycle() => Return(this);
}

internal sealed class SetCmd<T> : Command where T : struct
{
    public EntityId Entity;
    public T Value;
    public static SetCmd<T> Rent(EntityId e, T value)
    {
        var c = Rent<SetCmd<T>>();
        c.Entity = e; c.Value = value; return c;
    }
    internal override void Apply(World w) => w.Set(Entity, Value);
    internal override void Recycle()
    {
        Value = default; // release any managed refs in T
        Return(this);
    }
}

// ============================================================================
// DeferScope — RAII helper. `using var _ = world.Defer();`
// ============================================================================
public readonly struct DeferScope : IDisposable
{
    private readonly World _world;
    internal DeferScope(World world) { _world = world; }
    public void Dispose() => _world.EndDefer();
}

// ============================================================================
// ScopeHandle — RAII helper for WithScope. Entities created inside the using
// block auto-receive (ChildOf, scope). Mirrors flecs world.with_scope.
// ============================================================================
public readonly struct ScopeHandle : IDisposable
{
    private readonly World _world;
    private readonly EntityId _prev;
    internal ScopeHandle(World w, EntityId prev) { _world = w; _prev = prev; }
    public void Dispose() => _world.RestoreScope(_prev);
}
