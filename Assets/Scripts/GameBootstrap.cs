using UnityEngine;

/// Game scene setup. Reads the song picked in SongSelect and feeds it to Conductor + NoteSpawner.
/// Runs BEFORE them (execution order -100) so clip and chart are set before they load.
/// If GameSession.Selected is null (Game scene started directly), inspector values are used. Good for testing.
[DefaultExecutionOrder(-100)]
public class GameBootstrap : MonoBehaviour
{
    [SerializeField] Conductor conductor;
    [SerializeField] NoteSpawner spawner;

    void Awake()
    {
        var song = GameSession.Selected;
        if (song == null) return;

        AudioClip clip = string.IsNullOrEmpty(song.audio) ? null : Resources.Load<AudioClip>(song.audio);
        if (clip == null)
            Debug.LogWarning($"Audio not found: Resources/{song.audio}. Running silent.");

        conductor.SetClip(clip);
        spawner.SetChart(song.chart);
    }
}
