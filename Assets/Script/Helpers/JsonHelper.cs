using System;

public static class JsonHelper
{
    public static T[] FromJson<T>(string json)
    {
        Wrapper<T> wrapper = UnityEngine.JsonUtility.FromJson<Wrapper<T>>(FixJson(json));
        return wrapper.Items;
    }

    public static string FixJson(string value)
    {
        if (!value.StartsWith("{"))
            value = "{\"Items\":" + value + "}";
        return value;
    }

    [Serializable]
    private class Wrapper<T>
    {
        public T[] Items;
    }
}
