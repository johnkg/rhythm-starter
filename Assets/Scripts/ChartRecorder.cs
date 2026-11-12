using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;

/// Chart recorder. Song plays (via Conductor), you tap lanes in time, notes get recorded.
/// Quick tap = tap note. Hold the key longer than holdThresholdSec = hold note.
/// Enter = save JSON. Backspace = undo last note. Also autosaves when the object is disabled.
/// Put this in a scene that has Conductor + AudioSource with your song. No NoteSpawner/Judge needed.
/// Output: <persistentDataPath>/charts/<chartName>.json. Copy it to Assets/Resources/Charts/ to play it.
public class ChartRecorder : MonoBehaviour
{
    [SerializeField] string chartName = "mysong";
    [SerializeField] int lanes = 4;

    [Header("Snap to grid (fixes sloppy taps)")]
    [SerializeField] bool quantize = true;
    [SerializeField] float bpm = 120f;
    [SerializeField] float firstBeatSec = 0f;   // song time of beat 1
    [SerializeField] int snapPerBeat = 4;       // 4 = 16th notes, 2 = 8th notes, 1 = quarter notes

    [Header("Holds")]
    [SerializeField] float holdThresholdSec = 0.25f;   // key down longer than this = hold note

    ChartData chart;
    InputAction[] laneActions;
    InputAction saveAction, undoAction;
    float[] flashTime;
    NoteData[] pressNote;       // per lane: note created by the current key press
    double[] pressRawTime;      // per lane: raw (unsnapped) song time of that press
    string status = "Recording... Enter = save, Backspace = undo";
    bool dirty;

    void Awake()
    {
        chart = new ChartData { title = chartName, lanes = lanes };
        flashTime = new float[lanes];
        pressNote = new NoteData[lanes];
        pressRawTime = new double[lanes];
    }

    void OnEnable()
    {
        laneActions = new InputAction[lanes];
        for (int i = 0; i < lanes && i < KeyBindings.Lanes; i++)
        {
            int lane = i;
            var a = new InputAction($"rec{lane}", InputActionType.Button);
            foreach (var path in KeyBindings.ForLane(i)) a.AddBinding(path);
            a.performed += ctx => OnLane(lane, ctx.time);      // event timestamp, same as Judge
            a.canceled += ctx => OnRelease(lane, ctx.time);
            a.Enable();
            laneActions[i] = a;
        }

        saveAction = new InputAction("save", InputActionType.Button);
        saveAction.AddBinding("<Keyboard>/enter");
        saveAction.performed += _ => Save();
        saveAction.Enable();

        undoAction = new InputAction("undo", InputActionType.Button);
        undoAction.AddBinding("<Keyboard>/backspace");
        undoAction.performed += _ => Undo();
        undoAction.Enable();
    }

    void OnDisable()
    {
        if (dirty) Save();   // don't lose a take if you stop play mode
        foreach (var a in laneActions) { a?.Disable(); a?.Dispose(); }
        saveAction?.Disable(); saveAction?.Dispose();
        undoAction?.Disable(); undoAction?.Dispose();
    }

    double Snap(double t)
    {
        if (!quantize || bpm <= 0f || snapPerBeat <= 0) return t;
        double grid = 60.0 / bpm / snapPerBeat;
        return System.Math.Max(0, firstBeatSec + System.Math.Round((t - firstBeatSec) / grid) * grid);
    }

    void OnLane(int lane, double eventRealtime)
    {
        if (Conductor.Instance == null || !Conductor.Instance.Started) return;

        // Calibration offset is applied inside Conductor, so recorded time = what player meant to hit.
        double raw = Conductor.Instance.SongTimeAtRealtime(eventRealtime);
        if (raw < 0) return;   // still in lead-in

        float ms = Mathf.Round((float)(Snap(raw) * 1000.0));

        // Skip duplicate: same lane, same time (two taps snapped to one slot)
        foreach (var n in chart.notes)
            if (n.lane == lane && Mathf.Abs(n.time_ms - ms) < 1f) return;

        var note = new NoteData { time_ms = ms, lane = lane, type = "tap" };
        chart.notes.Add(note);
        pressNote[lane] = note;        // may become a hold on release
        pressRawTime[lane] = raw;
        flashTime[lane] = Time.unscaledTime;
        dirty = true;
    }

    void OnRelease(int lane, double eventRealtime)
    {
        var note = pressNote[lane];
        if (note == null || Conductor.Instance == null) return;
        pressNote[lane] = null;

        double rawEnd = Conductor.Instance.SongTimeAtRealtime(eventRealtime);
        double dur = rawEnd - pressRawTime[lane];
        if (dur < holdThresholdSec) return;   // short press stays a tap

        if (quantize && bpm > 0f && snapPerBeat > 0)
        {
            double grid = 60.0 / bpm / snapPerBeat;
            dur = System.Math.Max(grid, System.Math.Round(dur / grid) * grid);
        }
        note.duration_ms = Mathf.Round((float)(dur * 1000.0));
        note.type = "hold";
        dirty = true;
    }

    void Undo()
    {
        if (chart.notes.Count == 0) return;
        chart.notes.RemoveAt(chart.notes.Count - 1);
        dirty = true;
    }

    void Save()
    {
        chart.notes.Sort((a, b) => a.time_ms.CompareTo(b.time_ms));
        // Desktop: file in persistentDataPath. Web build: browser download.
        string where = WebDownload.SaveText("charts", chartName + ".json", JsonUtility.ToJson(chart, true));
        dirty = false;
        status = $"Saved {chart.notes.Count} notes:\n{where}";
        Debug.Log("Chart saved: " + where);
    }

    void OnGUI()
    {
        GUI.skin.label.fontSize = 24;
        GUI.Label(new Rect(20, 20, 1200, 40), $"Notes: {chart.notes.Count}   Song time: {Conductor.Instance.SongTime:0.00}s");
        GUI.Label(new Rect(20, 60, 1200, 90), status);

        // Lane boxes: bright on press, stay bright while key is held.
        for (int i = 0; i < lanes; i++)
        {
            bool lit = pressNote[i] != null || Time.unscaledTime - flashTime[i] < 0.12f;
            GUI.color = lit ? Color.white : new Color(1f, 1f, 1f, 0.2f);
            GUI.Box(new Rect(20 + i * 110, 170, 100, 100), i < KeyBindings.Lanes ? KeyBindings.KeyLabel(i) : (i + 1).ToString());
        }
        GUI.color = Color.white;
    }
}
