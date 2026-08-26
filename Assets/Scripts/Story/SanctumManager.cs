using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
/// <summary>
/// Orchestrates the complete sanctum gameplay loop:
/// Entry -> Exploration -> Tablet Missions -> Boss Unlock -> Boss Fight -> Reward -> Exit
/// One SanctumManager exists per sanctum scene, configured via Inspector.
/// </summary>
public class SanctumManager : MonoBehaviour
{
    [Header("Sanctum Identity")]
    [Tooltip("Unique identifier matching MissionTabletQuests.json sanctumID")]
    public string sanctumID;
    [Tooltip("Display name shown to player")]
    public string sanctumDisplayName;
    [Tooltip("Brief lore description")]
    [TextArea(3, 5)]
    public string sanctumLore;
    [Header("Scene References")]
    [Tooltip("Name of the MainMap scene to return to")]
    public string mainMapSceneName = "MainMap";
    [Tooltip("Transform where player spawns on sanctum entry")]
    public Transform playerSpawnPoint;
    [Tooltip("Transform where player returns to MainMap")]
    public Transform exitPortalPoint;
    [Header("Progression Gates")]
    [Tooltip("XP threshold for boss unlock (should match XPManager)")]
    public int bossUnlockXPThreshold = 150;
    [Tooltip("Number of tablet missions required (default 3)")]
    public int requiredTabletMissions = 3;
    [Tooltip("Bonus XP awarded on boss defeat, set per sanctum in Inspector")]
    public int bossBonusXP = 50;
    // Events (Header attribute is invalid on events, only on fields - removed)
    public static event Action<string> OnSanctumEntered;
    public static event Action<string> OnSanctumExited;
    public static event Action<string> OnBossUnlockedEvent;
    public static event Action<string> OnBossDefeatedEvent;
    public static event Action<string, int> OnTabletMissionCompleted;
    public static event Action<string> OnSanctumCleared;
    [Header("Runtime State")]
    [SerializeField] private SanctumState currentState = SanctumState.Exploring;
    [SerializeField] private int completedMissionsCount = 0;
    [SerializeField] private bool bossUnlocked = false;
    [SerializeField] private bool bossDefeated = false;
    [SerializeField] private bool sanctumCleared = false;
    public enum SanctumState
    {
        Entering,
        Exploring,
        InEncounter,
        InPuzzle,
        InDialogue,
        BossUnlocked,
        InBossFight,
        RewardSequence,
        Exiting
    }
    // Singleton per-scene (not DontDestroyOnLoad)
    public static SanctumManager Instance { get; private set; }
    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        ValidateConfiguration();
    }
    private void Start()
    {
        InitializeSanctumState();
        StartCoroutine(SanctumEntrySequence());
    }
    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }
    #region Initialization
    private void ValidateConfiguration()
    {
        if (string.IsNullOrEmpty(sanctumID))
        {
            Debug.LogError("[SanctumManager] SanctumID is empty on " + gameObject.name + "!");
            sanctumID = gameObject.scene.name;
        }
        if (playerSpawnPoint == null)
        {
            Debug.LogWarning("[SanctumManager] No player spawn point assigned on " + sanctumID + ". Creating fallback.");
            GameObject fallback = new GameObject("FallbackSpawnPoint");
            fallback.transform.position = Vector3.zero;
            playerSpawnPoint = fallback.transform;
        }
    }
    private void InitializeSanctumState()
    {
        if (StoryProgressionManager.Instance != null)
        {
            bossDefeated = StoryProgressionManager.Instance.HasDefeatedBoss(sanctumID);
            sanctumCleared = bossDefeated;
        }
        if (MissionTabletManager.Instance != null)
        {
            bossUnlocked = MissionTabletManager.Instance.IsBossUnlockReady(sanctumID);
        }
        completedMissionsCount = CountCompletedMissionsInSave();
        if (bossDefeated)
        {
            OpenAllBossGates();
            ActivateAllRuneCrystals();
            CleanupCompletedSanctum();
        }
        Debug.Log("[SanctumManager] " + sanctumID + " initialized. State: " + currentState +
                  ", Missions: " + completedMissionsCount + "/" + requiredTabletMissions +
                  ", BossUnlocked: " + bossUnlocked + ", BossDefeated: " + bossDefeated);
    }
    private int CountCompletedMissionsInSave()
    {
        int count = 0;
        if (MissionTabletManager.Instance == null) return 0;
        TabletMissionObject[] tablets = FindObjectsOfType<TabletMissionObject>();
        foreach (var tablet in tablets)
        {
            if (tablet.sanctumID == sanctumID && tablet.IsRestored())
                count++;
        }
        return count;
    }
    #endregion
    #region Entry Sequence
    private IEnumerator SanctumEntrySequence()
    {
        currentState = SanctumState.Entering;
        if (SaveRestrictionEnforcer.Instance != null)
            SaveRestrictionEnforcer.Instance.AddBlocker("sanctum_entry");
        PositionPlayerAtSpawn();
        StudentLogManager.Instance?.LogSanctumEntry(sanctumID, sanctumDisplayName);
        if (UIManager.Instance != null)
            yield return StartCoroutine(UIManager.Instance.FadeFromBlack(1.5f));
        else
            yield return new WaitForSeconds(0.5f);
        UIManager.Instance?.ShowNotification("Entered: " + sanctumDisplayName, 3f);
        if (StoryProgressionManager.Instance != null)
            StoryProgressionManager.Instance.TriggerSanctumFirstVisit(sanctumID);
        currentState = SanctumState.Exploring;
        OnSanctumEntered?.Invoke(sanctumID);
        if (SaveRestrictionEnforcer.Instance != null)
            SaveRestrictionEnforcer.Instance.RemoveBlocker("sanctum_entry");
    }
    private void PositionPlayerAtSpawn()
    {
        GameObject player = GameObject.FindGameObjectWithTag("Player");
        if (player != null && playerSpawnPoint != null)
        {
            player.transform.position = playerSpawnPoint.position;
            player.transform.rotation = playerSpawnPoint.rotation;
        }
    }
    #endregion
    #region Tablet Mission Progression
    /// <summary>
    /// Called by TabletMissionObject when a mission is completed.
    /// </summary>
    public void RegisterTabletMissionComplete(string missionID)
    {
        completedMissionsCount++;
        OnTabletMissionCompleted?.Invoke(sanctumID, completedMissionsCount);
        StudentLogManager.Instance?.LogTabletMissionComplete(sanctumID, missionID, sanctumDisplayName);
        if (!bossUnlocked && MissionTabletManager.Instance != null
            && MissionTabletManager.Instance.IsBossUnlockReady(sanctumID))
        {
            UnlockBoss();
        }
        if (SaveLoadManager.Instance != null && SaveRestrictionEnforcer.Instance != null
            && SaveRestrictionEnforcer.Instance.IsSafeToSave)
        {
            SaveLoadManager.Instance.AutoSave();
        }
    }
    private void UnlockBoss()
    {
        if (bossUnlocked) return;
        bossUnlocked = true;
        currentState = SanctumState.BossUnlocked;
        OnBossUnlockedEvent?.Invoke(sanctumID);
        if (StoryProgressionManager.Instance != null)
            StoryProgressionManager.Instance.TriggerBossUnlocked(sanctumID);
        UIManager.Instance?.ShowNotification(
            "BOSS UNLOCKED: The guardian of " + sanctumDisplayName + " stirs...", 5f);
        OpenAllBossGates();
        StudentLogManager.Instance?.LogBossUnlocked(sanctumID, sanctumDisplayName);
        Debug.Log("[SanctumManager] Boss unlocked in " + sanctumID + "!");
    }
    #endregion
    #region Boss Fight
    /// <summary>
    /// Called by ZoneTrigger when boss encounter begins.
    /// </summary>
    public void OnBossEncounterStarted()
    {
        currentState = SanctumState.InBossFight;
        if (SaveRestrictionEnforcer.Instance != null)
            SaveRestrictionEnforcer.Instance.AddBlocker("boss_fight");
        StudentLogManager.Instance?.LogBossEncounterStart(sanctumID, sanctumDisplayName);
    }
    /// <summary>
    /// Called by ZoneTrigger/EncounterManager when boss is defeated.
    /// Renamed from OnBossDefeated to avoid colliding with the
    /// OnBossDefeatedEvent static event above (CS0102).
    /// </summary>
    public void HandleBossDefeated()
    {
        if (bossDefeated) return;
        bossDefeated = true;
        sanctumCleared = true;
        currentState = SanctumState.RewardSequence;
        OnBossDefeatedEvent?.Invoke(sanctumID);
        if (SaveRestrictionEnforcer.Instance != null)
            SaveRestrictionEnforcer.Instance.RemoveBlocker("boss_fight");
        StartCoroutine(BossRewardSequence());
    }
    private IEnumerator BossRewardSequence()
    {
        yield return new WaitForSeconds(1f);
        ActivateAllRuneCrystals();
        if (StoryProgressionManager.Instance != null)
            StoryProgressionManager.Instance.TriggerBossDefeated(sanctumID);
        // Bug-005B FIX: advance the sanctum NPC's dialogue sequence to
        // "{npcID}_after_restore" so the next interaction plays the
        // post-boss sequence which completes the "restore_crystal"
        // quest step 4 and drives progression to the next sanctum.
        NPCController[] npcs = FindObjectsOfType<NPCController>();
        foreach (var npc in npcs)
        {
            if (npc == null || npc.HasDeparted()) continue;
            string afterRestoreID = $"{npc.npcID}_after_restore";
            if (DialogueManager.Instance != null
                && DialogueManager.Instance.HasSequence(afterRestoreID))
            {
                npc.SetNextSequence(afterRestoreID);
                Debug.Log($"[SanctumManager] NPC {npc.npcID} sequence advanced " +
                          $"→ {afterRestoreID} for post-boss farewell.");
            }
        }
        StudentLogManager.Instance?.LogBossDefeated(sanctumID, sanctumDisplayName);
        if (XPManager.Instance != null)
        {
            int bonusXP = CalculateBossBonusXP();
            XPManager.Instance.AddXP(bonusXP, "boss_bonus_" + sanctumID, sanctumID);
        }
        UIManager.Instance?.ShowNotification("SANCTUM CLEARED: " + sanctumDisplayName, 5f);
        OnSanctumCleared?.Invoke(sanctumID);
        SaveLoadManager.Instance?.AutoSave();
        currentState = SanctumState.Exploring;
        bossDefeated = true;
    }
    private int CalculateBossBonusXP() => bossBonusXP;
    #endregion
    #region Exit System
    /// <summary>
    /// Called by SanctumExitPortal when player chooses to leave.
    /// </summary>
    public void ExitSanctum()
    {
        if (currentState == SanctumState.Exiting) return;
        StartCoroutine(ExitSequence());
    }
    private IEnumerator ExitSequence()
    {
        currentState = SanctumState.Exiting;
        if (SaveRestrictionEnforcer.Instance != null)
            SaveRestrictionEnforcer.Instance.AddBlocker("sanctum_exit");
        if (UIManager.Instance != null)
            yield return StartCoroutine(UIManager.Instance.FadeToBlack(1f));
        StudentLogManager.Instance?.LogSanctumExit(sanctumID, sanctumDisplayName, sanctumCleared);
        if (SaveLoadManager.Instance != null && SaveRestrictionEnforcer.Instance != null
            && SaveRestrictionEnforcer.Instance.IsSafeToSave)
        {
            SaveLoadManager.Instance.AutoSave();
        }
        OnSanctumExited?.Invoke(sanctumID);
        // Note: no blocker removal before scene load intentionally -
        // SaveRestrictionEnforcer is DontDestroyOnLoad, so a stale
        // "sanctum_exit" blocker would persist into MainMap otherwise.
        SaveRestrictionEnforcer.Instance?.RemoveBlocker("sanctum_exit");
        SceneManager.LoadScene(mainMapSceneName);
    }
    #endregion
    #region State Queries
    public SanctumState GetCurrentState() => currentState;
    public bool IsBossUnlocked() => bossUnlocked;
    public bool IsBossDefeated() => bossDefeated;
    public bool IsSanctumCleared() => sanctumCleared;
    public int GetCompletedMissionsCount() => completedMissionsCount;
    public int GetRequiredMissionsCount() => requiredTabletMissions;
    public bool CanStartBossEncounter()
    {
        if (bossDefeated) return false;
        if (MissionTabletManager.Instance != null)
            return MissionTabletManager.Instance.IsBossUnlockReady(sanctumID);
        return bossUnlocked;
    }
    #endregion
    #region Helper Methods
    private void OpenAllBossGates()
    {
        BossGate[] gates = FindObjectsOfType<BossGate>();
        foreach (var gate in gates)
        {
            if (gate.sanctumID == sanctumID)
                gate.OpenGate();
        }
    }
    private void ActivateAllRuneCrystals()
    {
        RuneCrystal[] crystals = FindObjectsOfType<RuneCrystal>(true);
        foreach (var crystal in crystals)
        {
            if (crystal.sanctumID == sanctumID)
                crystal.OnBossDefeated();
        }
    }
    private void CleanupCompletedSanctum()
    {
        RuneCrystal[] crystals = FindObjectsOfType<RuneCrystal>(true);
        foreach (var crystal in crystals)
        {
            if (crystal.sanctumID != sanctumID) continue;
            // Restore crystal to glowing state (not broken default)
            crystal.Restore();
            // Disable corruption meshes permanently
            if (crystal.corruptionMeshes != null)
            {
                foreach (var mesh in crystal.corruptionMeshes)
                {
                    if (mesh != null) mesh.SetActive(false);
                }
            }
            // Permanently remove guide NPC
            InteractableObject io = crystal.GetComponent<InteractableObject>();
            if (io != null && io.guideNPC != null)
            {
                io.guideNPC.ForceDepart();
            }
        }
        Debug.Log($"[SanctumManager] Completed sanctum {sanctumID} cleaned up: crystal restored, corruption disabled, NPC removed.");
    }
    /// <summary>
    /// Called by external systems to update state (e.g., from SaveLoadManager).
    /// </summary>
    public void RestoreState(bool bossDefeatedState, int missionsCompleted)
    {
        bossDefeated = bossDefeatedState;
        sanctumCleared = bossDefeatedState;
        completedMissionsCount = missionsCompleted;
        if (bossDefeated)
        {
            OpenAllBossGates();
            ActivateAllRuneCrystals();
            CleanupCompletedSanctum();
        }
    }
    #endregion
}