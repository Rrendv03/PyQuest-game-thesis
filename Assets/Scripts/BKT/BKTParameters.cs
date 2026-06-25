using System;
using System.Collections.Generic;
using UnityEngine;

[Serializable]
public class KnowledgeComponent
{
    public string name;
    public float p_init;
    public float p_transit;
    public float p_guess;
    public float p_slip;
    public float mastery_threshold;
}

[Serializable]
public class BKTParamWrapper
{
    public List<KnowledgeComponent> components;
}