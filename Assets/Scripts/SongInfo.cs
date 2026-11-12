using System;
using System.Collections.Generic;

/// One entry in Resources/songs.json.
/// audio = Resources path without extension, e.g. "Audio/demo" -> Assets/Resources/Audio/demo.ogg
/// chart = name of Resources/Charts/<chart>.json
[Serializable]
public class SongInfo
{
    public string id;       // unique, used as high score key
    public string title;
    public string artist;
    public float bpm;
    public string audio;
    public string chart;
    public int level;
    public float previewStart;   // seconds into the audio where the select-screen preview starts. 0 = automatic
}

[Serializable]
public class SongList
{
    public List<SongInfo> songs = new List<SongInfo>();
}

/// Carries the picked song between scenes.
public static class GameSession
{
    // Scene names. Add all three to File > Build Settings.
    public const string MenuScene = "SongSelect";
    public const string GameScene = "Game";
    public const string CalibrationScene = "Calibration";
    public const string SettingsScene = "Settings";

    public static SongInfo Selected;   // null if Game scene is started directly in editor

    public static string BestKey(string songId) => "best_" + songId;
}
