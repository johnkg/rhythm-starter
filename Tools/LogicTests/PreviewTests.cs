using System;
using UnityEngine;

static class PreviewTests
{
    static int fails;
    static void Check(string name, object got, object want)
    {
        bool ok = got.ToString() == want.ToString();
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + "  got=" + got + " want=" + want);
    }

    static void Main()
    {
        // start time
        Check("auto start, 200 s song", Math.Round(PreviewPlayer.StartTime(200f, 0f), 2), 70);
        Check("auto start, 10 s song stays at 0", PreviewPlayer.StartTime(10f, 0f), 0);
        Check("auto start capped so 15 s fit", PreviewPlayer.StartTime(30f, 0f), 10.5f > 15f ? 15 : 10.5f);
        Check("configured start used", PreviewPlayer.StartTime(100f, 30f), 30);
        Check("configured start clamped inside clip", PreviewPlayer.StartTime(100f, 500f), 85);
        Check("configured start on short clip", PreviewPlayer.StartTime(8f, 5f), 0);

        // length
        Check("full length", PreviewPlayer.Length(200f, 70f), 15);
        Check("short clip", PreviewPlayer.Length(10f, 0f), 10);
        Check("near the end", PreviewPlayer.Length(100f, 95f), 5);
        Check("start past end", PreviewPlayer.Length(100f, 120f), 0);

        // fade
        Check("fade at 0", PreviewPlayer.FadeFactor(0f, 15f, 1f), 0);
        Check("fade in half", PreviewPlayer.FadeFactor(0.5f, 15f, 1f), 0.5f);
        Check("full volume middle", PreviewPlayer.FadeFactor(7f, 15f, 1f), 1);
        Check("fade out half", PreviewPlayer.FadeFactor(14.5f, 15f, 1f), 0.5f);
        Check("fade at end", PreviewPlayer.FadeFactor(15f, 15f, 1f), 0);
        Check("short loop is a triangle", Math.Round(PreviewPlayer.FadeFactor(0.9f, 1.8f, 1f), 2), 0.9);
        Check("past end clamps to 0", PreviewPlayer.FadeFactor(16f, 15f, 1f), 0);
        Check("no fade", PreviewPlayer.FadeFactor(0f, 15f, 0f), 1);

        // volume setting
        Check("default volume", GameSettings.PreviewVolume01, 0.7f);
        PlayerPrefs.SetInt(GameSettings.PreviewVolumeKey, 0);
        Check("volume off", GameSettings.PreviewVolume01, 0);
        PlayerPrefs.SetInt(GameSettings.PreviewVolumeKey, 10);
        Check("volume max", GameSettings.PreviewVolume01, 1);

        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
    }
}
