using System;
using System.Collections.Generic;

/// One note in the chart file. Times are SONG time (0 = start of audio).
/// Tap  = duration_ms 0 (or missing).
/// Hold = duration_ms > 0. Press at time_ms, keep down until time_ms + duration_ms.
[Serializable]
public class NoteData
{
    public float time_ms;
    public int lane;
    public string type = "tap";   // "tap" or "hold". Informational. duration_ms decides.
    public float duration_ms;
}

[Serializable]
public class ChartData
{
    public string title;
    public int lanes = 4;
    public List<NoteData> notes = new List<NoteData>();
}
