using Xunit;
using System.Collections.Generic;

namespace Flecs.Tests;

public class TableEventTests
{
    [Fact]
    public void OnTableCreate_FiresForNewArchetype()
    {
        var w = new World();
        var seen = new List<int>();
        w.OnTableCreate += (W, t) => seen.Add(t.ColumnCount);
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        Assert.NotEmpty(seen);
    }

    [Fact]
    public void OnTableCreate_NotRefiredForExistingArchetype()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0)); // creates Position table
        int countAfterFirst = 0;
        w.OnTableCreate += (W, t) => countAfterFirst++;
        var e2 = w.CreateEntity();
        w.Set(e2, new Position(1, 1)); // same archetype, no new table
        Assert.Equal(0, countAfterFirst);
    }

    [Fact]
    public void OnTableCreate_FiresPerDistinctArchetype()
    {
        var w = new World();
        int created = 0;
        w.OnTableCreate += (W, t) => created++;
        var a = w.CreateEntity();
        w.Set(a, new Position(0, 0));      // arch 1
        var b = w.CreateEntity();
        w.Set(b, new Position(0, 0));
        w.Set(b, new Velocity(0, 0));      // arch 2 (Position+Velocity)
        Assert.Equal(2, created);
    }
}
