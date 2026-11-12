using System;
using UnityEngine;
using UnityEngine.InputSystem;

public enum Grade { Perfect, Great, Good, Miss }

/// Reads input with event timestamps, converts to song time, grades against nearest note in lane.
/// Tap: judged on press. Hold: head judged on press, then key must stay down until the end.
/// Hold is lenient: if you are still holding at end time it completes by itself.
/// Needs Unity Input System package.
public class Judge : MonoBehaviour
{
    [SerializeField] NoteSpawner spawner;

    [Header("Windows (seconds, +/- from note time)")]
    [SerializeField] float perfectWindow = 0.040f;
    [SerializeField] float greatWindow = 0.080f;
    [SerializeField] float goodWindow = 0.120f;   // beyond this = miss. Also release leniency on hold end.

    const int HoldBonus = 500;

    public int Score { get; private set; }
    public int Combo { get; private set; }
    public int MaxCombo { get; private set; }
    public int PerfectCount { get; private set; }
    public int GreatCount { get; private set; }
    public int GoodCount { get; private set; }
    public int MissCount { get; private set; }
    public int DropCount { get; private set; }   // holds released too early

    public bool AllNotesDone =>
        spawner != null && spawner.Lanes != null && spawner.TotalNotes > 0 && resolvedCount >= spawner.TotalNotes;

    /// 0..1. Perfect=1, Great=0.7, Good=0.4, dropped hold -0.5.
    public float Accuracy
    {
        get
        {
            if (spawner.TotalNotes == 0) return 0f;
            float pts = PerfectCount + 0.7f * GreatCount + 0.4f * GoodCount - 0.5f * DropCount;
            return Mathf.Clamp01(pts / spawner.TotalNotes);
        }
    }

    /// lane, grade, delta seconds (negative = early, positive = late)
    public event Action<int, Grade, float> OnJudged;

    InputAction[] actions;
    int[] head;                   // per lane: index of first note not yet judged
    NoteRuntime[] activeHold;     // per lane: hold currently being held
    int resolvedCount;

    /// Fired on every lane press, before judging. For input feedback (lane glow). Not fired while paused.
    public event Action<int> OnLanePressed;

    // Key bindings come from KeyBindings (player can rebind in Settings).

    void Awake()
    {
        OnJudged += (lane, g, d) => lastText = $"{g}  {(d * 1000f):+0;-0}ms";
    }

    void Start()
    {
        if (spawner.Lanes == null) { enabled = false; return; }   // chart failed to load
        head = new int[spawner.Lanes.Length];
        activeHold = new NoteRuntime[spawner.Lanes.Length];
    }

    void OnEnable()
    {
        actions = new InputAction[KeyBindings.Lanes];
        for (int i = 0; i < KeyBindings.Lanes; i++)
        {
            int lane = i;   // capture
            var a = new InputAction($"lane{lane}", InputActionType.Button);
            foreach (var path in KeyBindings.ForLane(i)) a.AddBinding(path);
            // ctx.time = timestamp of the input EVENT, not of this frame. This is the whole point.
            a.performed += ctx => OnPress(lane, ctx.time);
            a.canceled += ctx => OnRelease(lane, ctx.time);
            a.Enable();
            actions[i] = a;
        }
    }

    void OnDisable()
    {
        if (actions == null) return;
        foreach (var a in actions) { a.Disable(); a.Dispose(); }
    }

    void OnPress(int lane, double eventRealtime)
    {
        if (head == null || Conductor.Instance == null || Conductor.Instance.Paused || lane >= head.Length) return;

        OnLanePressed?.Invoke(lane);

        // Input event time is on the same clock as Time.realtimeSinceStartup.
        double songT = Conductor.Instance.SongTimeAtRealtime(eventRealtime);

        var notes = spawner.Lanes[lane];
        while (head[lane] < notes.Count && (notes[head[lane]].resolved || notes[head[lane]].holding)) head[lane]++;
        if (head[lane] >= notes.Count) return;

        var note = notes[head[lane]];
        float delta = (float)(songT - note.Time);   // negative = early
        float abs = Mathf.Abs(delta);

        if (abs > goodWindow) return;   // too early to count: ghost tap, no penalty

        Grade g = abs <= perfectWindow ? Grade.Perfect
                : abs <= greatWindow   ? Grade.Great
                :                        Grade.Good;

        note.hitGrade = g;
        if (note.IsHold)
        {
            note.holding = true;        // not resolved yet, waits for release or end
            activeHold[lane] = note;
        }
        else
        {
            Finish(note);
        }
        Register(lane, g, delta);
    }

    void OnRelease(int lane, double eventRealtime)
    {
        // Input System fires 'canceled' while disabling actions on scene unload. Conductor may be gone by then.
        if (activeHold == null || Conductor.Instance == null || Conductor.Instance.Paused || lane >= activeHold.Length) return;
        var note = activeHold[lane];
        if (note == null) return;   // plain tap release, ignore

        double songT = Conductor.Instance.SongTimeAtRealtime(eventRealtime);
        activeHold[lane] = null;
        note.holding = false;
        Finish(note);

        if (songT >= note.EndTime - goodWindow)
        {
            Score += HoldBonus;     // held to the end
        }
        else
        {
            DropCount++;            // let go too early
            Combo = 0;
            OnJudged?.Invoke(lane, Grade.Miss, 0f);
        }
    }

    void Update()
    {
        if (head == null || !Conductor.Instance.Started) return;
        double t = Conductor.Instance.SongTime;

        for (int lane = 0; lane < spawner.Lanes.Length; lane++)
        {
            // Still holding at end time: complete it for the player.
            var h = activeHold[lane];
            if (h != null && t >= h.EndTime)
            {
                activeHold[lane] = null;
                h.holding = false;
                Finish(h);
                Score += HoldBonus;
            }

            // Auto-miss heads that slid past the good window.
            var notes = spawner.Lanes[lane];
            while (head[lane] < notes.Count)
            {
                var n = notes[head[lane]];
                if (n.resolved || n.holding) { head[lane]++; continue; }
                if (t - n.Time > goodWindow)
                {
                    n.hitGrade = Grade.Miss;
                    Finish(n);
                    Register(lane, Grade.Miss, goodWindow);
                    head[lane]++;
                }
                else break;
            }
        }
    }

    /// Called when the game pauses. Holds in progress count as dropped, so pausing is not a free pass.
    public void DropActiveHolds()
    {
        if (activeHold == null) return;
        for (int lane = 0; lane < activeHold.Length; lane++)
        {
            var note = activeHold[lane];
            if (note == null) continue;
            activeHold[lane] = null;
            note.holding = false;
            Finish(note);
            DropCount++;
            Combo = 0;
            OnJudged?.Invoke(lane, Grade.Miss, 0f);
        }
    }

    void Finish(NoteRuntime n)
    {
        if (n.resolved) return;
        n.resolved = true;
        resolvedCount++;
    }

    void Register(int lane, Grade g, float delta)
    {
        switch (g)
        {
            case Grade.Perfect: Score += 1000; Combo++; PerfectCount++; break;
            case Grade.Great:   Score += 700;  Combo++; GreatCount++;   break;
            case Grade.Good:    Score += 400;  Combo++; GoodCount++;    break;
            case Grade.Miss:    Combo = 0;     MissCount++;             break;
        }
        MaxCombo = Mathf.Max(MaxCombo, Combo);
        OnJudged?.Invoke(lane, g, delta);
    }

    // Throwaway HUD. Replace with real UI later.
    string lastText = "";
    void OnGUI()
    {
        GUI.skin.label.fontSize = 28;
        GUI.Label(new Rect(20, 20, 600, 40), $"Score {Score}   Combo {Combo}   Max {MaxCombo}");
        GUI.Label(new Rect(20, 60, 600, 40), lastText);
    }
}
