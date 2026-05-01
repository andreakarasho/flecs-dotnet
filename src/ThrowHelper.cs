using System;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Flecs;

// Centralized throw helpers. Inline `throw new X(...)` in a hot method
// blocks JIT inlining: the throw allocator + ctor expansion swells the
// IL above the inlining budget. Calling a NoInlining static helper that
// performs the throw keeps the caller small enough to inline. Every
// helper is decorated with [DoesNotReturn] so the JIT knows the call
// terminates a control path — improves dead-code elimination too.
//
// Mirrors System.ThrowHelper in the BCL (corelib uses the same pattern
// for Span<T>, List<T>, etc.).
internal static class ThrowHelper
{
    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void EntityDead()
        => throw new InvalidOperationException("Entity is dead.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ComponentNotRegistered(Type t)
        => throw new InvalidOperationException($"Component '{t.Name}' not registered.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void IsTagNotComponent(Type t)
        => throw new InvalidOperationException($"'{t.Name}' is a tag, has no data.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void EntityMissingComponent(Type t)
        => throw new InvalidOperationException($"Entity does not have component '{t.Name}'.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NotFoundInIsAChain(Type t, uint entityId)
        => throw new InvalidOperationException(
            $"Component '{t.Name}' not found on entity #{entityId} or any IsA ancestor.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NotFoundInRelationChain(Type t, uint relationId, uint entityId)
        => throw new InvalidOperationException(
            $"Component '{t.Name}' not found via #{relationId} chain from #{entityId}.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void EndDeferWithoutBegin()
        => throw new InvalidOperationException("EndDefer without matching BeginDefer.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void EndReadonlyWithoutBegin()
        => throw new InvalidOperationException("EndReadonly without matching BeginReadonly.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void StrictReadonlyMutation()
        => throw new InvalidOperationException(
            "StrictReadonly: structural mutation while world readonly. Wrap in Defer or " +
            "disable World.StrictReadonly.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void CannotCloneDeadEntity()
        => throw new InvalidOperationException("Cannot clone dead entity.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void TagAlreadyComponent(Type t)
        => throw new InvalidOperationException(
            $"Type '{t.Name}' already registered as component, not tag.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void ComponentAlreadyTag(Type t)
        => throw new InvalidOperationException(
            $"Type '{t.Name}' already registered as tag, not component.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NegativeCount(string paramName)
        => throw new ArgumentOutOfRangeException(paramName);

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void OptionalTypeMismatch(Type t, string termList)
        => throw new ArgumentException($"Optional<{t.Name}>: T must be {termList} of this query.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void NotUnionRelation(Type t)
        => throw new InvalidOperationException(
            $"Relation '{t.Name}' is not marked Union — call MarkUnion<{t.Name}>() first.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void JsonExpected(string what)
        => throw new InvalidOperationException($"JSON snapshot: expected {what}.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void IsAToFinal(uint targetId)
        => throw new InvalidOperationException($"Cannot IsA to entity #{targetId} marked Final.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void AcyclicSelfReference(uint relId, uint entityId)
        => throw new InvalidOperationException(
            $"Acyclic relation #{relId}: self-reference on #{entityId} forbidden.");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void AcyclicCycle(uint relId, uint entityId, uint targetId)
        => throw new InvalidOperationException(
            $"Acyclic relation #{relId}: cycle would form (#{entityId} → #{targetId}).");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void DeletePolicyPanic(uint deletedId, ulong idValue, uint holderId)
        => throw new InvalidOperationException(
            $"DeletePolicy.Panic: deleting #{deletedId} would orphan id {idValue} on " +
            $"#{holderId} (and possibly others).");

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void SystemCtxWrongType(Type t)
        => throw new InvalidOperationException(
            $"Iter.Ctx<{t.Name}>: ctx is null or wrong type.");
}
