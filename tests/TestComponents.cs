namespace Flecs.Tests;

// Shared test component / tag / pair-target types.

public record struct Position(float X, float Y);
public record struct Velocity(float Dx, float Dy);
public record struct Health(int Value);
public record struct Score(int Value);
public record struct Mana(int Value);
public record struct Damage(int Value);
public record struct Defense(int Value);

public struct TagA { }
public struct TagB { }
public struct TagC { }
public struct Boss { }
public struct Frozen { }
public struct Disabled_ { } // suffix to avoid clash with World.Disabled reserved

public struct Likes { }
public struct Hates { }
public struct ChildOfRel { }
public struct Apple { }
public struct Orange { }
public struct Pear { }
