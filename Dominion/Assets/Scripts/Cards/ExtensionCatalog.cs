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
    public int schemaVersion = 1;
    public string artwork;
    public List<ExtensionCardData> cards = new List<ExtensionCardData>();
    public List<ExtensionCardData> baseCards = new List<ExtensionCardData>();
    [NonSerialized] public string packageDirectory;
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
    public List<CardAbilityData> abilities = new List<CardAbilityData>();
}

[Serializable]
public sealed class CardAbilityData
{
    public string when;
    public string scope;
    public CardTriggerFilterData filter;
    public List<CardEffectData> effects = new List<CardEffectData>();
}

[Serializable]
public sealed class CardTriggerFilterData
{
    public string eventPlayer;
    public string cardId;
    public string cardType;
}

[Serializable]
public sealed class CardEffectData
{
    public string op;
    public string target;
    public string resource;
    public int amount;

    public string zone;
    public int min;
    public int max;
    public string prompt;

    public string sourceZone;
    public string destinationZone;

    // Generic card/pile choice constraints.
    public string cardId;
    public string cardType;

    // Supply-choice constraint. Negative means no cost ceiling.
    public int maxCost = -1;
}

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

    public static ExtensionCardData FindCard(ExtensionPackageData extension, string cardId)
    {
        if (extension == null || string.IsNullOrEmpty(cardId))
            return null;

        ExtensionCardData card = FindIn(extension.cards, cardId);
        return card ?? FindIn(extension.baseCards, cardId);
    }

    public static ExtensionCardData FindCard(string extensionId, string cardId)
    {
        return FindCard(Find(extensionId), cardId);
    }

    private static ExtensionCardData FindIn(List<ExtensionCardData> cards, string cardId)
    {
        if (cards == null)
            return null;

        return cards.Find(card =>
            card != null &&
            string.Equals(card.id, cardId, StringComparison.OrdinalIgnoreCase));
    }

    private static List<ExtensionPackageData> LoadAll()
    {
        List<ExtensionPackageData> result = new List<ExtensionPackageData>();
        string root = Path.Combine(Application.streamingAssetsPath, "Extensions");

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

                extension.packageDirectory = directory;
                if (extension.schemaVersion <= 0)
                    extension.schemaVersion = 1;
                if (extension.cards == null)
                    extension.cards = new List<ExtensionCardData>();
                if (extension.baseCards == null)
                    extension.baseCards = new List<ExtensionCardData>();

                NormaliseCards(extension.cards);
                NormaliseCards(extension.baseCards);
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

    private static void NormaliseCards(List<ExtensionCardData> cards)
    {
        if (cards == null)
            return;

        foreach (ExtensionCardData card in cards)
        {
            if (card == null)
                continue;

            if (card.types == null)
                card.types = new List<string>();
            if (card.abilities == null)
                card.abilities = new List<CardAbilityData>();

            foreach (CardAbilityData ability in card.abilities)
            {
                if (ability != null && ability.effects == null)
                    ability.effects = new List<CardEffectData>();
            }
        }
    }
}
