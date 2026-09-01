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
            if (slot != 0)
                StudentLogManager.Instance?.ExportCanonicalCsv();
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