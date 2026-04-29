using System;
using System.Diagnostics;
using Flecs;

public record struct Position(float X, float Y);
public record struct Velocity(float Dx, float Dy);

class Program
{
    static void RunQuery(Query<Position, Velocity> q)
    {
        for (int iter = 0; iter < 3600; iter++)
            foreach (var (p, v) in q.Rows()) { p.Value.X *= v.Value.Dx; p.Value.Y *= v.Value.Dy; }
    }


    static void Main()
    {
        const int N = 1_000_000;

        // ===== Own-only scenario =====
        var w = new World();
        for (int i = 0; i < N; i++)
        {
            var e = w.CreateEntity();
            w.Set(e, new Position(1f, 1f));
            w.Set(e, new Velocity(1.0000001f, 1.0000001f));
        }
        var q = w.Query<Position, Velocity>();

        // Warmup.
        for (int i = 0; i < 3; i++) RunQuery(q);

        Console.WriteLine($"== Own-only RowEnumerator — N={N:N0}, 3600 iters x 5 runs ==");
        for (int run = 0; run < 5; run++)
        {
            var sw = Stopwatch.StartNew();
            RunQuery(q);
            sw.Stop();
            long ops = (long)N * 3600;
            double mops = ops / sw.Elapsed.TotalSeconds / 1_000_000.0;
            Console.WriteLine($"  run {run + 1}: {sw.Elapsed.TotalMilliseconds,10:F2} ms  ({mops:F1} M ops/s)");
        }

        // ===== Inheritance scenario =====
        // Prefab carries Position; N instances inherit it via IsA. Each
        // instance owns Velocity. RowEnumerator broadcasts the shared Position
        // (stride=0) while advancing Velocity (stride=1).
        var w2 = new World();
        var prefab = w2.CreateEntity();
        w2.Set(prefab, new Position(1f, 1f));
        for (int i = 0; i < N; i++)
        {
            var e = w2.CreateEntity();
            w2.SetIsA(e, prefab);
            w2.Set(e, new Velocity(1.0000001f, 1.0000001f));
        }
        var qi = w2.Query<Position, Velocity>().Up<Position>();

        for (int i = 0; i < 3; i++) RunQuery(qi);

        Console.WriteLine($"== Inherited RowEnumerator (Position shared) — N={N:N0}, 3600 iters x 5 runs ==");
        for (int run = 0; run < 5; run++)
        {
            var sw = Stopwatch.StartNew();
            RunQuery(qi);
            sw.Stop();
            long ops = (long)N * 3600;
            double mops = ops / sw.Elapsed.TotalSeconds / 1_000_000.0;
            Console.WriteLine($"  run {run + 1}: {sw.Elapsed.TotalMilliseconds,10:F2} ms  ({mops:F1} M ops/s)");
        }
    }
}
