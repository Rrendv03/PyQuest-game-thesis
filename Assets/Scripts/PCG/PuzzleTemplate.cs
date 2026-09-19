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
}

[Serializable]
public class PuzzleTemplateLibrary
{
    public List<PuzzleTemplate> templates;
}