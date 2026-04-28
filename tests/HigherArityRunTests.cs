using Xunit;

namespace Flecs.Tests;

public class HigherArityRunTests
{
    [Fact]
    public void Run_Arity4_SpanAccess()
    {
        var w = new World();
        for (int i = 0; i < 3; i++)
        {
            var e = w.CreateEntity();
            w.Set(e, new Position(i, 0));
            w.Set(e, new Velocity(0, i));
            w.Set(e, new Health(i));
            w.Set(e, new Mana(i));
        }
        int total = 0;
        w.Query<Position, Velocity, Health, Mana>().Run((in Iter<Position, Velocity, Health, Mana> it) =>
        {
            var p = it.Field1();
            var v = it.Field2();
            var h = it.Field3();
            var m = it.Field4();
            for (int r = 0; r < it.Count; r++)
                total += (int)(p[r].X + v[r].Dy + h[r].Value + m[r].Value);
        });
        // (0+0+0+0) + (1+1+1+1) + (2+2+2+2) = 12
        Assert.Equal(12, total);
    }

    [Fact]
    public void Run_Arity5_SpanAccess()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 0));
        w.Set(e, new Velocity(0, 0));
        w.Set(e, new Health(2));
        w.Set(e, new Mana(3));
        w.Set(e, new Damage(4));
        int sum = 0;
        w.Query<Position, Velocity, Health, Mana, Damage>().Run(
            (in Iter<Position, Velocity, Health, Mana, Damage> it) =>
            {
                var p = it.Field1();
                var h = it.Field3();
                var m = it.Field4();
                var d = it.Field5();
                for (int r = 0; r < it.Count; r++)
                    sum += (int)p[r].X + h[r].Value + m[r].Value + d[r].Value;
            });
        Assert.Equal(1 + 2 + 3 + 4, sum);
    }

    [Fact]
    public void Run_Arity6_SpanAccess()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Velocity(0, 0));
        w.Set(e, new Health(10));
        w.Set(e, new Mana(20));
        w.Set(e, new Damage(30));
        w.Set(e, new Defense(40));
        int sum = 0;
        w.Query<Position, Velocity, Health, Mana, Damage, Defense>().Run(
            (in Iter<Position, Velocity, Health, Mana, Damage, Defense> it) =>
            {
                var h = it.Field3();
                var m = it.Field4();
                var d = it.Field5();
                var df = it.Field6();
                for (int r = 0; r < it.Count; r++)
                    sum += h[r].Value + m[r].Value + d[r].Value + df[r].Value;
            });
        Assert.Equal(10 + 20 + 30 + 40, sum);
    }

    [Fact]
    public void Run_Arity4_MutationViaSpan()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 1));
        w.Set(e, new Velocity(2, 2));
        w.Set(e, new Health(0));
        w.Set(e, new Mana(0));
        w.Query<Position, Velocity, Health, Mana>().Run(
            (in Iter<Position, Velocity, Health, Mana> it) =>
            {
                var p = it.Field1();
                var v = it.Field2();
                for (int r = 0; r < it.Count; r++)
                {
                    p[r].X += v[r].Dx;
                    p[r].Y += v[r].Dy;
                }
            });
        Assert.Equal(3, w.Get<Position>(e).X);
        Assert.Equal(3, w.Get<Position>(e).Y);
    }

    [Fact]
    public void Run_HigherArity_EntityAccess()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Velocity(0, 0));
        w.Set(e, new Health(0));
        w.Set(e, new Mana(0));
        EntityId seen = default;
        w.Query<Position, Velocity, Health, Mana>().Run(
            (in Iter<Position, Velocity, Health, Mana> it) =>
            {
                seen = it.Entity(0);
            });
        Assert.Equal(e.Id, seen.Id);
    }
}
