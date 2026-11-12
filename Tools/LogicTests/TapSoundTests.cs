using System;
using System.Reflection;
using UnityEngine;

static class TapSoundTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int fails;
    static void Check(string name, object got, object want)
    {
        bool ok = got.ToString() == want.ToString();
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + "  got=" + got + " want=" + want);
    }
    static void Set(object o, string n, object v) { o.GetType().GetField(n, F).SetValue(o, v); }
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static void Call(object o, string n, params object[] a) { o.GetType().GetMethod(n, F).Invoke(o, a); }

    static int Crossings(float[] d)
    {
        int c = 0;
        for (int i = 1; i < d.Length; i++) if ((d[i - 1] < 0) != (d[i] < 0)) c++;
        return c;
    }
    static double Energy(float[] d, int from, int to)
    {
        double e = 0;
        for (int i = from; i < to; i++) e += d[i] * d[i];
        return e;
    }

    static void Main()
    {
        // ---- sample data
        var hi = TapSound.Blip(44100, 1760f, 0.06f);
        var lo = TapSound.Blip(44100, 990f, 0.06f);
        Check("length 60 ms", hi.Length, 2646);
        float peak = 0f; bool allIn = true;
        foreach (var x in hi) { peak = Math.Max(peak, Math.Abs(x)); if (x < -1f || x > 1f) allIn = false; }
        Check("no clipping", allIn, true);
        Check("audible peak", peak > 0.3f, true);
        Check("ends near silence", Math.Abs(hi[hi.Length - 1]) < 0.001f, true);
        Check("decays", Energy(hi, 0, 441) > 20 * Energy(hi, hi.Length - 441, hi.Length), true);
        Check("higher freq = more cycles", Crossings(hi) > Crossings(lo), true);
        Check("tiny duration still works", TapSound.Blip(44100, 1000f, 0f).Length, 1);

        // ---- voice rotation
        Check("voice 0 -> 1", TapSound.NextVoice(0, 8), 1);
        Check("voice wraps", TapSound.NextVoice(7, 8), 0);

        // ---- behaviour: which hits make sound
        var judge = new Judge();
        Call(judge, "Awake");
        var tap = new TapSound();
        Set(tap, "judge", judge);
        Call(tap, "Start");

        AudioSource.PlayCount = 0;
        Call(judge, "Register", 0, Grade.Perfect, 0f);
        var perfectClip = AudioSource.LastClip;
        Call(judge, "Register", 1, Grade.Great, 0f);
        var greatClip = AudioSource.LastClip;
        Call(judge, "Register", 2, Grade.Good, 0f);
        var goodClip = AudioSource.LastClip;
        Check("three hits = three sounds", AudioSource.PlayCount, 3);
        Check("grades use different clips", perfectClip != greatClip && greatClip != goodClip && perfectClip != goodClip, true);

        Call(judge, "Register", 3, Grade.Miss, 0f);
        Check("miss is silent", AudioSource.PlayCount, 3);

        // ---- voices rotate, volume applied
        Check("voice index after 3 hits", Get(tap, "nextVoice"), 3);
        for (int i = 0; i < 5; i++) Call(judge, "Register", 0, Grade.Perfect, 0f);
        Check("voice index wraps after 8 hits", Get(tap, "nextVoice"), 0);
        var voices = (AudioSource[])Get(tap, "voices");
        Check("volume from setting", Math.Round(voices[0].volume, 2), 0.7);

        // ---- volume 0 = silent, no voice used
        PlayerPrefs.SetInt(GameSettings.TapVolumeKey, 0);
        int before = AudioSource.PlayCount;
        Call(judge, "Register", 0, Grade.Perfect, 0f);
        Check("volume 0 is silent", AudioSource.PlayCount, before);

        PlayerPrefs.SetInt(GameSettings.TapVolumeKey, 10);
        Call(judge, "Register", 0, Grade.Perfect, 0f);
        Check("volume 10 plays at full", voices[(int)Get(tap, "nextVoice") == 0 ? 7 : (int)Get(tap, "nextVoice") - 1].volume, 1f);

        // ---- settings defaults
        PlayerPrefs.SetInt(GameSettings.TapVolumeKey, 7);
        Check("default tap volume", GameSettings.TapVolume01, 0.7f);

        // ---- unsubscribes cleanly
        Call(tap, "OnDestroy");
        before = AudioSource.PlayCount;
        Call(judge, "Register", 0, Grade.Perfect, 0f);
        Check("silent after destroy", AudioSource.PlayCount, before);

        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        if (fails != 0) Environment.Exit(1);   // run.sh and the build server stop on a failed suite
    }
}
