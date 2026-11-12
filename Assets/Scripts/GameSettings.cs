using UnityEngine;

/// Player settings, stored in PlayerPrefs. Read once when a scene starts (cache it, do not poll per frame).
public static class GameSettings
{
    public const string AudioOffsetKey = "audioOffsetSec";
    public const string VisualOffsetKey = "visualOffsetSec";
    public const string NoteSpeedKey = "noteSpeed";

    public const string PreviewVolumeKey = "previewVolume";
    public const int MaxPreviewVolume = 10;
    public const int DefaultPreviewVolume = 7;

    public const string TapVolumeKey = "tapVolume";
    public const int MaxTapVolume = 10;
    public const int DefaultTapVolume = 7;

    public const int MinNoteSpeed = 1;
    public const int MaxNoteSpeed = 10;
    public const int DefaultNoteSpeed = 7;

    /// Audio offset in seconds. Set by calibration, fine-tuned in settings.
    public static float AudioOffsetSec(float fallback) => PlayerPrefs.GetFloat(AudioOffsetKey, fallback);

    /// Display lag compensation in seconds. Positive = draw notes a bit ahead of audio time.
    public static float VisualOffsetSec => PlayerPrefs.GetFloat(VisualOffsetKey, 0f);

    /// 1 (slow) .. 10 (fast).
    public static int NoteSpeed => PlayerPrefs.GetInt(NoteSpeedKey, DefaultNoteSpeed);

    /// Song select preview volume, 0 (off) .. 10.
    public static int PreviewVolume => PlayerPrefs.GetInt(PreviewVolumeKey, DefaultPreviewVolume);
    public static float PreviewVolume01 => PreviewVolume / (float)MaxPreviewVolume;

    /// Hit sound volume, 0 (off) .. 10.
    public static int TapVolume => PlayerPrefs.GetInt(TapVolumeKey, DefaultTapVolume);
    public static float TapVolume01 => TapVolume / (float)MaxTapVolume;

    /// Seconds a note takes to travel from spawn to hit line. Speed 7 = 1.5 s, speed 10 = 0.75 s, speed 1 = 3.0 s.
    public static float ApproachSec => 3.0f - 0.25f * (NoteSpeed - 1);
}
