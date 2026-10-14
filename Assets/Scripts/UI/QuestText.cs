using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// One implementation of "what is the current quest called, and what does the
/// player have to do about it", shared by the top-left HUD quest panel
/// (QuestHUDDisplay) and the expanded minimap's left-hand quest panel.
///
/// WHY THIS EXISTS
/// The phase text (find / speak / defeat_enemy / restore_crystal) and the XP +
/// mission-tablet checklist already lived inside QuestHUDDisplay. The map needs
/// the same words. Two copies of that logic means the HUD can tell the player one
/// thing while the map tells them another, and only one copy gets edited when a
/// quest is reworded or a phase is added - so QuestHUDDisplay calls in here
/// instead of owning its own phrasing. The strings below are the HUD's own
/// wording, moved unchanged.
///
/// Everything is static and returns a string: no components, no cached state, no
/// GameObjects touched, so both the map's refresh loop and a console test harness
/// can call it safely.
/// </summary>
public static class QuestText
{
    /// <summary>Checkmark glyphs, matching MissionTabletUI's done/pending colours.</summary>
    public const string CheckDone = "<color=#33CC33>\u2713</color>";
    public const string CheckPending = "<color=#999999>\u25CB</color>";

    /// <summary>
    /// Per-sanctum quest titles. QuestHUDDisplay keeps its own Inspector list and
    /// hands it over through <see cref="SetTitleOverrides"/>, so a designer rename
    /// on the HUD appears on the map too instead of becoming a second list to
    /// keep in sync.
    /// </summary>
    static readonly Dictionary<string, string> DefaultSanctumTitles = new Dictionary<string, string>
    {
        { "print_console", "The Print Console" },
        { "vars_vault", "The Vars Vault" },
        { "input_mists", "The Input Mists" },
        { "elif_labyrinth", "The Elif Labyrinth" },
    };

    /// <summary>
    /// A snapshot of the HUD panel's Inspector titles. Snapshotted rather than
    /// referenced: that list belongs to a scene object destroyed at the end of the
    /// scene, and a dead panel must not keep steering the map's wording.
    /// </summary>
    static Dictionary<string, string> titleOverrides;

    /// <summary>Install the HUD panel's title list, or clear it with null.</summary>
    public static void SetTitleOverrides(List<QuestHUDDisplay.SanctumTitle> titles)
    {
        if (titles == null || titles.Count == 0)
        {
            titleOverrides = null;
            return;
        }

        var snapshot = new Dictionary<string, string>();
        for (int i = 0; i < titles.Count; i++)
        {
            QuestHUDDisplay.SanctumTitle entry = titles[i];
            if (entry == null || string.IsNullOrEmpty(entry.sanctumID) || string.IsNullOrEmpty(entry.title))
                continue;
            snapshot[entry.sanctumID] = entry.title;
        }

        titleOverrides = snapshot.Count > 0 ? snapshot : null;
    }

    // === Titles ==============================================================================

    /// <summary>
    /// "vars_vault" -> "The Vars Vault". Fallback chain: the HUD panel's Inspector
    /// override, then the built-in table, then the prettified id - so a sanctum
    /// nobody listed still reads as a name rather than a snake_case key.
    /// </summary>
    public static string ResolveSanctumTitle(string sanctumID)
    {
        if (string.IsNullOrEmpty(sanctumID)) return "";

        if (titleOverrides != null)
        {
            string overrideTitle;
            if (titleOverrides.TryGetValue(sanctumID, out overrideTitle) && !string.IsNullOrEmpty(overrideTitle))
                return overrideTitle;
        }

        string defaultTitle;
        if (DefaultSanctumTitles.TryGetValue(sanctumID, out defaultTitle)) return defaultTitle;

        return Prettify(sanctumID);
    }

    /// <summary>
    /// Line 1 of the quest display: the main quest name for this sanctum. On a
    /// non-sanctum scene (MainMap) the live quest's own title is used instead.
    /// </summary>
    public static string BuildQuestName(string sanctumID)
    {
        string title = ResolveSanctumTitle(sanctumID);
        if (!string.IsNullOrEmpty(title)) return title;

        QuestManager qm = QuestManager.Instance;
        QuestManager.QuestEntry entry = qm != null ? qm.GetActiveQuestEntry() : null;
        if (entry != null && !string.IsNullOrEmpty(entry.displayName)) return entry.displayName;

        if (MinimapLogic.IsGameComplete()) return "Aethelscript Restored";
        return "Aethelgard";
    }

    static string Prettify(string id)
    {
        string[] words = id.Split('_');
        var sb = new StringBuilder();
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length == 0) continue;
            if (sb.Length > 0) sb.Append(' ');
            sb.Append(char.ToUpperInvariant(words[i][0]));
            if (words[i].Length > 1) sb.Append(words[i].Substring(1));
        }
        return sb.Length > 0 ? sb.ToString() : id;
    }

    // === Phases ==============================================================================

    /// <summary>
    /// Phase of a quest entry: "find" | "speak" | "defeat_enemy" |
    /// "restore_crystal" | the bare id for anything else. Derived by stripping the
    /// sanctum prefix from the questID, exactly as QuestHUDDisplay has always done.
    /// </summary>
    public static string GetQuestPhase(QuestManager.QuestEntry entry)
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

    /// <summary>True for the grind phase, the one the boss gate belongs to.</summary>
    public static bool IsPreparationPhase(QuestManager.QuestEntry entry)
    {
        string phase = GetQuestPhase(entry);
        return phase == "defeat_enemy" || phase == "restore_crystal";
    }

    /// <summary>
    /// "print_console_speak_printessa" -> "Printessa". A caller that knows the
    /// guide's real name passes npcNameOverride; this is the id-derived guess used
    /// when nobody does.
    ///
    /// Note on the offsets: "_find_" is six characters and "_speak_" is seven, and
    /// the version of this code in QuestHUDDisplay added a flat 7 to both, which
    /// turned "Printessa" into "rintessa" on every find_ quest. Splitting the two
    /// searches fixes that while keeping the speak_ result exactly as it was.
    /// </summary>
    public static string GetGuideName(QuestManager.QuestEntry entry, string npcNameOverride = null)
    {
        if (!string.IsNullOrEmpty(npcNameOverride)) return npcNameOverride;

        string id = entry != null ? entry.questID : null;
        if (string.IsNullOrEmpty(id)) return "the guide";

        int findIdx = id.IndexOf("_find_", System.StringComparison.Ordinal);
        if (findIdx >= 0) return Capitalise(id.Substring(findIdx + "_find_".Length));

        int speakIdx = id.IndexOf("_speak_", System.StringComparison.Ordinal);
        if (speakIdx >= 0) return Capitalise(id.Substring(speakIdx + "_speak_".Length));

        return Capitalise(id);
    }

    static string Capitalise(string name)
    {
        if (string.IsNullOrEmpty(name)) return "the guide";
        return char.ToUpper(name[0]) + name.Substring(1);
    }

    // === Objective ===========================================================================

    /// <summary>
    /// The objective block for a quest: the phase line, and during the preparation
    /// phase the full requirement checklist. Rich text, ready for a Text or
    /// TextMeshPro field.
    /// </summary>
    public static string BuildObjective(QuestManager.QuestEntry entry,
                                        string npcNameOverride = null,
                                        bool includeReadRows = true)
    {
        if (entry == null) return "";

        string sanctumTitle = ResolveSanctumTitle(entry.sanctumID);
        string guide = GetGuideName(entry, npcNameOverride);

        switch (GetQuestPhase(entry))
        {
            case "find":
                return "Meet your guide for this sanctum. Find " + guide +
                       (string.IsNullOrEmpty(sanctumTitle) ? "" : " in " + sanctumTitle) +
                       " and walk up to them.";

            case "speak":
                return "Talk to " + guide + " to receive this sanctum's mission.";

            case "defeat_enemy":
            case "restore_crystal":
                return BuildPreparationObjective(entry.sanctumID, includeReadRows);

            default:
                return entry.displayName;
        }
    }

    /// <summary>
    /// The XP / mission-tablet / guardian block shown during a sanctum's
    /// preparation phase, reading the same managers as the boss gate itself so the
    /// text can never promise a boss the gate will not open for.
    /// </summary>
    public static string BuildPreparationObjective(string sanctumID, bool includeReadRows = true)
    {
        if (string.IsNullOrEmpty(sanctumID)) return "";

        if (MinimapLogic.IsBossDefeated(sanctumID))
            return "The guardian is defeated. Restore the sanctum's Kernel Crystal to finish this quest.";

        var sb = new StringBuilder();

        bool bossUnlocked = MissionTabletManager.Instance != null
            && MissionTabletManager.Instance.IsBossUnlockReady(sanctumID);

        if (bossUnlocked) sb.AppendLine("<b>Defeat the boss in this location!</b>");
        else sb.AppendLine("Weaken the corruption in this sanctum to unlock the boss:");

        sb.Append(BuildChecklist(sanctumID, includeReadRows));
        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// XP row + one row per mission tablet, then optionally the two tablet
    /// read-state rows. The read rows are HUD decoration only - they gate nothing -
    /// which is why the map asks for the checklist without them.
    /// </summary>
    public static string BuildChecklist(string sanctumID, bool includeReadRows)
    {
        var sb = new StringBuilder();

        XPManager xp = XPManager.Instance;
        if (xp != null && !string.IsNullOrEmpty(sanctumID))
        {
            int threshold = xp.GetThreshold(sanctumID);
            int current = Mathf.Min(xp.CurrentXP, threshold);
            bool xpDone = threshold > 0 && xp.IsBossUnlocked(sanctumID);
            string mark = xpDone ? CheckDone : CheckPending;
            sb.AppendLine(string.Format("{0} Earn {1} XP ({2}/{1})", mark, threshold, current));
        }

        List<MissionTabletData> missions = MinimapLogic.GetSanctumMissions(sanctumID);
        for (int i = 0; i < missions.Count; i++)
        {
            MissionTabletData mission = missions[i];
            bool done = !MinimapLogic.IsTabletMarkerVisible(mission.missionID);
            string mark = done ? CheckDone : CheckPending;
            sb.AppendLine(string.Format("{0} {1}", mark, mission.description));
        }

        if (includeReadRows)
        {
            bool missionsRead = TabletReadTracker.AreSanctumMissionsRead(sanctumID);
            sb.AppendLine(string.Format("{0} Read the mission tablet quests",
                missionsRead ? CheckDone : CheckPending));

            bool lessonRead = TabletReadTracker.IsLessonRead(sanctumID);
            sb.AppendLine(string.Format("{0} Read the lesson tablet",
                lessonRead ? CheckDone : CheckPending));
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// The guardian's status in one line, phrased from whichever half of its
    /// unlock requirement is still missing, so the panel explains the marker that
    /// is not there yet.
    /// </summary>
    public static string BuildBossStatusLine(string sanctumID)
    {
        switch (MinimapLogic.GetBossMarkerState(sanctumID, true, true))
        {
            case MinimapLogic.BossMarkerState.Defeated:
                return "Guardian defeated.";

            case MinimapLogic.BossMarkerState.Unlocked:
                return CheckDone + " <color=#FF6B6B>Guardian awake - marked on the map.</color>";

            case MinimapLogic.BossMarkerState.Locked:
                XPManager xp = XPManager.Instance;
                bool xpMet = xp != null && xp.IsBossUnlocked(sanctumID);
                int pending = MinimapLogic.GetPendingTabletCount(sanctumID);

                if (!xpMet && pending > 0)
                    return string.Format("{0} Guardian sleeps: needs XP and {1} tablet(s).", CheckPending, pending);
                if (!xpMet)
                    return CheckPending + " Guardian sleeps: XP requirement not met.";
                return CheckPending + " Guardian sleeps: tablets remain.";

            default:
                return "";
        }
    }
}
