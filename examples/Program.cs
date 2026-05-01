using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
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


    [MethodImpl(MethodImplOptions.NoInlining)]
    static void CreateEntities(World w, int n)
    {
        for (int i = 0; i < n; i++) w.CreateEntity();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    static void SetPosVel(World w, int n)
    {
        for (int i = 0; i < n; i++)
        {
            var e = w.CreateEntity();
            w.Set(e, new Position(1f, 1f));
            w.Set(e, new Velocity(1.0000001f, 1.0000001f));
        }
    }
    
    static void Main()
    {
        const int N = 1_000_000;

        // Warmup JIT.
        var ww = new World();
        CreateEntities(ww, 1000);
        SetPosVel(ww, 1000);
        
        var query = ww.Query<Position, Velocity>();
        ww.System("ciao", ww.OnUpdate, it =>
        {
            (var i, var str, var query) = it.Ctx<(int, string, Query<Position, Velocity>)>();

            foreach (var (ent, p, v) in query)
            {
                
            }
            
        }).SetCtx((1, "asd", query));

        var info = ww.GetInfo();
        
        while (true) 
            ww.Progress(0f);
        
        // Bench CreateEntity alone.
        for (int run = 0; run < 3; run++)
        {
            var wc = new World();
            var sw = Stopwatch.StartNew();
            CreateEntities(wc, N);
            sw.Stop();
            double ns = sw.Elapsed.TotalNanoseconds / N;
            Console.WriteLine($"CreateEntity x {N:N0}: {sw.Elapsed.TotalMilliseconds,8:F2} ms ({ns:F1} ns/entity)");
        }

        // Bench Create+Set+Set.
        for (int run = 0; run < 3; run++)
        {
            var ws = new World();
            var sw = Stopwatch.StartNew();
            SetPosVel(ws, N);
            sw.Stop();
            double ns = sw.Elapsed.TotalNanoseconds / N;
            Console.WriteLine($"Create+Set+Set x {N:N0}: {sw.Elapsed.TotalMilliseconds,8:F2} ms ({ns:F1} ns/entity)");
        }
        Console.WriteLine();

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
