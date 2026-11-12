using System.Collections.Generic;
using UnityEngine;

/// Runtime wrapper: chart note + state. Judge changes the flags.
public class NoteRuntime
{
    public NoteData data;
    public NoteView view;
    public bool resolved;    // fully finished: tap judged, hold completed or dropped, or head missed
    public bool holding;     // hold head was hit, key is currently down
    public Grade hitGrade;   // grade of the head (valid once judged)

    public double Time => data.time_ms / 1000.0;
    public double EndTime => Time + data.duration_ms / 1000.0;
    public bool IsHold => data.duration_ms > 0f;
}

/// Loads chart, spawns note visuals ahead of hit time.
/// Chart lives in Assets/Resources/Charts/<chartName>.json (Resources = easy on Switch too).
public class NoteSpawner : MonoBehaviour
{
    [SerializeField] string chartName = "demo";
    [SerializeField] NoteView notePrefab;
    [SerializeField] float spawnY = 6f;
    [SerializeField] float hitY = -3f;
    [SerializeField] float laneSpacing = 1.5f;

    public List<NoteRuntime>[] Lanes { get; private set; }
    public int TotalNotes { get; private set; }
    public double LastNoteTime { get; private set; }   // end time of the last note (hold end included)
    public float ApproachSec => approachSec;           // from player setting (note speed)
    public float VisualOffsetSec => visualOffsetSec;   // display lag compensation, from player setting
    public float SpawnY => spawnY;
    public float HitY => hitY;

    List<NoteRuntime> all = new List<NoteRuntime>();
    int nextSpawn;
    int laneCount;
    float approachSec = 1.5f;
    float visualOffsetSec;

    /// Called by GameBootstrap (song select) BEFORE Awake loads the chart.
    public void SetChart(string name) => chartName = name;

    void Awake()
    {
        approachSec = GameSettings.ApproachSec;
        visualOffsetSec = GameSettings.VisualOffsetSec;

        var asset = Resources.Load<TextAsset>("Charts/" + chartName);
        if (asset == null)
        {
            Debug.LogError($"Chart not found: Resources/Charts/{chartName}.json");
            enabled = false;
            return;
        }

        var chart = JsonUtility.FromJson<ChartData>(asset.text);
        laneCount = Mathf.Max(1, chart.lanes);
        Lanes = new List<NoteRuntime>[laneCount];
        for (int i = 0; i < laneCount; i++) Lanes[i] = new List<NoteRuntime>();

        chart.notes.Sort((a, b) => a.time_ms.CompareTo(b.time_ms));
        foreach (var n in chart.notes)
        {
            if (n.lane < 0 || n.lane >= laneCount) continue;
            var rt = new NoteRuntime { data = n };
            all.Add(rt);
            Lanes[n.lane].Add(rt);
            LastNoteTime = System.Math.Max(LastNoteTime, rt.EndTime);
        }
        TotalNotes = all.Count;
    }

    void Update()
    {
        if (Conductor.Instance == null || !Conductor.Instance.Started) return;
        double t = Conductor.Instance.SongTime + visualOffsetSec;   // same clock the views use

        while (nextSpawn < all.Count && all[nextSpawn].Time - t <= approachSec)
        {
            var rt = all[nextSpawn++];
            var view = Instantiate(notePrefab, transform);
            view.Init(rt, this, LaneX(rt.data.lane));
            rt.view = view;
        }
    }

    public float LaneX(int lane) => (lane - (laneCount - 1) * 0.5f) * laneSpacing;
    public float LaneSpacing => laneSpacing;
}
