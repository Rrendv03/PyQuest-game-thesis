using System;
using System.Collections.Generic;
using UnityEngine;

public enum PuzzleType
{
    FillInTheBlank = 0,
    SpotTheBug = 1,
    LineScramble = 2,
    TrueOrFalse = 3,
    PredictTheOutput = 4,
    PairACode = 5
}
public enum DifficultyTier { Beginner, Intermediate, Advanced }

[Serializable]
public class VariablePair
{
    public string name;
    public string value;
}

[Serializable]
public class PuzzleTemplate
{
    public string id;
    public string knowledgeComponent;
    public PuzzleType puzzleType;
    public DifficultyTier difficulty;
    public List<string> codeLines;
    public string correctAnswer;
    public int bugLineIndex;
    public List<int> correctOrder;
    public List<string> distractors;
    public string variableName;
    public string variableValue;

    // Optional extra renamable variables beyond the primary
    // variableName/variableValue pair. Existing templates that never set
    // this (JsonUtility defaults it to an empty list) behave exactly as
    // before. Not currently populated by any shipped template -- wired in
    // for later, not exercised yet.
    public List<VariablePair> additionalVariables = new List<VariablePair>();

    // Optional goal line for PairACode: rendered as the first line of the
    // snippet ("# Goal: ...") so the blanked line's INTENT is visible to the
    // player. This is what makes distractors verifiably wrong by reasoning
    // against the goal instead of by spotting a one-character difference.
    // Templates that never set it (JsonUtility defaults to null/empty) keep
    // the legacy "Complete the missing line" header exactly as before.
    // Slot tokens like {name} are filled by PuzzleVariationEngine.
    public string goalText;

    // LineScramble only: every dependency-valid permutation of codeLines
    // that simulates to the SAME output as the canonical order, computed by
    // PuzzleVariationEngine.ComputeAcceptedOrders. Keys are comma-joined row
    // indices (e.g. "0,1,2"). Null/empty for legacy or control-flow templates
    // -> LineScramblePuzzleFormat falls back to its dependency validator.
    public List<string> acceptedOrders;
}

[Serializable]
public class PuzzleTemplateLibrary
{
    public List<PuzzleTemplate> templates;
}