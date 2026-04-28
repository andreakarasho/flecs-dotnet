namespace Flecs;

// ============================================================================
// Modules — packaged registration. Implement IModule.Build to register
// components, systems, observers, etc. Imported once per World; reimport is a
// no-op. Mirrors flecs ECS_IMPORT.
// ============================================================================
public interface IModule { void Build(World world); }

// ============================================================================
// Systems — System = (name, phase, action). Sequential dispatch via
// World.Progress(dt). No threading or dependency graph yet. Mirrors
// flecs ecs_system_desc_t (subset).
// ============================================================================
public delegate void SystemAction(World world, float deltaTime);

public sealed class SystemHandle
{
    public string Name { get; }
    public EntityId Phase { get; internal set; }
    public SystemAction Action { get; internal set; }
    public bool Enabled { get; set; } = true;
    internal SystemHandle(string name, EntityId phase, SystemAction action)
    { Name = name; Phase = phase; Action = action; }
}
