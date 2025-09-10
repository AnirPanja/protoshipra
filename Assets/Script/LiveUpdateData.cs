using System;
using System.Collections.Generic;

[Serializable]
public class LiveUpdateItem
{
    public string lost_or_found;       // e.g. "Alert"
    public string description;   // e.g. "Overcrowding at Bhandara..."
    public string entry_date;      // e.g. "4:30 PM"
}

[Serializable]
public class LiveUpdateList
{
    public List<LiveUpdateItem> data;
}
