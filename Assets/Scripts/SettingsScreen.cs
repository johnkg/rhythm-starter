using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// Settings screen. Up/Down = row, Left/Right = change, Enter = open row (rebind key, calibrate, reset), Esc = back.
/// Rebinding: Enter on a lane key row, then press the new key. Esc cancels. Throwaway IMGUI.
public class SettingsScreen : MonoBehaviour
{
    const int RowSpeed = 0, RowAudio = 1, RowVisual = 2, RowPreview = 3, RowTap = 4;
    const int RowKey0 = 5;                                  // rows 5..8 = lane keys
    const int RowResetKeys = RowKey0 + KeyBindings.Lanes;   // 9
    const int RowCalibrate = RowResetKeys + 1;              // 10
    const int RowCount = RowCalibrate + 1;

    int row;
    int rebindLane = -1;     // >= 0 while waiting for a key
    int rebindFrame;         // frame the wait started, so the Enter that opened it is not bound
    InputAction up, down, left, right, confirm, back;

    bool Idle => rebindLane < 0;

    void OnEnable()
    {
        up = Make("up", "<Keyboard>/upArrow", "<Gamepad>/dpad/up");
        down = Make("down", "<Keyboard>/downArrow", "<Gamepad>/dpad/down");
        left = Make("left", "<Keyboard>/leftArrow", "<Gamepad>/dpad/left");
        right = Make("right", "<Keyboard>/rightArrow", "<Gamepad>/dpad/right");
        confirm = Make("confirm", "<Keyboard>/enter", "<Gamepad>/buttonSouth");
        back = Make("back", "<Keyboard>/escape", "<Gamepad>/buttonEast");

        // Only arrows/d-pad navigate here (no WASD), so lane keys like D and S stay free to rebind.
        up.performed += _ => { if (Idle) row = (row + RowCount - 1) % RowCount; };
        down.performed += _ => { if (Idle) row = (row + 1) % RowCount; };
        left.performed += _ => { if (Idle) Adjust(-1); };
        right.performed += _ => { if (Idle) Adjust(1); };
        confirm.performed += _ => { if (Idle) Activate(); };
        back.performed += _ =>
        {
            if (!Idle) return;   // Update() cancels the rebind on Esc
            PlayerPrefs.Save();
            SceneManager.LoadScene(GameSession.MenuScene);
        };
    }

    void OnDisable()
    {
        foreach (var a in new[] { up, down, left, right, confirm, back }) { a?.Disable(); a?.Dispose(); }
    }

    static InputAction Make(string name, params string[] paths)
    {
        var a = new InputAction(name, InputActionType.Button);
        foreach (var p in paths) a.AddBinding(p);
        a.Enable();
        return a;
    }

    void Update()
    {
        if (Idle || Keyboard.current == null) return;
        if (Time.frameCount == rebindFrame) return;   // ignore the keypress that opened the prompt

        if (Keyboard.current.escapeKey.wasPressedThisFrame) { rebindLane = -1; return; }

        foreach (var key in Keyboard.current.allKeys)
        {
            if (!key.wasPressedThisFrame) continue;
            if (key == Keyboard.current.enterKey) continue;   // Enter is menu confirm, keep it out of lanes
            KeyBindings.SetKey(rebindLane, key.name);
            PlayerPrefs.Save();
            rebindLane = -1;
            return;
        }
    }

    void Adjust(int dir)
    {
        switch (row)
        {
            case RowSpeed:
                int speed = Mathf.Clamp(GameSettings.NoteSpeed + dir, GameSettings.MinNoteSpeed, GameSettings.MaxNoteSpeed);
                PlayerPrefs.SetInt(GameSettings.NoteSpeedKey, speed);
                break;
            case RowAudio:
                float a = Mathf.Clamp(GameSettings.AudioOffsetSec(0f) + dir * 0.005f, -0.5f, 0.5f);
                PlayerPrefs.SetFloat(GameSettings.AudioOffsetKey, a);
                break;
            case RowVisual:
                float v = Mathf.Clamp(GameSettings.VisualOffsetSec + dir * 0.005f, -0.3f, 0.3f);
                PlayerPrefs.SetFloat(GameSettings.VisualOffsetKey, v);
                break;
            case RowPreview:
                int pv = Mathf.Clamp(GameSettings.PreviewVolume + dir, 0, GameSettings.MaxPreviewVolume);
                PlayerPrefs.SetInt(GameSettings.PreviewVolumeKey, pv);
                break;
            case RowTap:
                int tv = Mathf.Clamp(GameSettings.TapVolume + dir, 0, GameSettings.MaxTapVolume);
                PlayerPrefs.SetInt(GameSettings.TapVolumeKey, tv);
                break;
        }
    }

    void Activate()
    {
        if (row >= RowKey0 && row < RowKey0 + KeyBindings.Lanes)
        {
            rebindLane = row - RowKey0;
            rebindFrame = Time.frameCount;
        }
        else if (row == RowResetKeys) KeyBindings.ResetAll();
        else if (row == RowCalibrate) SceneManager.LoadScene(GameSession.CalibrationScene);
    }

    void OnGUI()
    {
        var title = new GUIStyle(GUI.skin.label) { fontSize = 40, fontStyle = FontStyle.Bold };
        var line = new GUIStyle(GUI.skin.label) { fontSize = 30 };

        GUI.Label(new Rect(40, 30, 1200, 60), "Settings", title);

        var rows = new string[RowCount];
        rows[RowSpeed] = $"Note speed      {GameSettings.NoteSpeed} / {GameSettings.MaxNoteSpeed}     ({GameSettings.ApproachSec:0.00} s travel)";
        rows[RowAudio] = $"Audio offset    {GameSettings.AudioOffsetSec(0f) * 1000f:+0;-0;0} ms     (run Calibrate first, fine tune here)";
        rows[RowVisual] = $"Visual offset   {GameSettings.VisualOffsetSec * 1000f:+0;-0;0} ms     (raise if notes look late on your screen)";
        rows[RowPreview] = $"Preview volume {GameSettings.PreviewVolume} / {GameSettings.MaxPreviewVolume}     (song select, 0 = off)";
        rows[RowTap] = $"Hit sound volume {GameSettings.TapVolume} / {GameSettings.MaxTapVolume}     (click when you hit a note, 0 = off)";
        for (int i = 0; i < KeyBindings.Lanes; i++)
        {
            bool waiting = rebindLane == i;
            rows[RowKey0 + i] = $"Lane {i + 1} key     {(waiting ? "press a key... (Esc cancels)" : KeyBindings.KeyLabel(i))}";
        }
        rows[RowResetKeys] = "Reset lane keys to D F J K";
        rows[RowCalibrate] = "Calibrate audio  >";

        for (int i = 0; i < rows.Length; i++)
        {
            GUI.color = i == row ? Color.yellow : Color.white;
            GUI.Label(new Rect(40, 110 + i * 52, 1800, 50), (i == row ? "> " : "   ") + rows[i], line);
        }
        GUI.color = Color.white;

        GUI.Label(new Rect(40, Screen.height - 60, 1800, 50),
            "Up/Down = row    Left/Right = change    Enter = open    Esc = back", line);
    }
}
