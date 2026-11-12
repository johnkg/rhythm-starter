using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

// Conductor + Judge in audio-system-clock mode (the desktop default). Position mode is in TimelineTests.
static class ClockAndPauseTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static void Set(object o, string n, object v)
    {
        var f = o.GetType().GetField(n, F);
        if (f != null) { f.SetValue(o, v); return; }
        o.GetType().GetProperty(n, F).GetSetMethod(true).Invoke(o, new[] { v });
    }
    static void Call(object o, string n, params object[] a) { o.GetType().GetMethod(n, F).Invoke(o, a); }
    static int fails;
    static void Check(string name, object got, object want)
    {
        bool ok = got.ToString() == want.ToString();
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + "  got=" + got + " want=" + want);
    }
    static void Tick(Conductor c, double real, double dsp)
    {
        Time.realtimeSinceStartupAsDouble = real; AudioSettings.dspTime = dsp; Call(c, "Update");
    }

    // Conductor that starts at real time 0 / audio time 0 with the given lead-in. Song time then equals real time minus lead-in.
    static Conductor Make(float leadIn)
    {
        var c = new Conductor();
        Set(c, "music", new AudioSource());
        Set(c, "startDelay", leadIn);
        Time.realtimeSinceStartupAsDouble = 0; AudioSettings.dspTime = 0;
        Call(c, "Awake"); Call(c, "Start");
        return c;
    }

    static void Main()
    {
        // ---- clock freeze / resume
        var c = Make(0f);
        Tick(c, 10, 10);
        Check("time before pause", Math.Round(c.SongTime, 3), 10);
        c.Pause();
        Check("paused flag", c.Paused, true);
        Tick(c, 15, 15);
        Check("frozen while paused", Math.Round(c.SongTime, 3), 10);
        c.Resume();
        Check("resume continues from 10", Math.Round(c.SongTime, 3), 10);
        Check("not paused after resume", c.Paused, false);
        Tick(c, 16, 16);
        Check("1s after resume", Math.Round(c.SongTime, 3), 11);

        // ---- lead-in: song time negative, pause refused
        var c2 = Make(5f);
        Tick(c2, 2, 2);
        Check("lead-in song time", Math.Round(c2.SongTime, 3), -3);
        c2.Pause();
        Check("no pause in lead-in", c2.Paused, false);
        Tick(c2, 6, 6);
        Check("after lead-in", Math.Round(c2.SongTime, 3), 1);

        // ---- user offset respected through pause
        var c3 = Make(0f);
        c3.userOffsetSec = 0.05f;
        Tick(c3, 10, 10);
        double before = c3.SongTime;           // 9.95
        c3.Pause();
        Check("offset: frozen equals pre-pause", Math.Round(c3.SongTime, 3), Math.Round(before, 3));
        Tick(c3, 20, 20);
        c3.Resume();
        Check("offset: resume same value", Math.Round(c3.SongTime, 3), Math.Round(before, 3));

        // ---- event timestamps convert to song time, not frame time
        var c4 = Make(0f);
        Tick(c4, 3.000, 3.000);
        Check("event 20 ms before the frame", Math.Round(c4.SongTimeAtRealtime(2.980), 3), 2.98);

        // ---- audio is scheduled once, on the audio timeline
        var src = new AudioSource { clip = new AudioClip { length = 60, frequency = 48000 } };
        var c5 = new Conductor();
        Set(c5, "music", src); Set(c5, "startDelay", 2f);
        Time.realtimeSinceStartupAsDouble = 0; AudioSettings.dspTime = 7;
        Call(c5, "Awake"); Call(c5, "Start");
        Check("scheduled at dsp + lead-in", src.ScheduledAt, 9);

        // ---- Judge: pause drops active hold, input ignored while paused
        var cj = Make(0f);
        Tick(cj, 5, 5);

        var lanes = new List<NoteRuntime>[4];
        for (int i = 0; i < 4; i++) lanes[i] = new List<NoteRuntime>();
        var hold = new NoteRuntime { data = new NoteData { time_ms = 5000, lane = 0, duration_ms = 3000 } };
        var tap = new NoteRuntime { data = new NoteData { time_ms = 6000, lane = 1 } };
        lanes[0].Add(hold); lanes[1].Add(tap);
        var s = new NoteSpawner();
        Set(s, "Lanes", lanes); Set(s, "TotalNotes", 2); Set(s, "LastNoteTime", 8.0);
        var j = new Judge();
        Set(j, "spawner", s);
        Call(j, "Awake"); Call(j, "Start");

        Call(j, "OnPress", 0, 5.0);
        Check("hold started", hold.holding, true);
        cj.Pause();
        j.DropActiveHolds();
        Check("hold dropped on pause", hold.resolved && !hold.holding, true);
        Check("drop count", j.DropCount, 1);
        Check("combo reset", j.Combo, 0);

        Call(j, "OnPress", 1, 6.0);   // ignored while paused
        Check("tap ignored while paused", tap.resolved, false);
        cj.Resume();
        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
    }
}
