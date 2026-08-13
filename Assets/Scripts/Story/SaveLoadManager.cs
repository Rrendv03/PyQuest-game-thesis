using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;

public class SaveLoadManager : MonoBehaviour
{
    public static SaveLoadManager Instance;
    public static bool IsSafeToSave = true;

    [Header("Autosave")]
    public float autosaveIntervalSeconds = 300f;
    public bool enableAutosave = true;

    [Header("Player Reference (for saving position)")]
    public Transform playerTransform;

    private float autosaveTimer = 0f;
    private float sessionPlayTime = 0f;
    private const string AutosaveFilename = "save_autosave.json";

    private HashSet<string> _defeatedNormalZones = new HashSet<string>();
    private HashSet<string> _defeatedBossZones = new HashSet<string>();
    private HashSet<string> _defeatedSanctumBosses = new HashSet<string>();

    private SaveSlotData _pendingLoadData = null;

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
        if (!IsSafeToSave || !enableAutosave) return;

        autosaveTimer += Time.unscaledDeltaTime;
        sessionPlayTime += Time.unscaledDeltaTime;

        if (autosaveTimer >= autosaveIntervalSeconds)
        {
            autosaveTimer = 0f;
            SaveToSlot(0);
        }
    }

    public void SaveToSlot(int slot)
    {
        if (!IsSafeToSave)
        {
            Debug.LogWarning("[SaveLoadManager] Save blocked: unsafe state.");
            return;
        }

        if (slot < 0 || slot > 3)
        {
            Debug.LogWarning("[SaveLoadManager] Invalid slot.");
            return;
        }

        SaveSlotData data = BuildSaveData(slot);
        string json = JsonUtility.ToJson(data, true);
        string path = GetSlotPath(slot);

        try
        {
            File.WriteAllText(path, json);
            Debug.Log($"[SaveLoadManager] Saved slot {slot} to {path}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveLoadManager] Save failed: {e.Message}");
        }
    }

    public SaveSlotData LoadFromSlot(int slot)
    {
        string path = GetSlotPath(slot);
        if (!File.Exists(path)) return null;

        try
        {
            string json = File.ReadAllText(path);
            return JsonUtility.FromJson<SaveSlotData>(json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveLoadManager] Load failed: {e.Message}");
            return null;
        }
    }

    public void ApplySaveData(SaveSlotData data)
    {
        if (data == null) return;

        _pendingLoadData = data;

        RestoreManagerData(data);

        SceneTransition.RespawnPoint = new Vector3(
            data.playerPositionX, data.playerPositionY, data.playerPositionZ);

        Time.timeScale = 1f;
        IsSafeToSave = true;

        SceneManager.sceneLoaded += OnSceneLoadedForRestore;
        SceneManager.LoadScene(data.currentScene);
    }

    public void StartNewGame()
    {
        _defeatedNormalZones.Clear();
        _defeatedBossZones.Clear();
        _defeatedSanctumBosses.Clear();
        sessionPlayTime = 0f;

        if (BKTEngine.Instance != null) BKTEngine.Instance.ResetAllMastery();
        if (XPManager.Instance != null) XPManager.Instance.ResetXP();
        if (StoryProgressionManager.Instance != null) StoryProgressionManager.Instance.ResetProgression();
        if (MissionTabletManager.Instance != null) MissionTabletManager.Instance.ResetMissions();

        SceneManager.LoadScene("IntroScene");
    }

    public bool SlotExists(int slot) => File.Exists(GetSlotPath(slot));

    public void DeleteSlot(int slot)
    {
        string path = GetSlotPath(slot);
        if (File.Exists(path))
        {
            File.Delete(path);
            Debug.Log($"[SaveLoadManager] Deleted slot {slot}.");
        }
    }

    public void RegisterZoneDefeated(string zoneID, bool isBoss, string sanctumID = null)
    {
        if (string.IsNullOrEmpty(zoneID)) return;

        if (isBoss)
        {
            _defeatedBossZones.Add(zoneID);
            if (!string.IsNullOrEmpty(sanctumID))
                _defeatedSanctumBosses.Add(sanctumID);
        }
        else
        {
            _defeatedNormalZones.Add(zoneID);
        }
    }

    public bool IsZoneDefeated(string zoneID, bool isBoss)
    {
        if (string.IsNullOrEmpty(zoneID)) return false;
        return isBoss ? _defeatedBossZones.Contains(zoneID) : _defeatedNormalZones.Contains(zoneID);
    }

    public bool IsSanctumBossDefeated(string sanctumID)
    {
        return !string.IsNullOrEmpty(sanctumID) && _defeatedSanctumBosses.Contains(sanctumID);
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
            data.playerRotY = playerTransform.eulerAngles.y;
        }

        SaveSlotData existing = LoadFromSlot(slot);
        float previousTime = (existing != null && existing.playTimeSeconds > 0) ? existing.playTimeSeconds : 0f;
        data.playTimeSeconds = previousTime + sessionPlayTime;
        sessionPlayTime = 0f;

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

        if (BKTEngine.Instance != null)
            data.bktMastery = BKTEngine.Instance.ExportMastery();

        if (XPManager.Instance != null)
            data.currentXP = XPManager.Instance.ExportXP();

        data.defeatedNormalZoneIDs = new List<string>(_defeatedNormalZones);
        data.defeatedBossZoneIDs = new List<string>(_defeatedBossZones);
        data.defeatedSanctumBosses = new List<string>(_defeatedSanctumBosses);

        if (MissionTabletManager.Instance != null)
            data.completedMissionIDs = MissionTabletManager.Instance.ExportCompletedMissions();

        data.missionDisplayName = GetMissionDisplayName(data.activeQuestID);
        data.locationDisplayName = GetLocationDisplayName(data.currentScene);

        return data;
    }

    private void RestoreManagerData(SaveSlotData data)
    {
        if (BKTEngine.Instance != null && data.bktMastery != null)
            BKTEngine.Instance.ImportMastery(data.bktMastery);

        if (XPManager.Instance != null)
            XPManager.Instance.ImportXP(data.currentXP);

        if (StoryProgressionManager.Instance != null)
        {
            StoryProgressionManager.Instance.ImportCompletedQuestIDs(data.completedQuestIDs);
            StoryProgressionManager.Instance.ImportActiveQuestID(data.activeQuestID);
        }

        _defeatedNormalZones.Clear();
        if (data.defeatedNormalZoneIDs != null)
            foreach (var id in data.defeatedNormalZoneIDs) _defeatedNormalZones.Add(id);

        _defeatedBossZones.Clear();
        if (data.defeatedBossZoneIDs != null)
            foreach (var id in data.defeatedBossZoneIDs) _defeatedBossZones.Add(id);

        _defeatedSanctumBosses.Clear();
        if (data.defeatedSanctumBosses != null)
            foreach (var id in data.defeatedSanctumBosses) _defeatedSanctumBosses.Add(id);

        if (MissionTabletManager.Instance != null && data.completedMissionIDs != null)
            MissionTabletManager.Instance.ImportCompletedMissions(data.completedMissionIDs);
    }

    private void OnSceneLoadedForRestore(Scene scene, LoadSceneMode mode)
    {
        SceneManager.sceneLoaded -= OnSceneLoadedForRestore;

        if (_pendingLoadData == null) return;

        if (_pendingLoadData.npcStates != null)
        {
            NPCController[] npcs = FindObjectsOfType<NPCController>();
            foreach (var npc in npcs)
            {
                foreach (var entry in _pendingLoadData.npcStates)
                {
                    if (entry.npcID == npc.npcID)
                    {
                        npc.RestoreState(entry.currentSequenceID, entry.hasDeparted);
                        break;
                    }
                }
            }
        }

        if (playerTransform != null)
        {
            Vector3 euler = playerTransform.eulerAngles;
            euler.y = _pendingLoadData.playerRotY;
            playerTransform.eulerAngles = euler;
        }

        if (MissionTabletUI.Instance != null)
            MissionTabletUI.Instance.Refresh();

        _pendingLoadData = null;
    }

    private static string GetSlotPath(int slot)
    {
        string filename = slot == 0 ? AutosaveFilename : $"save_slot_{slot}.json";
        return Path.Combine(Application.persistentDataPath, filename);
    }

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
        return sceneName switch
        {
            "IntroScene" => "Aethelscript - Arrival",
            "MainMap" => "Aethelscript",
            "EchoingAtrium" => "Echoing Atrium",
            "VaultOfEssence" => "Vault of Essence",
            "WhitewakeMist" => "Whitewake Mist",
            "LabyrinthOfLogic" => "Labyrinth of Logic",
            _ => string.IsNullOrEmpty(sceneName) ? "Unknown" : sceneName
        };
    }
}

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
    public float playerRotY;
    public string currentScene;
    public string activeQuestID;
    public List<string> completedQuestIDs = new List<string>();
    public List<NPCStateEntry> npcStates = new List<NPCStateEntry>();
    public List<BKTMasteryEntry> bktMastery = new List<BKTMasteryEntry>();
    public List<string> defeatedNormalZoneIDs = new List<string>();
    public List<string> defeatedBossZoneIDs = new List<string>();
    public List<string> completedMissionIDs = new List<string>();
    public List<string> defeatedSanctumBosses = new List<string>();
}

[Serializable]
public class NPCStateEntry
{
    public string npcID;
    public string currentSequenceID;
    public bool hasDeparted;
}

[Serializable]
public class BKTMasteryEntry
{
    public string componentName;
    public float masteryProbability;
}