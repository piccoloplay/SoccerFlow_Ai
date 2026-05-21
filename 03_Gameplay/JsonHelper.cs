using System;
using UnityEngine;

/// <summary>
/// SoccerFlaw_Ai — JsonHelper
/// Unity JsonUtility non supporta array a livello radice.
/// Usa wrapper serializzabili per convertire string[] e int[] in JSON e viceversa.
/// </summary>
public static class JsonHelper
{
    [Serializable] class SW { public string[] items; }
    [Serializable] class IW { public int[]    items; }

    public static string ToJson(string[] arr)
    {
        if (arr == null) return "{\"items\":[]}";
        return JsonUtility.ToJson(new SW { items = arr });
    }

    public static string ToJson(int[] arr)
    {
        if (arr == null) return "{\"items\":[]}";
        return JsonUtility.ToJson(new IW { items = arr });
    }

    public static string[] StringArrayFromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return Array.Empty<string>();
        try { return JsonUtility.FromJson<SW>(json)?.items ?? Array.Empty<string>(); }
        catch { return Array.Empty<string>(); }
    }

    public static int[] IntArrayFromJson(string json)
    {
        if (string.IsNullOrEmpty(json)) return Array.Empty<int>();
        try { return JsonUtility.FromJson<IW>(json)?.items ?? Array.Empty<int>(); }
        catch { return Array.Empty<int>(); }
    }

    /// <summary>
    /// Estrae la prima occorrenza di JSON object { … } da una stringa
    /// (utile quando l'LLM avvolge la risposta in markdown o testo libero).
    /// </summary>
    public static string ExtractJsonObject(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        int start = raw.IndexOf('{');
        int end   = raw.LastIndexOf('}');
        if (start < 0 || end <= start) return null;
        return raw.Substring(start, end - start + 1);
    }
}
