using System.Collections.Generic;
using UnityEngine;

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