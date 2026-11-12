using System.Collections.Generic;
using UnityEngine;

/// Lane key bindings. Keyboard key is rebindable (saved in PlayerPrefs), gamepad layout is fixed.
/// Judge and ChartRecorder both read from here so they always agree.
public static class KeyBindings
{
    public const int Lanes = 4;
    const string Prefix = "lanekey";

    // Key names are Input System control names: "d", "f", "space", "semicolon", ...
    static readonly string[] DefaultKeys = { "d", "f", "j", "k" };

    static readonly string[][] Pad =
    {
        new[] { "<Gamepad>/dpad/left",  "<Gamepad>/buttonWest"  },
        new[] { "<Gamepad>/dpad/down",  "<Gamepad>/buttonSouth" },
        new[] { "<Gamepad>/dpad/up",    "<Gamepad>/buttonNorth" },
        new[] { "<Gamepad>/dpad/right", "<Gamepad>/buttonEast"  },
    };

    public static string KeyName(int lane) => PlayerPrefs.GetString(Prefix + lane, DefaultKeys[lane]);

    /// Short label for UI, e.g. "D", "SPACE".
    public static string KeyLabel(int lane) => KeyName(lane).ToUpperInvariant();

    /// Binding paths for one lane: the keyboard key plus the gamepad buttons.
    public static string[] ForLane(int lane)
    {
        var list = new List<string> { "<Keyboard>/" + KeyName(lane) };
        list.AddRange(Pad[lane]);
        return list.ToArray();
    }

    /// Bind a key to a lane. If another lane already uses it, that lane gets this lane's old key (swap).
    public static void SetKey(int lane, string keyName)
    {
        string old = KeyName(lane);
        for (int other = 0; other < Lanes; other++)
        {
            if (other != lane && KeyName(other) == keyName)
                PlayerPrefs.SetString(Prefix + other, old);
        }
        PlayerPrefs.SetString(Prefix + lane, keyName);
    }

    public static void ResetAll()
    {
        for (int i = 0; i < Lanes; i++) PlayerPrefs.SetString(Prefix + i, DefaultKeys[i]);
    }
}
