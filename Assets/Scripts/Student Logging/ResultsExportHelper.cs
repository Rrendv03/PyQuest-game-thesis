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
///
/// Reveal behavior (opens the platform file manager at the CSV):
/// - Windows: Explorer opens with the file SELECTED (minimizes the game
///   window first if exclusive fullscreen so Explorer is visible).
/// - macOS: Finder reveals the file (open -R), falling back to open.
/// - Linux: xdg-open on the containing folder.
/// - Editor: EditorUtility.RevealInFinder.
/// - Android: scoped storage cannot "reveal" app-private folders, so the
///   CSV is copied into the public Downloads/PyQuest folder via MediaStore
///   (no storage permission needed on Android 10+), then ACTION_VIEW with
///   text/csv lets the OS offer the file manager / spreadsheet viewer at
///   that file. Falls back to the legacy share sheet if that fails.
/// - iOS: sandboxing forbids revealing app folders; best effort via
///   shareddocuments:// (Files app).
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
    /// <param name="csvFilePathOverride">
    /// Optional explicit CSV path to reveal. Leave null/empty to use the
    /// canonical CSV from StudentLogManager (the file that was just written).
    /// </param>
    public static void ExportAndShare(
        MonoBehaviour coroutineHost,
        string androidShareTitle,
        System.Action<string> onStatus,
        string csvFilePathOverride = null)
    {
        string path;
        if (!string.IsNullOrEmpty(csvFilePathOverride))
        {
            path = csvFilePathOverride;
        }
        else if (StudentLogManager.Instance == null)
        {
            onStatus?.Invoke("Could not find results, StudentLogManager missing.");
            return;
        }
        else
        {
            path = StudentLogManager.Instance.ExportCanonicalCsv();
        }

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            onStatus?.Invoke("Could not create the results file. Try again, or tell your evaluator.");
            return;
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        if (TryAndroidReveal(path, onStatus)) return;
        // Fallback: legacy share sheet still gets the file to the evaluator.
        bool launched = AndroidNativeShare.ShareFile(path, "text/csv", androidShareTitle);
        onStatus?.Invoke(launched
            ? "Opening the share menu..."
            : "Could not open the file manager or share menu. Try again, or tell your evaluator.");
#elif UNITY_STANDALONE_WIN
        coroutineHost.StartCoroutine(RevealInExplorerRoutine(path, onStatus));
#else
        RevealDesktop(path, onStatus);
#endif
    }

#if UNITY_ANDROID && !UNITY_EDITOR
    /// <summary>
    /// Publishes the CSV into public storage (Downloads/PyQuest) via
    /// MediaStore and fires ACTION_VIEW so the OS opens a file manager or
    /// spreadsheet viewer at the file. Returns false if anything failed so
    /// the caller can fall back to the share sheet.
    /// </summary>
    private static bool TryAndroidReveal(string path, System.Action<string> onStatus)
    {
        try
        {
            using (var upClass = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (var activity = upClass.GetStatic<AndroidJavaObject>("currentActivity"))
            {
                var apiLevel = new AndroidJavaClass("android.os.Build$VERSION").GetStatic<int>("SDK_INT");

                string uriString;
                if (apiLevel >= 29)
                {
                    // Scoped storage: insert into MediaStore.Downloads under PyQuest/.
                    using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                    using (var values = new AndroidJavaObject("android.content.ContentValues"))
                    {
                        values.Call<AndroidJavaObject>("put", "_display_name", Path.GetFileName(path))
                              .Call<AndroidJavaObject>("put", "mime_type", "text/csv")
                              .Call<AndroidJavaObject>("put", "relative_path", "Download/PyQuest");
                        var downloadsUri = new AndroidJavaClass("android.provider.MediaStore$Downloads").GetStatic<AndroidJavaObject>("CONTENT_URI");
                        using (var uri = resolver.Call<AndroidJavaObject>("insert", downloadsUri, values))
                        {
                            if (uri == null) return false;
                            uriString = uri.Call<string>("toString");
                        }
                    }
                }
                else
                {
                    // Pre-Android-10: file:// ACTION_VIEW is blocked by
                    // FileUriExposedException, so let the caller fall back to
                    // the legacy share sheet (which handles this correctly).
                    return false;
                }

                // Copy the bytes into the (new) MediaStore entry.
                using (var parsedUri = new AndroidJavaClass("android.net.Uri").CallStatic<AndroidJavaObject>("parse", uriString))
                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var stream = resolver.Call<AndroidJavaObject>("openOutputStream", parsedUri))
                {
                    // getOutputStream via reflection-free call: openOutputStream returns OutputStream.
                    byte[] buf = File.ReadAllBytes(path);
                    stream.Call<int>("write", buf);
                    stream.Call("flush");
                }

                // ACTION_VIEW on the published file.
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.VIEW"))
                using (var parsed = new AndroidJavaClass("android.net.Uri").CallStatic<AndroidJavaObject>("parse", uriString))
                {
                    intent.Call<AndroidJavaObject>("setDataAndType", parsed, "text/csv");
                    intent.Call<AndroidJavaObject>("addFlags", 0x10000000); // FLAG_ACTIVITY_NEW_TASK
                    activity.Call("startActivity", intent);
                }

                onStatus?.Invoke("Results copied to Downloads/PyQuest — opening your file manager.");
                return true;
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ResultsExportHelper] Android reveal failed: {e.Message}");
            return false;
        }
    }
#endif

#if !UNITY_ANDROID || UNITY_EDITOR
    /// <summary>Desktop/Editor reveal: Explorer-select, Finder reveal, or xdg-open.</summary>
    private static void RevealDesktop(string path, System.Action<string> onStatus)
    {
#if UNITY_EDITOR
        UnityEditor.EditorUtility.RevealInFinder(path);
        onStatus?.Invoke("Revealed in your file manager: " + path);
#elif UNITY_STANDALONE_OSX
        try
        {
            // open -R reveals the file itself in Finder.
            System.Diagnostics.Process.Start("open", "-R \"" + path + "\"");
            onStatus?.Invoke("Revealed in Finder.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ResultsExportHelper] Finder reveal failed ({e.Message}); opening folder instead.");
            try
            {
                System.Diagnostics.Process.Start("open", "\"" + Path.GetDirectoryName(path) + "\"");
                onStatus?.Invoke("Opened the folder in Finder.");
            }
            catch (System.Exception e2) { Debug.LogWarning($"[ResultsExportHelper] Folder open failed: {e2.Message}"); onStatus?.Invoke("Your results file is at: " + path); }
        }
#elif UNITY_STANDALONE_LINUX
        try
        {
            System.Diagnostics.Process.Start("xdg-open", "\"" + Path.GetDirectoryName(path) + "\"");
            onStatus?.Invoke("Opened the file's folder.");
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[ResultsExportHelper] xdg-open failed: {e.Message}");
            onStatus?.Invoke("Your results file is at: " + path);
        }
#else
        onStatus?.Invoke("Your results file is at: " + path);
#endif
    }
#endif

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