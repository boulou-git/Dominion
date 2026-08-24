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

    // Version of the card-data contract, independent from the extension's own content version.
    // Schema v2 introduces declarative abilities/effects while remaining backward compatible
    // with older cards that do not declare any abilities yet.
    public int schemaVersion = 1;

    public string artwork;

    // Cards available to the Kingdom-card pre-game selection.
    public List<ExtensionCardData> cards = new List<ExtensionCardData>();

    // Shared/base cards belonging to the extension but never entering the
    // random Kingdom selection (Cuivre, Domaine, etc.).
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

    // Human-readable French rules text. Gameplay never parses this field.
    public string text;

    // Machine-readable rules. The runtime will progressively use these abilities as the
    // source of truth; an empty list is valid during the migration from legacy cards.
    public List<CardAbilityData> abilities = new List<CardAbilityData>();
}

/// <summary>
/// One capability exposed by a card at a well-defined timing point.
///
/// scope is optional and defaults to "subject", preserving existing cards:
/// - subject: this card is itself the subject of the event (played/gained/etc.)
/// - in_hand: this card listens while in its owner's hand
/// - in_play: this card listens while in its owner's in-play zone
///
/// filter is optional and constrains the triggering event without embedding card-specific
/// logic in C#.
/// </summary>
[Serializable]
public sealed class CardAbilityData
{
    public string when;
    public string scope;
    public CardTriggerFilterData filter;
    public List<CardEffectData> effects = new List<CardEffectData>();
}

/// <summary>
/// Minimal declarative event filter. Empty fields mean "no restriction".
/// eventPlayer accepts "self", "other" or "any".
/// cardId and cardType apply to the card carried by the triggering event.
/// </summary>
[Serializable]
public sealed class CardTriggerFilterData
{
    public string eventPlayer;
    public string cardId;
    public string cardType;
}

/// <summary>
/// Declarative effect instruction consumed by the rules engine.
/// Generic fields are shared by many operations; choice fields are intentionally data-only
/// so extensions can request interaction without owning UI, networking or continuation code.
/// </summary>
[Serializable]
public sealed class CardEffectData
{
    public string op;
    public string target;
    public string resource;
    public int amount;

    // Interactive operations such as choose_cards.
    public string zone;
    public int min;
    public int max;
    public string prompt;

    // Generic selected-card movement.
    public string sourceZone;
    public string destinationZone;
}

/// <summary>
/// Reads drop-in extension packages from StreamingAssets/Extensions/*/extension.json.
/// Missing/empty artwork/image fields are valid and use visual fallbacks.
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

    /// <summary>
    /// Resolves either a Kingdom card or a non-Kingdom base/shared card inside an extension.
    /// Selection code still reads extension.cards only, so baseCards never leak into the
    /// random 10-card Kingdom pool.
    /// </summary>
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