using System;
using System.Collections.Generic;

/// <summary>
/// Pure data structures for the student analytics log. No Unity
/// dependencies so it serializes cleanly via JsonUtility and can be
/// embedded inside SaveSlotData. List-format only (no Dictionary
/// fields), since JsonUtility cannot deserialize Dictionary types.
/// </summary>
[Serializable]
public class StudentLogData
{
    public string studentID;
    public string sessionStartTime;
    public string sessionEndTime;
    public int totalSessions;
    public float totalPlayTimeHours;

    public List<SanctumLogEntry> sanctumEntries = new List<SanctumLogEntry>();
    public List<SanctumLogEntry> sanctumExits = new List<SanctumLogEntry>();
    public List<BossLogEntry> bossEncounters = new List<BossLogEntry>();
    public List<TabletMissionLogEntry> tabletMissions = new List<TabletMissionLogEntry>();
    public List<EncounterLogEntry> encounters = new List<EncounterLogEntry>();
    public List<PuzzleLogEntry> puzzles = new List<PuzzleLogEntry>();
    public List<XPLogEntry> xpGained = new List<XPLogEntry>();
    public List<LevelUpLogEntry> levelUps = new List<LevelUpLogEntry>();
    public List<StoryLogEntry> storyEvents = new List<StoryLogEntry>();
    public List<QuestLogEntry> questEvents = new List<QuestLogEntry>();
    public List<MasteryLogEntry> masteryUpdates = new List<MasteryLogEntry>();
    public List<KnowledgeComponentSummary> knowledgeComponentSummaries = new List<KnowledgeComponentSummary>();
    public List<ErrorLogEntry> errors = new List<ErrorLogEntry>();

    public int totalEncounters;
    public int totalPuzzlesAttempted;
    public int totalPuzzlesCorrect;
    public float overallAccuracy;
    public int totalDeaths;
    public int totalRestarts;
}

[Serializable]
public class SanctumLogEntry
{
    public string timestamp;
    public string sanctumID;
    public string sanctumName;
    public bool cleared;
    public int missionsCompleted;
}

[Serializable]
public class BossLogEntry
{
    public string timestamp;
    public string sanctumID;
    public string sanctumName;
    public string eventType;
    public int attemptsBeforeSuccess;
    public float fightDurationSeconds;
}

[Serializable]
public class TabletMissionLogEntry
{
    public string timestamp;
    public string sanctumID;
    public string missionID;
    public string missionName;
    public string knowledgeComponent;
    public string puzzleType;
    public bool success;
    public int attempts;
    public float timeSpentSeconds;
}

[Serializable]
public class EncounterLogEntry
{
    public string timestamp;
    public string encounterID;
    public string enemyType;
    public string enemyTier;
    public string sanctumID;
    public bool victory;
    public int puzzlesAttempted;
    public int puzzlesCorrect;
    public float encounterDurationSeconds;
    public int xpAwarded;
    public List<string> knowledgeComponentsTested = new List<string>();
}

[Serializable]
public class PuzzleLogEntry
{
    public string timestamp;
    public string puzzleID;
    public string puzzleType;
    public string knowledgeComponent;
    public string difficulty;
    public bool correct;
    public int attempts;
    public float timeSpentSeconds;
    public string playerAnswer;
    public string correctAnswer;
    public string sanctumID;
    public bool wasTabletMission;
}

[Serializable]
public class XPLogEntry
{
    public string timestamp;
    public int amount;
    public string source;
    public int runningTotal;
    public string sanctumID;
}

[Serializable]
public class LevelUpLogEntry
{
    public string timestamp;
    public int newLevel;
    public int previousLevel;
    public int xpAtLevelUp;
}

[Serializable]
public class StoryLogEntry
{
    public string timestamp;
    public string eventID;
    public string eventType;
    public string sanctumID;
    public string description;
}

[Serializable]
public class QuestLogEntry
{
    public string timestamp;
    public string questID;
    public string eventType;
    public string stageID;
    public string description;
}

[Serializable]
public class MasteryLogEntry
{
    public string timestamp;
    public string knowledgeComponent;
    public double previousMastery;
    public double newMastery;
    public bool correct;
    public string puzzleID;
}

[Serializable]
public class KnowledgeComponentSummary
{
    public string knowledgeComponent;
    public double currentMastery;
    public int totalAttempts;
    public int correctAttempts;
    public double accuracy;
    public string lastUpdated;
}

[Serializable]
public class ErrorLogEntry
{
    public string timestamp;
    public string errorType;
    public string context;
    public string sanctumID;
    public string details;
}