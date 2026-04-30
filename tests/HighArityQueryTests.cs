using Xunit;

namespace Flecs.Tests;

// Smoke tests for Query<T1..T16> generated in src/Query.Arity.cs.
public class HighArityQueryTests
{
    [Fact]
    public void Arity7_Iteration()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 0));
        w.Set(e, new Velocity(2, 0));
        w.Set(e, new Health(3));
        w.Set(e, new Mana(4));
        w.Set(e, new Damage(5));
        w.Set(e, new Defense(6));
        w.Set(e, new C7(7));
        int sum = 0;
        foreach (var (p, v, h, m, d, df, c7) in w.Query<Position, Velocity, Health, Mana, Damage, Defense, C7>())
            sum += (int)p.Value.X + (int)v.Value.Dx + h.Value.Value + m.Value.Value + d.Value.Value + df.Value.Value + c7.Value.V;
        Assert.Equal(1 + 2 + 3 + 4 + 5 + 6 + 7, sum);
    }

    [Fact]
    public void Arity12_Iteration()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Velocity(0, 0));
        w.Set(e, new Health(0));
        w.Set(e, new Mana(0));
        w.Set(e, new Damage(0));
        w.Set(e, new Defense(0));
        w.Set(e, new C7(7));
        w.Set(e, new C8(8));
        w.Set(e, new C9(9));
        w.Set(e, new C10(10));
        w.Set(e, new C11(11));
        w.Set(e, new C12(12));
        int sum = 0;
        foreach (var row in w.Query<Position, Velocity, Health, Mana, Damage, Defense, C7, C8, C9, C10, C11, C12>())
            sum += row.Component7.Value.V + row.Component8.Value.V + row.Component9.Value.V
                + row.Component10.Value.V + row.Component11.Value.V + row.Component12.Value.V;
        Assert.Equal(7 + 8 + 9 + 10 + 11 + 12, sum);
    }

    [Fact]
    public void Arity16_Iteration()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(0, 0));
        w.Set(e, new Velocity(0, 0));
        w.Set(e, new Health(0));
        w.Set(e, new Mana(0));
        w.Set(e, new Damage(0));
        w.Set(e, new Defense(0));
        w.Set(e, new C7(0));
        w.Set(e, new C8(0));
        w.Set(e, new C9(0));
        w.Set(e, new C10(0));
        w.Set(e, new C11(0));
        w.Set(e, new C12(0));
        w.Set(e, new C13(13));
        w.Set(e, new C14(14));
        w.Set(e, new C15(15));
        w.Set(e, new C16(16));
        int rows = 0;
        int sum = 0;
        foreach (var row in w.Query<Position, Velocity, Health, Mana, Damage, Defense,
                                    C7, C8, C9, C10, C11, C12, C13, C14, C15, C16>())
        {
            rows++;
            sum += row.Component13.Value.V + row.Component14.Value.V
                 + row.Component15.Value.V + row.Component16.Value.V;
        }
        Assert.Equal(1, rows);
        Assert.Equal(13 + 14 + 15 + 16, sum);
        Assert.Equal(e.Id, GetEntityId(w));

        static uint GetEntityId(World w)
        {
            uint id = 0;
            foreach (var row in w.Query<Position, Velocity, Health, Mana, Damage, Defense,
                                        C7, C8, C9, C10, C11, C12, C13, C14, C15, C16>())
                id = row.Entity.Id;
            return id;
        }
    }

    [Fact]
    public void Arity10_MutationViaPtr()
    {
        var w = new World();
        var e = w.CreateEntity();
        w.Set(e, new Position(1, 1));
        w.Set(e, new Velocity(2, 2));
        w.Set(e, new Health(0));
        w.Set(e, new Mana(0));
        w.Set(e, new Damage(0));
        w.Set(e, new Defense(0));
        w.Set(e, new C7(0));
        w.Set(e, new C8(0));
        w.Set(e, new C9(0));
        w.Set(e, new C10(0));
        foreach (var row in w.Query<Position, Velocity, Health, Mana, Damage, Defense, C7, C8, C9, C10>())
        {
            row.Component1.Value.X += row.Component2.Value.Dx;
            row.Component10.Value = new C10(99);
        }
        Assert.Equal(3f, w.Get<Position>(e).X);
        Assert.Equal(99, w.Get<C10>(e).V);
    }
}
