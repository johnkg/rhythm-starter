using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// One click project setup. Menu: Rhythm > Build Starter Scenes.
/// Creates the note prefab, the 4 game scenes (+ a ChartRecorder scene), wires every component,
/// and fills Build Settings. Safe to run again: it rebuilds the scenes and prefab from scratch.
/// Lives in an Editor folder so it never ships in a build.
public static class RhythmSetup
{
    const string ScenesDir = "Assets/Scenes";
    const string PrefabsDir = "Assets/Prefabs";
    const string ArtDir = "Assets/Art";

    [MenuItem("Rhythm/Build Starter Scenes")]
    public static void BuildAll()
    {
#if !ENABLE_INPUT_SYSTEM
        EditorUtility.DisplayDialog("Rhythm setup",
            "Input System is not active.\n\n1. Window > Package Manager > install 'Input System'.\n" +
            "2. Project Settings > Player > Active Input Handling = Input System Package (New) or Both.\n" +
            "3. Let Unity restart, then run this menu again.", "OK");
        return;
#else
        EnsureFolder(ScenesDir);
        EnsureFolder(PrefabsDir);
        EnsureFolder(ArtDir);
        EnsureFolder("Assets/Resources");
        EnsureFolder("Assets/Resources/Audio");
        EnsureFolder("Assets/Resources/Charts");

        var sprite = EnsureWhiteSprite();
        var notePrefab = EnsureNotePrefab(sprite);

        string songSelect = BuildSongSelect();
        string game = BuildGame(notePrefab, sprite);
        string calibration = BuildCalibration();
        string settings = BuildSettings();
        BuildRecorder();

        // Order matters: first scene is what a build starts in.
        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene(songSelect, true),
            new EditorBuildSettingsScene(game, true),
            new EditorBuildSettingsScene(calibration, true),
            new EditorBuildSettingsScene(settings, true),
        };

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        EditorUtility.DisplayDialog("Rhythm setup",
            "Done.\n\nScenes are in Assets/Scenes and added to Build Settings.\n" +
            "Open SongSelect and press Play to try it. The demo song is silent but notes still play.\n\n" +
            "To record a chart: open ChartRecorder, assign your song to the AudioSource, press Play.", "OK");
#endif
    }

    // ---------------------------------------------------------------- assets

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }

    /// 4x4 white PNG imported as a 1 world unit sprite. Everything visual is this sprite, tinted and scaled.
    static Sprite EnsureWhiteSprite()
    {
        string path = ArtDir + "/white.png";
        if (AssetDatabase.LoadAssetAtPath<Sprite>(path) == null)
        {
            var tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            var px = new Color32[16];
            for (int i = 0; i < px.Length; i++) px[i] = new Color32(255, 255, 255, 255);
            tex.SetPixels32(px);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);

            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Sprite;
            importer.spritePixelsPerUnit = 4;   // 4 px / 4 ppu = exactly 1 unit
            importer.filterMode = FilterMode.Point;
            importer.SaveAndReimport();
        }
        return AssetDatabase.LoadAssetAtPath<Sprite>(path);
    }

    static NoteView EnsureNotePrefab(Sprite sprite)
    {
        var go = new GameObject("Note");
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = new Color(0.3f, 0.85f, 1f, 1f);
        go.transform.localScale = new Vector3(1.3f, 0.3f, 1f);
        go.AddComponent<NoteView>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabsDir + "/Note.prefab");
        Object.DestroyImmediate(go);
        return prefab.GetComponent<NoteView>();
    }

    // ---------------------------------------------------------------- scenes

    static UnityEngine.SceneManagement.Scene NewScene()
    {
        return EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
    }

    static Camera MakeCamera(bool game)
    {
        var go = new GameObject("Main Camera");
        go.tag = "MainCamera";
        var cam = go.AddComponent<Camera>();
        go.AddComponent<AudioListener>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.06f, 0.06f, 0.10f, 1f);
        cam.orthographic = true;
        cam.orthographicSize = game ? 5.5f : 5f;
        // Game view spans y -4 .. 7 so spawn (6) and hit line (-3) both fit.
        go.transform.position = new Vector3(0f, game ? 1.5f : 0f, -10f);
        return cam;
    }

    static string Save(UnityEngine.SceneManagement.Scene scene, string name)
    {
        string path = ScenesDir + "/" + name + ".unity";
        EditorSceneManager.SaveScene(scene, path);
        return path;
    }

    static void SetRef(Object target, string field, Object value)
    {
        var so = new SerializedObject(target);
        var prop = so.FindProperty(field);
        if (prop == null) { Debug.LogError($"RhythmSetup: {target.GetType().Name} has no field '{field}'"); return; }
        prop.objectReferenceValue = value;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static string BuildSongSelect()
    {
        var scene = NewScene();
        MakeCamera(false);
        new GameObject("SongSelect").AddComponent<SongSelect>();
        return Save(scene, GameSession.MenuScene);
    }

    static string BuildSettings()
    {
        var scene = NewScene();
        MakeCamera(false);
        new GameObject("Settings").AddComponent<SettingsScreen>();
        return Save(scene, GameSession.SettingsScene);
    }

    static string BuildCalibration()
    {
        var scene = NewScene();
        MakeCamera(false);
        var go = new GameObject("Calibration");
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        go.AddComponent<CalibrationScreen>();
        return Save(scene, GameSession.CalibrationScene);
    }

    static void BuildRecorder()
    {
        var scene = NewScene();
        MakeCamera(false);
        var go = new GameObject("Recorder");
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;   // assign your song to Clip, then press Play
        go.AddComponent<Conductor>();
        go.AddComponent<ChartRecorder>();
        Save(scene, "ChartRecorder");
    }

    static string BuildGame(NoteView notePrefab, Sprite sprite)
    {
        var scene = NewScene();
        MakeCamera(true);

        // Lane strips and hit line, purely decorative. Positions match NoteSpawner defaults.
        var deco = new GameObject("Playfield");
        const float spacing = 1.5f, hitY = -3f;
        for (int i = 0; i < 4; i++)
            MakeSprite(deco.transform, sprite, "Lane" + i, new Vector3((i - 1.5f) * spacing, 1.5f, 0f),
                new Vector3(spacing * 0.95f, 12f, 1f), new Color(1f, 1f, 1f, 0.05f), -20);
        MakeSprite(deco.transform, sprite, "HitLine", new Vector3(0f, hitY, 0f),
            new Vector3(spacing * 4f, 0.08f, 1f), new Color(1f, 1f, 1f, 0.85f), -5);

        var go = new GameObject("Game");
        var src = go.AddComponent<AudioSource>();
        src.playOnAwake = false;
        var conductor = go.AddComponent<Conductor>();
        var spawner = go.AddComponent<NoteSpawner>();
        var judge = go.AddComponent<Judge>();
        var bootstrap = go.AddComponent<GameBootstrap>();
        var results = go.AddComponent<ResultsScreen>();
        var pause = go.AddComponent<PauseMenu>();
        var effects = go.AddComponent<HitEffects>();
        var tapSound = go.AddComponent<TapSound>();

        SetRef(spawner, "notePrefab", notePrefab);
        SetRef(judge, "spawner", spawner);
        SetRef(bootstrap, "conductor", conductor);
        SetRef(bootstrap, "spawner", spawner);
        SetRef(results, "judge", judge);
        SetRef(results, "spawner", spawner);
        SetRef(pause, "judge", judge);
        SetRef(pause, "results", results);
        SetRef(effects, "judge", judge);
        SetRef(effects, "spawner", spawner);
        SetRef(tapSound, "judge", judge);

        return Save(scene, GameSession.GameScene);
    }

    static void MakeSprite(Transform parent, Sprite sprite, string name, Vector3 pos, Vector3 scale, Color color, int order)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        go.transform.localScale = scale;
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sortingOrder = order;
    }
}
