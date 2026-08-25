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
    public bool oncePerTurn;
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

    // Restrict a card choice to the card most recently moved by this resolution.
    // Useful for reveal/discard-top flows without introducing card-specific operations.
    public bool lastMovedOnly;

    // Generic conditional execution: when true, this effect becomes a no-op if the
    // immediately preceding player choice selected no cards/piles.
    public bool requiresLastSelection;

    // Supply-choice constraints. Negative maxCost means no fixed cost ceiling.
    public int maxCost = -1;

    // When true, choose_supply uses LastSelectedCardCost + costOffset as its ceiling.
    // This is intentionally separate from maxCost so cards such as Rénovation/Mine stay data-driven.
    public bool useLastSelectionCost;
    public int costOffset;

    // If a mandatory-looking supply choice has no eligible non-empty pile, resolve it as
    // a no-op instead of rejecting the whole transaction. This matches Dominion's
    // "do as much as you can" behavior without making an available gain optional.
    public bool allowNoEligible;
}

public static class ExtensionCatalog
{
    private const string ExtensionFileName = "extension.json";
    public const int SupportedSchemaVersion = 2;
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
        if (string.IsNullOrWhiteSpace(extensionId))
            return null;

        string requestedId = extensionId.Trim();
        foreach (ExtensionPackageData extension in All)
        {
            if (extension != null && string.Equals(extension.id, requestedId, StringComparison.OrdinalIgnoreCase))
                return extension;
        }

        return null;
    }

    public static ExtensionCardData FindCard(ExtensionPackageData extension, string cardId)
    {
        if (extension == null || string.IsNullOrWhiteSpace(cardId))
            return null;

        string requestedId = cardId.Trim();
        ExtensionCardData card = FindIn(extension.cards, requestedId);
        return card ?? FindIn(extension.baseCards, requestedId);
    }

    public static ExtensionCardData FindCard(string extensionId, string cardId)
    {
        return FindCard(Find(extensionId), cardId);
    }

    /// <summary>
    /// Validates one in-memory package without touching the filesystem. This is kept
    /// public so EditMode tests and future authoring tools can validate custom packages
    /// using exactly the same rules as the runtime loader.
    /// </summary>
    public static bool TryValidatePackage(ExtensionPackageData extension, out string error)
    {
        error = string.Empty;
        if (extension == null)
        {
            error = "Extension package is null.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(extension.id))
        {
            error = "Extension id is required.";
            return false;
        }

        if (extension.id.IndexOf(':') >= 0)
        {
            error = "Extension id '" + extension.id + "' cannot contain ':'.";
            return false;
        }

        if (extension.schemaVersion < 1 || extension.schemaVersion > SupportedSchemaVersion)
        {
            error = "Extension '" + extension.id + "' uses unsupported schema version " + extension.schemaVersion +
                    ". Supported versions are 1 to " + SupportedSchemaVersion + ".";
            return false;
        }

        HashSet<string> cardIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!ValidateCards(extension.cards, "cards", cardIds, out error))
            return false;
        if (!ValidateCards(extension.baseCards, "baseCards", cardIds, out error))
            return false;

        return true;
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
        HashSet<string> extensionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
                if (extension == null)
                {
                    Debug.LogError("Ignored invalid Dominion extension file '" + path + "': package is null.");
                    continue;
                }

                extension.packageDirectory = directory;
                NormalisePackage(extension);

                if (!TryValidatePackage(extension, out string validationError))
                {
                    Debug.LogError("Ignored invalid Dominion extension file '" + path + "': " + validationError);
                    continue;
                }

                if (!extensionIds.Add(extension.id))
                {
                    Debug.LogError("Ignored duplicate Dominion extension id '" + extension.id + "' from: " + path);
                    continue;
                }

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

    private static void NormalisePackage(ExtensionPackageData extension)
    {
        extension.id = (extension.id ?? string.Empty).Trim();
        extension.name = (extension.name ?? string.Empty).Trim();
        if (extension.schemaVersion <= 0)
            extension.schemaVersion = 1;
        if (extension.cards == null)
            extension.cards = new List<ExtensionCardData>();
        if (extension.baseCards == null)
            extension.baseCards = new List<ExtensionCardData>();

        NormaliseCards(extension.cards);
        NormaliseCards(extension.baseCards);
    }

    private static void NormaliseCards(List<ExtensionCardData> cards)
    {
        if (cards == null)
            return;

        foreach (ExtensionCardData card in cards)
        {
            if (card == null)
                continue;

            card.id = (card.id ?? string.Empty).Trim();
            card.name = (card.name ?? string.Empty).Trim();
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

    private static bool ValidateCards(List<ExtensionCardData> cards, string listName, HashSet<string> cardIds, out string error)
    {
        error = string.Empty;
        if (cards == null)
        {
            error = listName + " cannot be null after normalisation.";
            return false;
        }

        for (int cardIndex = 0; cardIndex < cards.Count; cardIndex++)
        {
            ExtensionCardData card = cards[cardIndex];
            if (card == null)
            {
                error = listName + "[" + cardIndex + "] is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(card.id))
            {
                error = listName + "[" + cardIndex + "] has no card id.";
                return false;
            }

            if (card.id.IndexOf(':') >= 0)
            {
                error = "Card id '" + card.id + "' cannot contain ':'.";
                return false;
            }

            if (!cardIds.Add(card.id))
            {
                error = "Duplicate card id '" + card.id + "' across the extension package.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(card.name))
            {
                error = "Card '" + card.id + "' has no display name.";
                return false;
            }

            if (card.cost < 0)
            {
                error = "Card '" + card.id + "' has a negative cost.";
                return false;
            }

            if (card.abilities == null)
            {
                error = "Card '" + card.id + "' has a null abilities list.";
                return false;
            }

            for (int abilityIndex = 0; abilityIndex < card.abilities.Count; abilityIndex++)
            {
                CardAbilityData ability = card.abilities[abilityIndex];
                if (ability == null)
                {
                    error = "Card '" + card.id + "' ability[" + abilityIndex + "] is null.";
                    return false;
                }

                if (string.IsNullOrWhiteSpace(ability.when))
                {
                    error = "Card '" + card.id + "' ability[" + abilityIndex + "] has no timing ('when').";
                    return false;
                }

                if (ability.effects == null)
                {
                    error = "Card '" + card.id + "' ability[" + abilityIndex + "] has a null effects list.";
                    return false;
                }

                for (int effectIndex = 0; effectIndex < ability.effects.Count; effectIndex++)
                {
                    CardEffectData effect = ability.effects[effectIndex];
                    if (effect == null)
                    {
                        error = "Card '" + card.id + "' ability[" + abilityIndex + "] effect[" + effectIndex + "] is null.";
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(effect.op))
                    {
                        error = "Card '" + card.id + "' ability[" + abilityIndex + "] effect[" + effectIndex + "] has no operation ('op').";
                        return false;
                    }
                }
            }
        }

        return true;
    }
}
