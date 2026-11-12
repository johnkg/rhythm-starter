using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

static class JudgeTests
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

    static NoteRuntime N(List<NoteRuntime>[] lanes, int lane, float ms, float dur)
    {
        var n = new NoteRuntime { data = new NoteData { time_ms = ms, lane = lane, duration_ms = dur } };
        lanes[lane].Add(n);
        return n;
    }

    static void Main()
    {
        var c = new Conductor();
        Set(c, "music", new AudioSource());
        Set(c, "startDelay", 0f);
        Time.realtimeSinceStartupAsDouble = 0; AudioSettings.dspTime = 0;
        Call(c, "Awake"); Call(c, "Start");   // one clock reading (0, 0): song time follows real time from here

        var lanes = new List<NoteRuntime>[4];
        for (int i = 0; i < 4; i++) lanes[i] = new List<NoteRuntime>();
        N(lanes, 0, 2000, 0); N(lanes, 0, 2500, 0); N(lanes, 0, 3000, 0); N(lanes, 0, 3500, 0);
        var hComplete = N(lanes, 2, 5000, 1000);
        var hDrop = N(lanes, 1, 7000, 1000);
        var hAuto = N(lanes, 2, 9000, 1000);
        var hMiss = N(lanes, 3, 11000, 1000);

        var s = new NoteSpawner();
        Set(s, "Lanes", lanes); Set(s, "TotalNotes", 8); Set(s, "LastNoteTime", 12.0);

        var j = new Judge();
        Set(j, "spawner", s);
        Call(j, "Awake"); Call(j, "Start");

        var ev = new List<KeyValuePair<double, Action>>();
        Action<double, Action> At = (t, a) => ev.Add(new KeyValuePair<double, Action>(t, a));
        At(0.50, () => Call(j, "OnPress", 0, 0.50));      // ghost tap, far from any note
        At(0.55, () => Call(j, "OnRelease", 0, 0.55));
        At(2.010, () => Call(j, "OnPress", 0, 2.010));    // perfect
        At(2.570, () => Call(j, "OnPress", 0, 2.570));    // great
        At(3.110, () => Call(j, "OnPress", 0, 3.110));    // good
        // 3.5 no press -> miss
        At(5.000, () => Call(j, "OnPress", 2, 5.000));    // hold, perfect head
        At(6.000, () => Call(j, "OnRelease", 2, 6.000));  // held to end -> bonus
        At(7.000, () => Call(j, "OnPress", 1, 7.000));
        At(7.500, () => Call(j, "OnRelease", 1, 7.500));  // early -> drop
        At(9.000, () => Call(j, "OnPress", 2, 9.000));    // never released, auto complete at 10.0
        At(10.500, () => Call(j, "OnRelease", 2, 10.500));// late release after auto complete, ignored
        // hold at 11.0 never pressed -> miss
        ev.Sort((a, b) => a.Key.CompareTo(b.Key));

        int ei = 0;
        var log = new List<string>();
        j.OnJudged += (lane, g, d) => log.Add($"{lane}:{g}");
        for (double t = 0; t <= 13.0; t += 0.005)
        {
            while (ei < ev.Count && ev[ei].Key <= t) { Time.realtimeSinceStartupAsDouble = ev[ei].Key; ev[ei].Value(); ei++; }
            Time.realtimeSinceStartupAsDouble = t;
            Call(j, "Update");
        }

        Console.WriteLine("log: " + string.Join(" ", log));
        Check("perfect", j.PerfectCount, 4);
        Check("great", j.GreatCount, 1);
        Check("good", j.GoodCount, 1);
        Check("miss", j.MissCount, 2);
        Check("drop", j.DropCount, 1);
        Check("score", j.Score, 6100);
        Check("maxcombo", j.MaxCombo, 3);
        Check("combo", j.Combo, 0);
        Check("allDone", j.AllNotesDone, true);
        Check("accuracy", Math.Round(j.Accuracy, 3), 0.575);
        Check("hComplete.resolved", hComplete.resolved, true);
        Check("hDrop.resolved", hDrop.resolved, true);
        Check("hAuto.holding", hAuto.holding, false);
        Check("hMiss.grade", hMiss.hitGrade, Grade.Miss);
        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
    }
}
