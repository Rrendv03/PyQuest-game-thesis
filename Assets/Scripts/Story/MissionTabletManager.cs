using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class MissionTabletManager : MonoBehaviour
{
    public static MissionTabletManager Instance;

    private List<MissionTabletData> allMissions = new List<MissionTabletData>();
    private HashSet<string> _completedMissionIDs = new HashSet<string>();

    // ANDROID LOADING NOTES (same pattern as DialogueManager / dialogue.json):
    // - Application.streamingAssetsPath on Android is a jar:file://...!/assets
    //   URL INSIDE the APK. System.IO.File.Exists / File.ReadAllText cannot
    //   read it there, which is why this manager loaded 0 missions on device
    //   while working in the editor (where StreamingAssets is a real folder).
    //   The file must be fetched with UnityWebRequest.
    // - Loading is therefore asynchronous, so consumers (TabletMissionObject,
    //   MissionTabletUI, BossGate-adjacent UI, etc.) must check IsLoaded or
    //   subscribe to MissionsLoaded before calling GetMissionByID /
    //   GetMissionsForSanctum.

    /// <summary>
    /// True once MissionTabletQuests.json has been fetched and parsed
    /// (successfully or not). Safe to poll in a coroutine:
    /// yield return new WaitUntil(() => MissionTabletManager.Instance &amp;&amp; MissionTabletManager.Instance.IsLoaded);
    /// </summary>
    public bool IsLoaded { get; private set; } = false;

    /// <summary>Fired exactly once, right after IsLoaded becomes true.</summary>
    public event Action MissionsLoaded;

    private bool _warnedNotLoaded = false;

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            StartCoroutine(LoadMissionsRoutine());
        }
        else
        {
            Destroy(gameObject);
        }
    }

    // === Load MissionTabletQuests.json =========================================================
    private IEnumerator LoadMissionsRoutine()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "MissionTabletQuests.json");
        string json = "";

#if UNITY_ANDROID && !UNITY_EDITOR
        // Diagnostic logging: the exact URL requested and the exact status
        // returned. If the file is packaged at this path inside the APK this
        // request cannot 404; a 404 means the build does not contain the file
        // (wrong folder, different casing, or a stale APK). Android is
        // case-sensitive: MissionTabletQuests.json must match the file name
        // byte-for-byte, including capital M, T and Q.
        Debug.Log("[MissionTabletManager] Requesting MissionTabletQuests.json from: " + path);
        using (var req = UnityEngine.Networking.UnityWebRequest.Get(path))
        {
            yield return req.SendWebRequest();
            Debug.Log($"[MissionTabletManager] MissionTabletQuests.json request finished | " +
                      $"result={req.result} | responseCode={req.responseCode} | " +
                      $"url={req.url} | error={req.error}");

            if (req.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
            {
                json = req.downloadHandler.text ?? "";

                // Byte-level probe: a UTF-8 BOM is EF BB BF; UTF-16 LE is FF FE.
                // File.ReadAllText in the editor silently strips a BOM, while
                // downloadHandler.text keeps it, which is how the same file can
                // parse in-editor but throw on device.
                byte[] bytes = req.downloadHandler.data;
                if (bytes != null && bytes.Length >= 3)
                    Debug.Log($"[MissionTabletManager] first bytes: " +
                              $"{bytes[0]:X2} {bytes[1]:X2} {bytes[2]:X2} " +
                              $"(UTF-8 BOM would be EF BB BF; UTF-16 LE would be FF FE)");
            }
            else
            {
                Debug.LogError("[MissionTabletManager] Failed to load MissionTabletQuests.json: " + req.error +
                               " | responseCode=" + req.responseCode +
                               " | If responseCode=404, open the APK as a zip and confirm " +
                               "assets/MissionTabletQuests.json exists with this exact name and casing.");
            }
        }
#else
        if (File.Exists(path))
            json = File.ReadAllText(path); // editor/desktop: real filesystem, strips a BOM automatically
        else
            Debug.LogError("[MissionTabletManager] MissionTabletQuests.json not found at: " + path);
        yield return null; // keep editor timing async too, like on device
#endif

        // File.ReadAllText (editor) consumes a leading UTF-8 BOM, but
        // downloadHandler.text (Android) keeps it as U+FEFF and JsonUtility
        // then throws before a single mission deserializes. Trim it here so
        // both platforms parse the exact same string. Harmless without a BOM.
        json = (json ?? "").TrimStart('\uFEFF');

        Debug.Log($"[MissionTabletManager] MissionTabletQuests.json text length: {json.Length}");

        allMissions = new List<MissionTabletData>();

        if (string.IsNullOrEmpty(json))
        {
            Debug.LogError("[MissionTabletManager] MissionTabletQuests.json was empty or unreadable; " +
                           "no tablet missions will be available. Check the log above for the request error.");
        }
        else
        {
            // try/catch so a malformed/BOM'd file logs a clear error and still
            // reaches IsLoaded = true, instead of killing this coroutine and
            // leaving every tablet waiting forever.
            try
            {
                MissionTabletWrapper wrapper = JsonUtility.FromJson<MissionTabletWrapper>(json);
                if (wrapper != null && wrapper.missions != null)
                {
                    allMissions = wrapper.missions;
                }
                else
                {
                    // Loaded fine but nothing deserialized: on IL2CPP Android
                    // builds with Managed Stripping Level above Minimal, the
                    // fields of [Serializable] classes can be stripped, so
                    // JsonUtility silently yields null. Fix with link.xml or
                    // Player Settings > Managed Stripping Level = Minimal.
                    Debug.LogError("[MissionTabletManager] MissionTabletQuests.json parsed but wrapper.missions is null. " +
                                   "If this only happens on device, check Managed Stripping Level (Player Settings) " +
                                   "and add a link.xml preserving MissionTabletData / MissionTabletWrapper.");
                }
            }
            catch (Exception e)
            {
                Debug.LogError("[MissionTabletManager] JSON parse failed. Read the 'first bytes' log above: " +
                               "U+FEFF or bytes EF BB BF mean the file was saved as UTF-8 WITH BOM " +
                               "(re-save as plain UTF-8); FF FE or garbage means it is not UTF-8 at all. " +
                               "Exception: " + e);
            }
        }

        IsLoaded = true;
        Debug.Log($"[MissionTabletManager] Loaded {allMissions.Count} missions.");
        MissionsLoaded?.Invoke();
    }

    public MissionTabletData GetMissionByID(string missionID)
    {
        if (!IsLoaded)
            WarnNotLoaded($"GetMissionByID('{missionID}')");
        foreach (var m in allMissions)
            if (m.missionID == missionID) return m;
        return null;
    }

    public List<MissionTabletData> GetMissionsForSanctum(string sanctumID)
    {
        if (!IsLoaded)
            WarnNotLoaded($"GetMissionsForSanctum('{sanctumID}')");
        List<MissionTabletData> result = new List<MissionTabletData>();
        foreach (var m in allMissions)
            if (m.sanctumID == sanctumID) result.Add(m);
        return result;
    }

    // Logged once, not per call: surfaces the async-timing race (a consumer
    // reading missions before the fetch finished) instead of hiding it as a
    // misleading "missionID not found" error.
    private void WarnNotLoaded(string call)
    {
        if (_warnedNotLoaded) return;
        _warnedNotLoaded = true;
        Debug.LogWarning("[MissionTabletManager] " + call + " was called before MissionTabletQuests.json finished " +
                         "loading (it is fetched asynchronously on Android). Wait for IsLoaded / MissionsLoaded. " +
                         "This warning is logged once per session.");
    }

    public void CompleteMission(string missionID)
    {
        if (string.IsNullOrEmpty(missionID)) return;
        if (_completedMissionIDs.Add(missionID))
        {
            Debug.Log($"[MissionTabletManager] Mission complete: {missionID}");

            // ADD THIS: Refresh all gates in case this unlocked the boss
            RefreshAllBossGates();
        }
    }

    // ADD THIS METHOD:
    private void RefreshAllBossGates()
    {
        BossGate[] gates = FindObjectsOfType<BossGate>();
        foreach (var gate in gates)
            gate.Refresh();
    }

    public bool IsMissionComplete(string missionID)
    {
        return !string.IsNullOrEmpty(missionID) && _completedMissionIDs.Contains(missionID);
    }

    public bool AreAllSanctumMissionsComplete(string sanctumID)
    {
        foreach (var m in allMissions)
            if (m.sanctumID == sanctumID && !_completedMissionIDs.Contains(m.missionID))
                return false;
        return true;
    }

    public bool IsBossUnlockReady(string sanctumID)
    {
        bool xpMet = XPManager.Instance != null && XPManager.Instance.IsBossUnlocked(sanctumID);
        bool missionsMet = AreAllSanctumMissionsComplete(sanctumID);
        return xpMet && missionsMet;
    }

    public List<string> ExportCompletedMissions()
    {
        return new List<string>(_completedMissionIDs);
    }

    public void ImportCompletedMissions(List<string> ids)
    {
        _completedMissionIDs.Clear();
        if (ids == null) return;
        foreach (var id in ids) _completedMissionIDs.Add(id);
    }

    public void ResetMissions()
    {
        _completedMissionIDs.Clear();
    }
}
