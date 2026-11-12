using System;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// Build entry point for the build server (GitHub Actions, see .github/workflows/ci.yml).
/// Runs without a window:  Unity -batchmode -executeMethod RhythmCI.BuildWebGL -customBuildPath <folder>
/// It first runs the same scene builder as the menu (scenes are not stored in the repository),
/// then makes a WebGL build that a plain static file host can serve.
public static class RhythmCI
{
    const string DefaultPath = "build/WebGL/WebGL";

    public static void BuildWebGL()
    {
        try
        {
#if !ENABLE_INPUT_SYSTEM
            Fail("Input System is not active, so the scene builder cannot run. " +
                 "ProjectSettings/ProjectSettings.asset needs 'activeInputHandler: 1' and " +
                 "Packages/manifest.json needs com.unity.inputsystem.");
#else
            RhythmSetup.BuildAll();

            var scenes = EnabledScenes();
            if (scenes.Length == 0) { Fail("Scene builder made no scenes."); return; }

            // GitHub Pages cannot send the headers that compressed Unity builds need. Plain files always load.
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Disabled;
            PlayerSettings.WebGL.decompressionFallback = false;
            PlayerSettings.runInBackground = true;

            string path = Arg("-customBuildPath") ?? DefaultPath;
            Debug.Log("RhythmCI: building " + scenes.Length + " scenes to " + path);
            var options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = path,
                target = BuildTarget.WebGL,
                options = BuildOptions.None,
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                Fail("Build " + report.summary.result + " with " + report.summary.totalErrors + " error(s).");
                return;
            }
            Debug.Log("RhythmCI: build succeeded, " + (report.summary.totalSize / (1024 * 1024)) + " MB.");
#endif
        }
        catch (Exception e)
        {
            Fail(e.ToString());
        }
    }

    static string[] EnabledScenes()
    {
        var all = EditorBuildSettings.scenes;
        if (all == null) return new string[0];
        int n = 0;
        foreach (var s in all) if (s.enabled) n++;
        var paths = new string[n];
        int i = 0;
        foreach (var s in all) if (s.enabled) paths[i++] = s.path;
        return paths;
    }

    /// Value that follows a command line switch, or null.
    public static string Arg(string name) => Arg(Environment.GetCommandLineArgs(), name);

    public static string Arg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name && !string.IsNullOrEmpty(args[i + 1])) return args[i + 1];
        return null;
    }

    static void Fail(string message)
    {
        Debug.LogError("RhythmCI: " + message);
        EditorApplication.Exit(1);      // non-zero exit code = the build server marks the run as failed
    }
}
