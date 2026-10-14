using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// All of the minimap's *decision* rules in one place, with no UI, camera or
/// GameObject code at all. Everything here reads the existing managers and
/// answers one question: "what should the map be showing right now?".
///
/// WHY IT IS SPLIT OUT
/// The map sits on top of four separate sources of truth (XPManager,
/// MissionTabletManager, StoryProgressionManager, QuestManager) and has to stay
/// correct through scene transitions AND save loads. Two load paths -
/// StoryProgressionManager.ImportDefeatedBosses and
/// MissionTabletManager.ImportCompletedMissions - replace their sets in place and
/// fire NO event, so an events-only design silently shows a stale map after
/// loading a slot. Keeping the rules pure means they can be driven from fake
/// manager state in the tools/dllpatch harness (minimap_logic_test.cs) instead of
/// being verified by eye in ten minutes of playtesting.
///
/// Nothing here creates or destroys anything, so it is safe to call from
/// anywhere, including a console harness with no scene loaded. Wording lives in
/// QuestText, not here.
/// </summary>
public static class MinimapLogic
{
    /// <summary>
    /// Canonical progression order. Agrees with MainMapSanctumCompass's setup note
    /// ("Print Console, Vars Vault, Input Mists, Elif Labyrinth") and with
    /// XPManager's ascending thresholds (150 / 550 / 900 / 1500).
    /// </summary>
    public static readonly string[] ProgressionOrder = new string[]
    {
        "print_console",
        "vars_vault",
        "input_mists",
        "elif_labyrinth"
    };

    /// <summary>
    /// Unity scene name (normalised) -> sanctum_id, mirroring
    /// ZoneTrigger.GetSanctumIDFromScene. Unlike that helper there is deliberately
    /// no "default to print_console" fallback: for the map, guessing wrong means
    /// drawing Print Console's tablets on MainMap, so an unknown scene resolves to
    /// "not a sanctum" instead.
    /// </summary>
    static readonly Dictionary<string, string> SceneToSanctum = new Dictionary<string, string>
    {
        { "printconsole", "print_console" },
        { "varsvault", "vars_vault" },
        { "inputmists", "input_mists" },
        { "eliflabyrinth", "elif_labyrinth" },
    };

    const string MainMapSceneName = "mainmap";

    public enum BossMarkerState
    {
        /// <summary>No marker: the guardian is still locked. The default.</summary>
        Hidden,
        /// <summary>Dim ghost marker, only when showBossMarkerWhileLocked is on.</summary>
        Locked,
        /// <summary>Bright marker: this sanctum's XP threshold met AND every tablet done.</summary>
        Unlocked,
        /// <summary>Dim marker where the defeated guardian fell.</summary>
        Defeated
    }

    // === Scene / sanctum identity ============================================================

    public static string NormalizeSceneName(string sceneName)
    {
        if (string.IsNullOrEmpty(sceneName)) return "";
        var sb = new StringBuilder(sceneName.Length);
        for (int i = 0; i < sceneName.Length; i++)
        {
            char c = sceneName[i];
            if (c == '_' || c == ' ' || c == '-') continue;
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    public static string CurrentSceneName()
    {
        Scene scene = SceneManager.GetActiveScene();
        return scene.name ?? "";
    }

    public static bool TryGetSanctumIDForScene(string sceneName, out string sanctumID)
    {
        return SceneToSanctum.TryGetValue(NormalizeSceneName(sceneName), out sanctumID);
    }

    /// <summary>True when the scene is the overworld holding the sanctum entrances.</summary>
    public static bool IsMainMapScene(string sceneName)
    {
        return NormalizeSceneName(sceneName) == MainMapSceneName;
    }

    /// <summary>
    /// Sanctum the map is describing. Priority: explicit Inspector value ->
    /// canonical scene-name mapping -> the scene's own SanctumManager -> "" (a
    /// non-sanctum scene such as MainMap, a menu or the intro).
    /// </summary>
    public static string ResolveSanctumID(string inspectorSanctumID)
    {
        if (!string.IsNullOrEmpty(inspectorSanctumID)) return inspectorSanctumID;

        string sceneName = CurrentSceneName();

        string fromSceneMapping;
        if (TryGetSanctumIDForScene(sceneName, out fromSceneMapping))
            return fromSceneMapping;

        // A sanctum scene renamed away from the table still knows its own id.
        SanctumManager sm = SanctumManager.Instance;
        if (sm != null && !string.IsNullOrEmpty(sm.sanctumID) && !IsMainMapScene(sceneName))
            return sm.sanctumID;

        return "";
    }

    /// <summary>True when this scene has tablet + sanctum-boss content to draw.</summary>
    public static bool IsSanctumScene(string sanctumID)
    {
        return !string.IsNullOrEmpty(sanctumID) && !IsMainMapScene(CurrentSceneName());
    }

    // === Marker rules ========================================================================

    /// <summary>
    /// Tablet markers belong to sanctums only - the one place a
    /// TabletMissionObject exists. False on MainMap and in menus.
    /// </summary>
    public static bool ShouldShowTabletMarkers(string sanctumID)
    {
        return IsSanctumScene(sanctumID);
    }

    /// <summary>
    /// A tablet marker exists while its mission is unsolved and disappears the
    /// moment it is solved. Completion is read from MissionTabletManager - the set
    /// the save system exports and imports - never from the tablet's own
    /// GameObject, so a loaded save hides the marker even if the world model is
    /// still mid-restore or the tablet was never re-rendered.
    /// </summary>
    public static bool IsTabletMarkerVisible(string missionID)
    {
        if (string.IsNullOrEmpty(missionID)) return false;
        MissionTabletManager mgr = MissionTabletManager.Instance;
        if (mgr == null) return true;   // no mission data yet: show, and refresh when it loads
        return !mgr.IsMissionComplete(missionID);
    }

    public static List<MissionTabletData> GetSanctumMissions(string sanctumID)
    {
        if (string.IsNullOrEmpty(sanctumID) || MissionTabletManager.Instance == null)
            return new List<MissionTabletData>();
        List<MissionTabletData> missions = MissionTabletManager.Instance.GetMissionsForSanctum(sanctumID);
        return missions ?? new List<MissionTabletData>();
    }

    public static int GetPendingTabletCount(string sanctumID)
    {
        List<MissionTabletData> missions = GetSanctumMissions(sanctumID);
        int pending = 0;
        for (int i = 0; i < missions.Count; i++)
            if (IsTabletMarkerVisible(missions[i].missionID)) pending++;
        return pending;
    }

    /// <summary>
    /// Boss marker gate. The condition is exactly the one the game already uses to
    /// decide whether the boss zone will start -
    /// MissionTabletManager.IsBossUnlockReady == XPManager.IsBossUnlocked(sanctum)
    /// AND AreAllSanctumMissionsComplete(sanctum) - so the map can never point at
    /// a boss the player cannot actually begin.
    /// </summary>
    public static BossMarkerState GetBossMarkerState(string sanctumID, bool showWhileLocked, bool showAfterDefeat)
    {
        if (!IsSanctumScene(sanctumID) && string.IsNullOrEmpty(sanctumID)) return BossMarkerState.Hidden;
        if (string.IsNullOrEmpty(sanctumID)) return BossMarkerState.Hidden;

        if (IsBossDefeated(sanctumID))
            return showAfterDefeat ? BossMarkerState.Defeated : BossMarkerState.Hidden;

        MissionTabletManager mgr = MissionTabletManager.Instance;
        if (mgr == null) return showWhileLocked ? BossMarkerState.Locked : BossMarkerState.Hidden;

        if (mgr.IsBossUnlockReady(sanctumID)) return BossMarkerState.Unlocked;
        return showWhileLocked ? BossMarkerState.Locked : BossMarkerState.Hidden;
    }

    /// <summary>
    /// Defeat ledger, with the same secondary check BossGate uses for the window
    /// after a load where the in-memory progression set has not been restored yet.
    /// </summary>
    public static bool IsBossDefeated(string sanctumID)
    {
        if (string.IsNullOrEmpty(sanctumID)) return false;

        StoryProgressionManager spm = StoryProgressionManager.Instance;
        if (spm != null && spm.HasDefeatedBoss(sanctumID)) return true;

        SaveLoadManager slm = SaveLoadManager.Instance;
        if (slm != null) return slm.IsSanctumBossDefeated(sanctumID);
        return false;
    }

    /// <summary>
    /// The sanctum the player should travel to next, by the exact rule
    /// MainMapSanctumCompass uses: the first sanctum in progression order whose
    /// boss has not been defeated. One ledger, so the map marker and the HUD
    /// compass can never disagree about which door is next.
    /// Returns "" when every sanctum is clear.
    /// </summary>
    public static string GetNextSanctumID()
    {
        for (int i = 0; i < ProgressionOrder.Length; i++)
            if (!IsBossDefeated(ProgressionOrder[i])) return ProgressionOrder[i];
        return "";
    }

    public static bool IsGameComplete()
    {
        StoryProgressionManager spm = StoryProgressionManager.Instance;
        if (spm != null && spm.GetActiveQuestID() == "game_complete") return true;
        return GetNextSanctumID() == "";
    }

    // === Save / load reactivity ==============================================================

    /// <summary>
    /// Discrete snapshot of everything the map's markers are derived from. The
    /// controller keeps the last signature and re-renders whenever it changes.
    ///
    /// This is what makes the map safe against the save system. Loading a slot
    /// reaches MissionTabletManager.ImportCompletedMissions and
    /// StoryProgressionManager.ImportCompletedQuestIDs / ImportDefeatedBosses,
    /// which replace those sets silently. Polling this signature - a handful of
    /// integer and string reads, one small StringBuilder - catches every load
    /// path, including ones added later that the minimap has never heard of,
    /// without the minimap having to hook SaveLoadManager at all.
    /// </summary>
    public static string BuildStateSignature(string sanctumID)
    {
        var sb = new StringBuilder(128);
        sb.Append(CurrentSceneName()).Append('|');
        sb.Append(sanctumID).Append('|');

        XPManager xp = XPManager.Instance;
        sb.Append(xp != null ? xp.CurrentXP : -1).Append('|');

        StoryProgressionManager spm = StoryProgressionManager.Instance;
        sb.Append(spm != null ? spm.GetActiveQuestID() : "?").Append('|');

        // Sanctum clear state across the whole game, not just the current scene:
        // on MainMap this is what picks the next-sanctum marker.
        for (int i = 0; i < ProgressionOrder.Length; i++)
            sb.Append(IsBossDefeated(ProgressionOrder[i]) ? '1' : '0');
        sb.Append('|');

        // Per-mission bits for the sanctum on display, each labelled with its own
        // id, so a re-ordered JSON file cannot silently shift a bit onto the wrong
        // tablet and hide the wrong marker.
        List<MissionTabletData> missions = GetSanctumMissions(sanctumID);
        for (int i = 0; i < missions.Count; i++)
        {
            sb.Append(missions[i].missionID).Append(':');
            sb.Append(IsTabletMarkerVisible(missions[i].missionID) ? '0' : '1');
            sb.Append(';');
        }
        sb.Append('|');

        MissionTabletManager mgr = MissionTabletManager.Instance;
        sb.Append(mgr != null && mgr.IsLoaded ? 'L' : 'l');
        if (mgr != null && !string.IsNullOrEmpty(sanctumID))
            sb.Append(mgr.IsBossUnlockReady(sanctumID) ? 'U' : 'u');

        return sb.ToString();
    }
}
