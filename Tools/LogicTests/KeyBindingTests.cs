using System;
using UnityEngine;

static class KeyBindingTests
{
    static int fails;
    static void Check(string name, object got, object want)
    {
        bool ok = got.ToString() == want.ToString();
        if (!ok) fails++;
        Console.WriteLine((ok ? "PASS " : "FAIL ") + name + "  got=" + got + " want=" + want);
    }

    static string Keys() => KeyBindings.KeyName(0) + KeyBindings.KeyName(1) + KeyBindings.KeyName(2) + KeyBindings.KeyName(3);

    static void Main()
    {
        Check("defaults", Keys(), "dfjk");
        Check("label", KeyBindings.KeyLabel(2), "J");
        Check("lane0 bindings", string.Join(",", KeyBindings.ForLane(0)), "<Keyboard>/d,<Gamepad>/dpad/left,<Gamepad>/buttonWest");
        Check("lane3 bindings", string.Join(",", KeyBindings.ForLane(3)), "<Keyboard>/k,<Gamepad>/dpad/right,<Gamepad>/buttonEast");

        KeyBindings.SetKey(0, "a");
        Check("rebind free key", Keys(), "afjk");

        KeyBindings.SetKey(0, "f");   // f is lane 1's key: swap, lane 1 gets lane 0's old key "a"
        Check("rebind taken key swaps", Keys(), "fajk");

        KeyBindings.SetKey(2, "j");   // same key again, no change
        Check("rebind same key", Keys(), "fajk");

        KeyBindings.SetKey(3, "space");
        Check("special key name", KeyBindings.KeyLabel(3), "SPACE");
        Check("no duplicates", Keys(), "fajspace");

        KeyBindings.ResetAll();
        Check("reset", Keys(), "dfjk");

        // Settings persist through GameSettings too
        Check("default speed", GameSettings.NoteSpeed, 7);
        Check("default approach", GameSettings.ApproachSec, 1.5f);
        PlayerPrefs.SetInt(GameSettings.NoteSpeedKey, 10);
        Check("speed 10 approach", GameSettings.ApproachSec, 0.75f);
        PlayerPrefs.SetInt(GameSettings.NoteSpeedKey, 1);
        Check("speed 1 approach", GameSettings.ApproachSec, 3f);

        Console.WriteLine(fails == 0 ? "ALL OK" : fails + " FAILED");
        if (fails != 0) Environment.Exit(1);   // run.sh and the build server stop on a failed suite
    }
}
