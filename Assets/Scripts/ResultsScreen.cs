using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// Shows results when the last note is done. Saves best score per song.
/// On the results screen: Esc / B = song select, Enter / Space / A = retry.
public class ResultsScreen : MonoBehaviour
{
    [SerializeField] Judge judge;
    [SerializeField] NoteSpawner spawner;
    [SerializeField] float endDelaySec = 1.5f;   // wait after last note before showing

    bool shown;
    public bool Shown => shown;
    bool newBest;
    int bestBefore;
    string rank = "";
    InputAction retry, back;

    void OnEnable()
    {
        retry = new InputAction("retry", InputActionType.Button);
        retry.AddBinding("<Keyboard>/enter");
        retry.AddBinding("<Keyboard>/space");
        retry.AddBinding("<Gamepad>/buttonSouth");
        retry.performed += _ => { if (shown) SceneManager.LoadScene(SceneManager.GetActiveScene().name); };
        retry.Enable();

        back = new InputAction("back", InputActionType.Button);
        back.AddBinding("<Keyboard>/escape");
        back.AddBinding("<Gamepad>/buttonEast");
        back.performed += _ => { if (shown) SceneManager.LoadScene(GameSession.MenuScene); };   // during play, PauseMenu owns Esc
        back.Enable();
    }

    void OnDisable()
    {
        retry?.Disable(); retry?.Dispose();
        back?.Disable(); back?.Dispose();
    }

    void Update()
    {
        if (shown || !Conductor.Instance.Started) return;
        if (judge.AllNotesDone && Conductor.Instance.SongTime >= spawner.LastNoteTime + endDelaySec)
            Show();
    }

    void Show()
    {
        shown = true;

        float acc = judge.Accuracy;
        rank = acc >= 0.95f ? "S" : acc >= 0.85f ? "A" : acc >= 0.70f ? "B" : acc >= 0.50f ? "C" : "D";

        string id = GameSession.Selected != null ? GameSession.Selected.id : "default";
        string key = GameSession.BestKey(id);
        bestBefore = PlayerPrefs.GetInt(key, 0);
        newBest = judge.Score > bestBefore;
        if (newBest)
        {
            PlayerPrefs.SetInt(key, judge.Score);
            PlayerPrefs.Save();
        }
    }

    void OnGUI()
    {
        if (!shown) return;
        GUI.depth = -10;   // draw on top of HUD

        var big = new GUIStyle(GUI.skin.label) { fontSize = 120, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        var mid = new GUIStyle(GUI.skin.label) { fontSize = 34, alignment = TextAnchor.MiddleCenter };

        var r = new Rect(Screen.width * 0.2f, Screen.height * 0.08f, Screen.width * 0.6f, Screen.height * 0.84f);
        GUI.Box(r, GUIContent.none);
        GUILayout.BeginArea(r);

        string title = GameSession.Selected != null ? GameSession.Selected.title : "Results";
        GUILayout.Label(title, mid);
        GUILayout.Label(rank, big);
        GUILayout.Label($"Score  {judge.Score}", mid);
        GUILayout.Label($"Accuracy  {judge.Accuracy * 100f:0.0}%     Max combo  {judge.MaxCombo}", mid);
        GUILayout.Label($"Perfect {judge.PerfectCount}   Great {judge.GreatCount}   Good {judge.GoodCount}", mid);
        GUILayout.Label($"Miss {judge.MissCount}   Dropped holds {judge.DropCount}", mid);

        bool fc = judge.MissCount == 0 && judge.DropCount == 0;
        if (fc) GUILayout.Label("FULL COMBO", mid);
        GUILayout.Label(newBest ? "NEW BEST!" : $"Best  {bestBefore}", mid);

        GUILayout.Space(20);
        GUILayout.Label("Enter = retry     Esc = song select", mid);
        GUILayout.EndArea();
    }
}
