using System;
using System.Collections;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages three manual save slots and one dedicated autosave slot.
/// Autosave never overwrites manual slots.
/// Fires every 5 minutes while IsSafeToSave is true.
/// DialogueManager and EncounterManager must set IsSafeToSave = false
/// while active and restore it when done.
///
/// Save files live in Application.persistentDataPath:
///   save_slot_1.json
///   save_slot_2.json
///   save_slot_3.json
///   save_autosave.json
///
/// SaveSlotData is what gets serialized per slot.
/// SaveSlotMeta is the lightweight summary read for the slot selection UI
/// without loading the full save.
/// </summary>
public class SaveLoadManager : MonoBehaviour
{
    public static SaveLoadManager Instance;

    // Set false by DialogueManager and EncounterManager while active.
    // Autosave checks this before writing.
    public static bool IsSafeToSave = true;

    [Header("Autosave")]
    public float autosaveIntervalSeconds = 300f; // 5 minutes

    [Header("Player Reference")]
    public Transform playerTransform;

    private float autosaveTimer = 0f;
    private const string AutosaveFilename = "save_autosave.json";

    // ?? Slot filename map ?????????????????????????????????????????????????????
    private static string SlotFilename(int slot)
    {
        if (slot == 0) return AutosaveFilename;
        return $"save_slot_{slot}.json";
    }

    private static string SlotPath(int slot)
    {
        return Path.Combine(Application.persistentDataPath, SlotFilename(slot));
    }

    // ?????????????????????????????????????????????????????????????????????????
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
        if (!IsSafeToSave) return;

        autosaveTimer += Time.deltaTime;
        if (autosaveTimer >= autosaveIntervalSeconds)
        {
            autosaveTimer = 0f;
            SaveToSlot(0); // slot 0 = autosave
        }
    }

    // ?? Save ??????????????????????????????????????????????????????????????????
    public void SaveToSlot(int slot)
    {
        if (slot < 0 || slot > 3)
        {
            Debug.LogWarning($"[SaveLoadManager] Invalid slot {slot}. Valid: 1-3, 0 = autosave.");
            return;
        }

        SaveSlotData data = BuildSaveData(slot);
        string json = JsonUtility.ToJson(data, true);

        try
        {
            File.WriteAllText(SlotPath(slot), json);
            Debug.Log($"[SaveLoadManager] Saved slot {slot} to {SlotPath(slot)}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveLoadManager] Failed to write slot {slot}: {e.Message}");
        }
    }

    private SaveSlotData BuildSaveData(int slot)
    {
        SaveSlotData data = new SaveSlotData();

        data.slotNumber = slot;
        data.dateSavedISO = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        data.currentScene = SceneManager.GetActiveScene().name;

        // Player position
        if (playerTransform != null)
        {
            data.playerPositionX = playerTransform.position.x;
            data.playerPositionY = playerTransform.position.y;
            data.playerPositionZ = playerTransform.position.z;
        }

        // Story progression
        if (StoryProgressionManager.Instance != null)
        {
            data.completedQuestIDs = StoryProgressionManager.Instance.ExportCompletedQuestIDs();
            data.activeQuestID = StoryProgressionManager.Instance.ExportActiveQuestID();
        }

        // NPC states
        data.npcStates = new System.Collections.Generic.List<NPCStateEntry>();
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

        // Active quest display name (looked up from questDisplayNames table)
        data.missionDisplayName = GetMissionDisplayName(data.activeQuestID);
        data.locationDisplayName = GetLocationDisplayName(data.currentScene);

        // Playtime: carried forward from existing save if one exists, plus
        // time elapsed since scene load. Full playtime tracking requires a
        // session timer, which is a separate addition. For now stores the
        // value from the last save if one exists, otherwise 0.
        SaveSlotData existing = LoadFromSlot(slot);
        data.playTimeSeconds = existing != null ? existing.playTimeSeconds : 0f;

        return data;
    }

    // ?? Load ??????????????????????????????????????????????????????????????????
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

    /// <summary>
    /// Loads a save slot and restores game state into the current session.
    /// Call this from the save slot selection UI after the player taps Load.
    /// </summary>
    public void ApplySaveData(SaveSlotData data)
    {
        if (data == null) return;

        // Restore story progression
        if (StoryProgressionManager.Instance != null)
        {
            StoryProgressionManager.Instance.ImportCompletedQuestIDs(data.completedQuestIDs);
            StoryProgressionManager.Instance.ImportActiveQuestID(data.activeQuestID);
        }

        // Set respawn point so PlayerMovement.Awake() places the player correctly
        SceneTransition.RespawnPoint = new Vector3(
            data.playerPositionX,
            data.playerPositionY,
            data.playerPositionZ);

        // Load the scene — NPCs restore state via OnSceneLoaded below
        _pendingNPCStates = data.npcStates;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.LoadScene(data.currentScene);
    }

    private System.Collections.Generic.List<NPCStateEntry> _pendingNPCStates;

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;

        if (_pendingNPCStates == null) return;

        NPCController[] npcs = FindObjectsOfType<NPCController>();
        foreach (var npc in npcs)
        {
            foreach (var entry in _pendingNPCStates)
            {
                if (entry.npcID == npc.npcID)
                {
                    npc.RestoreState(entry.currentSequenceID, entry.hasDeparted);
                    break;
                }
            }
        }

        _pendingNPCStates = null;
    }

    public bool SlotExists(int slot)
    {
        return File.Exists(SlotPath(slot));
    }

    public void DeleteSlot(int slot)
    {
        string path = SlotPath(slot);
        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log($"[SaveLoadManager] Deleted slot {slot}.");
        }
    }

    // ?? Display Name Lookups ??????????????????????????????????????????????????
    // Author these two tables as your quest IDs and scene names are finalized.
    // Neither list is complete; add entries here as new quests and scenes
    // are built out. Unrecognized IDs fall back to the raw ID string.

    private string GetMissionDisplayName(string questID)
    {
        switch (questID)
        {
            case "intro_complete": return "Find the Echoing Atrium";
            case "echoing_atrium_first_talk": return "Speak with Echo";
            case "echoing_atrium_complete": return "Find the Vault of Essence";
            case "vault_first_talk": return "Speak with Lyra";
            case "vault_complete": return "Find the Whitewake Mist";
            case "whitewake_first_talk": return "Speak with Auralis";
            case "whitewake_complete": return "Find the Labyrinth of Logic";
            case "labyrinth_first_talk": return "Speak with Selvara";
            case "labyrinth_complete": return "Aethelscript Restored";
            default:
                return string.IsNullOrEmpty(questID) ? "No Active Mission" : questID;
        }
    }

    private string GetLocationDisplayName(string sceneName)
    {
        switch (sceneName)
        {
            case "IntroScene": return "Aethelscript - Arrival";
            case "MainMap": return "Aethelscript";
            case "EchoingAtrium": return "Echoing Atrium";
            case "VaultOfEssence": return "Vault of Essence";
            case "WhitewakeMist": return "Whitewake Mist";
            case "LabyrinthOfLogic": return "Labyrinth of Logic";
            default:
                return string.IsNullOrEmpty(sceneName) ? "Unknown" : sceneName;
        }
    }
}

// ?? Data Structures ???????????????????????????????????????????????????????????
[Serializable]
public class SaveSlotData
{
    public int slotNumber;
    public string missionDisplayName;
    public string locationDisplayName;
    public float playTimeSeconds;
    public string dateSavedISO;

    public float playerPositionX;
    public float playerPositionY;
    public float playerPositionZ;
    public string currentScene;

    public string activeQuestID;
    public System.Collections.Generic.List<string> completedQuestIDs
        = new System.Collections.Generic.List<string>();

    public System.Collections.Generic.List<NPCStateEntry> npcStates
        = new System.Collections.Generic.List<NPCStateEntry>();
}

[Serializable]
public class NPCStateEntry
{
    public string npcID;
    public string currentSequenceID;
    public bool hasDeparted;
}