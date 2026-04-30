using Xunit;

namespace Flecs.Tests;

public class HigherArityRunTests
{
    [Fact]
    public void Arity4_Iteration()
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
        foreach (var (p, v, h, m) in w.Query<Position, Velocity, Health, Mana>())
            total += (int)(p.Value.X + v.Value.Dy + h.Value.Value + m.Value.Value);
        // (0+0+0+0) + (1+1+1+1) + (2+2+2+2) = 12
        Assert.Equal(12, total);
    }

    [Fact]
    public void Arity5_Iteration()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 0));
        w.Set(e, new Velocity(0, 0));
        w.Set(e, new Health(2));
        w.Set(e, new Mana(3));
        w.Set(e, new Damage(4));
        int sum = 0;
        foreach (var (p, _, h, m, d) in w.Query<Position, Velocity, Health, Mana, Damage>())
            sum += (int)p.Value.X + h.Value.Value + m.Value.Value + d.Value.Value;
        Assert.Equal(1 + 2 + 3 + 4, sum);
    }

    [Fact]
    public void Arity6_Iteration()
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
        foreach (var (_, _, h, m, d, df) in w.Query<Position, Velocity, Health, Mana, Damage, Defense>())
            sum += h.Value.Value + m.Value.Value + d.Value.Value + df.Value.Value;
        Assert.Equal(10 + 20 + 30 + 40, sum);
    }

    [Fact]
    public void Arity4_MutationViaPtr()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 1));
        w.Set(e, new Velocity(2, 2));
        w.Set(e, new Health(0));
        w.Set(e, new Mana(0));
        foreach (var (p, v, _, _) in w.Query<Position, Velocity, Health, Mana>())
        {
            p.Value.X += v.Value.Dx;
            p.Value.Y += v.Value.Dy;
        }
        Assert.Equal(3, w.Get<Position>(e).X);
        Assert.Equal(3, w.Get<Position>(e).Y);
    }
}
