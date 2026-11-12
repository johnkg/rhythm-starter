using System;
using System.Reflection;
using UnityEngine;

// PauseMenu.OnApplicationFocus is compiled out in the editor (#if !UNITY_EDITOR), tests build without that define, so it is present.
static class FocusTests
{
    const BindingFlags F = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static int fails;
    static void Check(string name, object got, object want)
    {
        bool ok = got.ToString() == want.ToString();
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + "  got=" + got + " want=" + want);
    }
    static void Set(object o, string n, object v)
    {
        var f = o.GetType().GetField(n, F);
        if (f != null) { f.SetValue(o, v); return; }
        o.GetType().GetProperty(n, F).GetSetMethod(true).Invoke(o, new[] { v });   // auto-property with private setter
    }
    static object Get(object o, string n) { return o.GetType().GetField(n, F).GetValue(o); }
    static void Call(object o, string n, params object[] a) { o.GetType().GetMethod(n, F).Invoke(o, a); }

    static void Main()
    {
        var c = new Conductor();
        Set(c, "music", new AudioSource());
        Set(c, "startDelay", 0f);
        Time.realtimeSinceStartupAsDouble = 0; AudioSettings.dspTime = 0;
        Call(c, "Awake"); Call(c, "Start");
        Time.realtimeSinceStartupAsDouble = 10; AudioSettings.dspTime = 10; Call(c, "Update");

        var lanes = new System.Collections.Generic.List<NoteRuntime>[4];
        for (int i = 0; i < 4; i++) lanes[i] = new System.Collections.Generic.List<NoteRuntime>();
        var spawner = new NoteSpawner();
        Set(spawner, "Lanes", lanes);
        var judge = new Judge();
        Set(judge, "spawner", spawner);
        Call(judge, "Awake"); Call(judge, "Start");

        var menu = new PauseMenu();
        Set(menu, "judge", judge);

        Call(menu, "OnApplicationFocus", true);
        Check("gaining focus does not pause", c.Paused, false);

        Call(menu, "OnApplicationFocus", false);
        Check("losing focus pauses clock", c.Paused, true);
        Check("menu shows paused", Get(menu, "paused"), true);

        // Player already resuming (countdown running): a second focus loss must not reset the countdown
        Set(menu, "countdown", 2f);
        Call(menu, "OnApplicationFocus", false);
        Check("second focus loss keeps countdown", Get(menu, "countdown"), 2f);

        // Focus loss during lead-in: refused, no stuck menu
        var c2 = new Conductor();
        Set(c2, "music", new AudioSource());
        Set(c2, "startDelay", 40f);                              // song starts 40 s in, now is 10: lead-in
        Time.realtimeSinceStartupAsDouble = 0; AudioSettings.dspTime = 0;
        Call(c2, "Awake"); Call(c2, "Start");
        Time.realtimeSinceStartupAsDouble = 10; AudioSettings.dspTime = 10; Call(c2, "Update");
        var menu2 = new PauseMenu();
        Set(menu2, "judge", judge);
        Call(menu2, "OnApplicationFocus", false);
        Check("lead-in focus loss: clock not paused", c2.Paused, false);
        Check("lead-in focus loss: menu not shown", Get(menu2, "paused"), false);

        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        if (fails != 0) Environment.Exit(1);   // run.sh and the build server stop on a failed suite
    }
}
