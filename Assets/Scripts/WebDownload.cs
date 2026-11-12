using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

/// Save a text file in a way that works on every platform.
/// Desktop / editor: writes to persistentDataPath/<folder>/<filename> and returns the full path.
/// WebGL build: a browser tab has no real file system, so it triggers a normal browser download instead.
/// The download is done by Assets/Plugins/WebGL/RhythmWeb.jslib.
public static class WebDownload
{
#if UNITY_WEBGL && !UNITY_EDITOR
    [DllImport("__Internal")] static extern void RhythmDownloadText(string filename, string text);
#endif

    /// Returns a short description of where the file went, for showing on screen.
    public static string SaveText(string folder, string filename, string text)
    {
#if UNITY_WEBGL && !UNITY_EDITOR
        RhythmDownloadText(filename, text);
        return "browser download: " + filename + "  (check your Downloads folder)";
#else
        string dir = Path.Combine(Application.persistentDataPath, folder);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, filename);
        File.WriteAllText(path, text);
        return path;
#endif
    }
}
