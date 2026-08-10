using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Manages three manual save slots and one dedicated autosave slot.
/// Autosave never overwrites manual slots.
/// Fires every 5 minutes while IsSafeToSave is true.
/// </summary>
public class SaveLoadManager : MonoBehaviour
{
    public static SaveLoadManager Instance;
    public static bool IsSafeToSave = true;

    [Header("Autosave")]
    public float autosaveIntervalSeconds = 300f;

    [Header("Player Reference")]
    public Transform playerTransform;

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
        if (!IsSafeToSave) return;

        autosaveTimer += Time.deltaTime;
        if (autosaveTimer >= autosaveIntervalSeconds)
        {
            autosaveTimer = 0f;
            SaveToSlot(0);
        }
    }

    // ?? Save ??????????????????????????????????????????????????????????????????
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

        if (playerTransform != null)
        {
            data.playerPositionX = playerTransform.position.x;
            data.playerPositionY = playerTransform.position.y;
            data.playerPositionZ = playerTransform.position.z;
        }

        if (StoryProgressionManager.Instance != null)
        {
            data.completedQuestIDs = StoryProgressionManager.Instance.ExportCompletedQuestIDs();
            data.activeQuestID = StoryProgressionManager.Instance.ExportActiveQuestID();
        }

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

        // XP
        if (XPManager.Instance != null)
            data.currentXP = XPManager.Instance.ExportXP();

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

    public void ApplySaveData(SaveSlotData data)
    {
        if (data == null) return;

        // Restore story progression
        if (StoryProgressionManager.Instance != null)
        {
            StoryProgressionManager.Instance.ImportCompletedQuestIDs(data.completedQuestIDs);
            StoryProgressionManager.Instance.ImportActiveQuestID(data.activeQuestID);
        }

        // Notify QuestManager to re-evaluate active quest from loaded state
        if (QuestManager.Instance != null)
            QuestManager.Instance.OnQuestCompleted(data.activeQuestID);

        // Set respawn point before scene load so PlayerMovement.Awake() reads it
        // Restore XP
        if (XPManager.Instance != null)
            XPManager.Instance.ImportXP(data.currentXP);

        SceneTransition.RespawnPoint = new Vector3(
            data.playerPositionX,
            data.playerPositionY,
            data.playerPositionZ);

        // Critical: restore timeScale and save flag before loading.
        // The pause menu sets timeScale = 0 when open. If we load a scene
        // while timeScale is 0, SceneEntrance.FadeIn() coroutine never runs
        // and the black overlay never disappears.
        Time.timeScale = 1f;
        IsSafeToSave = true;

        _pendingNPCStates = data.npcStates;
        SceneManager.sceneLoaded += OnSceneLoaded;
        SceneManager.LoadScene(data.currentScene);
    }

    private List<NPCStateEntry> _pendingNPCStates;

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

    // ?? Display Name Lookups ??????????????????????????????????????????????????
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
            case "EchoingAtrium": return "Echoing Atrium";
            case "VaultOfEssence": return "Vault of Essence";
            case "WhitewakeMist": return "Whitewake Mist";
            case "LabyrinthOfLogic": return "Labyrinth of Logic";
            default: return string.IsNullOrEmpty(sceneName) ? "Unknown" : sceneName;
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
    public int currentXP;
    public string dateSavedISO;
    public float playerPositionX;
    public float playerPositionY;
    public float playerPositionZ;
    public string currentScene;
    public string activeQuestID;
    public List<string> completedQuestIDs = new List<string>();
    public List<NPCStateEntry> npcStates = new List<NPCStateEntry>();
}

[Serializable]
public class NPCStateEntry
{
    public string npcID;
    public string currentSequenceID;
    public bool hasDeparted;
}