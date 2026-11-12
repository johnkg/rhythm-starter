using System;
using UnityEngine;

// AudioTimeline against a tiny fake audio engine. The engine moves the read position in 1024-sample chunks,
// starts late after Play() and UnPause(), and freezes on Pause(), like a browser would.
static class TimelineTests
{
    static int fails;
    static void Check(string name, object got, object want)
    {
        bool ok = got.ToString() == want.ToString();
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + "  got=" + got + " want=" + want);
    }
    static void Near(string name, double got, double want, double tolMs)
    {
        bool ok = Math.Abs(got - want) * 1000 <= tolMs;
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + "  got=" + got.ToString("0.0000") + " want=" + want.ToString("0.0000") + " (tol " + tolMs + " ms)");
    }

    const int Freq = 48000;

    class FakeAudio
    {
        public AudioSource Src;
        public double PlayLatency = 0.040, ResumeLatency = 0.030;
        public double StartReal;            // real time at which position 0 passes
        public bool Running;
        public double Frozen;               // position while not running
        int seenPlay, seenPause, seenUnpause;
        double resumeAt = -1;

        public double Pos(double r)
        {
            if (!Running) return Frozen;
            return Math.Max(0, r - StartReal);
        }

        // true read cursor, what the audio engine is really doing
        public double TruePos(double r)
        {
            double p = Pos(r);
            if (Src.clip != null && p >= Src.clip.length) return Src.clip.length;
            return p;
        }

        public void Step(double r)
        {
            if (Src.PlayCalls > seenPlay) { seenPlay = Src.PlayCalls; StartReal = r + PlayLatency; Running = true; Frozen = 0; }
            if (Src.PauseCalls > seenPause) { seenPause = Src.PauseCalls; Frozen = Pos(r); Running = false; }
            if (Src.UnPauseCalls > seenUnpause) { seenUnpause = Src.UnPauseCalls; resumeAt = r + ResumeLatency; }
            if (resumeAt >= 0 && r >= resumeAt) { StartReal = r - Frozen; Running = true; resumeAt = -1; }

            double p = Pos(r);
            if (Src.clip != null && p >= Src.clip.length) { Src.timeSamples = 0; Src.isPlaying = false; return; }   // ended
            Src.timeSamples = (int)(Math.Floor(p * Freq / 1024.0) * 1024.0);
        }
    }

    static Random rng = new Random(11);
    static double NextFrame(double r) { return r + 1.0 / 60 + (rng.NextDouble() * 2 - 1) * 0.002; }

    static void Main()
    {
        // ================= position mode (web) =================
        {
            var clip = new AudioClip { length = 100f, frequency = Freq };
            var src = new AudioSource { clip = clip };
            var fake = new FakeAudio { Src = src };
            var tl = new AudioTimeline(src, true);

            double r = 100.0;
            tl.Begin(2.0, r, 0);
            Near("lead-in start", tl.TimeAt(100.0), -2.0, 0.01);
            Near("lead-in runs on real time", tl.TimeAt(101.0), -1.0, 0.01);
            Check("free running at start", tl.FreeRunning, true);
            Check("pause refused in lead-in", tl.Pause(101.0), false);

            double maxErr = 0; bool leftFreeRun = false; double jumpAtSwitch = 0; double prevT = tl.TimeAt(r), maxBackStep = 0;
            int playCallsAtSecond = -1;
            while (r < 112)
            {
                fake.Step(r);
                tl.Tick(r, 0);
                double t = tl.TimeAt(r);
                if (r >= 101.0 && playCallsAtSecond < 0) playCallsAtSecond = src.PlayCalls;
                if (!leftFreeRun && !tl.FreeRunning) { leftFreeRun = true; jumpAtSwitch = t - prevT; }
                if (r >= 105) maxErr = Math.Max(maxErr, Math.Abs(t - fake.TruePos(r)));
                maxBackStep = Math.Max(maxBackStep, prevT - t);
                prevT = t;
                r = NextFrame(r);
            }
            Check("Play not called before song time 0", playCallsAtSecond, 0);
            Check("Play called once", src.PlayCalls, 1);
            Check("switched to following the audio", leftFreeRun, true);
            Near("jump back when audio is seen (audio started 40 ms late)", jumpAtSwitch, -0.040, 25);
            Near("following the audio, max error", maxErr, 0, 3);
        }

        // ---- pause and resume (position mode)
        {
            var src = new AudioSource { clip = new AudioClip { length = 100f, frequency = Freq } };
            var fake = new FakeAudio { Src = src };
            var tl = new AudioTimeline(src, true);
            double r = 100.0;
            tl.Begin(2.0, r, 0);
            while (r < 106) { fake.Step(r); tl.Tick(r, 0); r = NextFrame(r); }

            Check("pause accepted", tl.Pause(r), true);
            Check("audio paused", src.PauseCalls, 1);
            double frozenTime = tl.TimeAt(r);
            for (int i = 0; i < 120; i++) { fake.Step(r); tl.Tick(r, 0); r = NextFrame(r); }   // 2 s paused
            Near("frozen while paused", tl.TimeAt(r), frozenTime, 0.01);
            Check("still paused", tl.Paused, true);

            tl.Resume(r, 0);
            Check("audio resumed", src.UnPauseCalls, 1);
            Near("resume continues from the frozen time", tl.TimeAt(r), frozenTime, 0.01);
            double rr = r;
            fake.Step(r); tl.Tick(r, 0); r = NextFrame(r);   // audio resumes 30 ms late, not seen yet
            Near("runs on real time until audio is seen", tl.TimeAt(r), frozenTime + (r - rr), 0.01);
            double maxErr = 0;
            while (r < rr + 4) { fake.Step(r); tl.Tick(r, 0); if (r > rr + 1.5) maxErr = Math.Max(maxErr, Math.Abs(tl.TimeAt(r) - fake.TruePos(r))); r = NextFrame(r); }
            Check("following the audio again", tl.FreeRunning, false);
            Near("error after resume", maxErr, 0, 3);
        }

        // ---- clip ends, chart goes on (position mode)
        {
            var src = new AudioSource { clip = new AudioClip { length = 5f, frequency = Freq } };
            var fake = new FakeAudio { Src = src };
            var tl = new AudioTimeline(src, true);
            double r = 100.0;
            tl.Begin(1.0, r, 0);
            double prevT = -1, prevR = r, maxJump = 0; bool endedFreeRun = false;
            while (r < 112)
            {
                fake.Step(r); tl.Tick(r, 0);
                double t = tl.TimeAt(r);
                if (r > 103) maxJump = Math.Max(maxJump, Math.Abs((t - prevT) - (r - prevR)));
                prevR = r;
                if (t > 5.5 && tl.FreeRunning) endedFreeRun = true;
                prevT = t;
                r = NextFrame(r);
            }
            Check("keeps free running after the clip ends", endedFreeRun, true);
            Check("time kept advancing past the clip end", tl.TimeAt(r) > 8, true);
            Near("no jump at the clip end", maxJump, 0, 30);
        }

        // ---- no audio clip (silent run)
        {
            var src = new AudioSource();
            var tl = new AudioTimeline(src, true);
            double r = 50;
            tl.Begin(2.0, r, 0);
            for (int i = 0; i < 600; i++) { tl.Tick(r, 0); r += 1.0 / 60; }
            Check("silent run never plays", src.PlayCalls, 0);
            Near("silent run follows real time", tl.TimeAt(r), (r - 50) - 2.0, 0.01);
            Check("silent run can pause", tl.Pause(r), true);
            Check("no audio call without a clip", src.PauseCalls, 0);
            tl.Resume(r + 1.0, 0);
            Near("silent resume", tl.TimeAt(r + 1.0), tl.TimeAt(r + 1.0), 0.01);
        }

        // ================= audio system clock mode (desktop) =================
        {
            var src = new AudioSource { clip = new AudioClip { length = 100f, frequency = Freq } };
            var tl = new AudioTimeline(src, false);
            double r = 0;
            double dsp0 = 7.0;
            double B = 1024.0 / Freq;
            Func<double, double> dsp = real => Math.Floor((dsp0 + real) / B) * B;   // stepwise audio system time

            tl.Begin(2.0, r, dsp(r));
            Check("scheduled once at dsp + lead-in", src.ScheduledAt.ToString("0.000"), (dsp(0) + 2.0).ToString("0.000"));
            Check("no plain Play in this mode", src.PlayCalls, 0);

            double maxErr = 0;
            while (r < 20)
            {
                tl.Tick(r, dsp(r));
                // truth: song time = real time - lead-in, relative to the exact audio start
                if (r > 3) maxErr = Math.Max(maxErr, Math.Abs(tl.TimeAt(r) - (r - 2.0 - (dsp(0) - dsp0))));
                r = NextFrame(r);
            }
            Near("audio system clock, max error", maxErr, 0, 3);

            // pause and resume
            double t0 = tl.TimeAt(r);
            Check("pause accepted", tl.Pause(r), true);
            for (int i = 0; i < 180; i++) { r = NextFrame(r); tl.Tick(r, dsp(r)); }
            Near("frozen while paused", tl.TimeAt(r), t0, 0.01);
            tl.Resume(r, dsp(r));
            Near("resumes where it stopped", tl.TimeAt(r), t0, 4);
            double rr = r;
            for (int i = 0; i < 120; i++) { r = NextFrame(r); tl.Tick(r, dsp(r)); }
            Near("one second later", tl.TimeAt(r), t0 + (r - rr), 4);
        }

        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        if (fails != 0) Environment.Exit(1);   // run.sh and the build server stop on a failed suite
    }
}
