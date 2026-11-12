using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// Metronome calibration. Plays clicks, player taps along, we measure average
/// (tap time - click time) and save it as the audio offset.
/// Measures audio output latency + player habit together, which is what Conductor needs.
/// Put on a GameObject in its own scene (or a settings menu). Needs an AudioSource.
[RequireComponent(typeof(AudioSource))]
public class CalibrationScreen : MonoBehaviour
{
    public const string PrefKey = "audioOffsetSec";

    [SerializeField] float bpm = 120f;
    [SerializeField] int warmupBeats = 4;     // ignore taps for first beats, player is still catching on
    [SerializeField] int tapsNeeded = 16;
    [SerializeField] float startDelay = 1f;

    AudioSource src;
    double beat;                              // seconds per beat, exact to the sample
    double dspStart, lastDsp, lastDspReal;
    readonly List<float> deltas = new List<float>();
    InputAction tap;
    bool done;
    float resultSec, spreadSec;

    void Awake()
    {
        src = GetComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = true;

        // Build a 1-beat looping click clip in code. No asset needed.
        int rate = AudioSettings.outputSampleRate;
        int len = Mathf.RoundToInt((60f / bpm) * rate);
        beat = len / (double)rate;            // use real loop length so clicks never drift
        var data = new float[len];
        int clickLen = rate / 50;             // 20 ms blip
        for (int i = 0; i < clickLen && i < len; i++)
        {
            float env = 1f - (float)i / clickLen;
            data[i] = Mathf.Sin(2f * Mathf.PI * 1000f * i / rate) * env * 0.8f;
        }
        var clip = AudioClip.Create("click", len, 1, rate, false);
        clip.SetData(data, 0);
        src.clip = clip;

        lastDsp = AudioSettings.dspTime;
        lastDspReal = Time.realtimeSinceStartupAsDouble;
    }

    void OnEnable()
    {
        tap = new InputAction("tap", InputActionType.Button);
        tap.AddBinding("<Keyboard>/space");
        tap.AddBinding("<Gamepad>/buttonSouth");
        tap.AddBinding("<Mouse>/leftButton");
        tap.AddBinding("<Touchscreen>/primaryTouch/press");
        tap.performed += ctx => OnTap(ctx.time);
        tap.Enable();
        StartRun();
    }

    void OnDisable()
    {
        tap?.Disable();
        tap?.Dispose();
        if (src != null) src.Stop();
    }

    void StartRun()
    {
        deltas.Clear();
        done = false;
        src.Stop();
        dspStart = AudioSettings.dspTime + startDelay;
        src.PlayScheduled(dspStart);          // sample-accurate start on the audio timeline
    }

    void Update()
    {
        // Same dsp-to-realtime mapping as Conductor. dspTime steps per audio buffer, so interpolate.
        double d = AudioSettings.dspTime;
        if (d != lastDsp)
        {
            lastDsp = d;
            lastDspReal = Time.realtimeSinceStartupAsDouble;
        }

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame) StartRun();
    }

    void OnTap(double eventRealtime)
    {
        if (done) return;

        // Event timestamp, not frame time. Same clock as Time.realtimeSinceStartup.
        double dsp = lastDsp + (eventRealtime - lastDspReal);
        double t = dsp - dspStart;
        if (t < warmupBeats * beat) return;

        // Distance to nearest click, wrapped to [-beat/2, +beat/2]. Positive = late.
        double phase = t % beat;
        if (phase > beat * 0.5) phase -= beat;
        deltas.Add((float)phase);

        if (deltas.Count >= tapsNeeded) Finish();
    }

    void Finish()
    {
        done = true;
        var sorted = new List<float>(deltas);
        sorted.Sort();
        resultSec = sorted[sorted.Count / 2];          // median: ignores a few bad taps

        double sumSq = 0;
        foreach (var x in deltas) sumSq += (x - resultSec) * (x - resultSec);
        spreadSec = Mathf.Sqrt((float)(sumSq / deltas.Count));

        // Conductor subtracts userOffsetSec from song time. Late taps -> positive offset. Signs match.
        PlayerPrefs.SetFloat(PrefKey, resultSec);
        PlayerPrefs.Save();
        if (Conductor.Instance != null) Conductor.Instance.userOffsetSec = resultSec;
        src.Stop();
    }

    void OnGUI()
    {
        GUI.skin.label.fontSize = 28;
        if (!done)
        {
            GUI.Label(new Rect(20, 20, 900, 40), "Tap SPACE / A button / click to the beat.");
            GUI.Label(new Rect(20, 60, 900, 40), $"Taps {deltas.Count} / {tapsNeeded}   (first {warmupBeats} beats ignored)");
            if (deltas.Count > 0)
                GUI.Label(new Rect(20, 100, 900, 40), $"Last: {deltas[deltas.Count - 1] * 1000f:+0;-0} ms");
        }
        else
        {
            GUI.Label(new Rect(20, 20, 900, 40), $"Offset saved: {resultSec * 1000f:+0;-0} ms");
            GUI.Label(new Rect(20, 60, 900, 40), spreadSec > 0.05f
                ? $"Taps were uneven (spread {spreadSec * 1000f:0} ms). Press R to retry."
                : $"Good consistency (spread {spreadSec * 1000f:0} ms). Press R to redo.");
        }
    }
}
