using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages three manual save slots and one dedicated autosave slot.
/// Autosave never overwrites manual slots.
/// Fires every 5 minutes while IsSafeToSave is true.
///
/// Manual saves also refresh the student's canonical log CSV, but only
/// when the current playthrough is not behind a more-progressed
/// playthrough already recorded (see TryExportCanonicalCsv).
///
/// Canonical single source of truth for SaveSlotData - do not keep
/// a second copy of this class anywhere else in the project, having
/// two SaveLoadManager/SaveSlotData definitions is what caused the
/// missing-field compile errors this file fixes.
/// </summary>
public class SaveLoadManager : MonoBehaviour
{
    public static SaveLoadManager Instance;
    public static bool IsSafeToSave = true;

    private NPCStateEntry[] _lastAppliedNPCStates = null;

    [Header("Autosave")]
    public float autosaveIntervalSeconds = 300f;

    private float autosaveTimer = 0f;
    private const string AutosaveFilename = "save_autosave.json";

    // Canonical CSV overwrite protection (see TryExportCanonicalCsv).
    // The student log is ONE fixed-name CSV per student that is
    // overwritten wholesale on every export, so an export must never
    // fire while the current playthrough is behind the data already in
    // that file (a replayed, less-progressed storyline would otherwise
    // destroy the recorded playthrough's data).
    private const string LastExportFilename = "studentlog_export_progress.json";

    // Clearing this sanctum is what fires the epilogue (see
    // EpilogueSequenceController.finalSanctumID), so "this playthrough
    // has defeated it" is the same statement as "this playthrough
    // reached the epilogue".
    private const string FinalSanctumID = "elif_labyrinth";

    private static string LastExportPath
        => Path.Combine(Application.persistentDataPath, LastExportFilename);

    private static string SlotFilename(int slot)
        => slot == 0 ? AutosaveFilename : $"save_slot_{slot}.json";

    private static string SlotPath(int slot)
        => Path.Combine(Application.persistentDataPath, SlotFilename(slot));

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void Update()
    {
        // Prefer SaveRestrictionEnforcer's blocker set when present;
        // fall back to the legacy static flag for scenes that haven't
        // wired a blocker in yet.
        bool safe = SaveRestrictionEnforcer.Instance != null
            ? SaveRestrictionEnforcer.Instance.IsSafeToSave
            : IsSafeToSave;

        if (!safe) return;

        autosaveTimer += Time.deltaTime;
        if (autosaveTimer >= autosaveIntervalSeconds)
        {
            autosaveTimer = 0f;
            SaveToSlot(0);
        }
    }

    // == Save ==================================================
    public void SaveToSlot(int slot)
    {
        if (slot < 0 || slot > 3)
        {
            Debug.LogWarning($"[SaveLoadManager] Invalid slot {slot}.");
            return;
        }

        // Snapshot what the existing save files hold BEFORE this save
        // rewrites one of them. These snapshots are the reference points
        // for the canonical CSV protection: if any of them holds a more
        // progressed playthrough than the one being saved now, the CSV
        // currently on disk may belong to that more progressed play, so
        // the export must not fire. This must therefore run BEFORE
        // File.WriteAllText below, never after.
        List<CsvProgressSnapshot> priorSaves = SnapshotExistingSaves();

        SaveSlotData data = BuildSaveData(slot);
        string json = JsonUtility.ToJson(data, true);

        try
        {
            File.WriteAllText(SlotPath(slot), json);
            Debug.Log($"[SaveLoadManager] Saved slot {slot}.");

            // Cement the student log CSV on MANUAL saves only (slot != 0).
            // Autosave (slot 0) does not trigger this, per the requirement
            // that this only fires when the respondent deliberately saves,
            // not on the 5-minute timer. ExportCanonicalCsv() overwrites
            // one fixed-name file every time, it does not append, so
            // calling it on every manual save cannot produce duplicate rows.
            //
            // It also CANNOT overwrite a more-progressed playthrough's
            // data with a replay's log without losing that playthrough, so
            // the export call is threaded through TryExportCanonicalCsv,
            // which refuses to export while the current playthrough's
            // story progression is behind any more-progressed playthrough
            // already recorded (existing saves, last export, or a finished
            // epilogue on this device).
            if (slot != 0)
                TryExportCanonicalCsv(slot, data, priorSaves);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveLoadManager] Failed to write slot {slot}: {e.Message}");
        }
    }

    /// <summary>
    /// Convenience wrapper used by SanctumManager and other systems
    /// that just want "save now to the dedicated autosave slot"
    /// without knowing slot numbering.
    /// </summary>
    public void AutoSave() => SaveToSlot(0);

    /// <summary>
    /// Convenience query for BossGate and similar scripts that need a
    /// yes/no on "has this sanctum's boss been defeated" without going
    /// through StoryProgressionManager directly. Delegates to it rather
    /// than tracking a second copy of the same state.
    /// </summary>
    public bool IsSanctumBossDefeated(string sanctumID)
    {
        return StoryProgressionManager.Instance != null
            && StoryProgressionManager.Instance.HasDefeatedBoss(sanctumID);
    }

    /// <summary>
    /// Player is always scene-local by design (see PlayerMovement.cs /
    /// SceneTransition.RespawnPoint), so there is no valid persistent
    /// reference to cache. Always resolve fresh, by tag, at save time.
    /// </summary>
    private Transform ResolvePlayerTransform()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
        return playerObj != null ? playerObj.transform : null;
    }

    private SaveSlotData BuildSaveData(int slot)
    {
        SaveSlotData data = new SaveSlotData();
        data.slotNumber = slot;
        data.dateSavedISO = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        data.currentScene = SceneManager.GetActiveScene().name;

        Transform player = ResolvePlayerTransform();
        if (player != null)
        {
            data.playerPositionX = player.position.x;
            data.playerPositionY = player.position.y;
            data.playerPositionZ = player.position.z;
            data.playerYRotation = player.eulerAngles.y;
        }
        else
        {
            Debug.LogWarning("[SaveLoadManager] No Player found in scene; " +
                              "position not saved for this slot.");
        }

        if (StoryProgressionManager.Instance != null)
        {
            data.completedQuestIDs = StoryProgressionManager.Instance.ExportCompletedQuestIDs();
            data.activeQuestID = StoryProgressionManager.Instance.ExportActiveQuestID();

            data.storyProgression = new StoryProgressionData
            {
                visitedSanctums = StoryProgressionManager.Instance.ExportVisitedSanctums(),
                defeatedBosses = StoryProgressionManager.Instance.ExportDefeatedBosses()
            };

            // Rune crystals activate on boss defeat, so "restored" is
            // the same signal as "defeated" - no separate tracking needed.
            data.restoredCrystalSanctums = StoryProgressionManager.Instance.ExportDefeatedBosses();
        }

        if (BKTEngine.Instance != null)
            data.bktMastery = BKTEngine.Instance.ExportMastery();

        if (MissionTabletManager.Instance != null)
            data.completedMissionIDs = MissionTabletManager.Instance.ExportCompletedMissions();

        if (StudentLogManager.Instance != null)
            data.studentLogs = StudentLogManager.Instance.ExportLogs();

        data.npcStates = new List<NPCStateEntry>();
        NPCController[] npcs = FindObjectsOfType<NPCController>();
        foreach (var npc in npcs)
        {
            data.npcStates.Add(new NPCStateEntry
            {
                npcID = npc.npcID,
                currentSequenceID = npc.GetCurrentSequenceID(),
                hasDeparted = npc.HasDeparted()
            });
        }

        data.missionDisplayName = GetMissionDisplayName(data.activeQuestID);
        data.locationDisplayName = GetLocationDisplayName(data.currentScene);

        SaveSlotData existing = LoadFromSlot(slot);
        data.playTimeSeconds = existing != null ? existing.playTimeSeconds : 0f;

        if (XPManager.Instance != null)
            data.currentXP = XPManager.Instance.ExportXP();

        return data;
    }

    // == Load ==================================================
    public SaveSlotData LoadFromSlot(int slot)
    {
        string path = SlotPath(slot);
        if (!File.Exists(path)) return null;

        try
        {
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<SaveSlotData>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveLoadManager] Failed to read slot {slot}: {e.Message}");
            return null;
        }
    }

    public void ApplySaveData(SaveSlotData data)
    {
        if (data == null) return;

        if (StoryProgressionManager.Instance != null)
        {
            StoryProgressionManager.Instance.ImportCompletedQuestIDs(data.completedQuestIDs);
            StoryProgressionManager.Instance.ImportActiveQuestID(data.activeQuestID);

            if (data.storyProgression != null)
            {
                StoryProgressionManager.Instance.ImportVisitedSanctums(data.storyProgression.visitedSanctums);
                StoryProgressionManager.Instance.ImportDefeatedBosses(data.storyProgression.defeatedBosses);
            }
        }

        if (QuestManager.Instance != null)
            QuestManager.Instance.OnQuestCompleted(data.activeQuestID);

        if (XPManager.Instance != null)
            XPManager.Instance.ImportXP(data.currentXP);

        if (BKTEngine.Instance != null)
            BKTEngine.Instance.ImportMastery(data.bktMastery);

        if (MissionTabletManager.Instance != null)
            MissionTabletManager.Instance.ImportCompletedMissions(data.completedMissionIDs);

        if (StudentLogManager.Instance != null)
            StudentLogManager.Instance.ImportLogs(data.studentLogs);

        SceneTransition.RespawnPoint = new Vector3(
            data.playerPositionX,
            data.playerPositionY,
            data.playerPositionZ);
        SceneTransition.RespawnYRotation = data.playerYRotation;
        SceneTransition.SkipSpawnPositioning = true;

        // Critical: restore timeScale and save flag before loading.
        // The pause menu sets timeScale = 0 when open. If we load a scene
        // while timeScale is 0, SceneEntrance.FadeIn() coroutine never runs
        // and the black overlay never disappears.
        Time.timeScale = 1f;
        IsSafeToSave = true;
        SaveRestrictionEnforcer.Instance?.ClearAllBlockers();

        _lastAppliedNPCStates = data.npcStates != null ? data.npcStates.ToArray() : null;

        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.LoadScene(data.currentScene);
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        NPCController[] npcs = FindObjectsOfType<NPCController>();
        if (_lastAppliedNPCStates != null)
        {
            foreach (var entry in _lastAppliedNPCStates)
            {
                foreach (var npc in npcs)
                {
                    if (entry.npcID == npc.npcID)
                    {
                        npc.RestoreState(entry.currentSequenceID, entry.hasDeparted);
                        break;
                    }
                }
            }
        }
    }

    public bool SlotExists(int slot) => File.Exists(SlotPath(slot));

    public void DeleteSlot(int slot)
    {
        string path = SlotPath(slot);
        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log($"[SaveLoadManager] Deleted slot {slot}.");
        }
    }

    // == Display Name Lookups ==================================
    private string GetMissionDisplayName(string questID)
    {
        if (QuestManager.Instance != null)
        {
            string name = QuestManager.Instance.GetActiveQuestDisplayName();
            if (!string.IsNullOrEmpty(name)) return name;
        }

        return string.IsNullOrEmpty(questID) ? "No Active Mission" : questID;
    }

    private string GetLocationDisplayName(string sceneName)
    {
        switch (sceneName)
        {
            case "IntroScene": return "Aethelscript - Arrival";
            case "MainMap": return "Aethelscript";
            case "PrintConsole": return "Print Console";
            case "VarsVault": return "Vars Vault";
            case "InputMists": return "Input Mists";
            case "ElifLabyrinth": return "Elif Labyrinth";
            default: return string.IsNullOrEmpty(sceneName) ? "Unknown" : sceneName;
        }
    }

    // == Canonical CSV overwrite protection ====================

    /// <summary>
    /// Storyline-progress fingerprint of one playthrough. Quests
    /// completed are the primary measure of how far through the
    /// storyline a play got; defeated bosses are only the tie-breaker
    /// for two plays with the same quest count.
    /// </summary>
    private static void ProgressOf(SaveSlotData data, out int quests, out int bosses)
    {
        if (data == null)
        {
            quests = 0;
            bosses = 0;
            return;
        }

        quests = data.completedQuestIDs != null ? data.completedQuestIDs.Count : 0;
        bosses = data.storyProgression != null && data.storyProgression.defeatedBosses != null
            ? data.storyProgression.defeatedBosses.Count
            : 0;
    }

    /// <summary>
    /// True when playthrough A got further through the storyline than
    /// playthrough B (lexicographic on quests, then bosses). Equal
    /// progress is NOT "more progressed": a replay that has caught up
    /// to the same point is allowed to export, it is the freshest data
    /// at that progress level.
    /// </summary>
    private static bool IsMoreProgressed(int aQuests, int aBosses, int bQuests, int bBosses)
    {
        if (aQuests != bQuests) return aQuests > bQuests;
        return aBosses > bBosses;
    }

    /// <summary>
    /// Reads every save slot file that exists BEFORE the caller
    /// overwrites one of them, so the protection in
    /// ExportAllowedByReferences still sees the playthrough this save
    /// is about to replace. Slots that are missing or fail to parse
    /// are skipped, not fatal.
    /// </summary>
    private List<CsvProgressSnapshot> SnapshotExistingSaves()
    {
        var snapshots = new List<CsvProgressSnapshot>();
        for (int slot = 0; slot <= 3; slot++)
        {
            SaveSlotData existing = LoadFromSlot(slot);
            if (existing == null) continue;

            ProgressOf(existing, out int quests, out int bosses);
            snapshots.Add(new CsvProgressSnapshot
            {
                slot = slot,
                questCount = quests,
                bossCount = bosses,
                dateSavedISO = existing.dateSavedISO,
                currentScene = existing.currentScene
            });
        }
        return snapshots;
    }

    /// <summary>
    /// Save-triggered canonical export. Called from SaveToSlot on
    /// manual saves only, with the snapshots taken before the slot
    /// file was rewritten. Decides via ExportAllowedByReferences,
    /// then delegates to StudentLogManager.ExportCanonicalCsv and
    /// stamps the export-progress marker on success.
    /// </summary>
    private void TryExportCanonicalCsv(int slot, SaveSlotData data, List<CsvProgressSnapshot> priorSaves)
    {
        if (StudentLogManager.Instance == null)
        {
            Debug.LogWarning("[SaveLoadManager] StudentLogManager missing; canonical CSV export not attempted.");
            return;
        }

        ProgressOf(data, out int curQuests, out int curBosses);

        bool thisRunFinished = data != null
            && data.storyProgression != null
            && data.storyProgression.defeatedBosses != null
            && data.storyProgression.defeatedBosses.Contains(FinalSanctumID);

        string studentID = StudentLogManager.Instance.studentID;

        if (!ExportAllowedByReferences(curQuests, curBosses, thisRunFinished, studentID,
                priorSaves, out string blockReason))
        {
            Debug.LogWarning("[SaveLoadManager] Canonical CSV export SKIPPED (slot " + slot + "): " +
                "this playthrough has " + curQuests + " quests / " + curBosses + " bosses, but " +
                blockReason + ". The existing pyquest_log CSV is kept so the earlier, " +
                "more-progressed playthrough's data is not lost.");
            return;
        }

        string path = StudentLogManager.Instance.ExportCanonicalCsv();
        if (path == null) return; // Exporter already logged the write failure.

        WriteExportMarker(studentID, curQuests, curBosses);
        Debug.Log("[SaveLoadManager] Canonical CSV exported (slot " + slot + "): " + path);
    }

    /// <summary>
    /// UI-triggered canonical export (results / share screens), used by
    /// ResultsExportHelper. Same protection as the save-triggered
    /// export, but reads the CURRENT live playthrough state from
    /// StoryProgressionManager instead of a save slot. Returns false
    /// with a human-readable blockReason when exporting would clobber
    /// a more-progressed playthrough's recorded CSV; on success returns
    /// the path that was written.
    /// </summary>
    public static bool TryExportCanonicalCsv(out string path, out string blockReason)
    {
        path = null;
        blockReason = null;

        if (Instance == null)
        {
            blockReason = "SaveLoadManager is not in the scene";
            return false;
        }
        if (StudentLogManager.Instance == null)
        {
            blockReason = "StudentLogManager is not in the scene";
            return false;
        }

        int curQuests = 0;
        int curBosses = 0;
        bool thisRunFinished = false;

        if (StoryProgressionManager.Instance != null)
        {
            List<string> questsDone = StoryProgressionManager.Instance.ExportCompletedQuestIDs();
            curQuests = questsDone != null ? questsDone.Count : 0;

            List<string> bossesDone = StoryProgressionManager.Instance.ExportDefeatedBosses();
            curBosses = bossesDone != null ? bossesDone.Count : 0;
            thisRunFinished = bossesDone != null && bossesDone.Contains(FinalSanctumID);
        }

        string studentID = StudentLogManager.Instance.studentID;

        if (!ExportAllowedByReferences(curQuests, curBosses, thisRunFinished, studentID,
                Instance.SnapshotExistingSaves(), out blockReason))
            return false;

        path = StudentLogManager.Instance.ExportCanonicalCsv();
        if (path == null)
        {
            blockReason = "the CSV write failed (see the log)";
            return false;
        }

        WriteExportMarker(studentID, curQuests, curBosses);
        return true;
    }

    /// <summary>
    /// The actual protection decision. The canonical CSV may only be
    /// overwritten when every progress reference this device still
    /// holds is no further through the storyline than the playthrough
    /// about to be exported:
    ///   1. every save slot file currently on disk (including the slot
    ///      this save is overwriting, since SnapshotExistingSaves ran
    ///      before the rewrite),
    ///   2. the last-export marker - what the canonical CSV holds right
    ///      now, which survives even when every save slot has since
    ///      been reused by a replay or deleted, and
    ///   3. the finished-epilogue flag, because completing the game
    ///      once on this device means an unfinished replay must never
    ///      overwrite that run.
    /// References 1 and 2 are keyed to the current student ID, so a
    /// different respondent on the same device starts with a fresh
    /// baseline. Reference 3 is a device-level PlayerPrefs flag and
    /// assumes one respondent per device, matching how
    /// StudentLogManager identifies students.
    /// </summary>
    private static bool ExportAllowedByReferences(
        int curQuests, int curBosses, bool thisRunFinished, string studentID,
        List<CsvProgressSnapshot> priorSaves, out string blockReason)
    {
        blockReason = null;

        // (1) Save slots as they existed on disk.
        if (priorSaves != null)
        {
            foreach (CsvProgressSnapshot snap in priorSaves)
            {
                if (snap == null) continue;
                if (IsMoreProgressed(snap.questCount, snap.bossCount, curQuests, curBosses))
                {
                    blockReason = "save slot " + snap.slot + " holds a playthrough further " +
                        "along the storyline (saved " + snap.dateSavedISO + ", " +
                        snap.questCount + " quests / " + snap.bossCount + " bosses, in " +
                        snap.currentScene + ")";
                    return false;
                }
            }
        }

        // (2) What the canonical CSV holds right now.
        ExportProgressMarker marker = ReadExportMarker();
        if (marker != null
            && marker.studentID == studentID
            && IsMoreProgressed(marker.questCount, marker.bossCount, curQuests, curBosses))
        {
            blockReason = "the canonical CSV already records a playthrough further " +
                "along the storyline (exported " + marker.dateISO + ", " +
                marker.questCount + " quests / " + marker.bossCount + " bosses)";
            return false;
        }

        // (3) A finished run exists on this device.
        if (GameCompletionState.HasCompletedGame() && !thisRunFinished)
        {
            blockReason = "the game was already completed on this device and this " +
                "playthrough has not reached the epilogue (" + FinalSanctumID + " not cleared)";
            return false;
        }

        return true;
    }

    private static ExportProgressMarker ReadExportMarker()
    {
        try
        {
            if (!File.Exists(LastExportPath)) return null;
            return JsonUtility.FromJson<ExportProgressMarker>(File.ReadAllText(LastExportPath));
        }
        catch (Exception e)
        {
            Debug.LogWarning("[SaveLoadManager] Could not read export-progress marker: " + e.Message);
            return null;
        }
    }

    private static void WriteExportMarker(string studentID, int quests, int bosses)
    {
        try
        {
            ExportProgressMarker marker = new ExportProgressMarker
            {
                studentID = studentID,
                questCount = quests,
                bossCount = bosses,
                dateISO = DateTime.UtcNow.ToString("o")
            };
            File.WriteAllText(LastExportPath, JsonUtility.ToJson(marker, true));
        }
        catch (Exception e)
        {
            // The export itself already succeeded; a missing marker only
            // weakens future protection, it never loses data.
            Debug.LogWarning("[SaveLoadManager] Could not write export-progress marker: " + e.Message);
        }
    }
}

// == Data Structures ==========================================
[Serializable]
public class SaveSlotData
{
    public int slotNumber;
    public string missionDisplayName;
    public string locationDisplayName;
    public float playTimeSeconds;
    public int currentXP;
    public string dateSavedISO;
    public float playerPositionX;
    public float playerPositionY;
    public float playerPositionZ;
    public float playerYRotation;
    public string currentScene;
    public string activeQuestID;
    public List<string> completedQuestIDs = new List<string>();
    public List<NPCStateEntry> npcStates = new List<NPCStateEntry>();

    // Added for BKT / tablet / logging integration
    public List<BKTMasteryEntry> bktMastery = new List<BKTMasteryEntry>();
    public List<string> completedMissionIDs = new List<string>();
    public List<string> restoredCrystalSanctums = new List<string>();
    public StudentLogData studentLogs;
    public StoryProgressionData storyProgression;
}

[Serializable]
public class NPCStateEntry
{
    public string npcID;
    public string currentSequenceID;
    public bool hasDeparted;
}

/// <summary>
/// Sanctum-level story state that isn't part of the linear quest
/// ledger: which sanctums have been visited and which bosses have
/// been defeated. Kept as its own class (rather than flat fields on
/// SaveSlotData) so StoryProgressionManager's export/import stays a
/// single call each.
/// </summary>
[Serializable]
public class StoryProgressionData
{
    public List<string> visitedSanctums = new List<string>();
    public List<string> defeatedBosses = new List<string>();
}

/// <summary>
/// Storyline-progress fingerprint of one save slot file, taken by
/// SnapshotExistingSaves before a save rewrites that slot. In-memory
/// only, never serialized itself - QuestCount/BossCount mirror what
/// SaveLoadManager.ProgressOf extracts from a full SaveSlotData.
/// </summary>
[Serializable]
public class CsvProgressSnapshot
{
    public int slot;
    public int questCount;
    public int bossCount;
    public string dateSavedISO;
    public string currentScene;
}

/// <summary>
/// Persistent record of how far through the storyline the playthrough
/// was the last time the canonical student-log CSV was successfully
/// exported (studentlog_export_progress.json in persistentDataPath).
/// This is what the overwrite protection can still consult when no
/// save slot holds the earlier playthrough anymore, because unlike the
/// slot files this marker is never touched by Save/Delete/New Game.
/// </summary>
[Serializable]
public class ExportProgressMarker
{
    public string studentID;
    public int questCount;
    public int bossCount;
    public string dateISO;
}