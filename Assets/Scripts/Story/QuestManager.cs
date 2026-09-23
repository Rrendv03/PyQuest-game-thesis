using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Defines the full quest chain for PyQuest in order.
/// Each entry is one atomic objective shown on the HUD.
/// When a questIDToComplete is marked done by DialogueManager or
/// EncounterManager, QuestManager advances to the next quest and
/// updates StoryProgressionManager's active quest pointer.
///
/// NEW: each QuestEntry now carries a sanctumID so the HUD can pair the
/// main quest name (the sanctum's quest) with a live, specific objective
/// description, mission-tablet checkmarks, the XP check, and the
/// boss-unlock phase (see QuestHUDDisplay).
///
/// Place on the same DontDestroyOnLoad GameObject as StoryProgressionManager.
/// </summary>
public class QuestManager : MonoBehaviour
{
    public static QuestManager Instance;

    [System.Serializable]
    public class QuestEntry
    {
        public string unlockedByQuestID;
        public string questID;
        public string displayName;
        [Tooltip("Sanctum this quest belongs to (matches MissionTabletQuests.json sanctumID). Empty for non-sanctum quests.")]
        public string sanctumID;
    }

    [Header("Quest Chain")]
    public List<QuestEntry> quests = new List<QuestEntry>();

    [Header("DEBUG — Unlock Boss on Play")]
    [Tooltip("CHECK THIS BOX, then press Play: the game fast-forwards THE SANCTUM OF THE SCENE YOU ARE IN (via ZoneTrigger's canonical scene-name mapping — Play in ElifLabyrinth to test the Elif Labyrinth boss), falling back to the first unfinished sanctum when launched from a menu. All quest steps up to that sanctum's boss fight are completed, its mission tablets are marked complete, tablet read-states are marked read, and the player's XP is set DIRECTLY to 1000 on startup (raised to the sanctum's threshold if that is higher — repeated AwardXPCapped awards stall at one enemy's worth of XP, which is why the old debug only ever gave 150). The questline then sits on the boss-fight phase ('Defeat the boss in this location!') with the boss gate open and the HUD compass pointing at the boss. UNCHECK for normal play — launch-time debug switch, applied once per launch, ~0.5s after the managers finish loading.")]
    public bool debugUnlockBossOnPlay = false;

    private bool _debugSkipApplied = false; // once per launch; QuestManager is DontDestroyOnLoad

    /// <summary>Legacy single-line event (kept for compatibility).</summary>
    public event System.Action<string> OnQuestUpdated;

    /// <summary>
    /// NEW: fired whenever the active quest changes (and once on Start),
    /// carrying the full entry so the HUD can render name + objective +
    /// checklist. Also fired by RefreshActiveQuest for live HUD updates.
    /// </summary>
    public event System.Action<QuestEntry> OnActiveQuestChanged;

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
            return;
        }

        if (quests == null || quests.Count == 0)
            BuildDefaultChain();
    }

    void Start()
    {
        EvaluateActiveQuest();

        if (debugUnlockBossOnPlay && !_debugSkipApplied)
        {
            _debugSkipApplied = true;
            Debug.LogWarning("[QuestManager] DEBUG 'Unlock Boss on Play' is CHECKED — fast-forwarding to the boss fight once managers finish loading. UNCHECK for normal play.");
            StartCoroutine(DebugUnlockBossRoutine());
        }
    }

    // === DEBUG: fast-forward to the boss fight ==================================

    /// <summary>
    /// Runtime hook (e.g. for a debug panel): applies the same fast-forward the
    /// checkbox applies on launch, for whatever sanctum is currently active.
    /// </summary>
    public void DebugUnlockBossNow()
    {
        StartCoroutine(DebugUnlockBossRoutine());
    }

    private System.Collections.IEnumerator DebugUnlockBossRoutine()
    {
        // MissionTabletQuests.json loads asynchronously (UnityWebRequest on
        // Android, one frame in editor); XPManager / StoryProgressionManager /
        // SanctumManager are DontDestroyOnLoad singletons. Wait for all of them.
        float timeout = 15f;
        while (timeout > 0f
               && (MissionTabletManager.Instance == null
                   || !MissionTabletManager.Instance.IsLoaded
                   || XPManager.Instance == null
                   || StoryProgressionManager.Instance == null))
        {
            timeout -= Time.unscaledDeltaTime;
            yield return null;
        }

        if (MissionTabletManager.Instance == null || !MissionTabletManager.Instance.IsLoaded)
        {
            Debug.LogError("[QuestManager] DEBUG boss skip aborted: MissionTabletManager never finished loading MissionTabletQuests.json.");
            yield break;
        }

        string sanctumID = ResolveDebugTargetSanctum();
        if (string.IsNullOrEmpty(sanctumID))
        {
            Debug.Log("[QuestManager] DEBUG boss skip: no unfinished sanctum found (game already complete). Nothing to do.");
            yield break;
        }

        if (StoryProgressionManager.Instance.HasDefeatedBoss(sanctumID))
        {
            Debug.Log($"[QuestManager] DEBUG boss skip: the boss of '{sanctumID}' is already defeated. Nothing to do.");
            yield break;
        }

        Debug.Log($"[QuestManager] DEBUG boss skip: fast-forwarding '{sanctumID}' straight to the boss fight...");

        // 1) Quest steps: walk the chain IN ORDER and complete every step up
        //    to the target sanctum's boss fight. This also fast-forwards
        //    EARLIER sanctums (guide, fights, crystal restores) so that
        //    EvaluateActiveQuest lands on '{sanctum}_defeat_enemy' — the phase
        //    whose HUD objective becomes "Defeat the boss in this location!"
        //    once the boss unlocks — even when testing a higher-level sanctum
        //    on a fresh save. The tested sanctum's defeat_enemy (completed by
        //    actually beating the boss, see ZoneTrigger) and restore_crystal
        //    stay open for the player.
        foreach (QuestEntry step in quests)
        {
            if (step.sanctumID == sanctumID && step.questID.EndsWith("_defeat_enemy"))
                break; // everything up to the tested boss fight is done; the fight + restore stay open
            if (step.sanctumID == sanctumID && step.questID.EndsWith("_restore_crystal"))
                continue; // safety: never complete the tested sanctum's restore step
            if (!StoryProgressionManager.Instance.IsQuestComplete(step.questID))
            {
                StoryProgressionManager.Instance.CompleteQuest(step.questID);
                Debug.Log($"[QuestManager] DEBUG boss skip: quest step complete '{step.questID}'.");
            }
        }

        // 2) XP: set the player's XP DIRECTLY to 1000 on startup (raised to
        //    the sanctum's threshold when that is higher, so every sanctum is
        //    testable). The old approach — repeated AwardXPCapped — stalls at
        //    one enemy's worth of XP because of its farming caps, which is why
        //    the debug only ever banked 150. A direct set bypasses the award
        //    path entirely.
        int threshold = XPManager.Instance.GetThreshold(sanctumID);
        int targetXP = Mathf.Max(1000, threshold);
        int xpBefore = XPManager.Instance.CurrentXP;

        if (XPManager.Instance.IsBossUnlocked(sanctumID))
        {
            Debug.Log($"[QuestManager] DEBUG boss skip: boss XP already met ({xpBefore}/{threshold}) — leaving XP untouched.");
        }
        else if (TrySetXPDirect(targetXP, out string pathUsed))
        {
            int xpNow = XPManager.Instance.CurrentXP;
            Debug.Log($"[QuestManager] DEBUG boss skip: XP set directly {xpBefore} -> {xpNow} (target {targetXP}) via {pathUsed}.");
            if (xpNow != targetXP)
                Debug.LogWarning("[QuestManager] DEBUG boss skip: XP readback mismatch — CurrentXP may be derived; the boss unlock is re-verified at the end.");
            TryFireXPChanged(xpNow);
        }
        else
        {
            // Last resort if XPManager exposes no settable XP member: cycle
            // every award type until the threshold is met or awards stall.
            DebugAwardXPCycle(sanctumID, threshold);
        }

        Debug.Log($"[QuestManager] DEBUG boss skip: XP {XPManager.Instance.CurrentXP}/{threshold} for '{sanctumID}' | boss XP met: {XPManager.Instance.IsBossUnlocked(sanctumID)}");

        // 3) Mission tablets: complete every mission of this sanctum through
        //    the SAME two calls the real tablet flow makes (TabletMissionObject.
        //    OnPuzzleResolved): the gameplay tracker (which also refreshes all
        //    BossGates) AND SanctumManager (logging + unlock propagation).
        List<MissionTabletData> sanctumMissions = MissionTabletManager.Instance.GetMissionsForSanctum(sanctumID);
        if (sanctumMissions.Count == 0)
            Debug.LogWarning($"[QuestManager] DEBUG boss skip: no mission tablets found for '{sanctumID}' in MissionTabletQuests.json. " +
                             "Zero missions counts as 'all complete' for IsBossUnlockReady, but check the sanctumID spelling.");
        foreach (MissionTabletData mission in sanctumMissions)
        {
            if (MissionTabletManager.Instance.IsMissionComplete(mission.missionID))
                continue;
            MissionTabletManager.Instance.CompleteMission(mission.missionID);
            if (SanctumManager.Instance != null)
                SanctumManager.Instance.RegisterTabletMissionComplete(mission.missionID);
            Debug.Log($"[QuestManager] DEBUG boss skip: mission tablet complete '{mission.missionID}'.");
        }
        Debug.Log($"[QuestManager] DEBUG boss skip: mission tablets of '{sanctumID}' considered done: " +
                  $"{MissionTabletManager.Instance.AreAllSanctumMissionsComplete(sanctumID)} ({sanctumMissions.Count} mission(s)).");

        // 4) Tablet read states, so the HUD checklist rows tick too.
        TabletReadTracker.MarkSanctumMissionsRead(sanctumID);
        TabletReadTracker.MarkLessonRead(sanctumID);

        // 5) Open every boss gate in the scene now that XP + missions are done
        //    (CompleteMission already refreshes gates on transition; this also
        //    covers the case where everything was ALREADY complete and only XP
        //    was missing, so no transition fired).
        foreach (BossGate gate in FindObjectsOfType<BossGate>())
            gate.Refresh();

        // 6) Re-evaluate the chain: the active quest is now '{sanctum}_defeat_enemy'
        //    with IsBossUnlockReady = true, so the HUD renders "Defeat the boss
        //    in this location!" and the compass activates toward the boss zone.
        EvaluateActiveQuest();

        // 7) Final verification — the exact two gates the boss zone checks:
        //    ZoneTrigger.OnTriggerEnter checks IsBossUnlocked (XP) and BossGate
        //    checks IsBossUnlockReady (XP + all mission tablets). Re-apply the
        //    XP set if an async save load landed after it and clobbered it.
        if (!XPManager.Instance.IsBossUnlocked(sanctumID) && XPManager.Instance.CurrentXP < targetXP)
        {
            Debug.LogWarning("[QuestManager] DEBUG boss skip: XP changed after the direct set (async save load?) — re-applying.");
            if (TrySetXPDirect(targetXP, out string pathUsedRetry))
                TryFireXPChanged(XPManager.Instance.CurrentXP);
        }

        bool xpMet = XPManager.Instance.IsBossUnlocked(sanctumID);
        bool missionsDone = MissionTabletManager.Instance.AreAllSanctumMissionsComplete(sanctumID);
        Debug.Log($"[QuestManager] DEBUG boss skip DONE for '{sanctumID}' | XP: {XPManager.Instance.CurrentXP} (threshold {threshold}) -> met: {xpMet} | " +
                  $"mission tablets done: {missionsDone} | boss unlock ready: {MissionTabletManager.Instance.IsBossUnlockReady(sanctumID)} | active quest: {GetActiveQuestDisplayName()}");

        if (xpMet && missionsDone)
            Debug.Log($"[QuestManager] DEBUG boss skip: the boss zone of '{sanctumID}' is now enterable — walk into it to start the boss fight.");
        else
            Debug.LogWarning($"[QuestManager] DEBUG boss skip: boss zone NOT fully unlocked — XP met: {xpMet}, mission tablets done: {missionsDone}. Read the logs above for the failing half.");
    }

    // === DEBUG helpers ==========================================================================

    /// <summary>
    /// DEBUG: sets the player's XP directly, bypassing the award path (whose
    /// farming caps stall repeated debug awards at one enemy's worth of XP).
    /// XPManager exposes no public setter for its XP, so this locates the XP
    /// member by name at runtime: property first (a private setter is fine —
    /// reflection reaches it), then plain field, then any int field that looks
    /// like the XP store (compiler-generated "&lt;CurrentXP&gt;k__BackingField"
    /// included). Returns true and fills pathUsed on success; false means the
    /// caller should use the AwardXPCapped fallback instead. Reflection is
    /// safe here: the XP member is written by the game's own award path, so
    /// IL2CPP managed stripping never removes it.
    /// </summary>
    private bool TrySetXPDirect(int newXP, out string pathUsed)
    {
        pathUsed = null;
        if (XPManager.Instance == null) return false;

        var type = XPManager.Instance.GetType();
        const System.Reflection.BindingFlags Flags =
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
            System.Reflection.BindingFlags.Instance;

        // Common member names, most-likely first. "CurrentXP" is what the rest
        // of the codebase already reads (QuestHUDDisplay, MissionTabletManager).
        string[] memberNames =
        {
            "CurrentXP", "currentXP", "currentXp", "currentxp", "XP", "xp",
            "TotalXP", "totalXP", "totalXp", "playerXP", "PlayerXP",
            "_currentXP", "m_currentXP", "_xp", "_totalXP"
        };

        // 1) Property with any setter (public or private).
        foreach (string name in memberNames)
        {
            System.Reflection.PropertyInfo prop = type.GetProperty(name, Flags);
            if (prop == null || !prop.CanWrite || prop.PropertyType != typeof(int)) continue;
            try
            {
                prop.SetValue(XPManager.Instance, newXP, null);
                pathUsed = $"property '{name}'";
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[QuestManager] DEBUG: setting XP property '{name}' threw: {e.Message}");
            }
        }

        // 2) Plain field (public or [SerializeField]-style private).
        foreach (string name in memberNames)
        {
            System.Reflection.FieldInfo field = type.GetField(name, Flags);
            if (field == null || field.FieldType != typeof(int)) continue;
            try
            {
                field.SetValue(XPManager.Instance, newXP);
                pathUsed = $"field '{name}'";
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[QuestManager] DEBUG: setting XP field '{name}' threw: {e.Message}");
            }
        }

        // 3) Last sweep: any int field whose name looks like the XP store,
        //    covering auto-property backing fields and odd prefixes.
        foreach (System.Reflection.FieldInfo field in type.GetFields(Flags))
        {
            if (field.FieldType != typeof(int)) continue;
            bool looksLikeXP = field.Name.Contains("urrentXP") || field.Name.Contains("otalXP");
            if (!looksLikeXP) continue;
            try
            {
                field.SetValue(XPManager.Instance, newXP);
                pathUsed = $"field '{field.Name}'";
                return true;
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[QuestManager] DEBUG: setting XP field '{field.Name}' threw: {e.Message}");
            }
        }

        return false;
    }

    /// <summary>
    /// DEBUG: re-fires XPManager.OnXPChanged after a direct XP set so HUD
    /// listeners (QuestHUDDisplay) refresh exactly like they do after a normal
    /// AwardXPCapped. Works for Action, Action&lt;int&gt; and similar signatures;
    /// one failing listener never blocks the others.
    /// </summary>
    private void TryFireXPChanged(int newXP)
    {
        if (XPManager.Instance == null) return;
        try
        {
            var type = XPManager.Instance.GetType();
            const System.Reflection.BindingFlags Flags =
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance;

            System.Delegate handlers =
                (type.GetField("OnXPChanged", Flags)?.GetValue(XPManager.Instance)
                 ?? type.GetProperty("OnXPChanged", Flags)?.GetValue(XPManager.Instance)) as System.Delegate;
            if (handlers == null) return;

            foreach (System.Delegate handler in handlers.GetInvocationList())
            {
                try
                {
                    var parameters = handler.Method.GetParameters();
                    object[] args = parameters.Length == 0 ? new object[0] : new object[] { newXP };
                    handler.DynamicInvoke(args);
                }
                catch (System.Exception e) { Debug.LogWarning($"[QuestManager] DEBUG: OnXPChanged listener failed: {e.Message}"); }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[QuestManager] DEBUG: could not re-fire OnXPChanged: {e.Message}");
        }
    }

    /// <summary>
    /// DEBUG fallback, used only when no settable XP member was found: cycles
    /// every EnemyType through AwardXPCapped until the sanctum's threshold is
    /// met or every award type stalls (farming caps). Logged loudly because it
    /// can still come up short on high-threshold sanctums.
    /// </summary>
    private void DebugAwardXPCycle(string sanctumID, int threshold)
    {
        Debug.LogWarning("[QuestManager] DEBUG: no settable XP member found on XPManager — falling back to repeated AwardXPCapped. " +
                         "If this still comes up short, send XPManager.cs so the debug can set XP through a proper API.");
        var awardTypes = System.Enum.GetValues(typeof(XPManager.EnemyType));
        int cycleGuard = 0;
        while (!XPManager.Instance.IsBossUnlocked(sanctumID) && cycleGuard++ < 200)
        {
            int before = XPManager.Instance.CurrentXP;
            foreach (var awardType in awardTypes)
                XPManager.Instance.AwardXPCapped((XPManager.EnemyType)awardType, sanctumID);
            if (XPManager.Instance.CurrentXP == before)
                break; // every award type stalled — farming caps
        }
        Debug.Log($"[QuestManager] DEBUG fallback: XP {XPManager.Instance.CurrentXP}/{threshold} for '{sanctumID}' | boss XP met: {XPManager.Instance.IsBossUnlocked(sanctumID)}");
    }

    /// <summary>
    /// Which sanctum the debug skip applies to — NO inspector field needed:
    /// 1) the sanctum of the SCENE you pressed Play in (ZoneTrigger's canonical
    ///    scene-name mapping, e.g. "ElifLabyrinth" -> "elif_labyrinth"), so
    ///    higher-level sanctums can be tested directly, or
    /// 2) the first sanctum in the chain whose restore_crystal step is not yet
    ///    complete (used when launching from a menu/boot scene, whose mapping
    ///    defaults to print_console).
    /// </summary>
    private string ResolveDebugTargetSanctum()
    {
        // 1) The scene being played decides the target — this is what makes
        //    testing higher-level sanctums possible at all.
        string sceneSanctum = ZoneTrigger.GetSanctumIDFromScene();
        if (!string.IsNullOrEmpty(sceneSanctum))
        {
            if (StoryProgressionManager.Instance == null || !StoryProgressionManager.Instance.HasDefeatedBoss(sceneSanctum))
                return sceneSanctum;
            Debug.Log($"[QuestManager] DEBUG boss skip: this scene's sanctum '{sceneSanctum}' is already beaten — falling back to the first unfinished sanctum.");
        }

        if (StoryProgressionManager.Instance == null) return null;

        foreach (QuestEntry step in quests)
        {
            if (string.IsNullOrEmpty(step.sanctumID)) continue;
            if (!step.questID.EndsWith("_restore_crystal")) continue;
            if (!StoryProgressionManager.Instance.IsQuestComplete(step.questID))
                return step.sanctumID;
        }
        return null;
    }

    public void OnQuestCompleted(string completedQuestID)
    {
        EvaluateActiveQuest();
    }

    public void EvaluateActiveQuest()
    {
        if (StoryProgressionManager.Instance == null) return;

        foreach (var quest in quests)
        {
            if (StoryProgressionManager.Instance.IsQuestComplete(quest.questID))
                continue;

            bool unlocked = string.IsNullOrEmpty(quest.unlockedByQuestID)
                || StoryProgressionManager.Instance.IsQuestComplete(quest.unlockedByQuestID);

            if (unlocked)
            {
                StoryProgressionManager.Instance.SetActiveQuest(quest.questID);
                OnQuestUpdated?.Invoke(quest.displayName);
                OnActiveQuestChanged?.Invoke(quest);
                Debug.Log($"[QuestManager] Active quest: {quest.displayName}");
                return;
            }
        }

        StoryProgressionManager.Instance.SetActiveQuest("game_complete");
        OnQuestUpdated?.Invoke("Aethelscript Restored");
        OnActiveQuestChanged?.Invoke(null);
        Debug.Log("[QuestManager] All quests complete.");
    }

    /// <summary>
    /// NEW: ask the HUD to re-render the CURRENT active quest without the
    /// quest having changed (XP gained, mission completed, boss unlocked).
    /// Re-evaluates the chain so the returned entry is always accurate.
    /// </summary>
    public void RefreshActiveQuest()
    {
        EvaluateActiveQuest();
    }

    public QuestEntry GetActiveQuestEntry()
    {
        if (StoryProgressionManager.Instance == null) return null;

        string activeID = StoryProgressionManager.Instance.GetActiveQuestID();
        if (activeID == "game_complete") return null;

        foreach (var quest in quests)
            if (quest.questID == activeID)
                return quest;

        return null;
    }

    public string GetActiveQuestDisplayName()
    {
        QuestEntry entry = GetActiveQuestEntry();
        if (entry != null) return entry.displayName;
        if (StoryProgressionManager.Instance != null
            && StoryProgressionManager.Instance.GetActiveQuestID() == "game_complete")
            return "Aethelscript Restored";
        return "";
    }

    private void BuildDefaultChain()
    {
        quests = new List<QuestEntry>
        {
            // --- Print Console ---
            new QuestEntry { unlockedByQuestID = "intro_complete",
                questID = "print_console_find_printessa",
                displayName = "Find Printessa in the Print Console",
                sanctumID = "print_console" },
            new QuestEntry { unlockedByQuestID = "print_console_find_printessa",
                questID = "print_console_speak_printessa",
                displayName = "Speak with Printessa",
                sanctumID = "print_console" },
            new QuestEntry { unlockedByQuestID = "print_console_speak_printessa",
                questID = "print_console_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption",
                sanctumID = "print_console" },
            new QuestEntry { unlockedByQuestID = "print_console_defeat_enemy",
                questID = "print_console_restore_crystal",
                displayName = "Restore the Kernel Crystal",
                sanctumID = "print_console" },

            // --- Vars Vault ---
            new QuestEntry { unlockedByQuestID = "print_console_restore_crystal",
                questID = "vars_vault_find_variel",
                displayName = "Find Variel in the Vars Vault",
                sanctumID = "vars_vault" },
            new QuestEntry { unlockedByQuestID = "vars_vault_find_variel",
                questID = "vars_vault_speak_variel",
                displayName = "Speak with Variel",
                sanctumID = "vars_vault" },
            new QuestEntry { unlockedByQuestID = "vars_vault_speak_variel",
                questID = "vars_vault_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption",
                sanctumID = "vars_vault" },
            new QuestEntry { unlockedByQuestID = "vars_vault_defeat_enemy",
                questID = "vars_vault_restore_crystal",
                displayName = "Restore the Kernel Crystal",
                sanctumID = "vars_vault" },

            // --- Input Mists ---
            new QuestEntry { unlockedByQuestID = "vars_vault_restore_crystal",
                questID = "input_mists_find_evalyn",
                displayName = "Find Evalyn in the Input Mists",
                sanctumID = "input_mists" },
            new QuestEntry { unlockedByQuestID = "input_mists_find_evalyn",
                questID = "input_mists_speak_evalyn",
                displayName = "Speak with Evalyn",
                sanctumID = "input_mists" },
            new QuestEntry { unlockedByQuestID = "input_mists_speak_evalyn",
                questID = "input_mists_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption",
                sanctumID = "input_mists" },
            new QuestEntry { unlockedByQuestID = "input_mists_defeat_enemy",
                questID = "input_mists_restore_crystal",
                displayName = "Restore the Kernel Crystal",
                sanctumID = "input_mists" },

            // --- Elif Labyrinth ---
            new QuestEntry { unlockedByQuestID = "input_mists_restore_crystal",
                questID = "elif_labyrinth_find_whilow",
                displayName = "Find Whilow in the Elif Labyrinth",
                sanctumID = "elif_labyrinth" },
            new QuestEntry { unlockedByQuestID = "elif_labyrinth_find_whilow",
                questID = "elif_labyrinth_speak_whilow",
                displayName = "Speak with Whilow",
                sanctumID = "elif_labyrinth" },
            new QuestEntry { unlockedByQuestID = "elif_labyrinth_speak_whilow",
                questID = "elif_labyrinth_defeat_enemy",
                displayName = "Defeat the Null Wraith's corruption",
                sanctumID = "elif_labyrinth" },
            new QuestEntry { unlockedByQuestID = "elif_labyrinth_defeat_enemy",
                questID = "elif_labyrinth_restore_crystal",
                displayName = "Restore the Kernel Crystal",
                sanctumID = "elif_labyrinth" },
            new QuestEntry { unlockedByQuestID = "elif_labyrinth_restore_crystal",
                questID = "epilogue_return_to_mainmap",
                displayName = "Return to Aethelscript" },

            // --- Epilogue handled separately in EpilogueSequenceController.cs ---
            new QuestEntry { unlockedByQuestID = "epilogue_return_to_mainmap",
                questID = "world_restored",
                displayName = "Aethelscript Restored" },
        };
    }
}