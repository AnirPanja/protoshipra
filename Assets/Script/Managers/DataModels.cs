using System;

[System.Serializable]
public class SOSFormData
{
    public string name;
    public string contact;
    public string emergencyTypeInput;
    public string point_address;
    public float lat;
    public float @long;     // "long" is reserved keyword, so use @long
    public string description;
}

[Serializable]
public class LostFoundRequest
{
    public string reportType;
    public string description;
    public string name;
    public string mobile;
    public string location;
}

[Serializable]
public class NavigationRequest
{
    public string destination;
}

[Serializable]
public class LiveUpdate
{
   public string alert;       
    public string time;
    public string description;
}

[Serializable]
public class DynamicData
{
    public string title;
    public string description;
   public string navigationUrl;
}
