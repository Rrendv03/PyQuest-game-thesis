using System.Collections;
using System.IO;
using UnityEngine;
#if UNITY_STANDALONE_WIN
using System.Runtime.InteropServices;
#endif

/// <summary>
/// Extracted from EpilogueEndScreenController so the results-export/share
/// flow has exactly one implementation, called from both the epilogue end
/// screen's button and the failsafe button on the general Settings screen.
/// Behavior and every status message are unchanged from the original.
///
/// Static and scene-independent by design: the Settings-screen button
/// needs to work from any scene, not just MainMap, where
/// EpilogueEndScreenController lives.
/// </summary>
public static class ResultsExportHelper
{
    /// <param name="coroutineHost">
    /// Needed only for the Windows Explorer-reveal coroutine. Pass the
    /// calling MonoBehaviour (any active one works, it's just used to
    /// host StartCoroutine).
    /// </param>
    /// <param name="onStatus">
    /// Called with each status message as the original SetStatus did.
    /// Caller decides where that text goes (or ignores it).
    /// </param>
    public static void ExportAndShare(MonoBehaviour coroutineHost, string androidShareTitle, System.Action<string> onStatus)
    {
        if (StudentLogManager.Instance == null)
        {
            onStatus?.Invoke("Could not find results, StudentLogManager missing.");
            return;
        }

        string path = StudentLogManager.Instance.ExportCanonicalCsv();

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            onStatus?.Invoke("Could not create the results file. Try again, or tell your evaluator.");
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        bool launched = AndroidNativeShare.ShareFile(path, "text/csv", androidShareTitle);
        onStatus?.Invoke(launched
            ? "Opening the share menu..."
            : "Could not open the share menu. Try again, or tell your evaluator.");
#elif UNITY_STANDALONE_WIN
        coroutineHost.StartCoroutine(RevealInExplorerRoutine(path, onStatus));
#else
        onStatus?.Invoke("Your results file is at: " + path);
#endif
    }

#if UNITY_STANDALONE_WIN
    [DllImport("user32.dll")]
    private static extern System.IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(System.IntPtr hWnd, int nCmdShow);

    private const int SW_MINIMIZE = 6;

    private static IEnumerator RevealInExplorerRoutine(string path, System.Action<string> onStatus)
    {
        string windowsPath = path.Replace('/', '\\');

        Debug.Log($"[ResultsExportHelper] Current FullScreenMode: {Screen.fullScreenMode}");

        if (Screen.fullScreenMode == FullScreenMode.ExclusiveFullScreen)
        {
            Screen.fullScreenMode = FullScreenMode.Windowed;
            yield return new WaitForSecondsRealtime(0.2f);
        }

        try
        {
            System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + windowsPath + "\"");
            Debug.Log("[ResultsExportHelper] Process.Start for explorer.exe returned without throwing.");

            System.IntPtr gameWindow = GetForegroundWindow();
            if (gameWindow != System.IntPtr.Zero)
            {
                bool minimized = ShowWindow(gameWindow, SW_MINIMIZE);
                Debug.Log($"[ResultsExportHelper] ShowWindow(SW_MINIMIZE) returned: {minimized}");
            }
            else
            {
                Debug.LogWarning("[ResultsExportHelper] GetForegroundWindow() returned zero, could not minimize.");
            }

            onStatus?.Invoke("Opening file location, check your taskbar.");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ResultsExportHelper] Failed to open Explorer: {e.Message}\n{e.StackTrace}");
            onStatus?.Invoke("Could not open the folder. The file is at: " + path);
        }
    }
#endif
}