using System;
using System.Collections.Generic;

namespace Flecs;

// ============================================================================
// Entity — fluent (World, EntityId) wrapper. Forwards to World methods so
// callers can chain ops directly: w.Entity().Set(new Position()).Add<Tag>().
// EntityId stays the bare handle for storage / queries / pair construction;
// Entity is opt-in sugar with an implicit conversion back to EntityId.
// Mirrors flecs C++ entity vs entity_t.
// ============================================================================
public readonly struct Entity : IEquatable<Entity>
{
    public readonly World World;
    public readonly EntityId Id;

    internal Entity(World world, EntityId id) { World = world; Id = id; }

    public static implicit operator EntityId(Entity e) => e.Id;
    public static implicit operator Flecs.Id(Entity e) => (Flecs.Id)e.Id;

    public bool IsValid => Id.IsValid;
    public bool IsAlive => World.IsAlive(Id);

    // ---- Components ----
    public Entity Add<T>() where T : struct { World.Add<T>(Id); return this; }
    public Entity Add(Flecs.Id componentId) { World.Add(Id, componentId); return this; }
    public Entity Add(EntityId relation, EntityId target) { World.Add(Id, relation, target); return this; }
    public Entity Add<TR, TT>() where TR : struct where TT : struct { World.Add<TR, TT>(Id); return this; }
    public Entity Remove<T>() where T : struct { World.Remove<T>(Id); return this; }
    public Entity Remove(Flecs.Id componentId) { World.Remove(Id, componentId); return this; }
    public Entity Remove<TR, TT>() where TR : struct where TT : struct { World.Remove<TR, TT>(Id); return this; }
    public Entity Set<T>(T value) where T : struct { World.Set(Id, value); return this; }
    public ref T Get<T>() where T : struct => ref World.Get<T>(Id);
    public ref T TryGetRef<T>() where T : struct => ref World.TryGetRef<T>(Id);
    public bool TryGet<T>(out T value) where T : struct => World.TryGetComponent(Id, out value);
    public bool Has<T>() where T : struct => World.Has<T>(Id);
    public bool Has(Flecs.Id componentId) => World.Has(Id, componentId);
    public bool Has<TR, TT>() where TR : struct where TT : struct => World.Has<TR, TT>(Id);
    public bool Owns<T>() where T : struct => World.Owns<T>(Id);
    public bool Owns<TR, TT>() where TR : struct where TT : struct => World.Owns<TR, TT>(Id);

    // ---- Toggle / enable ----
    public Entity Toggle<T>() where T : struct { World.Toggle<T>(Id); return this; }
    public Entity SetEnabled<T>(bool enabled) where T : struct { World.SetEnabled<T>(Id, enabled); return this; }
    public bool IsEnabled<T>() where T : struct => World.IsEnabled<T>(Id);
    public Entity Disable() { World.Disable(Id); return this; }
    public Entity Enable() { World.Enable(Id); return this; }
    public bool IsEnabled() => World.IsEnabled(Id);

    // ---- Lifecycle ----
    public void Destroy() => World.Delete(Id);
    public Entity Clone() => new(World, World.Clone(Id));

    // ---- Naming ----
    public Entity SetName(string name) { World.SetName(Id, name); return this; }
    public string? Name => World.GetName(Id);

    // ---- Hierarchy ----
    public Entity SetParent(EntityId parent) { World.SetParent(Id, parent); return this; }
    public Entity ClearParent() { World.ClearParent(Id); return this; }
    public Entity Parent => new(World, World.GetParent(Id));
    public bool HasParent(EntityId parent) => World.HasParent(Id, parent);
    public IEnumerable<EntityId> Children() => World.Children(Id);
    // Inverse of SetParent — make 'child' a child of this entity.
    public Entity AddChild(EntityId child) { World.SetParent(child, Id); return this; }

    // ---- Inheritance ----
    public Entity SetIsA(EntityId prefab) { World.SetIsA(Id, prefab); return this; }
    public bool HasIsA(EntityId prefab) => World.HasIsA(Id, prefab);
    public ref T GetInherited<T>() where T : struct => ref World.GetInherited<T>(Id);
    public bool TryGetInherited<T>(out T value) where T : struct => World.TryGetInherited<T>(Id, out value);
    public bool HasInherited<T>() where T : struct => World.HasInherited<T>(Id);

    public bool Equals(Entity other) => Id.Equals(other.Id) && ReferenceEquals(World, other.World);
    public override bool Equals(object? obj) => obj is Entity e && Equals(e);
    public override int GetHashCode() => Id.GetHashCode();
    public override string ToString() => Name ?? $"Entity#{Id.Id}";
}
