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
public enum DifficultyTier { Remembering, Understanding, Applying, Analyzing}

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
}

[Serializable]
public class PuzzleTemplateLibrary
{
    public List<PuzzleTemplate> templates;
}