using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Runtime analytics logging singleton. DontDestroyOnLoad. Every
/// method is safe to call via null-conditional (Instance?.LogX(...))
/// so the game runs fine if this manager isn't present in a scene.
///
/// ExportLogs() / ImportLogs() bridge to SaveLoadManager for
/// persistence across sessions. Timestamps use ISO 8601 (UTC) so
/// they parse consistently regardless of the machine reading them
/// later during offline pyBKT analysis.
/// </summary>
public class StudentLogManager : MonoBehaviour
{
    public static StudentLogManager Instance { get; private set; }

    [Header("Identity")]
    public string studentID = "unknown_student";

    private StudentLogData data = new StudentLogData();
    private DateTime sessionStart;

    // Duration tracking: keyed by an ID the caller controls
    // (e.g. encounterID, puzzleID) so overlapping calls don't collide.
    private readonly Dictionary<string, DateTime> encounterStartTimes = new Dictionary<string, DateTime>();
    private readonly Dictionary<string, DateTime> puzzleStartTimes = new Dictionary<string, DateTime>();
    private readonly Dictionary<string, DateTime> bossFightStartTimes = new Dictionary<string, DateTime>();

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);

        data.studentID = studentID;
        sessionStart = DateTime.UtcNow;
        data.sessionStartTime = sessionStart.ToString("o");
        data.totalSessions++;
    }

    private static string Now() => DateTime.UtcNow.ToString("o");

    #region Sanctum Entry/Exit

    public void LogSanctumEntry(string sanctumID, string sanctumName)
    {
        data.sanctumEntries.Add(new SanctumLogEntry
        {
            timestamp = Now(),
            sanctumID = sanctumID,
            sanctumName = sanctumName,
            cleared = false,
            missionsCompleted = 0
        });
    }

    public void LogSanctumExit(string sanctumID, string sanctumName, bool cleared)
    {
        data.sanctumExits.Add(new SanctumLogEntry
        {
            timestamp = Now(),
            sanctumID = sanctumID,
            sanctumName = sanctumName,
            cleared = cleared,
            missionsCompleted = 0
        });
    }

    #endregion

    #region Boss Encounters

    public void LogBossEncounterStart(string sanctumID, string sanctumName)
    {
        bossFightStartTimes[sanctumID] = DateTime.UtcNow;
        data.bossEncounters.Add(new BossLogEntry
        {
            timestamp = Now(),
            sanctumID = sanctumID,
            sanctumName = sanctumName,
            eventType = "start",
            attemptsBeforeSuccess = 0,
            fightDurationSeconds = 0f
        });
    }

    public void LogBossUnlocked(string sanctumID, string sanctumName)
    {
        data.bossEncounters.Add(new BossLogEntry
        {
            timestamp = Now(),
            sanctumID = sanctumID,
            sanctumName = sanctumName,
            eventType = "unlocked",
            attemptsBeforeSuccess = 0,
            fightDurationSeconds = 0f
        });
    }

    public void LogBossDefeated(string sanctumID, string sanctumName)
    {
        float duration = 0f;
        if (bossFightStartTimes.TryGetValue(sanctumID, out DateTime start))
        {
            duration = (float)(DateTime.UtcNow - start).TotalSeconds;
            bossFightStartTimes.Remove(sanctumID);
        }

        data.bossEncounters.Add(new BossLogEntry
        {
            timestamp = Now(),
            sanctumID = sanctumID,
            sanctumName = sanctumName,
            eventType = "defeated",
            attemptsBeforeSuccess = 0,
            fightDurationSeconds = duration
        });
    }

    #endregion

    #region Tablet Missions

    public void StartTabletMissionTracking(string missionID)
    {
        puzzleStartTimes[missionID] = DateTime.UtcNow;
    }

    public void LogTabletMissionComplete(string sanctumID, string missionID, string sanctumName)
    {
        float timeSpent = 0f;
        if (puzzleStartTimes.TryGetValue(missionID, out DateTime start))
        {
            timeSpent = (float)(DateTime.UtcNow - start).TotalSeconds;
            puzzleStartTimes.Remove(missionID);
        }

        data.tabletMissions.Add(new TabletMissionLogEntry
        {
            timestamp = Now(),
            sanctumID = sanctumID,
            missionID = missionID,
            missionName = sanctumName,
            knowledgeComponent = "",
            puzzleType = "",
            success = true,
            attempts = 1,
            timeSpentSeconds = timeSpent
        });
    }

    #endregion

    #region Encounters

    public void StartEncounterTracking(string encounterID)
    {
        encounterStartTimes[encounterID] = DateTime.UtcNow;
    }

    public void LogEncounterComplete(string encounterID, string enemyType, string enemyTier,
        string sanctumID, bool victory, int puzzlesAttempted, int puzzlesCorrect, int xpAwarded,
        List<string> kcsTested = null)
    {
        float duration = 0f;
        if (encounterStartTimes.TryGetValue(encounterID, out DateTime start))
        {
            duration = (float)(DateTime.UtcNow - start).TotalSeconds;
            encounterStartTimes.Remove(encounterID);
        }

        data.encounters.Add(new EncounterLogEntry
        {
            timestamp = Now(),
            encounterID = encounterID,
            enemyType = enemyType,
            enemyTier = enemyTier,
            sanctumID = sanctumID,
            victory = victory,
            puzzlesAttempted = puzzlesAttempted,
            puzzlesCorrect = puzzlesCorrect,
            encounterDurationSeconds = duration,
            xpAwarded = xpAwarded,
            knowledgeComponentsTested = kcsTested ?? new List<string>()
        });

        data.totalEncounters++;
    }

    #endregion

    #region Puzzles

    public void StartPuzzleTracking(string puzzleID)
    {
        puzzleStartTimes[puzzleID] = DateTime.UtcNow;
    }

    public void LogPuzzleComplete(string puzzleID, string puzzleType, string knowledgeComponent,
        string difficulty, bool correct, string playerAnswer, string correctAnswer,
        string sanctumID, bool wasTabletMission = false)
    {
        float timeSpent = 0f;
        if (puzzleStartTimes.TryGetValue(puzzleID, out DateTime start))
        {
            timeSpent = (float)(DateTime.UtcNow - start).TotalSeconds;
            puzzleStartTimes.Remove(puzzleID);
        }

        data.puzzles.Add(new PuzzleLogEntry
        {
            timestamp = Now(),
            puzzleID = puzzleID,
            puzzleType = puzzleType,
            knowledgeComponent = knowledgeComponent,
            difficulty = difficulty,
            correct = correct,
            attempts = 1,
            timeSpentSeconds = timeSpent,
            playerAnswer = playerAnswer,
            correctAnswer = correctAnswer,
            sanctumID = sanctumID,
            wasTabletMission = wasTabletMission
        });

        data.totalPuzzlesAttempted++;
        if (correct) data.totalPuzzlesCorrect++;
        data.overallAccuracy = data.totalPuzzlesAttempted > 0
            ? (float)data.totalPuzzlesCorrect / data.totalPuzzlesAttempted
            : 0f;
    }

    #endregion

    #region XP / Level

    public void LogXPGained(int amount, string source, string sanctumID = "")
    {
        int runningTotal = XPManager.Instance != null ? XPManager.Instance.CurrentXP : 0;
        data.xpGained.Add(new XPLogEntry
        {
            timestamp = Now(),
            amount = amount,
            source = source,
            runningTotal = runningTotal,
            sanctumID = sanctumID
        });
    }

    public void LogLevelUp(int newLevel, int previousLevel, int xpAtLevelUp)
    {
        data.levelUps.Add(new LevelUpLogEntry
        {
            timestamp = Now(),
            newLevel = newLevel,
            previousLevel = previousLevel,
            xpAtLevelUp = xpAtLevelUp
        });
    }

    #endregion

    #region Story / Quest

    public void LogStoryEvent(string eventID, string eventType, string description, string sanctumID = "")
    {
        data.storyEvents.Add(new StoryLogEntry
        {
            timestamp = Now(),
            eventID = eventID,
            eventType = eventType,
            sanctumID = sanctumID,
            description = description
        });
    }

    public void LogQuestEvent(string questID, string eventType, string stageID, string description)
    {
        data.questEvents.Add(new QuestLogEntry
        {
            timestamp = Now(),
            questID = questID,
            eventType = eventType,
            stageID = stageID,
            description = description
        });
    }

    #endregion

    #region Mastery

    public void LogMasteryUpdate(string knowledgeComponent, double previousMastery,
        double newMastery, bool correct, string puzzleID = "")
    {
        data.masteryUpdates.Add(new MasteryLogEntry
        {
            timestamp = Now(),
            knowledgeComponent = knowledgeComponent,
            previousMastery = previousMastery,
            newMastery = newMastery,
            correct = correct,
            puzzleID = puzzleID
        });

        UpdateKCSummary(knowledgeComponent, newMastery, correct);
    }

    private void UpdateKCSummary(string kc, double currentMastery, bool correct)
    {
        KnowledgeComponentSummary summary = data.knowledgeComponentSummaries.Find(s => s.knowledgeComponent == kc);
        if (summary == null)
        {
            summary = new KnowledgeComponentSummary { knowledgeComponent = kc };
            data.knowledgeComponentSummaries.Add(summary);
        }

        summary.currentMastery = currentMastery;
        summary.totalAttempts++;
        if (correct) summary.correctAttempts++;
        summary.accuracy = summary.totalAttempts > 0
            ? (double)summary.correctAttempts / summary.totalAttempts
            : 0.0;
        summary.lastUpdated = Now();
    }

    #endregion

    #region Errors

    public void LogError(string errorType, string context, string sanctumID = "", string details = "")
    {
        data.errors.Add(new ErrorLogEntry
        {
            timestamp = Now(),
            errorType = errorType,
            context = context,
            sanctumID = sanctumID,
            details = details
        });
    }

    #endregion

    #region Save Bridge

    public StudentLogData ExportLogs()
    {
        data.sessionEndTime = Now();
        data.totalPlayTimeHours += (float)(DateTime.UtcNow - sessionStart).TotalHours;
        return data;
    }

    /// <summary>
    /// Total play time across all sessions plus the current
    /// in-progress session, as a TimeSpan for display formatting.
    /// </summary>
    public TimeSpan GetTotalPlayTime()
    {
        double totalSeconds = (data.totalPlayTimeHours * 3600.0)
            + (DateTime.UtcNow - sessionStart).TotalSeconds;
        return TimeSpan.FromSeconds(totalSeconds);
    }

    public void ImportLogs(StudentLogData imported)
    {
        if (imported == null) return;
        data = imported;
        data.studentID = studentID;
        sessionStart = DateTime.UtcNow;
        data.sessionStartTime = sessionStart.ToString("o");
        data.totalSessions++;
    }

    /// <summary>
    /// Resets all runtime analytics back to a blank first-session state.
    /// Called by MainMenuController when the user starts a New Game so
    /// a previously-loaded save's logs don't bleed into the new run.
    /// </summary>
    public void ResetLogs()
    {
        data = new StudentLogData();
        data.studentID = studentID;
        encounterStartTimes.Clear();
        puzzleStartTimes.Clear();
        bossFightStartTimes.Clear();
        sessionStart = DateTime.UtcNow;
        data.sessionStartTime = sessionStart.ToString("o");
        data.totalSessions = 1;
        Debug.Log("[StudentLogManager] Logs reset for New Game.");
    }

    #endregion
}