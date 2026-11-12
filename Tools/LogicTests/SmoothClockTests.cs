using System;

// Simulates a game loop reading a stepwise audio clock, and compares the old interpolation with SmoothClock.
// Model: true source time A(t); the source reports A rounded down to its buffer length B (stale by 0..B).
// Frames come every Tf seconds with jitter. Nothing here is Unity: SmoothClock is plain C#.
static class SmoothClockTests
{
    static int fails;
    static void Check(string name, object got, object want)
    {
        bool ok = got.ToString() == want.ToString();
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + "  got=" + got + " want=" + want);
    }
    static void Less(string name, double value, double limit)
    {
        bool ok = value < limit;
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + "  " + (value * 1000).ToString("0.00") + " ms  (limit " + (limit * 1000).ToString("0.0") + " ms)");
    }
    static void More(string name, double value, double limit)
    {
        bool ok = value > limit;
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + "  " + (value * 1000).ToString("0.00") + " ms  (needs more than " + (limit * 1000).ToString("0.0") + " ms)");
    }

    class Result
    {
        public double MaxErr, StdErr, MaxStep;
        public SmoothClock Clock;
    }

    // truth(t) = true source time. observe(t, reading) lets a test corrupt what the game sees.
    static Result Run(bool useNew, double B, double Tf, double jitter, double seconds, double warmup,
                      Func<double, double> truth, Func<double, double, double> observe, int seed)
    {
        var rng = new Random(seed);
        var clock = new SmoothClock();
        var r = new Result { Clock = clock };
        double t = 0;
        double lastD = double.NaN, lastT = 0;
        double sum = 0, sumSq = 0, prevErr = 0; int n = 0; bool havePrev = false;
        int frame = 0;

        while (t < seconds)
        {
            double a = truth(t);
            double d = Math.Floor(a / B) * B;                 // what the audio system reports
            d = observe(t, d);

            double est;
            if (useNew)
            {
                clock.Add(t, d);
                est = clock.SourceAt(t);
            }
            else
            {
                if (double.IsNaN(lastD) || d != lastD) { lastD = d; lastT = t; }   // old: restart at the frame that noticed the change
                est = lastD + (t - lastT);
            }

            if (t >= warmup)
            {
                double err = est - a;
                r.MaxErr = Math.Max(r.MaxErr, Math.Abs(err));
                sum += err; sumSq += err * err; n++;
                if (havePrev) r.MaxStep = Math.Max(r.MaxStep, Math.Abs(err - prevErr));
                prevErr = err; havePrev = true;
            }

            frame++;
            double dt = Tf + (rng.NextDouble() * 2 - 1) * jitter;
            if (frame % 180 == 0) dt += 0.045;                 // an occasional hitch
            t += dt;
        }
        double mean = n > 0 ? sum / n : 0;
        r.StdErr = n > 0 ? Math.Sqrt(Math.Max(0, sumSq / n - mean * mean)) : 0;
        return r;
    }

    static void Main()
    {
        double c0 = 123.456;

        // ---- compare old and new across buffer sizes and frame rates
        double[] buffers = { 0.01067, 0.02133, 0.04267, 0.08533 };   // 512, 1024, 2048, 4096 samples at 48 kHz
        double[] frames = { 1.0 / 60, 1.0 / 144, 1.0 / 30 };
        foreach (double B in buffers)
        {
            foreach (double Tf in frames)
            {
                string tag = "B=" + (B * 1000).ToString("0.0") + "ms frame=" + (Tf * 1000).ToString("0.0") + "ms";
                var o = Run(false, B, Tf, 0.002, 12, 3, t => c0 + t, (t, d) => d, 1);
                var n = Run(true, B, Tf, 0.002, 12, 3, t => c0 + t, (t, d) => d, 1);
                Console.WriteLine("INFO " + tag + "  old max " + (o.MaxErr * 1000).ToString("0.0") + " std " + (o.StdErr * 1000).ToString("0.0")
                    + "  | new max " + (n.MaxErr * 1000).ToString("0.0") + " std " + (n.StdErr * 1000).ToString("0.0") + " step " + (n.MaxStep * 1000).ToString("0.0"));
                Less("new error " + tag, n.MaxErr, 0.006);
                Less("new std " + tag, n.StdErr, 0.002);
                Less("new step " + tag, n.MaxStep, 0.004);
            }
        }

        // The old method really is worse (otherwise the test model is too kind)
        var oldRun = Run(false, 0.02133, 1.0 / 60, 0.002, 12, 3, t => c0 + t, (t, d) => d, 1);
        var newRun = Run(true, 0.02133, 1.0 / 60, 0.002, 12, 3, t => c0 + t, (t, d) => d, 1);
        More("old method jitters", oldRun.MaxErr, 0.010);
        Less("new is far smaller", newRun.MaxErr * 3, oldRun.MaxErr);

        // ---- warm-up: usable within a second
        var warm = Run(true, 0.02133, 1.0 / 60, 0.002, 3, 0.5, t => c0 + t, (t, d) => d, 2);
        Less("error after 0.5 s", warm.MaxErr, 0.03);

        // ---- source clock slightly fast (100 ppm drift)
        var drift = Run(true, 0.02133, 1.0 / 60, 0.002, 30, 3, t => c0 + t * 1.0001, (t, d) => d, 3);
        Less("100 ppm drift over 30 s", drift.MaxErr, 0.006);

        // ---- a single glitched reading (position doubled) is ignored
        int calls = 0;
        var glitch = Run(true, 0.02133, 1.0 / 60, 0.002, 12, 3, t => c0 + t, (t, d) => (++calls == 400) ? d * 2 : d, 4);
        Less("doubled reading ignored", glitch.MaxErr, 0.006);
        Check("glitch counted as rejected", glitch.Clock.RejectedSpikes, 1);
        Check("no reset for a glitch", glitch.Clock.Resets, 0);

        // ---- a real jump (+0.5 s, for example a seek) is followed after a few frames
        var jump = Run(true, 0.02133, 1.0 / 60, 0.002, 12, 8, t => c0 + t + (t >= 5 ? 0.5 : 0), (t, d) => d, 5);
        Less("after a real jump", jump.MaxErr, 0.006);
        Check("one reset for the jump", jump.Clock.Resets, 1);
        More("jump readings rejected first", jump.Clock.RejectedSpikes, 2);

        // ---- source stalls for 3 s, then continues
        double ts = 5, te = 8;
        Func<double, double> stalled = t => c0 + t - Math.Max(0, Math.Min(t, te) - ts);
        var stall = Run(true, 0.02133, 1.0 / 60, 0.002, 14, te + 0.8, stalled, (t, d) => d, 6);
        Less("recovered 0.8 s after a stall", stall.MaxErr, 0.006);
        var during = Run(true, 0.02133, 1.0 / 60, 0.002, te, ts, stalled, (t, d) => d, 6);
        Less("during a stall stays near the source", during.MaxErr, 0.6);

        // ---- reset and first reading
        var clock = new SmoothClock();
        Check("empty has no value", clock.HasValue, false);
        clock.Add(10.0, 20.0);
        Check("first reading sets offset", clock.Offset, 10);
        Check("source at", clock.SourceAt(11.0), 21);
        clock.Reset();
        Check("reset clears", clock.HasValue, false);

        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        if (fails != 0) Environment.Exit(1);   // run.sh and the build server stop on a failed suite
    }
}
