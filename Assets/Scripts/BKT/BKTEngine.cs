using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.IO;
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
    public float UpdateMastery(string componentName, bool isCorrect)
    {
        if (!parameters.ContainsKey(componentName)) return 0f;
        
        KnowledgeComponent kc = parameters[componentName];
        float pL = masteryProbabilities[componentName];

        // Bayesian update
        float pLGivenObs;
        if (isCorrect)
        {
            float numerator = pL * (1 - kc.p_slip);
            float denominator = numerator + (1 - pL) * kc.p_guess;
            pLGivenObs = numerator / denominator;
        }
        else
        {
            float numerator = pL * kc.p_slip;
            float denominator = numerator + (1 - pL) * (1 - kc.p_guess);
            pLGivenObs = numerator / denominator;
        }

        // Learning transition
        float newPL = pLGivenObs + (1f - pLGivenObs) * kc.p_transit;
        newPL = Mathf.Clamp01(newPL);

        masteryProbabilities[componentName] = newPL;
        Debug.Log($"[BKT] Updated mastery for {componentName}: {pL:F4} -> {newPL:F4} (Correct={isCorrect})");
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
}
