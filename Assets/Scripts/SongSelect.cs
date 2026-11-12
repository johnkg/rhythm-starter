using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// Song select screen. Reads Resources/songs.json. Throwaway IMGUI UI, swap for real UI later.
/// Up/Down (or W/S, d-pad) = move. Enter / Space / A = play. C = calibration. O = settings. Esc = quit.
public class SongSelect : MonoBehaviour
{
    SongList list = new SongList();
    int index;
    InputAction up, down, confirm, calibrate, options, quit;
    PreviewPlayer preview;

#if UNITY_WEBGL
    const string Hint = "Up/Down = choose    Enter = play    C = calibrate    O = settings";   // a browser tab cannot quit itself
#else
    const string Hint = "Up/Down = choose    Enter = play    C = calibrate    O = settings    Esc = quit";
#endif

    void Awake()
    {
        var asset = Resources.Load<TextAsset>("songs");
        if (asset != null) list = JsonUtility.FromJson<SongList>(asset.text);
        else Debug.LogError("Resources/songs.json not found");

        // Coming back from a song: start on the one just played.
        if (GameSession.Selected != null)
        {
            int found = list.songs.FindIndex(s => s.id == GameSession.Selected.id);
            if (found >= 0) index = found;
        }

        preview = gameObject.AddComponent<PreviewPlayer>();   // also adds the AudioSource it needs
    }

    void Start()
    {
        RequestPreview();
    }

    void RequestPreview()
    {
        preview.Request(list.songs.Count > 0 ? list.songs[index] : null);
    }

    void OnEnable()
    {
        up = Make("up", "<Keyboard>/upArrow", "<Keyboard>/w", "<Gamepad>/dpad/up");
        down = Make("down", "<Keyboard>/downArrow", "<Keyboard>/s", "<Gamepad>/dpad/down");
        confirm = Make("confirm", "<Keyboard>/enter", "<Keyboard>/space", "<Gamepad>/buttonSouth");
        calibrate = Make("calibrate", "<Keyboard>/c", "<Gamepad>/buttonWest");
        options = Make("options", "<Keyboard>/o", "<Gamepad>/buttonNorth");
        quit = Make("quit", "<Keyboard>/escape");

        up.performed += _ => Move(-1);
        down.performed += _ => Move(1);
        confirm.performed += _ => Play();
        calibrate.performed += _ => SceneManager.LoadScene(GameSession.CalibrationScene);
        options.performed += _ => SceneManager.LoadScene(GameSession.SettingsScene);
#if !UNITY_WEBGL
        quit.performed += _ => Application.Quit();
#endif
    }

    void OnDisable()
    {
        foreach (var a in new[] { up, down, confirm, calibrate, options, quit }) { a?.Disable(); a?.Dispose(); }
    }

    static InputAction Make(string name, params string[] paths)
    {
        var a = new InputAction(name, InputActionType.Button);
        foreach (var p in paths) a.AddBinding(p);
        a.Enable();
        return a;
    }

    void Move(int dir)
    {
        if (list.songs.Count == 0) return;
        index = (index + dir + list.songs.Count) % list.songs.Count;
        RequestPreview();
    }

    void Play()
    {
        if (list.songs.Count == 0) return;
        preview.Stop();
        GameSession.Selected = list.songs[index];
        SceneManager.LoadScene(GameSession.GameScene);
    }

    void OnGUI()
    {
        var title = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold };
        var row = new GUIStyle(GUI.skin.label) { fontSize = 30 };

        GUI.Label(new Rect(40, 30, 1200, 60), "Select Song", title);

        if (list.songs.Count == 0)
        {
            GUI.Label(new Rect(40, 110, 1200, 50), "No songs. Check Resources/songs.json", row);
            return;
        }

        for (int i = 0; i < list.songs.Count; i++)
        {
            var s = list.songs[i];
            int best = PlayerPrefs.GetInt(GameSession.BestKey(s.id), 0);
            GUI.color = i == index ? Color.yellow : Color.white;
            string cursor = i == index ? "> " : "   ";
            GUI.Label(new Rect(40, 110 + i * 50, 1600, 50),
                $"{cursor}{s.title} - {s.artist}    Lv {s.level}    {s.bpm:0} BPM    Best {best}", row);
        }
        GUI.color = Color.white;

        GUI.Label(new Rect(40, Screen.height - 60, 1600, 50),
            Hint, row);
    }
}
