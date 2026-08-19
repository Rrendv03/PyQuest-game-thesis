# Debug Session: gameplay-loop-bugs
Status: [OPEN]
Date: 2026-08-20
Environment: Unity C# project, Windows, d:\Unity\PyQuest

## Bug Manifest (5 bugs)

1. **Bug-001: Mission name not changing after talking with Printessa.
2. **Bug-002: Dialogue still in starting sequence after reloading save.
3. **Bug-003: Quit-to-menu then New Game retains loaded-game progression.
4. **Bug-004: Boss encounter zone respawns after save reload.
5. **Bug-005: Mission not changing/progressing after defeating boss.

---

## Hypotheses (Falsifiable)

### Bug-001 Mission name / Printessa
- H1.1: `QuestHUDdisplay`/mission name UI listens to event that does not fire after Dialogue complete — perhaps `QuestManager.CompleteQuest` calls but the Dialogue only fires quest complete for mission name update event.
- H1.2: Dialogue.json `questIDToComplete` for first_meeting or mission_reminder does not match activeQuestID in QuestManager HUD subscription flow.
- H1.3: `DialogueManager` finishes sequence but does not call the mission ID to quest manager's next quest assignment setter — maybe `Dialoguemanager.cs` missing link.

### Bug-002 Dialogue starting-sequence persistence on reload
- H2.1: SaveLoadManager.ApplySaveData doesn't load/restore NPC dialogue states or NPC dialogue state persists from persisted `npcStates` dict but Introsequencecontroller `intro flag `hasRunOnce` bool not restored and re-triggers.
- H2.2: DialogueManager saved in Progress Start runs the intro_prologue unconditionally on scene load regardless of `storyProgression save data state.
- H2.3: Prologue scene has completed flag not serializable.

### Bug-003 New Game state leakage
- H3.1: NewGame/Play New button code doesn't call Reset/clear singletons: BKTEngine, StoryProgressionManager, QuestManager, XPManager, SaveLoadManager state.
- H3.2: DontDestroyOnLoad singletons aren't destroyed between Quit-to-Menu, stay alive so New carries state over.
- H3.3: New game starts but uses a different flow.

### Bug-004 Boss zone respawn
- H4.1: ZoneTrigger's defeatedBossSanctumIDs isn't being persisted/loaded.
- H4.2: Boss defeat check only in XPManager.bossDefeated states only in-memory not serializable and Save slot.
- H4.3: defeatedBossSanctumIDs restored properly but zoneTrigger.bossZone defeated status only checked at runtime during collision so respawn still loads new trigger.

### Bug-005 Boss defeat mission progression
- H5.1: `SanctumManager fires `OnBossDefeated`, quest chain step 4 of each sanctum's quest (restore crystal/defeat boss quest) never completes after boss fight
- H5.2: StoryProgressionManager / ZoneTrigger only marks defeatedBossSanctumIDs set but never calls quest complete check to advance the next sanctum `sanctums 1→2 etc.
- H5.3: `RuneCrystal Restore and `BossGate.cs` events trigger but QuestManager's `restore_crystal` quest ID never calls `CompleteQuest`.

---

## Evidence Log
Static analysis collected across 17 core files.

**Bug-001 (mission name after Printessa): CONFIRMED**
Root cause: Quest chain BuildDefaultChain requires `print_console_find_printessa` (unlocked by `intro_complete`) → to unlock `print_console_speak_printessa`. However nothing ever marks `find_printessa` complete. DialogueManager.HandleSequenceComplete marks `speak_printessa` complete. Then QuestManager.EvaluateActiveQuest() scans from the beginning and picks the first not-completed quest whose `unlockedByQuestID` is completed — which is still `find_printessa` (intro complete, so it's unlocked). Active quest never advances past Find → mission name stuck.

Files: QuestManager.EvaluateActiveQuest L57-L81 vs NPCController.HandleSequenceComplete L157-158, Dialogue.json questIDToComplete `printessa_first_meeting` → speak_printessa.

**Bug-002 (starting sequence dialogue after save reload): CONFIRMED**
Root cause: SaveLoadManager.ApplySaveData subscribes `OnSceneLoaded` once → loads save's `currentScene` (usually MainMap) → fires → runs pendingNPCStates restore against NPCControllers found in MainMap (which are 0) → clears `_pendingNPCStates = null` and unsubscribes → Later player enters sanctum scene (PrintConsole / etc) via normal scene travel → sceneLoaded fires with no subscription, no pending states → NPCController.Start() on line 53 resets `currentSequenceID = startingSequenceID` → first_meeting plays again. Also, for save files created *during* IntroScene, IntroSequenceController.Start() unconditionally replays intro_prologue because no check against `StoryProgressionManager.IsQuestComplete("intro_complete")`.

Files: SaveLoadManager.ApplySaveData L193-L240, OnSceneLoaded L244-L264, NPCController.Start L51-L60, IntroSequenceController.Start L46-L60.

**Bug-003 (New Game retains loaded game state): CONFIRMED**
Root cause: MainMenuController.OnNewGameClicked only calls SceneManager.LoadScene(IntroScene). It does NOT destroy or reset the DontDestroyOnLoad singletons: StoryProgressionManager, QuestManager, BKTEngine, MissionTabletManager, XPManager, StudentLogManager. The singletons carry the previous game's state into the new playthrough → intro_complete + other quests already marked done → EvaluateActiveQuest jumps into the middle of the chain.

Files: MainMenuController.OnNewGameClicked L56-L59. No call to reset any singleton.

**Bug-004 (Boss encounter zone respawns on save reload): CONFIRMED**
Root cause: ZoneTrigger.cs is a static scene object. On scene load (after save) it is instantiated fresh. OnTriggerEnter for isBossZone only checks XPManager.IsBossUnlocked (XP threshold check), but never checks StoryProgressionManager.HasDefeatedBoss(sanctumID). Thus players can re-fight any boss after reloading, farming infinite xpBoss XP. There is also no Start()/Awake() check to disable the collider if already defeated.

Files: ZoneTrigger.OnTriggerEnter L31-L48, no HasDefeatedBoss guard. No Start() check.

**Bug-005 (Mission not progressing after boss defeat): CONFIRMED 2 SUB-BUGS**
Sub A: QuestManager chain step 3 `{sanctum}_defeat_enemy` (unlocks step 4 restore_crystal) is NEVER marked complete by anything. ZoneTrigger.OnEncounterCompleted only marks `{sanctum}_boss_defeated` on boss zones, and for regular zones only awards XP + destroys. StoryProgressionManager.CompleteQuest never called with `print_console_defeat_enemy` / etc.

Sub B: After boss defeat, the NPC dialogue sequence never advances to `{npc}_after_restore` (the only thing whose questIDToComplete completes `{sanctum}_restore_crystal` step 4). SanctumManager.HandleBossDefeated fires ActivateAllRuneCrystals but does not update any NPC. So NPC stays on mission_reminder forever, restore_crystal quest never fires.

Files: ZoneTrigger.OnEncounterCompleted L108-L158 (no defeat_enemy complete), SanctumManager.BossRewardSequence L276-L303 (no NPC sequence advance), Dialogue.json after_restore sequences with questIDToComplete = sanctum_restore_crystal.

---

## Root Cause Summary
1. `find_printessa` quests have no completion trigger → EvaluateActiveQuest returns Find not Defeat after speak
2. _pendingNPCStates single-shot fires on saved scene, not later sanctum scenes, no re-persist
3. DDOL singletons not reset on New Game
4. ZoneTrigger doesn't exclude already-defeated bosses
5. Two missing pieces: defeat_enemy quest never completed; NPC sequence never advanced to after_restore

---

## Fixes Applied

**Bug-001 (mission name after Printessa)**
File: [Npccontroller.cs TriggerInteraction L109-L153
Before: No find_printessa quest never completes
After: When player talks to NPC, auto-complete `{sanctumID}_find_{npcID} quest via StoryProgressionManager.CompleteQuest before dialogue starts. Then speak_printessa completes normally → EvaluateActiveQuest() skips Find → advances to step 3 Defeat. Mission name now updates.

**Bug-002 (starting sequence after reload)**
3 files:
- [SaveLoadManager.cs](file:///d:/Unity/PyQuest/Assets/Scripts/Story/SaveLoadManager.cs#L193-L295) — NPC restore handler persistent sceneLoaded, matches npc entries one-by-one until all matched, not one-shot.
- [SaveLoadManager.cs](file:///d:/Unity/PyQuest/Assets/Scripts/Story/SaveLoadManager.cs#L209-L213) — replaced the buggy `QuestManager.OnQuestCompleted(data.activeQuestID)` call (was passing active ID to a "this wrong because OnQuestCompleted expects a completed ID) with QuestManager.Instance.EvaluateActiveQuest() after import.
- [Introsequencecontroller.cs](file:///d:/Unity/PyQuest/Assets/Scripts/NPC/Introsequencecontroller.cs#L67-L77) — guard: if `intro_complete` already done, skip prologue entirely and jump MainMap.

**Bug-003 (New Game retains old state)**
2 files:
- [MainMenuController.cs](file:///d:/Unity/PyQuest/Assets/Scripts/UI/MainMenu/MainMenuController.cs#L56-L102) ResetCrossSceneSingletons() destroys DDOL + Reset APIs + ResetAllMastery
- [StudentLogManager.cs](file:///d:/Unity/PyQuest/Assets/Scripts/Student%20Logging/StudentLogManager.cs#L381-L397) ResetLogs()

**Bug-004 (Boss encounter respawn)**
File: [ZoneTrigger.cs](file:///d:/Unity/PyQuest/Assets/Scripts/ZoneTrigger.cs#L32-L67)
Adds:
- Start(): if isBossZone && HasDefeatedBoss(sanctumID) → disable collider + SetActive(false)
- OnTriggerEnter top guard: additional HasDefeatedBoss check

**Bug-005 (Mission not progressing after boss)**
2 sub fixes:
A. [ZoneTrigger.cs](file:///d:/Unity/PyQuest/Assets/Scripts/ZoneTrigger.cs#L158-L170) OnEncounterCompleted non-boss victory marks `{sanctum}_defeat_enemy` step 3 quest complete.
B. [SanctumManager.cs](file:///d:/Unity/PyQuest/Assets/Scripts/Story/SanctumManager.cs#L285-L301) BossRewardSequence → find scene NPCs and set sequence to `{npcID}_after_restore` dialogue (Dialogue.HasSequence) → talking to NPC after boss triggers the quest restore_crystal completes progression.

Supporting public API: QuestManager.EvaluateActiveQuest promoted public so callable.

Diagnostic check: 0 errors 0 warnings.

---

## Verification: Pre vs Post-Fix Comparison
(Pending)
