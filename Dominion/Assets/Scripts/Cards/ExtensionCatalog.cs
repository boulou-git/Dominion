using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public sealed class ExtensionPackageData
{
    public string id;
    public string name;
    public int version;
    public List<ExtensionCardData> cards = new List<ExtensionCardData>();
}

[Serializable]
public sealed class ExtensionCardData
{
    public string id;
    public string name;
    public int cost;
    public List<string> types = new List<string>();
    public string image;
    public string text;
}

/// <summary>
/// Reads drop-in extension packages from StreamingAssets/Extensions/*/extension.json.
/// Missing/empty image fields are valid and are handled later by CardView's fallback.
/// </summary>
public static class ExtensionCatalog
{
    private const string ExtensionFileName = "extension.json";
    private static List<ExtensionPackageData> _cached;

    public static IReadOnlyList<ExtensionPackageData> All
    {
        get
        {
            if (_cached == null)
                _cached = LoadAll();
            return _cached;
        }
    }

    public static void Reload()
    {
        _cached = LoadAll();
    }

    public static ExtensionPackageData Find(string extensionId)
    {
        if (string.IsNullOrEmpty(extensionId))
            return null;

        foreach (ExtensionPackageData extension in All)
        {
            if (extension != null && string.Equals(extension.id, extensionId, StringComparison.OrdinalIgnoreCase))
                return extension;
        }

        return null;
    }

    private static List<ExtensionPackageData> LoadAll()
    {
        List<ExtensionPackageData> result = new List<ExtensionPackageData>();
        string root = Path.Combine(Application.streamingAssetsPath, "Extensions");

        // The current desktop/Editor target exposes StreamingAssets as normal files.
        // A web/mobile transport can be added later without changing the JSON format.
        if (root.Contains("://"))
        {
            Debug.LogWarning("StreamingAssets extension discovery currently expects a local filesystem path: " + root);
            return result;
        }

        if (!Directory.Exists(root))
        {
            Debug.LogWarning("No Dominion extension directory found at: " + root);
            return result;
        }

        string[] directories = Directory.GetDirectories(root);
        Array.Sort(directories, StringComparer.OrdinalIgnoreCase);

        foreach (string directory in directories)
        {
            string path = Path.Combine(directory, ExtensionFileName);
            if (!File.Exists(path))
                continue;

            try
            {
                string json = File.ReadAllText(path);
                ExtensionPackageData extension = JsonUtility.FromJson<ExtensionPackageData>(json);
                if (extension == null || string.IsNullOrWhiteSpace(extension.id))
                {
                    Debug.LogWarning("Ignored invalid Dominion extension file: " + path);
                    continue;
                }

                if (extension.cards == null)
                    extension.cards = new List<ExtensionCardData>();

                result.Add(extension);
            }
            catch (Exception exception)
            {
                Debug.LogError("Could not load Dominion extension '" + path + "': " + exception.Message);
            }
        }

        Debug.Log("Dominion extension catalog loaded: " + result.Count + " extension(s).");
        return result;
    }
}
