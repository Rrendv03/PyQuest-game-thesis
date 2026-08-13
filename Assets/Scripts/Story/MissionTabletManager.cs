using System.Collections.Generic;
using System.IO;
using UnityEngine;

public class MissionTabletManager : MonoBehaviour
{
    public static MissionTabletManager Instance;

    private List<MissionTabletData> allMissions = new List<MissionTabletData>();
    private HashSet<string> _completedMissionIDs = new HashSet<string>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadMissions();
        }
        else
        {
            Destroy(gameObject);
        }
    }

    void LoadMissions()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "MissionTabletQuests.json");
        if (!File.Exists(path))
        {
            Debug.LogError("[MissionTabletManager] MissionTabletQuests.json not found at: " + path);
            return;
        }

        string json = File.ReadAllText(path);
        MissionTabletWrapper wrapper = JsonUtility.FromJson<MissionTabletWrapper>(json);
        allMissions = wrapper?.missions ?? new List<MissionTabletData>();

        Debug.Log($"[MissionTabletManager] Loaded {allMissions.Count} missions.");
    }

    public MissionTabletData GetMissionByID(string missionID)
    {
        foreach (var m in allMissions)
            if (m.missionID == missionID) return m;
        return null;
    }

    public List<MissionTabletData> GetMissionsForSanctum(string sanctumID)
    {
        List<MissionTabletData> result = new List<MissionTabletData>();
        foreach (var m in allMissions)
            if (m.sanctumID == sanctumID) result.Add(m);
        return result;
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