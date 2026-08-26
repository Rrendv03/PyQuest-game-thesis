using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// UI panel for viewing student logs and learning analytics.
/// Accessible from pause menu or sanctum tablet.
/// </summary>
public class LogUI : MonoBehaviour
{
    [Header("UI References")]
    public GameObject logPanel;
    public Transform contentParent;
    public GameObject logEntryPrefab;
    public GameObject categoryButtonPrefab;
    public Transform categoryParent;

    [Header("Detail View")]
    public Text detailTitle;
    public Text detailContent;
    public ScrollRect scrollRect;

    [Header("Summary")]
    public Text summaryText;
    public Image accuracyBar;

    private string currentCategory = "all";

    public enum LogCategory
    {
        All, Encounters, Puzzles, Bosses, Missions,
        XP, Story, Mastery, Errors
    }

    private void Start()
    {
        if (logPanel != null) logPanel.SetActive(false);
    }

    public void ShowLogPanel()
    {
        if (logPanel == null) return;

        logPanel.SetActive(true);
        RefreshLogs();

        // Block save while viewing
        SaveRestrictionEnforcer.Instance?.AddBlocker("log_ui");

        // Pause
        Time.timeScale = 0f;
    }

    public void HideLogPanel()
    {
        if (logPanel == null) return;

        logPanel.SetActive(false);

        // Unblock save
        SaveRestrictionEnforcer.Instance?.RemoveBlocker("log_ui");

        // Unpause
        Time.timeScale = 1f;
    }

    public void SetCategory(int categoryIndex)
    {
        currentCategory = ((LogCategory)categoryIndex).ToString().ToLower();
        RefreshLogs();
    }

    private void RefreshLogs()
    {
        if (StudentLogManager.Instance == null) return;

        // Clear existing
        foreach (Transform child in contentParent)
            Destroy(child.gameObject);

        var logs = StudentLogManager.Instance.ExportLogs();
        if (logs == null) return;

        // Update summary
        UpdateSummary(logs);

        // Filter and display
        DisplayFilteredLogs(logs);
    }

    private void UpdateSummary(StudentLogData logs)
    {
        if (summaryText == null) return;

        var playTime = StudentLogManager.Instance.GetTotalPlayTime();

        string summary = $"Play Time: {playTime.Hours}h {playTime.Minutes}m\n" +
                        $"Encounters: {logs.totalEncounters}\n" +
                        $"Puzzles: {logs.totalPuzzlesCorrect}/{logs.totalPuzzlesAttempted}\n" +
                        $"Accuracy: {logs.overallAccuracy:P1}\n" +
                        $"Deaths: {logs.totalDeaths}\n" +
                        $"Sanctums Cleared: {logs.sanctumExits.Count(e => e.cleared)}";

        summaryText.text = summary;

        if (accuracyBar != null)
            accuracyBar.fillAmount = logs.overallAccuracy;
    }

    private void DisplayFilteredLogs(StudentLogData logs)
    {
        List<string> entries = new List<string>();

        switch (currentCategory)
        {
            case "all":
                AddSanctumEntries(logs, entries);
                AddBossEntries(logs, entries);
                AddMissionEntries(logs, entries);
                break;
            case "encounters":
                AddEncounterEntries(logs, entries);
                break;
            case "puzzles":
                AddPuzzleEntries(logs, entries);
                break;
            case "bosses":
                AddBossEntries(logs, entries);
                break;
            case "missions":
                AddMissionEntries(logs, entries);
                break;
            case "xp":
                AddXPEntries(logs, entries);
                break;
            case "story":
                AddStoryEntries(logs, entries);
                break;
            case "mastery":
                AddMasteryEntries(logs, entries);
                break;
            case "errors":
                AddErrorEntries(logs, entries);
                break;
        }

        // Create UI entries
        foreach (var entry in entries)
        {
            GameObject go = Instantiate(logEntryPrefab, contentParent);
            Text text = go.GetComponentInChildren<Text>();
            if (text != null) text.text = entry;
        }
    }

    private void AddSanctumEntries(StudentLogData logs, List<string> entries)
    {
        foreach (var e in logs.sanctumEntries)
            entries.Add($"[Enter] {e.sanctumName} at {e.timestamp}");
        foreach (var e in logs.sanctumExits)
            entries.Add($"[Exit] {e.sanctumName} - Cleared: {e.cleared}");
    }

    private void AddBossEntries(StudentLogData logs, List<string> entries)
    {
        foreach (var e in logs.bossEncounters)
        {
            string status = e.eventType switch
            {
                "defeated" => $"DEFEATED (attempts: {e.attemptsBeforeSuccess})",
                "failed" => "FAILED",
                _ => e.eventType.ToUpper()
            };
            entries.Add($"[Boss] {e.sanctumName} - {status}");
        }
    }

    private void AddMissionEntries(StudentLogData logs, List<string> entries)
    {
        foreach (var e in logs.tabletMissions)
            entries.Add($"[Mission] {e.missionName} - {(e.success ? "COMPLETE" : "FAILED")} in {e.timeSpentSeconds:F1}s");
    }

    private void AddEncounterEntries(StudentLogData logs, List<string> entries)
    {
        foreach (var e in logs.encounters)
            entries.Add($"[Combat] {e.enemyType} - {(e.victory ? "WIN" : "LOSS")} | XP: {e.xpAwarded}");
    }

    private void AddPuzzleEntries(StudentLogData logs, List<string> entries)
    {
        foreach (var e in logs.puzzles)
            entries.Add($"[Puzzle] {e.puzzleType} [{e.knowledgeComponent}] - {(e.correct ? "CORRECT" : "WRONG")} ({e.timeSpentSeconds:F1}s)");
    }

    private void AddXPEntries(StudentLogData logs, List<string> entries)
    {
        foreach (var e in logs.xpGained)
            entries.Add($"[XP] +{e.amount} from {e.source} (total: {e.runningTotal})");
        foreach (var e in logs.levelUps)
            entries.Add($"[LEVEL UP] {e.previousLevel} -> {e.newLevel}!");
    }

    private void AddStoryEntries(StudentLogData logs, List<string> entries)
    {
        foreach (var e in logs.storyEvents)
            entries.Add($"[Story] {e.eventType}: {e.description}");
    }

    private void AddMasteryEntries(StudentLogData logs, List<string> entries)
    {
        foreach (var e in logs.knowledgeComponentSummaries)
            entries.Add($"[KC] {e.knowledgeComponent}: {e.currentMastery:P1} mastery ({e.accuracy:P1} accuracy, {e.totalAttempts} attempts)");
    }

    private void AddErrorEntries(StudentLogData logs, List<string> entries)
    {
        foreach (var e in logs.errors)
            entries.Add($"[Error] {e.errorType}: {e.details}");
    }
}