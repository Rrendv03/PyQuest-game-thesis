using UnityEngine;

#if UNITY_EDITOR
/// <summary>
/// EDITOR-ONLY debug tool: lets you set which quest the game starts at
/// when you press Play, by marking every quest before it in
/// QuestManager's chain complete before QuestManager or
/// EpilogueSequenceController read any state.
///
/// This entire class is wrapped in #if UNITY_EDITOR. UNITY_EDITOR is only
/// ever defined while running inside the Unity Editor, never in a built
/// player of any kind, Android APK included. That means this class does
/// not exist at all in the compiled game your students run, not "disabled",
/// not "hidden", it is not present in the assembly. Still recommended:
/// delete or disable the GameObject holding this before your final Android
/// build, just so you don't see a "missing script" warning in the Editor
/// once you switch build targets back to Android and this stops compiling
/// into the scene's component list.
///
/// IMPORTANT, this is not optional: quest completion and boss-defeated
/// state are two SEPARATE ledgers in StoryProgressionManager.
///   - QuestManager.EvaluateActiveQuest() reads completedQuestIDs, set via
///     CompleteQuest(). This drives the HUD quest name.
///   - EpilogueSequenceController reads HasDefeatedBoss(sanctumID), set
///     via TriggerBossDefeated(). This drives whether the epilogue fires
///     at all.
/// Marking quests complete alone will get you the right HUD text but will
/// NOT trigger the epilogue. Both sections below need to be configured
/// together if you're jumping to test the epilogue specifically.
///
/// Timing: runs in Start(), not Awake(). StoryProgressionManager's own
/// Awake() has already run and set Instance by the time any Start() runs
/// anywhere in the scene, Unity guarantees that ordering. If
/// QuestManager's own Start() happens to run its first
/// EvaluateActiveQuest() call before this script's Start() does, the HUD
/// may flash the wrong quest for a single frame before self-correcting,
/// since every CompleteQuest() call below re-triggers QuestManager's
/// evaluation internally (StoryProgressionManager.CompleteQuest() calls
/// QuestManager.Instance.OnQuestCompleted()). Harmless for debug use, not
/// worth fighting Unity's Start() ordering to eliminate.
///
/// Place on an empty, always-active GameObject in whichever scene you
/// want to jump-start (commonly MainMap, to test the epilogue directly
/// without playing the first three sanctums every time).
/// </summary>
public class DebugMissionJumper : MonoBehaviour
{
    [Header("Master Switch")]
    [Tooltip("Set false to disable the jump without removing the component.")]
    public bool enableJump = true;

    [Header("Quest Chain Jump")]
    [Tooltip("The questID to start at, the first quest that should show as ACTIVE when Play is pressed. Every quest listed BEFORE this one in QuestManager.BuildDefaultChain() gets marked complete. Leave blank to mark the entire chain complete (jump straight to world_restored / \"Aethelscript Restored\").\n\n" +
        "Valid IDs, in chain order:\n" +
        "print_console_find_printessa, print_console_speak_printessa, print_console_defeat_enemy, print_console_restore_crystal,\n" +
        "vars_vault_find_variel, vars_vault_speak_variel, vars_vault_defeat_enemy, vars_vault_restore_crystal,\n" +
        "input_mists_find_evalyn, input_mists_speak_evalyn, input_mists_defeat_enemy, input_mists_restore_crystal,\n" +
        "elif_labyrinth_find_whilow, elif_labyrinth_speak_whilow, elif_labyrinth_defeat_enemy, elif_labyrinth_restore_crystal,\n" +
        "epilogue_return_to_mainmap, world_restored")]
    public string debugStartAtQuestID = "epilogue_return_to_mainmap";

    [Header("Boss-Defeated Flags (separate ledger, see class notes above)")]
    [Tooltip("EpilogueSequenceController's trigger condition depends on THIS, not on quest completion. Add every sanctumID that should be marked as boss-defeated. To test the epilogue itself, elif_labyrinth must be in this list, marking the quest chain complete is not enough on its own.")]
    public string[] sanctumsToMarkBossDefeated = new string[]
    {
        "print_console",
        "vars_vault",
        "input_mists",
        "elif_labyrinth"
    };

    private void Start()
    {
        if (!enableJump)
        {
            Debug.Log("[DebugMissionJumper] enableJump is false, skipping.");
            return;
        }

        if (StoryProgressionManager.Instance == null)
        {
            Debug.LogError("[DebugMissionJumper] StoryProgressionManager.Instance is null. Its prefab isn't present in this scene, or hasn't run Awake() yet.");
            return;
        }

        if (QuestManager.Instance == null)
        {
            Debug.LogError("[DebugMissionJumper] QuestManager.Instance is null. Its prefab isn't present in this scene.");
            return;
        }

        foreach (string sanctumID in sanctumsToMarkBossDefeated)
        {
            if (string.IsNullOrEmpty(sanctumID)) continue;

            StoryProgressionManager.Instance.TriggerSanctumFirstVisit(sanctumID);
            StoryProgressionManager.Instance.TriggerBossUnlocked(sanctumID);
            StoryProgressionManager.Instance.TriggerBossDefeated(sanctumID);
        }

        int completedCount = 0;
        foreach (var quest in QuestManager.Instance.quests)
        {
            if (quest.questID == debugStartAtQuestID)
                break;

            if (!StoryProgressionManager.Instance.IsQuestComplete(quest.questID))
            {
                StoryProgressionManager.Instance.CompleteQuest(quest.questID);
                completedCount++;
            }
        }

        Debug.Log($"[DebugMissionJumper] Marked {completedCount} quest(s) complete, {sanctumsToMarkBossDefeated.Length} sanctum(s) boss-defeated. " +
            $"Active quest should now read: \"{QuestManager.Instance.GetActiveQuestDisplayName()}\"");
    }
}
#endif