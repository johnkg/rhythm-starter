using UnityEngine;

/// Visual only. Position = f(songTime, noteTime). No physics, no velocity.
/// Same song time always gives same position, so it can never drift from audio.
/// Hold notes get a tail (child sprite built in code, copies the head sprite).
public class NoteView : MonoBehaviour
{
    NoteRuntime note;
    NoteSpawner spawner;
    float x;
    Transform tail;
    SpriteRenderer headSr, tailSr;
    bool dimmed;

    public void Init(NoteRuntime n, NoteSpawner s, float laneX)
    {
        note = n;
        spawner = s;
        x = laneX;
        headSr = GetComponent<SpriteRenderer>();
        if (note.IsHold) BuildTail();
        Place();
    }

    void BuildTail()
    {
        var go = new GameObject("Tail");
        tail = go.transform;
        tail.SetParent(transform, false);
        tailSr = go.AddComponent<SpriteRenderer>();
        if (headSr != null)
        {
            var c = headSr.color;
            tailSr.sprite = headSr.sprite;
            tailSr.color = new Color(c.r, c.g, c.b, 0.5f);
            tailSr.sortingOrder = headSr.sortingOrder - 1;
        }
    }

    void Update()
    {
        Place();

        // Hit or finished notes vanish.
        if (note.resolved && note.hitGrade != Grade.Miss) { Destroy(gameObject); return; }

        // Missed notes: gray out, keep falling a bit, then vanish.
        if (note.resolved && !dimmed) Dim();
        double endT = note.IsHold ? note.EndTime : note.Time;
        if (Conductor.Instance.SongTime - endT > 0.5) Destroy(gameObject);
    }

    void Place()
    {
        // Visual offset only moves the picture. Judging still uses real song time.
        double now = Conductor.Instance.SongTime + spawner.VisualOffsetSec;
        float k = (spawner.SpawnY - spawner.HitY) / spawner.ApproachSec;   // world units per second

        float yStart = spawner.HitY + (float)(note.Time - now) * k;
        if (note.holding) yStart = Mathf.Max(yStart, spawner.HitY);       // head sticks to hit line while held
        transform.localPosition = new Vector3(x, yStart, 0f);

        if (tail != null)
        {
            float yEnd = spawner.HitY + (float)(note.EndTime - now) * k;
            float len = Mathf.Max(0f, yEnd - yStart);                      // world units, shrinks while held
            float sy = Mathf.Max(0.0001f, transform.localScale.y);         // undo parent scale so tail is true length
            tail.localScale = new Vector3(0.6f, len / sy, 1f);
            tail.localPosition = new Vector3(0f, (len * 0.5f) / sy, 0f);   // tail extends upward from head
        }
    }

    void Dim()
    {
        dimmed = true;
        var gray = new Color(0.4f, 0.4f, 0.4f, 0.6f);
        if (headSr != null) headSr.color = gray;
        if (tailSr != null) tailSr.color = gray;
    }
}
