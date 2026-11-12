using System.Collections;
using UnityEngine;

/// Plays a short loop of the highlighted song on the song select screen.
/// Request(song) after the cursor moves. It waits a moment (so scrolling fast does not load every song),
/// loads the audio without freezing the game, fades in, loops about 15 s, fades out at the loop end.
/// Song start comes from songs.json "previewStart" (seconds). 0 = automatic, about 35% into the song.
[RequireComponent(typeof(AudioSource))]
public class PreviewPlayer : MonoBehaviour
{
    public const float PreviewLengthSec = 15f;
    public const float FadeSec = 1f;
    public const float DelaySec = 0.4f;

    AudioSource src;
    AudioClip current;
    int generation;        // bumped on every request, lets older async loads cancel themselves
    bool playing;
    float began;           // unscaled time the current loop started
    float startAt;         // position in the clip the loop starts from
    float length;          // loop length in seconds

    void Awake()
    {
        src = GetComponent<AudioSource>();
        src.playOnAwake = false;
        src.loop = false;
    }

    /// Switch preview to this song. null (or a song without audio) = silence.
    public void Request(SongInfo song)
    {
        generation++;
        StopPlayback();
        if (song == null || string.IsNullOrEmpty(song.audio)) return;
        StartCoroutine(LoadAndPlay(song, generation));
    }

    public void Stop()
    {
        generation++;   // cancels any load in flight
        StopPlayback();
    }

    IEnumerator LoadAndPlay(SongInfo song, int myGeneration)
    {
        yield return new WaitForSecondsRealtime(DelaySec);
        if (myGeneration != generation) yield break;

        var request = Resources.LoadAsync<AudioClip>(song.audio);
        yield return request;

        var clip = request.asset as AudioClip;
        if (clip == null) yield break;   // song has no audio file yet: stay silent

        if (myGeneration != generation)  // player moved on while it loaded
        {
            Resources.UnloadAsset(clip);
            yield break;
        }

        ReleaseClip();
        current = clip;
        src.clip = clip;
        startAt = StartTime(clip.length, song.previewStart);
        length = Length(clip.length, startAt);
        src.time = startAt;
        src.volume = 0f;
        src.Play();
        began = Time.unscaledTime;
        playing = true;
    }

    void Update()
    {
        if (!playing) return;

        float elapsed = Time.unscaledTime - began;
        if (elapsed >= length)
        {
            // Loop: jump back to the start, fade in again.
            src.time = startAt;
            began = Time.unscaledTime;
            elapsed = 0f;
        }
        src.volume = GameSettings.PreviewVolume01 * FadeFactor(elapsed, length, FadeSec);
    }

    void StopPlayback()
    {
        playing = false;
        if (src != null) src.Stop();
    }

    void ReleaseClip()
    {
        if (current == null) return;
        src.clip = null;
        Resources.UnloadAsset(current);   // free the decoded audio, songs can be big
        current = null;
    }

    void OnDestroy()
    {
        generation++;
        ReleaseClip();
    }

    // ---------------------------------------------------------------- pure helpers (unit tested)

    /// Where in the clip the preview starts. configured > 0 is used (kept inside the clip),
    /// otherwise about 35% in, which usually lands on the chorus rather than the intro.
    public static float StartTime(float clipLength, float configured)
    {
        float latest = Mathf.Max(0f, clipLength - PreviewLengthSec);   // keep a full-length preview if possible
        float wanted = configured > 0f ? configured : clipLength * 0.35f;
        return Mathf.Min(wanted, latest);
    }

    /// Loop length: 15 s, or what is left of the clip.
    public static float Length(float clipLength, float start)
    {
        return Mathf.Min(PreviewLengthSec, Mathf.Max(0f, clipLength - start));
    }

    /// 0..1 volume multiplier: ramps up over fade seconds, ramps down over the last fade seconds.
    public static float FadeFactor(float elapsed, float length, float fade)
    {
        if (fade <= 0f) return 1f;
        float fin = elapsed / fade;
        float fout = (length - elapsed) / fade;
        return Mathf.Clamp01(Mathf.Min(fin, fout));
    }
}
