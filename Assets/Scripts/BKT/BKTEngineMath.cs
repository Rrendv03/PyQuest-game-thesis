using UnityEngine;

/// <summary>
/// Independent closed-form standard-BKT recurrence — the CROSS-CHECK, not the object under test.
///
/// ROLE (updated 2026-10-07): the Unity Editor validation runner replays the dataset through the
/// SHIPPED game engine — the BKTEngine component itself, the only class whose
/// UpdateMastery is called (BKTEngine exposes no prediction method; the game never needed one).
/// This file is a second,
/// independently written formulation of the same equations in float32; the runner (and the
/// harness, section 5) require max |engine - this| == 0 before any metric is reported, so this
/// class can never quietly agree with a wrong engine.
///
/// Runner parity loop, per (student, skill) sequence:
///       engine.ResetAllMastery();                        // sequence starts at p_init
///       float pLBefore = engine.GetMastery(skill);         // the engine's OWN state
///       double pred = BKTEngineMath.PredictCorrect(pLBefore, slip, guess); // emission, BEFORE the update
///       double post = engine.UpdateMastery(skill, correct);      // the real update — the engine's own
///       assert |post - BKTEngineMath.UpdateMastery(pLBefore, correct, pT, pG, pS)| == 0
///
/// Executable evidence: tools/bkt_validation_check/ sections 5-6 (3,936-state grid,
/// max |delta| = 0.000000 exactly, plus an end-to-end runner replay) - all passing.
/// The game code itself was verified separately against a real playtest log (91/91 transitions
/// reproduced, worst error 4e-4) — see defense doc 06_engine_verification.md.
///
/// Standard BKT equations (Corbett & Anderson 1994; as formalised in
/// Badrinath, Wang & Pardos 2021, EDM '21):
///   P(correct)          = P(L)(1 - P(S)) + (1 - P(L)) P(G)
///   P(L | obs = 1)      = P(L)(1 - P(S)) / [P(L)(1 - P(S)) + (1 - P(L)) P(G)]
///   P(L | obs = 0)      = P(L) P(S) / [P(L) P(S) + (1 - P(L))(1 - P(G))]
///   P(L_{t+1})          = P(L | obs) + (1 - P(L | obs)) P(T)
/// Standard BKT assumes no forgetting: P(F) = 0.
/// </summary>
public static class BKTEngineMath
{
    /// <summary>
    /// One standard-BKT update: Bayesian posterior given the observation,
    /// then the learning transition. Identical expressions and operation
    /// order to BKTEngine.UpdateMastery (float32).
    /// </summary>
    public static float UpdateMastery(float pL, bool isCorrect, float pTransit, float pGuess, float pSlip)
    {
        float posterior;
        if (isCorrect)
        {
            float numerator = pL * (1f - pSlip);
            float denominator = numerator + (1f - pL) * pGuess;
            posterior = numerator / denominator;
        }
        else
        {
            float numerator = pL * pSlip;
            float denominator = numerator + (1f - pL) * (1f - pGuess);
            posterior = numerator / denominator;
        }

        float newPL = posterior + (1f - posterior) * pTransit;
        return Mathf.Clamp01(newPL);
    }

    /// <summary>
    /// Emission probability P(correct) for the NEXT response, computed from
    /// the mastery state BEFORE that response is observed. This is the value
    /// that predictive metrics (AUC-ROC, RMSE, MAE) are computed against.
    /// </summary>
    public static float PredictCorrect(float pL, float pSlip, float pGuess)
    {
        return pL * (1f - pSlip) + (1f - pL) * pGuess;
    }
}