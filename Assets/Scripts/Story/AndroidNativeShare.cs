using System.IO;
using UnityEngine;

/// <summary>
/// Fires Android's native share sheet (Intent.ACTION_SEND) with a file
/// attached, so a student can send their results CSV via whatever app
/// they already have installed (Gmail, Messenger, Drive, etc.) without
/// touching a file manager.
///
/// REQUIRES, outside of this script, in the Unity Android project:
///   1. Player Settings > Publishing Settings > Custom Main Manifest
///      enabled, with a &lt;provider&gt; entry for androidx.core.content.FileProvider
///      added to AndroidManifest.xml (see chat for the exact XML to merge in,
///      not provided as a standalone file here since it must be merged into
///      Unity's generated manifest, not overwrite it).
///   2. Assets/Plugins/Android/res/xml/file_paths.xml declaring an
///      &lt;external-files-path&gt; (not &lt;files-path&gt;), since
///      Application.persistentDataPath on Android resolves to
///      getExternalFilesDir(null), not internal app storage.
///   3. AndroidX available in the Gradle build (Unity 2022.3 LTS default).
///
/// Does nothing on non-Android platforms, callers should branch on
/// platform themselves for the Windows equivalent (Explorer reveal).
/// </summary>
public static class AndroidNativeShare
{
    /// <summary>
    /// Opens the native "Send with..." chooser for filePath.
    /// Returns false immediately (before the chooser opens) if the file
    /// doesn't exist or isn't Android; check the return value to know
    /// whether to show an in-game error instead of assuming success,
    /// the chooser itself can't report back whether the student
    /// actually completed the send.
    /// </summary>
    public static bool ShareFile(string filePath, string mimeType, string chooserTitle)
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
        {
            Debug.LogError($"[AndroidNativeShare] File not found: {filePath}");
            return false;
        }

        try
        {
            using (AndroidJavaClass unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
            using (AndroidJavaObject currentActivity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
            using (AndroidJavaObject context = currentActivity.Call<AndroidJavaObject>("getApplicationContext"))
            {
                string authority = Application.identifier + ".fileprovider";

                using (AndroidJavaObject javaFile = new AndroidJavaObject("java.io.File", filePath))
                using (AndroidJavaClass fileProviderClass = new AndroidJavaClass("androidx.core.content.FileProvider"))
                {
                    AndroidJavaObject contentUri = fileProviderClass.CallStatic<AndroidJavaObject>(
                        "getUriForFile", context, authority, javaFile);

                    using (AndroidJavaObject intentObject = new AndroidJavaObject("android.content.Intent"))
                    using (AndroidJavaClass intentClass = new AndroidJavaClass("android.content.Intent"))
                    {
                        intentObject.Call<AndroidJavaObject>("setAction", "android.intent.action.SEND");
                        intentObject.Call<AndroidJavaObject>("putExtra", "android.intent.extra.STREAM", contentUri);
                        intentObject.Call<AndroidJavaObject>("setType", mimeType);

                        int grantReadFlag = intentClass.GetStatic<int>("FLAG_GRANT_READ_URI_PERMISSION");
                        intentObject.Call<AndroidJavaObject>("addFlags", grantReadFlag);

                        using (AndroidJavaObject chooser = intentClass.CallStatic<AndroidJavaObject>(
                            "createChooser", intentObject, chooserTitle))
                        {
                            currentActivity.Call("startActivity", chooser);
                        }
                    }
                }
            }

            Debug.Log($"[AndroidNativeShare] Share sheet launched for: {filePath}");
            return true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[AndroidNativeShare] Failed to launch share sheet: {e.Message}\n{e.StackTrace}");
            return false;
        }
#else
        Debug.LogWarning("[AndroidNativeShare] ShareFile called on a non-Android platform, ignoring.");
        return false;
#endif
    }
}