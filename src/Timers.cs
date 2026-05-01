namespace Flecs;

// ============================================================================
// Tick sources — drive systems off a timer or rate-divided source instead of
// every Progress call.
//
//   var slow = world.Timer(0.5f);              // ticks every 0.5s
//   var rare = world.Rate(slow, 4);            // ticks every 4th slow-tick
//   world.System("AI", world.OnUpdate, action).SetTickSource(rare);
//
// Timer/RateFilter live as data on regular entities (one of each per source);
// TickSource carries the per-frame state. Progress evaluates timers first,
// then rate filters in registration order so chains resolve in one pass.
// Mirrors flecs ecs_timer_t / ecs_rate_filter_t.
// ============================================================================

public record struct TickSource(bool Tick);

public record struct Timer(float Period, float Accumulated);

public record struct RateFilter(uint Rate, uint Counter, uint SourceId);
