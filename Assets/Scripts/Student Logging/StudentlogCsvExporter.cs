using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Flattens StudentLogData (13 differently-shaped lists) into one CSV
/// file, one row per logged event, tagged by a "record_type" column.
/// Filter by record_type in Excel/Sheets to isolate any one table.
///
/// One file, not one-per-list, because the Google Form file-upload
/// question takes a single attachment and this is the evidence file
/// tying a student's log data to their questionnaire response.
///
/// No external dependency. Every field here is a string, bool, int,
/// float, or double, plain CSV escaping is enough, nothing here needs
/// a real CSV library.
/// </summary>
public static class StudentLogCsvExporter
{
    private static readonly string[] Columns = new[]
    {
        "record_type", "timestamp",
        // session-level (one row, record_type = "session_summary")
        "student_id", "session_start_time", "session_end_time",
        "total_sessions", "total_play_time_hours", "total_encounters",
        "total_puzzles_attempted", "total_puzzles_correct", "overall_accuracy",
        "total_deaths", "total_restarts",
        // shared across most event types
        "sanctum_id", "sanctum_name", "knowledge_component", "description",
        // sanctum entry/exit
        "cleared", "missions_completed",
        // boss
        "event_type", "attempts_before_success", "fight_duration_seconds",
        // tablet mission
        "mission_id", "mission_name", "puzzle_type", "success", "attempts",
        "time_spent_seconds",
        // encounter
        "encounter_id", "enemy_type", "enemy_tier", "victory",
        "puzzles_attempted", "puzzles_correct", "encounter_duration_seconds",
        "xp_awarded", "kcs_tested",
        // puzzle
        "puzzle_id", "difficulty", "correct", "player_answer",
        "correct_answer", "was_tablet_mission",
        // xp
        "xp_amount", "xp_source", "xp_running_total",
        // level up
        "new_level", "previous_level", "xp_at_level_up",
        // story / quest
        "event_id", "quest_id", "stage_id",
        // mastery
        "previous_mastery", "new_mastery",
        // kc summary
        "current_mastery", "total_attempts", "correct_attempts", "accuracy",
        "last_updated",
        // error
        "error_type", "context", "details"
    };

    /// <summary>
    /// Writes data to a CSV at outputPath (or a default persistentDataPath
    /// filename if null) and returns the path actually written to.
    /// </summary>
    public static string Export(StudentLogData data, string outputPath = null)
    {
        if (data == null)
        {
            Debug.LogError("[StudentLogCsvExporter] Export called with null StudentLogData.");
            return null;
        }

        if (string.IsNullOrEmpty(outputPath))
        {
            string safeID = string.IsNullOrEmpty(data.studentID) ? "unknown" : Sanitize(data.studentID);
            string fileName = $"pyquest_log_{safeID}_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
            outputPath = Path.Combine(Application.persistentDataPath, fileName);
        }

        var rows = new List<Dictionary<string, string>>();

        rows.Add(Row("session_summary", timestamp: data.sessionEndTime, extra: new Dictionary<string, string>
        {
            ["student_id"] = data.studentID,
            ["session_start_time"] = data.sessionStartTime,
            ["session_end_time"] = data.sessionEndTime,
            ["total_sessions"] = data.totalSessions.ToString(),
            ["total_play_time_hours"] = data.totalPlayTimeHours.ToString("F3"),
            ["total_encounters"] = data.totalEncounters.ToString(),
            ["total_puzzles_attempted"] = data.totalPuzzlesAttempted.ToString(),
            ["total_puzzles_correct"] = data.totalPuzzlesCorrect.ToString(),
            ["overall_accuracy"] = data.overallAccuracy.ToString("F4"),
            ["total_deaths"] = data.totalDeaths.ToString(),
            ["total_restarts"] = data.totalRestarts.ToString(),
        }));

        foreach (var e in data.sanctumEntries)
            rows.Add(Row("sanctum_entry", e.timestamp, new Dictionary<string, string>
            {
                ["sanctum_id"] = e.sanctumID,
                ["sanctum_name"] = e.sanctumName,
                ["cleared"] = Bool(e.cleared),
                ["missions_completed"] = e.missionsCompleted.ToString(),
            }));

        foreach (var e in data.sanctumExits)
            rows.Add(Row("sanctum_exit", e.timestamp, new Dictionary<string, string>
            {
                ["sanctum_id"] = e.sanctumID,
                ["sanctum_name"] = e.sanctumName,
                ["cleared"] = Bool(e.cleared),
                ["missions_completed"] = e.missionsCompleted.ToString(),
            }));

        foreach (var e in data.bossEncounters)
            rows.Add(Row("boss_" + (string.IsNullOrEmpty(e.eventType) ? "event" : e.eventType), e.timestamp, new Dictionary<string, string>
            {
                ["sanctum_id"] = e.sanctumID,
                ["sanctum_name"] = e.sanctumName,
                ["event_type"] = e.eventType,
                ["attempts_before_success"] = e.attemptsBeforeSuccess.ToString(),
                ["fight_duration_seconds"] = e.fightDurationSeconds.ToString("F2"),
            }));

        foreach (var e in data.tabletMissions)
            rows.Add(Row("tablet_mission", e.timestamp, new Dictionary<string, string>
            {
                ["sanctum_id"] = e.sanctumID,
                ["mission_id"] = e.missionID,
                ["mission_name"] = e.missionName,
                ["knowledge_component"] = e.knowledgeComponent,
                ["puzzle_type"] = e.puzzleType,
                ["success"] = Bool(e.success),
                ["attempts"] = e.attempts.ToString(),
                ["time_spent_seconds"] = e.timeSpentSeconds.ToString("F2"),
            }));

        foreach (var e in data.lessonTablets)
            rows.Add(Row("lesson_tablet", e.timestamp, new Dictionary<string, string>
            {
                ["sanctum_id"] = e.sanctumID,
                ["knowledge_component"] = e.knowledgeComponent,
                ["time_spent_seconds"] = e.timeSpentSeconds.ToString("F2"),
            }));

        foreach (var e in data.encounters)
            rows.Add(Row("encounter", e.timestamp, new Dictionary<string, string>
            {
                ["encounter_id"] = e.encounterID,
                ["enemy_type"] = e.enemyType,
                ["enemy_tier"] = e.enemyTier,
                ["sanctum_id"] = e.sanctumID,
                ["victory"] = Bool(e.victory),
                ["puzzles_attempted"] = e.puzzlesAttempted.ToString(),
                ["puzzles_correct"] = e.puzzlesCorrect.ToString(),
                ["encounter_duration_seconds"] = e.encounterDurationSeconds.ToString("F2"),
                ["xp_awarded"] = e.xpAwarded.ToString(),
                ["kcs_tested"] = e.knowledgeComponentsTested != null ? string.Join("|", e.knowledgeComponentsTested) : "",
            }));

        foreach (var e in data.puzzles)
            rows.Add(Row("puzzle", e.timestamp, new Dictionary<string, string>
            {
                ["puzzle_id"] = e.puzzleID,
                ["puzzle_type"] = e.puzzleType,
                ["knowledge_component"] = e.knowledgeComponent,
                ["difficulty"] = e.difficulty,
                ["correct"] = Bool(e.correct),
                ["attempts"] = e.attempts.ToString(),
                ["time_spent_seconds"] = e.timeSpentSeconds.ToString("F2"),
                ["player_answer"] = e.playerAnswer,
                ["correct_answer"] = e.correctAnswer,
                ["sanctum_id"] = e.sanctumID,
                ["was_tablet_mission"] = Bool(e.wasTabletMission),
            }));

        foreach (var e in data.xpGained)
            rows.Add(Row("xp_gained", e.timestamp, new Dictionary<string, string>
            {
                ["xp_amount"] = e.amount.ToString(),
                ["xp_source"] = e.source,
                ["xp_running_total"] = e.runningTotal.ToString(),
                ["sanctum_id"] = e.sanctumID,
            }));

        foreach (var e in data.levelUps)
            rows.Add(Row("level_up", e.timestamp, new Dictionary<string, string>
            {
                ["new_level"] = e.newLevel.ToString(),
                ["previous_level"] = e.previousLevel.ToString(),
                ["xp_at_level_up"] = e.xpAtLevelUp.ToString(),
            }));

        foreach (var e in data.storyEvents)
            rows.Add(Row("story_" + (string.IsNullOrEmpty(e.eventType) ? "event" : e.eventType), e.timestamp, new Dictionary<string, string>
            {
                ["event_id"] = e.eventID,
                ["event_type"] = e.eventType,
                ["sanctum_id"] = e.sanctumID,
                ["description"] = e.description,
            }));

        foreach (var e in data.questEvents)
            rows.Add(Row("quest_" + (string.IsNullOrEmpty(e.eventType) ? "event" : e.eventType), e.timestamp, new Dictionary<string, string>
            {
                ["quest_id"] = e.questID,
                ["event_type"] = e.eventType,
                ["stage_id"] = e.stageID,
                ["description"] = e.description,
            }));

        foreach (var e in data.masteryUpdates)
            rows.Add(Row("mastery_update", e.timestamp, new Dictionary<string, string>
            {
                ["knowledge_component"] = e.knowledgeComponent,
                ["previous_mastery"] = e.previousMastery.ToString("F4"),
                ["new_mastery"] = e.newMastery.ToString("F4"),
                ["correct"] = Bool(e.correct),
                ["puzzle_id"] = e.puzzleID,
            }));

        foreach (var e in data.knowledgeComponentSummaries)
            rows.Add(Row("kc_summary", e.lastUpdated, new Dictionary<string, string>
            {
                ["knowledge_component"] = e.knowledgeComponent,
                ["current_mastery"] = e.currentMastery.ToString("F4"),
                ["total_attempts"] = e.totalAttempts.ToString(),
                ["correct_attempts"] = e.correctAttempts.ToString(),
                ["accuracy"] = e.accuracy.ToString("F4"),
                ["last_updated"] = e.lastUpdated,
            }));

        foreach (var e in data.errors)
            rows.Add(Row("error", e.timestamp, new Dictionary<string, string>
            {
                ["error_type"] = e.errorType,
                ["context"] = e.context,
                ["sanctum_id"] = e.sanctumID,
                ["details"] = e.details,
            }));

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", Columns));
            foreach (var row in rows)
                sb.AppendLine(string.Join(",", Columns.Select(c => Csv(row.TryGetValue(c, out var v) ? v : ""))));

            File.WriteAllText(outputPath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[StudentLogCsvExporter] Wrote {rows.Count} rows to: {outputPath}");
            return outputPath;
        }
        catch (Exception e)
        {
            Debug.LogError($"[StudentLogCsvExporter] Failed to write CSV: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Converts an existing save file directly, no Play Mode needed.
    /// Useful for batch-converting save files you already collected
    /// during earlier testing, before this exporter existed.
    /// </summary>
    public static string ExportFromSaveFile(string saveFilePath, string outputCsvPath = null)
    {
        if (!File.Exists(saveFilePath))
        {
            Debug.LogError($"[StudentLogCsvExporter] Save file not found: {saveFilePath}");
            return null;
        }

        string json = File.ReadAllText(saveFilePath);
        SaveSlotData slot = JsonUtility.FromJson<SaveSlotData>(json);

        if (slot?.studentLogs == null)
        {
            Debug.LogWarning($"[StudentLogCsvExporter] Save file has no studentLogs data: {saveFilePath}");
            return null;
        }

        return Export(slot.studentLogs, outputCsvPath);
    }

    private static Dictionary<string, string> Row(string recordType, string timestamp, Dictionary<string, string> extra)
    {
        var row = new Dictionary<string, string> { ["record_type"] = recordType, ["timestamp"] = timestamp ?? "" };
        foreach (var kv in extra)
            row[kv.Key] = kv.Value ?? "";
        return row;
    }

    private static string Bool(bool b) => b ? "1" : "0";

    private static string Csv(string field)
    {
        if (string.IsNullOrEmpty(field)) return "";
        if (field.Contains(",") || field.Contains("\"") || field.Contains("\n"))
            return "\"" + field.Replace("\"", "\"\"") + "\"";
        return field;
    }

    private static string Sanitize(string s)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            s = s.Replace(c, '_');
        return s;
    }
}