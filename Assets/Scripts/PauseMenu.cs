using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// Pause overlay for the Game scene. Esc / gamepad Start toggles.
/// Paused: Enter / A = resume (3 s countdown), R / Y = restart, Q / B = song select.
/// Pausing mid-hold drops the hold. Cannot pause during lead-in or on the results screen.
public class PauseMenu : MonoBehaviour
{
    [SerializeField] Judge judge;
    [SerializeField] ResultsScreen results;
    [SerializeField] float countdownSec = 3f;

    bool paused;
    float countdown = -1f;   // < 0 = waiting for player, >= 0 = counting down to resume
    InputAction toggle, confirm, restart, quit;

    void OnEnable()
    {
        toggle = Make("toggle", "<Keyboard>/escape", "<Gamepad>/start");
        confirm = Make("confirm", "<Keyboard>/enter", "<Keyboard>/space", "<Gamepad>/buttonSouth");
        restart = Make("restart", "<Keyboard>/r", "<Gamepad>/buttonNorth");
        quit = Make("quit", "<Keyboard>/q", "<Gamepad>/buttonEast");

        toggle.performed += _ => { if (!paused) Pause(); else BeginResume(); };
        confirm.performed += _ => { if (paused) BeginResume(); };
        restart.performed += _ => { if (paused) SceneManager.LoadScene(SceneManager.GetActiveScene().name); };
        quit.performed += _ => { if (paused) SceneManager.LoadScene(GameSession.MenuScene); };
    }

    void OnDisable()
    {
        foreach (var a in new[] { toggle, confirm, restart, quit }) { a?.Disable(); a?.Dispose(); }
    }

#if !UNITY_EDITOR
    // Window lost focus, alt-tab, or (web) tab hidden: pause so the song does not run away from the player.
    // A hidden browser tab stops drawing frames but the audio clock keeps going.
    // Skipped in the editor, where clicking the Inspector would pause all the time.
    void OnApplicationFocus(bool focus)
    {
        if (!focus && !paused) Pause();
    }
#endif

    static InputAction Make(string name, params string[] paths)
    {
        var a = new InputAction(name, InputActionType.Button);
        foreach (var p in paths) a.AddBinding(p);
        a.Enable();
        return a;
    }

    void Pause()
    {
        if (results != null && results.Shown) return;
        Conductor.Instance.Pause();
        if (!Conductor.Instance.Paused) return;   // refused (lead-in)
        judge.DropActiveHolds();
        paused = true;
        countdown = -1f;
    }

    void BeginResume()
    {
        if (countdown < 0f) countdown = countdownSec;
    }

    void Update()
    {
        if (!paused || countdown < 0f) return;

        // Real time, not song time: song clock is frozen on purpose.
        countdown -= Time.unscaledDeltaTime;
        if (countdown <= 0f)
        {
            Conductor.Instance.Resume();
            paused = false;
            countdown = -1f;
        }
    }

    void OnGUI()
    {
        if (!paused) return;
        GUI.depth = -20;

        var big = new GUIStyle(GUI.skin.label) { fontSize = 60, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
        var mid = new GUIStyle(GUI.skin.label) { fontSize = 32, alignment = TextAnchor.MiddleCenter };

        var r = new Rect(Screen.width * 0.25f, Screen.height * 0.25f, Screen.width * 0.5f, Screen.height * 0.5f);
        GUI.Box(r, GUIContent.none);
        GUILayout.BeginArea(r);
        if (countdown < 0f)
        {
            GUILayout.Label("PAUSED", big);
            GUILayout.Space(20);
            GUILayout.Label("Enter = resume", mid);
            GUILayout.Label("R = restart", mid);
            GUILayout.Label("Q = song select", mid);
        }
        else
        {
            GUILayout.Label("Get ready", mid);
            GUILayout.Label(Mathf.CeilToInt(countdown).ToString(), big);
        }
        GUILayout.EndArea();
    }
}
