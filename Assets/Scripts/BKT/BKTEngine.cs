using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.SceneManagement;

public class BKTEngine : MonoBehaviour
{
    public static BKTEngine Instance;

    private Dictionary<string, float> masteryProbabilities = new Dictionary<string, float>();
    private Dictionary<string, KnowledgeComponent> parameters = new Dictionary<string, KnowledgeComponent>();

    void Awake()
    {
        if (Instance == null)
        {
            Instance = this;
            DontDestroyOnLoad(gameObject);
            LoadParameters();
        }
        else Destroy(gameObject);
    }

    // Update is called once per frame
    void LoadParameters()
    {
        string path = Path.Combine(Application.streamingAssetsPath, "bkt_params.json");
        if ( !File.Exists(path)) { Debug.LogError("[BKT] bkt_params.json not found at: " + path); return; }

        string json = File.ReadAllText(path);
        BKTParamWrapper wrapper = JsonUtility.FromJson<BKTParamWrapper>(json);

        foreach (var kc in wrapper.components)
        {
            parameters[kc.name] = kc;
            masteryProbabilities[kc.name] = kc.p_init;
            Debug.Log($"[BKT] Loaded parameters for {kc.name}: p_init={kc.p_init}, p_transit={kc.p_transit}, p_guess={kc.p_guess}, p_slip={kc.p_slip}, mastery_threshold={kc.mastery_threshold}");
        }
    }

    /// Call after every puzzle attempt. Returns updated P(L).
    /// pGuessOverride: pass the actual guess probability for the puzzle
    /// format that was just answered (1 / option count). If null, falls
    /// back to the static p_guess from bkt_params.json.
    public float UpdateMastery(string componentName, bool isCorrect, float? pGuessOverride = null)
    {
        if (!parameters.ContainsKey(componentName)) return 0f;

        KnowledgeComponent kc = parameters[componentName];
        float pL = masteryProbabilities[componentName];

        float effectivePGuess = pGuessOverride ?? kc.p_guess;

        // Bayesian update
        float pLGivenObs;
        if (isCorrect)
        {
            float numerator = pL * (1 - kc.p_slip);
            float denominator = numerator + (1 - pL) * effectivePGuess;
            pLGivenObs = numerator / denominator;
        }
        else
        {
            float numerator = pL * kc.p_slip;
            float denominator = numerator + (1 - pL) * (1 - effectivePGuess);
            pLGivenObs = numerator / denominator;
        }

        // Learning transition
        float newPL = pLGivenObs + (1f - pLGivenObs) * kc.p_transit;
        newPL = Mathf.Clamp01(newPL);
        masteryProbabilities[componentName] = newPL;

        Debug.Log($"[BKT] Updated mastery for {componentName}: {pL:F4} -> {newPL:F4} " +
                  $"(Correct={isCorrect}, p_guess used={effectivePGuess:F3})");

        return newPL;
    }

    public float GetMastery(string componentName)
        => masteryProbabilities.ContainsKey(componentName) ? masteryProbabilities[componentName] : 0f;

    public bool HasMastered(string componentName)
    { 
        if (!parameters.ContainsKey(componentName)) return false;
        return GetMastery(componentName) >= parameters[componentName].mastery_threshold;
    }

    public Dictionary<string, float> GetAllMasteryScores()
                => new Dictionary<string, float>(masteryProbabilities);

    // ========== SAVE/LOAD BRIDGE METHODS ==========

    public List<BKTMasteryEntry> ExportMastery()
    {
        List<BKTMasteryEntry> result = new List<BKTMasteryEntry>();
        foreach (var kvp in masteryProbabilities)
        {
            result.Add(new BKTMasteryEntry
            {
                componentName = kvp.Key,
                masteryProbability = kvp.Value
            });
        }
        return result;
    }

    public void ImportMastery(List<BKTMasteryEntry> entries)
    {
        if (entries == null) return;
        foreach (var entry in entries)
        {
            if (parameters.ContainsKey(entry.componentName))
            {
                masteryProbabilities[entry.componentName] = Mathf.Clamp01(entry.masteryProbability);
            }
        }
        Debug.Log($"[BKT] Imported {entries.Count} mastery entries.");
    }

    public void ResetAllMastery()
    {
        masteryProbabilities.Clear();
        foreach (var kvp in parameters)
        {
            masteryProbabilities[kvp.Key] = kvp.Value.p_init;
        }
        Debug.Log("[BKT] All mastery reset to p_init.");
    }
}

[System.Serializable]
public class BKTMasteryEntry
{
    public string componentName;
    public float masteryProbability;
}