using System;
using System.Collections.Generic;
using System.IO;
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

    [Header("Debug")]
    [Tooltip("Prints a line to the Console every time any Log* method is called, so you can watch data come in while testing. Turn off for the actual student evaluation build, it's noisy.")]
    public bool enableDebugLogging = true;

    private StudentLogData data = new StudentLogData();
    private DateTime sessionStart;
    private DateTime lastPlayTimeFlush;

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
        lastPlayTimeFlush = sessionStart;
        data.sessionStartTime = sessionStart.ToString("o");
        data.totalSessions++;
    }

    private static string Now() => DateTime.UtcNow.ToString("o");

    private void DebugLog(string message)
    {
        if (enableDebugLogging)
            Debug.Log($"[StudentLogManager] {message}");
    }

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
        DebugLog($"Sanctum entry: {sanctumName} ({sanctumID})");
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
        DebugLog($"Sanctum exit: {sanctumName} ({sanctumID}), cleared={cleared}");
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
        DebugLog($"Boss encounter started: {sanctumName} ({sanctumID})");
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
        DebugLog($"Boss unlocked: {sanctumName} ({sanctumID})");
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
        DebugLog($"Boss defeated: {sanctumName} ({sanctumID}), fight duration={duration:F1}s");
    }

    #endregion

    #region Tablet Missions

    public void StartTabletMissionTracking(string missionID)
    {
        puzzleStartTimes[missionID] = DateTime.UtcNow;
        DebugLog($"Tablet mission tracking started: {missionID}");
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
        DebugLog($"Tablet mission complete: {missionID} in {sanctumName} ({sanctumID}), time={timeSpent:F1}s");
    }

    #endregion

    #region Encounters

    public void StartEncounterTracking(string encounterID)
    {
        encounterStartTimes[encounterID] = DateTime.UtcNow;
        DebugLog($"Encounter tracking started: {encounterID}");
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
        DebugLog($"Encounter complete: {encounterID} ({enemyType}/{enemyTier}) in {sanctumID}, victory={victory}, puzzles={puzzlesCorrect}/{puzzlesAttempted}, xp={xpAwarded}, duration={duration:F1}s. Total encounters logged: {data.totalEncounters}");
    }

    #endregion

    #region Puzzles

    public void StartPuzzleTracking(string puzzleID)
    {
        puzzleStartTimes[puzzleID] = DateTime.UtcNow;
        DebugLog($"Puzzle tracking started: {puzzleID}");
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
        DebugLog($"Puzzle complete: {puzzleID} ({puzzleType}/{knowledgeComponent}/{difficulty}), correct={correct}, time={timeSpent:F1}s. Running accuracy: {data.totalPuzzlesCorrect}/{data.totalPuzzlesAttempted} ({data.overallAccuracy:P0})");
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
        DebugLog($"XP gained: +{amount} ({source}) in {sanctumID}. Running total: {runningTotal}");
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
        DebugLog($"Level up: {previousLevel} -> {newLevel} at {xpAtLevelUp} XP");
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
        DebugLog($"Story event: {eventID} ({eventType}) in '{sanctumID}': {description}");
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
        DebugLog($"Quest event: {questID} ({eventType}): {description}");
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
        DebugLog($"Mastery update: {knowledgeComponent} {previousMastery:F3} -> {newMastery:F3} (correct={correct}, puzzleID={puzzleID})");
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
        Debug.LogWarning($"[StudentLogManager] Error logged: {errorType} in '{context}' ({sanctumID}): {details}");
    }

    #endregion

    #region Save Bridge

    public StudentLogData ExportLogs()
    {
        DateTime now = DateTime.UtcNow;
        // Elapsed since the LAST flush, not since sessionStart. Calling
        // this more than once per session (which happens the moment
        // this is wired to fire on every manual save) used to add the
        // full session-elapsed time again on every call, compounding
        // on top of what a previous call already added. This adds only
        // the incremental time since the last flush, so it's safe to
        // call as often as you want.
        data.totalPlayTimeHours += (float)(now - lastPlayTimeFlush).TotalHours;
        lastPlayTimeFlush = now;
        data.sessionEndTime = Now();
        return data;
    }

    /// <summary>
    /// Read-only snapshot of the current in-progress session, does NOT
    /// stamp sessionEndTime or add to totalPlayTimeHours the way
    /// ExportLogs() does. Use this for any mid-session peek (CSV export,
    /// debug tooling, etc.) so calling it doesn't corrupt the numbers
    /// ExportLogs() will commit later when the session actually ends.
    /// </summary>
    public StudentLogData PeekCurrentData() => data;

    /// <summary>
    /// Total play time across all sessions plus time since the last
    /// flush, as a TimeSpan for display formatting. Uses
    /// lastPlayTimeFlush, not sessionStart, so this stays correct even
    /// after ExportLogs() has already flushed part of the current
    /// session into totalPlayTimeHours (e.g. from an earlier manual
    /// save in the same session).
    /// </summary>
    public TimeSpan GetTotalPlayTime()
    {
        double totalSeconds = (data.totalPlayTimeHours * 3600.0)
            + (DateTime.UtcNow - lastPlayTimeFlush).TotalSeconds;
        return TimeSpan.FromSeconds(totalSeconds);
    }

    public void ImportLogs(StudentLogData imported)
    {
        if (imported == null) return;
        data = imported;
        data.studentID = studentID;
        sessionStart = DateTime.UtcNow;
        lastPlayTimeFlush = sessionStart;
        data.sessionStartTime = sessionStart.ToString("o");
        data.totalSessions++;
    }

    #endregion

    #region Debug

    /// <summary>
    /// Dumps running totals to the Console in one block instead of
    /// scrolling back through individual log lines. Call this from a
    /// debug button, the pause menu, or just press F9 in a dev build
    /// (see Update() below).
    /// </summary>
    public void PrintSessionSummary()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("========== StudentLogManager Session Summary ==========");
        sb.AppendLine($"Student ID: {data.studentID}");
        sb.AppendLine($"Session start: {data.sessionStartTime}");
        sb.AppendLine($"Total sessions (incl. this one): {data.totalSessions}");
        sb.AppendLine($"Sanctum entries logged: {data.sanctumEntries.Count}");
        sb.AppendLine($"Sanctum exits logged: {data.sanctumExits.Count}");
        sb.AppendLine($"Boss encounter events logged: {data.bossEncounters.Count}");
        sb.AppendLine($"Tablet missions logged: {data.tabletMissions.Count}");
        sb.AppendLine($"Encounters logged: {data.encounters.Count} (totalEncounters counter: {data.totalEncounters})");
        sb.AppendLine($"Puzzles logged: {data.puzzles.Count} (attempted={data.totalPuzzlesAttempted}, correct={data.totalPuzzlesCorrect}, accuracy={data.overallAccuracy:P1})");
        sb.AppendLine($"XP events logged: {data.xpGained.Count}");
        sb.AppendLine($"Level-up events logged: {data.levelUps.Count}");
        sb.AppendLine($"Story events logged: {data.storyEvents.Count}");
        sb.AppendLine($"Quest events logged: {data.questEvents.Count}");
        sb.AppendLine($"Mastery updates logged: {data.masteryUpdates.Count}");
        sb.AppendLine($"Known knowledge components: {data.knowledgeComponentSummaries.Count}");
        foreach (var kc in data.knowledgeComponentSummaries)
            sb.AppendLine($"    {kc.knowledgeComponent}: mastery={kc.currentMastery:F3}, accuracy={kc.accuracy:P0} ({kc.correctAttempts}/{kc.totalAttempts})");
        sb.AppendLine($"Errors logged: {data.errors.Count}");
        sb.AppendLine($"Currently open tracking timers: {encounterStartTimes.Count} encounter(s), {puzzleStartTimes.Count} puzzle/mission(s), {bossFightStartTimes.Count} boss fight(s)");
        if (encounterStartTimes.Count > 0 || puzzleStartTimes.Count > 0 || bossFightStartTimes.Count > 0)
            sb.AppendLine("    (Non-zero here after gameplay has settled usually means a Start*Tracking call has no matching Log*Complete call, an unclosed timer.)");
        sb.AppendLine("=========================================================");
        Debug.Log(sb.ToString());
    }

    [Tooltip("Dev-build convenience: press this key at any time to dump PrintSessionSummary() to the Console. Set to None to disable.")]
    public KeyCode summaryHotkey = KeyCode.F9;

    [Tooltip("Dev-build convenience: press this key to export the current session's data to a CSV file right now, without waiting for a save. Set to None to disable.")]
    public KeyCode csvExportHotkey = KeyCode.F10;

    void Update()
    {
        if (summaryHotkey != KeyCode.None && Input.GetKeyDown(summaryHotkey))
            PrintSessionSummary();

        if (csvExportHotkey != KeyCode.None && Input.GetKeyDown(csvExportHotkey))
            ExportCurrentSessionToCsv();
    }

    /// <summary>
    /// Exports everything logged so far in THIS session to a CSV file.
    /// Does not wait for a save, this is separate from SaveLoadManager's
    /// JSON persistence, meant for pulling a Google-Form-ready file at
    /// any point, including mid-session during testing.
    /// </summary>
    public string ExportCurrentSessionToCsv(string outputPath = null)
    {
        StudentLogData snapshot = PeekCurrentData();
        string path = StudentLogCsvExporter.Export(snapshot, outputPath);
        if (path != null)
            DebugLog($"Session exported to CSV: {path}");
        return path;
    }

    /// <summary>
    /// The canonical, single, per-student CSV. Always the same
    /// filename, always fully overwritten, never appended to. Call this
    /// from SaveLoadManager on every MANUAL save (slot != 0), not on
    /// autosave. Overwriting instead of appending is what guarantees no
    /// duplicate rows across repeated saves, each call writes the
    /// complete current truth, not a delta on top of the last write.
    /// </summary>
    public string ExportCanonicalCsv()
    {
        string safeID = string.IsNullOrEmpty(studentID) ? "unknown_student" : SanitizeFileName(studentID);
        string fixedPath = Path.Combine(Application.persistentDataPath, $"pyquest_log_{safeID}.csv");
        return ExportCurrentSessionToCsv(fixedPath);
    }

    private string SanitizeFileName(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }

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