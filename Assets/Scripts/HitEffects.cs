using UnityEngine;

/// Hit feedback for the Game scene: lane glow on press, colored flash on judgement,
/// floating grade text, combo counter. No assets needed (white texture made at runtime, text is IMGUI).
public class HitEffects : MonoBehaviour
{
    [SerializeField] Judge judge;
    [SerializeField] NoteSpawner spawner;
    [SerializeField] Camera cam;   // optional, falls back to Camera.main

    const float FlashSec = 0.2f;
    const float PopupSec = 0.6f;

    SpriteRenderer[] flashes;
    Color[] flashColor;
    float[] flashStart;

    string popupText = "";
    Color popupColor = Color.white;
    float popupStart = -10f;
    int popupLane;

    static readonly Color PerfectColor = new Color(1f, 0.85f, 0.2f, 1f);
    static readonly Color GreatColor = new Color(0.4f, 1f, 0.5f, 1f);
    static readonly Color GoodColor = new Color(0.4f, 0.7f, 1f, 1f);
    static readonly Color MissColor = new Color(1f, 0.3f, 0.3f, 1f);

    void Start()
    {
        if (spawner.Lanes == null) { enabled = false; return; }   // chart failed to load
        if (cam == null) cam = Camera.main;

        var tex = Texture2D.whiteTexture;
        var sprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), tex.width);   // 1 world unit square

        int n = spawner.Lanes.Length;
        flashes = new SpriteRenderer[n];
        flashColor = new Color[n];
        flashStart = new float[n];
        for (int i = 0; i < n; i++)
        {
            var go = new GameObject("LaneFlash" + i);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(spawner.LaneX(i), spawner.HitY, 0f);
            go.transform.localScale = new Vector3(spawner.LaneSpacing * 0.9f, 0.5f, 1f);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = -1;
            sr.color = new Color(1f, 1f, 1f, 0f);
            flashes[i] = sr;
            flashStart[i] = -10f;
        }

        judge.OnLanePressed += OnPressed;
        judge.OnJudged += OnJudged;
    }

    void OnDestroy()
    {
        if (judge == null) return;
        judge.OnLanePressed -= OnPressed;
        judge.OnJudged -= OnJudged;
    }

    void OnPressed(int lane)
    {
        if (lane >= flashes.Length) return;
        flashColor[lane] = new Color(1f, 1f, 1f, 0.35f);   // dim white: key went down
        flashStart[lane] = Time.unscaledTime;
    }

    void OnJudged(int lane, Grade g, float delta)
    {
        if (lane >= flashes.Length) return;
        var c = ColorFor(g);
        flashColor[lane] = new Color(c.r, c.g, c.b, 0.8f);
        flashStart[lane] = Time.unscaledTime;

        popupText = g.ToString().ToUpperInvariant();
        popupColor = c;
        popupStart = Time.unscaledTime;
        popupLane = lane;
    }

    static Color ColorFor(Grade g)
    {
        switch (g)
        {
            case Grade.Perfect: return PerfectColor;
            case Grade.Great: return GreatColor;
            case Grade.Good: return GoodColor;
            default: return MissColor;
        }
    }

    void Update()
    {
        for (int i = 0; i < flashes.Length; i++)
        {
            float t = (Time.unscaledTime - flashStart[i]) / FlashSec;
            var c = flashColor[i];
            c.a = t >= 1f ? 0f : c.a * (1f - t);
            flashes[i].color = c;
        }
    }

    void OnGUI()
    {
        GUI.depth = 5;   // behind pause and results overlays

        float age = Time.unscaledTime - popupStart;
        if (age < PopupSec && cam != null)
        {
            Vector3 sp = cam.WorldToScreenPoint(new Vector3(spawner.LaneX(popupLane), spawner.HitY + 1.2f, 0f));
            float y = Screen.height - sp.y - age * 60f;   // drifts upward
            var style = new GUIStyle(GUI.skin.label) { fontSize = 36, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(popupColor.r, popupColor.g, popupColor.b, 1f - age / PopupSec);
            GUI.Label(new Rect(sp.x - 150f, y - 25f, 300f, 50f), popupText, style);
            GUI.color = Color.white;
        }

        if (judge.Combo >= 2)
        {
            var big = new GUIStyle(GUI.skin.label) { fontSize = 48, fontStyle = FontStyle.Bold, alignment = TextAnchor.MiddleCenter };
            GUI.color = new Color(1f, 1f, 1f, 0.8f);
            GUI.Label(new Rect(Screen.width * 0.5f - 200f, Screen.height * 0.18f, 400f, 80f), judge.Combo + " COMBO", big);
            GUI.color = Color.white;
        }
    }
}
