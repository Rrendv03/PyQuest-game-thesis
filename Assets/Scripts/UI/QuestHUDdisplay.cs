using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Persistent top-left HUD quest panel — now a two-part display instead of
/// a stale one-line objective:
///
///   Line 1 (questNameText):     the MAIN quest name for the current sanctum
///                               (e.g. "The Print Console").
///   Line 2+ (questObjectiveText): a live objective description that changes
///                               with what the player must actually DO, plus
///                               checkmark rows for each mission tablet
///                               (same check style as MissionTabletUI) and
///                               for the sanctum XP requirement.
///
/// Phases (driven by the active QuestManager entry's questID):
///   find_...          -> "Find {NPC} in {sanctum}..." guidance
///   speak_...         -> "Talk to {NPC}..." guidance
///   defeat_enemy /    -> XP check + one checkmark row per mission tablet,
///   restore_crystal      and when BOTH requirements are met
///                        (MissionTabletManager.IsBossUnlockReady) the
///                        objective becomes "Defeat the boss in this
///                        location!" and the HUD compass activates and
///                        points at the boss (EncounterManager's
///                        isBossZone / bossEnemyPrefab zone).
///                        The compass hides when the boss is defeated.
///
/// Setup: place on a GameObject in HUDCanvas. Assign questNameText and
/// questObjectiveText (richText ON, sized for multiple lines). Optionally
/// assign bossTargetOverride; otherwise the boss zone is found
/// automatically via the ZoneTrigger with isBossZone = true.
/// </summary>
public class QuestHUDDisplay : MonoBehaviour
{
    [Header("UI")]
    [Tooltip("Line 1: the main quest name (sanctum quest title).")]
    public Text questNameText;
    [Tooltip("Line 2+: live objective description with checkmark rows. Rich Text must be enabled.")]
    public Text questObjectiveText;
    public GameObject questPanel;

    [Header("Boss Compass")]
    [Tooltip("Optional direct reference to the boss / boss spawn point. Leave empty to auto-find the ZoneTrigger with isBossZone = true.")]
    public Transform bossTargetOverride;
    [Tooltip("Label shown above the compass arrow while pointing at the boss.")]
    public string bossCompassLabel = "Boss";

    [System.Serializable]
    public class SanctumTitle
    {
        public string sanctumID;
        public string title;
    }

    [Header("Main Quest Names (per sanctum)")]
    [Tooltip("Main quest name shown on line 1 for each sanctum. Defaults below; override in Inspector if you rename sanctums.")]
    public List<SanctumTitle> sanctumTitles = new List<SanctumTitle>
    {
        new SanctumTitle { sanctumID = "print_console",   title = "The Print Console" },
        new SanctumTitle { sanctumID = "vars_vault",      title = "The Vars Vault" },
        new SanctumTitle { sanctumID = "input_mists",     title = "The Input Mists" },
        new SanctumTitle { sanctumID = "elif_labyrinth",  title = "The Elif Labyrinth" },
    };

    // Checkmark glyphs, colored to match MissionTabletUI's done/pending style.
    private const string CheckDone = "<color=#33CC33>\u2713</color>";   // green check
    private const string CheckPending = "<color=#999999>\u25CB</color>"; // gray open circle
    private static readonly Color DoneColor = new Color(0.2f, 0.8f, 0.2f);
    private static readonly Color PendingColor = new Color(0.8f, 0.8f, 0.8f);

    private bool _showingBossCompass = false;
    private Transform _cachedBossTarget;
    private QuestManager.QuestEntry _currentEntry;

    void Start()
    {
        // QuestManager / XPManager are DontDestroyOnLoad; MissionTabletManager
        // loads its JSON asynchronously, so also refresh when it finishes.
        if (QuestManager.Instance != null)
        {
            QuestManager.Instance.OnQuestUpdated += HandleQuestUpdated;
            QuestManager.Instance.OnActiveQuestChanged += HandleActiveQuestChanged;
        }
        if (XPManager.Instance != null)
            XPManager.Instance.OnXPChanged += HandleXPChanged;
        if (MissionTabletManager.Instance != null)
            MissionTabletManager.Instance.MissionsLoaded += HandleMissionsLoaded;
        TabletReadTracker.ReadStateChanged += HandleReadStateChanged;

        // SanctumManager is scene-local, but its events are static — safe to
        // subscribe here and unsubscribe in OnDestroy.
        SanctumManager.OnBossUnlockedEvent += HandleBossUnlocked;
        SanctumManager.OnBossDefeatedEvent += HandleBossDefeated;
        SanctumManager.OnTabletMissionCompleted += HandleTabletMissionCompleted;

        // Show current quest immediately on scene load.
        Refresh();
    }

    void OnDestroy()
    {
        if (QuestManager.Instance != null)
        {
            QuestManager.Instance.OnQuestUpdated -= HandleQuestUpdated;
            QuestManager.Instance.OnActiveQuestChanged -= HandleActiveQuestChanged;
        }
        if (XPManager.Instance != null)
            XPManager.Instance.OnXPChanged -= HandleXPChanged;
        if (MissionTabletManager.Instance != null)
            MissionTabletManager.Instance.MissionsLoaded -= HandleMissionsLoaded;
        TabletReadTracker.ReadStateChanged -= HandleReadStateChanged;

        SanctumManager.OnBossUnlockedEvent -= HandleBossUnlocked;
        SanctumManager.OnBossDefeatedEvent -= HandleBossDefeated;
        SanctumManager.OnTabletMissionCompleted -= HandleTabletMissionCompleted;
    }

    // === Event handlers (all funnel into a full re-render) =====================

    private void HandleQuestUpdated(string _) => Refresh();
    private void HandleActiveQuestChanged(QuestManager.QuestEntry entry) => Refresh();
    private void HandleXPChanged(int _) => Refresh();
    private void HandleMissionsLoaded() => Refresh();
    private void HandleTabletMissionCompleted(string sanctumID, int count) => Refresh();
    private void HandleReadStateChanged() => Refresh();

    private void HandleBossUnlocked(string sanctumID)
    {
        Refresh();
        // Compass activation for the boss unlock moment.
        ShowBossCompass();
    }

    private void HandleBossDefeated(string sanctumID)
    {
        HideBossCompass();
        Refresh();
    }

    // === Core render ===========================================================

    private void Refresh()
    {
        if (QuestManager.Instance == null) return;

        QuestManager.QuestEntry entry = QuestManager.Instance.GetActiveQuestEntry();
        _currentEntry = entry;

        bool gameComplete = StoryProgressionManager.Instance != null
            && StoryProgressionManager.Instance.GetActiveQuestID() == "game_complete";

        if (gameComplete)
        {
            SetName("Aethelscript Restored");
            SetObjective("The Aethelscript is restored. Every sanctum is clear.");
            ShowPanel(true);
            HideBossCompass();
            return;
        }

        if (entry == null)
        {
            ShowPanel(false);
            return;
        }

        // Line 1: main quest name for this sanctum.
        SetName(ResolveSanctumTitle(entry.sanctumID));

        // Line 2+: phase-specific objective + checklist.
        string action = GetQuestAction(entry);
        switch (action)
        {
            case "find":
                SetObjective($"Meet your guide for this sanctum. Find {GetNpcName(entry)} in {ResolveSanctumTitle(entry.sanctumID)} and walk up to them.");
                break;

            case "speak":
                SetObjective($"Talk to {GetNpcName(entry)} to receive this sanctum's mission.");
                break;

            case "defeat_enemy":
            case "restore_crystal":
                RenderSanctumGrindPhase(entry);
                break;

            default:
                SetObjective(entry.displayName);
                break;
        }

        ShowPanel(true);
        RefreshBossCompassState(entry.sanctumID);
    }

    /// <summary>
    /// The XP / mission-tablet / boss phase. Renders:
    ///   - a current-objective line,
    ///   - the XP checkmark row (vs. the sanctum's boss-unlock threshold),
    ///   - one checkmark row per mission tablet for this sanctum
    ///     (same data source as MissionTabletUI: MissionTabletManager),
    ///   - and "Defeat the boss in this location!" once the boss unlocks.
    /// </summary>
    private void RenderSanctumGrindPhase(QuestManager.QuestEntry entry)
    {
        string sanctumID = entry.sanctumID;
        var sb = new StringBuilder();

        bool bossUnlocked = MissionTabletManager.Instance != null
            && MissionTabletManager.Instance.IsBossUnlockReady(sanctumID);
        bool bossDefeated = StoryProgressionManager.Instance != null
            && StoryProgressionManager.Instance.HasDefeatedBoss(sanctumID);

        if (bossDefeated)
        {
            sb.Append("The guardian is defeated. Restore the sanctum's Kernel Crystal to finish this quest.");
            SetObjective(sb.ToString());
            return;
        }

        if (bossUnlocked)
        {
            // Requirements stay visible as completed checks; the objective
            // itself switches to the boss.
            sb.AppendLine("<b>Defeat the boss in this location!</b>");
            sb.Append(BuildChecklist(sanctumID));
            SetObjective(sb.ToString().TrimEnd());
            return;
        }

        sb.AppendLine("Weaken the corruption in this sanctum to unlock the boss:");
        sb.Append(BuildChecklist(sanctumID));
        SetObjective(sb.ToString().TrimEnd());
    }

    /// <summary>XP row + one checkmark row per mission tablet of the sanctum.</summary>
    private string BuildChecklist(string sanctumID)
    {
        var sb = new StringBuilder();

        // XP requirement row — checked against the sanctum's own threshold.
        if (XPManager.Instance != null)
        {
            int threshold = XPManager.Instance.GetThreshold(sanctumID);
            int current = Mathf.Min(XPManager.Instance.CurrentXP, threshold);
            bool xpDone = threshold > 0 && XPManager.Instance.IsBossUnlocked(sanctumID);
            string mark = xpDone ? CheckDone : CheckPending;
            sb.AppendLine($"{mark} Earn {threshold} XP ({current}/{threshold})");
        }

        // Mission tablet rows — identical data source as MissionTabletUI.
        if (MissionTabletManager.Instance != null)
        {
            List<MissionTabletData> missions =
                MissionTabletManager.Instance.GetMissionsForSanctum(sanctumID);
            foreach (var mission in missions)
            {
                bool done = MissionTabletManager.Instance.IsMissionComplete(mission.missionID);
                string mark = done ? CheckDone : CheckPending;
                sb.AppendLine($"{mark} {mission.description}");
            }
        }

        // Reading rows — did the player view the mission tablet quest list and
        // the lesson tablet? Informational only; does not gate the boss.
        bool missionsRead = TabletReadTracker.AreSanctumMissionsRead(sanctumID);
        sb.AppendLine($"{(missionsRead ? CheckDone : CheckPending)} Read the mission tablet quests");
        bool lessonRead = TabletReadTracker.IsLessonRead(sanctumID);
        sb.AppendLine($"{(lessonRead ? CheckDone : CheckPending)} Read the lesson tablet");

        return sb.ToString();
    }

    // === Boss compass ==========================================================

    private void RefreshBossCompassState(string sanctumID)
    {
        bool bossUnlocked = MissionTabletManager.Instance != null
            && MissionTabletManager.Instance.IsBossUnlockReady(sanctumID);
        bool bossDefeated = StoryProgressionManager.Instance != null
            && StoryProgressionManager.Instance.HasDefeatedBoss(sanctumID);

        if (bossUnlocked && !bossDefeated)
            ShowBossCompass();
        else
            HideBossCompass();
    }

    private void ShowBossCompass()
    {
        if (HUDCompassController.Instance == null) return;

        Transform target = GetBossTarget();
        if (target == null)
        {
            Debug.LogWarning("[QuestHUDDisplay] Boss unlocked but no boss target found. " +
                             "Assign bossTargetOverride or add a ZoneTrigger with isBossZone = true.");
            return;
        }

        HUDCompassController.Instance.ShowCompassTo(target, bossCompassLabel);
        _showingBossCompass = true;
    }

    private void HideBossCompass()
    {
        if (!_showingBossCompass) return; // don't steal a compass another system owns
        _showingBossCompass = false;
        if (HUDCompassController.Instance != null)
            HUDCompassController.Instance.Hide();
    }

    /// <summary>
    /// Boss target: the inspector-assigned override, else the scene's
    /// ZoneTrigger with isBossZone = true (the zone EncounterManager uses to
    /// spawn bossEnemyPrefab). Cached per scene.
    /// </summary>
    private Transform GetBossTarget()
    {
        if (bossTargetOverride != null) return bossTargetOverride;

        if (_cachedBossTarget == null)
        {
            foreach (ZoneTrigger zone in FindObjectsOfType<ZoneTrigger>())
            {
                if (zone != null && zone.isBossZone)
                {
                    _cachedBossTarget = zone.transform;
                    break;
                }
            }
        }
        return _cachedBossTarget;
    }

    // === Helpers ===============================================================

    private string ResolveSanctumTitle(string sanctumID)
    {
        foreach (var t in sanctumTitles)
            if (t.sanctumID == sanctumID && !string.IsNullOrEmpty(t.title))
                return t.title;
        return !string.IsNullOrEmpty(sanctumID)
            ? char.ToUpper(sanctumID[0]) + sanctumID.Substring(1).Replace('_', ' ')
            : "";
    }

    /// <summary>"print_console_speak_printessa" + sanctum "print_console" -> "speak_printessa".</summary>
    private static string GetQuestAction(QuestManager.QuestEntry entry)
    {
        if (entry == null || string.IsNullOrEmpty(entry.questID)) return "";
        string id = entry.questID;
        if (!string.IsNullOrEmpty(entry.sanctumID) && id.StartsWith(entry.sanctumID + "_"))
            id = id.Substring(entry.sanctumID.Length + 1);

        if (id.StartsWith("find_")) return "find";
        if (id.StartsWith("speak_")) return "speak";
        if (id == "defeat_enemy") return "defeat_enemy";
        if (id == "restore_crystal") return "restore_crystal";
        return id;
    }

    /// <summary>"speak_printessa" -> "Printessa".</summary>
    private static string GetNpcName(QuestManager.QuestEntry entry)
    {
        string id = entry.questID ?? "";
        int idx = id.IndexOf("_find_", System.StringComparison.Ordinal);
        if (idx < 0) idx = id.IndexOf("_speak_", System.StringComparison.Ordinal);
        string name = idx >= 0 ? id.Substring(idx + 7) : id;
        return name.Length > 0
            ? char.ToUpper(name[0]) + name.Substring(1)
            : "the guide";
    }

    private void SetName(string text)
    {
        if (questNameText != null)
        {
            questNameText.text = text;
            questNameText.color = Color.white;
        }
    }

    private void SetObjective(string richText)
    {
        if (questObjectiveText != null)
            questObjectiveText.text = richText;
    }

    private void ShowPanel(bool show)
    {
        if (questPanel != null)
            questPanel.SetActive(show);
    }
}
