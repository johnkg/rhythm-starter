using UnityEngine;

public enum ClockMode
{
    Auto,              // audio position on WebGL builds, audio system clock everywhere else
    AudioSystemClock,
    AudioPosition
}

/// Audio clock = source of truth. Everything else reads SongTime from here.
/// The work is done by AudioTimeline (see there for the two clock modes) and SmoothClock.
[RequireComponent(typeof(AudioSource))]
public class Conductor : MonoBehaviour
{
    public static Conductor Instance { get; private set; }

    [SerializeField] AudioSource music;
    [SerializeField] float startDelay = 2f;      // lead-in before song starts (sec)
    [SerializeField] ClockMode clockMode = ClockMode.Auto;
    [Tooltip("Calibration offset in seconds. Positive = player hits late, shift song time back.")]
    public float userOffsetSec = 0f;

    AudioTimeline timeline;

    public bool Started { get; private set; }
    public bool Paused => timeline != null && timeline.Paused;

    /// For the timing overlay and tests.
    public AudioTimeline Timeline => timeline;

    static bool WebBuild
    {
        get
        {
#if UNITY_WEBGL && !UNITY_EDITOR
            return true;
#else
            return false;
#endif
        }
    }

    void Awake()
    {
        Instance = this;
        // Saved by CalibrationScreen / SettingsScreen. Falls back to inspector value if never set.
        userOffsetSec = GameSettings.AudioOffsetSec(userOffsetSec);
        if (music == null) music = GetComponent<AudioSource>();

        bool positionMode = clockMode == ClockMode.AudioPosition || (clockMode == ClockMode.Auto && WebBuild);
        timeline = new AudioTimeline(music, positionMode);
    }

    /// Called by GameBootstrap (song select) before Start. Null clip = silent run, clock still works.
    public void SetClip(AudioClip clip)
    {
        if (music == null) music = GetComponent<AudioSource>();
        music.clip = clip;
    }

    void Start()
    {
        timeline.Begin(startDelay, Time.realtimeSinceStartupAsDouble, AudioSettings.dspTime);
        Started = true;
    }

    void Update()
    {
        timeline.Tick(Time.realtimeSinceStartupAsDouble, AudioSettings.dspTime);
    }

    /// Freeze song time and audio. Ignored during lead-in (nothing playing yet).
    public void Pause()
    {
        if (!Started) return;
        timeline.Pause(Time.realtimeSinceStartupAsDouble);
    }

    /// Continue from the frozen position. No time is lost or gained.
    public void Resume()
    {
        if (!Started) return;
        timeline.Resume(Time.realtimeSinceStartupAsDouble, AudioSettings.dspTime);
    }

    /// Current song time in seconds. Negative during lead-in. Frozen while paused.
    public double SongTime => SongTimeAtRealtime(Time.realtimeSinceStartupAsDouble);

    /// Song time for a given Time.realtimeSinceStartup moment.
    /// Used to convert input event timestamps into song time.
    public double SongTimeAtRealtime(double realtime)
    {
        return timeline == null ? 0.0 : timeline.TimeAt(realtime) - userOffsetSec;
    }
}
