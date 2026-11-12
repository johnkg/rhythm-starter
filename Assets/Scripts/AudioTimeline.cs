using UnityEngine;

/// Song time for one AudioSource: starts it, follows it, pauses and resumes it.
/// Two ways to read the audio, because browsers differ:
///
///  Audio system clock (desktop, default): the song is started with PlayScheduled at an exact moment on the audio
///    timeline, song time = audio system time minus that moment.
///
///  Audio position (WebGL): AudioSource.PlayScheduled is unreliable in WebGL builds (it can start early, Unity issue
///    UUM-27084). So the lead-in runs on real time, Play() is called when song time reaches 0, and once the audio
///    really moves, song time follows AudioSource.timeSamples. Either way the stepwise reading goes through SmoothClock.
///
/// "Free run" means song time follows real time only, because there is no audio reading yet: during the lead-in,
/// just after a resume, and after the audio clip ended (the chart may be longer than the audio).
public sealed class AudioTimeline
{
    readonly AudioSource src;
    public readonly bool PositionMode;
    public readonly SmoothClock Clock = new SmoothClock();

    double zero;            // audio system mode: audio system time at which song time is 0
    bool freeRun;
    double freeOffset;      // song time = realtime + freeOffset while free running
    bool playIssued;
    int awaitSamples;       // free run ends when timeSamples differs from this. -1 = never (no audio)
    bool begun, paused;
    double pausedTime;

    public bool Begun => begun;
    public bool Paused => paused;
    public bool FreeRunning => freeRun;

    public AudioTimeline(AudioSource src, bool positionMode)
    {
        this.src = src;
        PositionMode = positionMode;
    }

    /// Start the song after leadInSec seconds. realtime and dsp are the current readings.
    public void Begin(double leadInSec, double realtime, double dsp)
    {
        Clock.Reset();
        begun = true;
        paused = false;

        if (!PositionMode)
        {
            zero = dsp + leadInSec;
            if (src.clip != null) src.PlayScheduled(zero);   // no clip = silent run, notes still play
            Clock.Add(realtime, dsp);
            freeRun = false;
        }
        else
        {
            freeRun = true;
            freeOffset = -leadInSec - realtime;
            playIssued = false;
            awaitSamples = src.clip != null ? 0 : -1;
        }
    }

    /// Call once per frame, before anything reads the time.
    public void Tick(double realtime, double dsp)
    {
        if (!begun) return;

        if (!PositionMode)
        {
            Clock.Add(realtime, dsp);   // keeps running while paused, so resume starts with a warm estimate
            return;
        }
        if (paused) return;

        if (freeRun)
        {
            if (src.clip == null) return;
            if (!playIssued)
            {
                if (realtime + freeOffset >= 0.0) { src.Play(); playIssued = true; }
                return;
            }
            if (awaitSamples >= 0 && src.timeSamples != awaitSamples)
            {
                // Audio is really moving now: follow it from here.
                freeRun = false;
                Clock.Reset();
                Clock.Add(realtime, Position());
            }
            return;
        }

        Clock.Add(realtime, Position());

        // Audio clip finished but the chart goes on: keep time running on real time.
        if (src.clip != null && TimeAt(realtime) >= src.clip.length)
        {
            double now = TimeAt(realtime);   // read before freeRun flips, TimeAt depends on it
            freeRun = true;
            freeOffset = now - realtime;
            awaitSamples = -1;
        }
    }

    /// Song time (seconds, before the player's offset) at a given real time. Negative during the lead-in.
    public double TimeAt(double realtime)
    {
        if (!begun) return 0.0;
        if (paused) return pausedTime;
        if (!PositionMode) return Clock.SourceAt(realtime) - zero;
        return freeRun ? realtime + freeOffset : Clock.SourceAt(realtime);
    }

    /// Freeze. Refused (false) during the lead-in, when nothing is playing yet.
    public bool Pause(double realtime)
    {
        if (!begun || paused) return false;
        double t = TimeAt(realtime);
        if (t < 0.0) return false;
        if (src.clip != null) src.Pause();
        pausedTime = t;
        paused = true;
        return true;
    }

    /// Continue from the frozen position without gaining or losing time.
    public void Resume(double realtime, double dsp)
    {
        if (!paused) return;
        paused = false;

        if (!PositionMode)
        {
            zero = Clock.SourceAt(realtime) - pausedTime;
        }
        else
        {
            // Audio restarts from where it stopped. Run on real time until it is seen moving.
            freeRun = true;
            freeOffset = pausedTime - realtime;
            playIssued = true;
            awaitSamples = src.clip != null ? src.timeSamples : -1;
        }
        if (src.clip != null) src.UnPause();
    }

    double Position()
    {
        int freq = src.clip.frequency;
        return freq > 0 ? src.timeSamples / (double)freq : 0.0;
    }
}
