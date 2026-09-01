using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Tracks named "blockers" that mark the game as unsafe to save
/// (encounters, puzzles, dialogue, scene transitions). Safe to save
/// only when zero blockers are active.
///
/// DontDestroyOnLoad singleton. Any system starting a state that
/// should block saving calls AddBlocker("some_reason") and
/// RemoveBlocker("some_reason") when that state ends. Using string
/// keys (rather than a single bool) means overlapping blockers from
/// different systems can't accidentally clear each other early.
/// </summary>
public class SaveRestrictionEnforcer : MonoBehaviour
{
    public static SaveRestrictionEnforcer Instance { get; private set; }

    private readonly HashSet<string> activeBlockers = new HashSet<string>();

    public bool IsSafeToSave => activeBlockers.Count == 0;

    void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        DontDestroyOnLoad(gameObject);
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    void OnDestroy()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
    }

    // Runs on EVERY scene load, regardless of what triggered it
    // (SceneTransition, SaveLoadManager.ApplySaveData, SanctumManager,
    // etc.), so "scene_transition" can't get stuck active after the
    // GameObject that set it is gone. Only clears that one key; other
    // blockers (encounter, sanctum_entry, boss_fight, ...) are untouched
    // and still managed by whichever system owns them.
    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        RemoveBlocker("scene_transition");
    }

    public void AddBlocker(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return;
        activeBlockers.Add(reason);
    }

    public void RemoveBlocker(string reason)
    {
        if (string.IsNullOrEmpty(reason)) return;
        activeBlockers.Remove(reason);
    }

    /// <summary>
    /// Escape hatch for scene loads or crashes that might leave a
    /// blocker stuck. Call on scene entry if you need a clean slate.
    /// </summary>
    public void ClearAllBlockers()
    {
        activeBlockers.Clear();
    }

    public List<string> GetActiveBlockers() => new List<string>(activeBlockers);
}