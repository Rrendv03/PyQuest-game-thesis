using System;
using System.Collections.Generic;

[Serializable]
public class MissionTabletData
{
    public string missionID;
    public string sanctumID;
    public string displayName;
    public string description;
    public string knowledgeComponent;
    public string puzzleType;
    public string promptText;
}

[Serializable]
public class MissionTabletWrapper
{
    public List<MissionTabletData> missions = new List<MissionTabletData>();
}