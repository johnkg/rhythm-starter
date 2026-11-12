using UnityEngine;

/// Hit sound for the Game scene. Plays a short click when a note is hit (not on a miss).
/// Sound is generated in code, so no asset is needed. Pitch rises with grade: Good < Great < Perfect.
/// Want your own sound? Put a clip at Assets/Resources/Sfx/tap.wav (or .ogg). It replaces the built-in one for all grades.
/// Several AudioSources take turns, so fast taps never cut each other off.
public class TapSound : MonoBehaviour
{
    [SerializeField] Judge judge;

    const int Voices = 8;
    const int SampleRate = 44100;

    AudioSource[] voices;
    int nextVoice;
    AudioClip perfectClip, greatClip, goodClip;

    void Start()
    {
        if (judge == null) { enabled = false; return; }

        var custom = Resources.Load<AudioClip>("Sfx/tap");
        if (custom != null)
        {
            perfectClip = greatClip = goodClip = custom;
        }
        else
        {
            perfectClip = Make("tap_perfect", 1760f);
            greatClip = Make("tap_great", 1320f);
            goodClip = Make("tap_good", 990f);
        }

        voices = new AudioSource[Voices];
        for (int i = 0; i < Voices; i++)
        {
            var go = new GameObject("TapVoice" + i);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.spatialBlend = 0f;   // plain 2D sound
            voices[i] = src;
        }

        judge.OnJudged += OnJudged;
    }

    void OnDestroy()
    {
        if (judge != null) judge.OnJudged -= OnJudged;
    }

    void OnJudged(int lane, Grade g, float delta)
    {
        float vol = GameSettings.TapVolume01;
        if (vol <= 0f) return;

        AudioClip clip = ClipFor(g);
        if (clip == null) return;   // miss: no sound

        var src = voices[nextVoice];
        nextVoice = NextVoice(nextVoice, Voices);
        src.clip = clip;
        src.volume = vol;
        src.Play();
    }

    AudioClip ClipFor(Grade g)
    {
        switch (g)
        {
            case Grade.Perfect: return perfectClip;
            case Grade.Great: return greatClip;
            case Grade.Good: return goodClip;
            default: return null;
        }
    }

    static AudioClip Make(string name, float freq)
    {
        float[] data = Blip(SampleRate, freq, 0.06f);
        var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
        clip.SetData(data, 0);
        return clip;
    }

    // ---------------------------------------------------------------- pure helpers (unit tested)

    /// Short click: sine at freq plus a quieter 2nd harmonic, with a fast decay.
    /// No fade-in: the waveform starts on the first sample so it feels instant. Fades to zero at the end (no pop).
    public static float[] Blip(int sampleRate, float freq, float durationSec)
    {
        int len = Mathf.Max(1, Mathf.RoundToInt(sampleRate * durationSec));
        var data = new float[len];
        for (int i = 0; i < len; i++)
        {
            float t = i / (float)sampleRate;
            float env = Mathf.Exp(-t * 70f);                 // decays to ~1.5% by 60 ms
            float tail = 1f - (float)i / len;                // forced to zero at the end, avoids a click
            float s = Mathf.Sin(2f * Mathf.PI * freq * t) + 0.35f * Mathf.Sin(4f * Mathf.PI * freq * t);
            data[i] = s * env * tail * 0.6f;
        }
        return data;
    }

    /// Round robin voice index.
    public static int NextVoice(int current, int count)
    {
        return (current + 1) % count;
    }
}
