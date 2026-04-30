using Xunit;

namespace Flecs.Tests;

// CanToggle — non-fragmenting bitset semantics for Toggle/SetEnabled/IsEnabled.
// When MarkCanToggle is called the toggle ops flip a bit instead of doing
// Add/Remove archetype migrations.
public class CanToggleTests
{
    [Fact]
    public void Toggle_NoArchetypeMigration_WhenCanToggle()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkCanToggle<Position>();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 2));
        int tablesBefore = w.TableCount;
        for (int i = 0; i < 10; i++)
        {
            w.Toggle<Position>(e);
        }
        Assert.Equal(tablesBefore, w.TableCount);
    }

    [Fact]
    public void SetEnabled_PreservesValueAcrossDisableEnable()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkCanToggle<Position>();
        var e = w.CreateEntity();
        w.Set(e, new Position(7, 9));
        w.SetEnabled<Position>(e, false);
        Assert.False(w.IsEnabled<Position>(e));
        // Component still owned (just disabled).
        Assert.True(w.Owns<Position>(e));
        w.SetEnabled<Position>(e, true);
        Assert.True(w.IsEnabled<Position>(e));
        Assert.Equal(new Position(7, 9), w.Get<Position>(e));
    }

    [Fact]
    public void IsEnabled_NewlyAdded_DefaultsTrue()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkCanToggle<Position>();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        Assert.True(w.IsEnabled<Position>(e));
    }

    [Fact]
    public void IsEnabled_Absent_ReturnsFalse()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkCanToggle<Position>();
        var e = w.CreateEntity();
        Assert.False(w.IsEnabled<Position>(e));
    }

    [Fact]
    public void IsCanToggle_ReportsTrait()
    {
        var w = new World();
        w.Component<Position>();
        Assert.False(w.IsCanToggle<Position>());
        w.MarkCanToggle<Position>();
        Assert.True(w.IsCanToggle<Position>());
    }

    [Fact]
    public void Query_SkipsDisabledRows()
    {
        var w = new World();
        w.Component<Position>();
        w.Component<Velocity>();
        w.MarkCanToggle<Velocity>();

        var a = w.CreateEntity(); w.Set(a, new Position(0, 0)); w.Set(a, new Velocity(1, 0));
        var b = w.CreateEntity(); w.Set(b, new Position(0, 0)); w.Set(b, new Velocity(2, 0));
        var c = w.CreateEntity(); w.Set(c, new Position(0, 0)); w.Set(c, new Velocity(3, 0));

        w.SetEnabled<Velocity>(b, false);

        int sum = 0;
        foreach (var (_, v) in w.Query<Position, Velocity>())
            sum += (int)v.Value.Dx;
        Assert.Equal(4, sum); // 1 + 3, b skipped
    }

    [Fact]
    public void Migration_PreservesDisabledBit()
    {
        var w = new World();
        w.Component<Position>();
        w.Component<Velocity>();
        w.MarkCanToggle<Position>();

        var e = w.CreateEntity();
        w.Set(e, new Position(5, 6));
        w.SetEnabled<Position>(e, false);
        Assert.False(w.IsEnabled<Position>(e));
        // Force archetype migration by adding Velocity.
        w.Set(e, new Velocity(1, 1));
        Assert.False(w.IsEnabled<Position>(e));
        // Value preserved across migration.
        Assert.Equal(new Position(5, 6), w.Get<Position>(e));
    }

    [Fact]
    public void RemoveSwapBack_KeepsBitsAlignedWithRows()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkCanToggle<Position>();
        var a = w.CreateEntity(); w.Set(a, new Position(1, 0));
        var b = w.CreateEntity(); w.Set(b, new Position(2, 0));
        var c = w.CreateEntity(); w.Set(c, new Position(3, 0));

        w.SetEnabled<Position>(a, false);
        w.SetEnabled<Position>(b, true);
        w.SetEnabled<Position>(c, false);

        // Delete b — c gets swapped into b's row. c's bit must follow.
        w.Delete(b);
        Assert.False(w.IsEnabled<Position>(a));
        Assert.False(w.IsEnabled<Position>(c));
    }

    [Fact]
    public void RetroactiveMarkCanToggle_AllocatesBitsForExistingTables()
    {
        var w = new World();
        w.Component<Position>();
        var e1 = w.CreateEntity(); w.Set(e1, new Position(1, 0));
        var e2 = w.CreateEntity(); w.Set(e2, new Position(2, 0));
        // Now opt in retroactively.
        w.MarkCanToggle<Position>();
        Assert.True(w.IsEnabled<Position>(e1));
        Assert.True(w.IsEnabled<Position>(e2));
        w.SetEnabled<Position>(e1, false);
        Assert.False(w.IsEnabled<Position>(e1));
        Assert.True(w.IsEnabled<Position>(e2));
    }

    [Fact]
    public void NonCanToggle_StillUsesAddRemove()
    {
        // Legacy semantics for non-CanToggle ids.
        var w = new World();
        var e = w.CreateEntity();
        w.SetEnabled<TagA>(e, true);
        Assert.True(w.Has<TagA>(e));
        w.SetEnabled<TagA>(e, false);
        Assert.False(w.Has<TagA>(e));
    }

    [Fact]
    public void Toggle_OnMissing_AddsThenIsEnabled()
    {
        var w = new World();
        w.Component<Position>();
        w.MarkCanToggle<Position>();
        var e = w.CreateEntity();
        // Toggle on missing: ensures presence (with bit defaulted true), then
        // flips → ends up disabled-but-present.
        w.Toggle<Position>(e);
        Assert.True(w.Owns<Position>(e));
        Assert.False(w.IsEnabled<Position>(e));
        w.Toggle<Position>(e);
        Assert.True(w.IsEnabled<Position>(e));
    }
}
