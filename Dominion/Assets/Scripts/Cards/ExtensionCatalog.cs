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
public sealed class CardChoiceOptionData
{
    public string id;
    public string label;
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

    public string cardId;
    public string cardType;

    public bool lastMovedOnly;
    public bool requiresLastSelection;
    public bool requiresNoLastSelection;
    public int requiresLastSelectionCount;
    public int maxCost = -1;
    public bool useLastSelectionCost;
    public int costOffset;
    public bool allowNoEligible;
    public bool allowPass;
    public bool minUpToAvailable;
    public List<CardChoiceOptionData> options = new List<CardChoiceOptionData>();
    public string requiresSelectedOption;
    public string requiresNoCardType;
    public string conditionZone;
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

        return cards.Find(card => card != null && string.Equals(card.id, cardId, StringComparison.OrdinalIgnoreCase));
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
                if (ability == null)
                    continue;

                ability.when = (ability.when ?? string.Empty).Trim();
                ability.scope = (ability.scope ?? string.Empty).Trim();
                if (ability.filter != null)
                {
                    ability.filter.eventPlayer = (ability.filter.eventPlayer ?? string.Empty).Trim();
                    ability.filter.cardId = (ability.filter.cardId ?? string.Empty).Trim();
                    ability.filter.cardType = (ability.filter.cardType ?? string.Empty).Trim();
                }
                if (ability.effects == null)
                    ability.effects = new List<CardEffectData>();

                foreach (CardEffectData effect in ability.effects)
                {
                    if (effect == null)
                        continue;

                    effect.op = (effect.op ?? string.Empty).Trim();
                    effect.target = (effect.target ?? string.Empty).Trim();
                    effect.resource = (effect.resource ?? string.Empty).Trim();
                    effect.zone = (effect.zone ?? string.Empty).Trim();
                    effect.sourceZone = (effect.sourceZone ?? string.Empty).Trim();
                    effect.destinationZone = (effect.destinationZone ?? string.Empty).Trim();
                    effect.cardId = (effect.cardId ?? string.Empty).Trim();
                    effect.cardType = (effect.cardType ?? string.Empty).Trim();
                    effect.prompt = (effect.prompt ?? string.Empty).Trim();
                    effect.requiresSelectedOption = (effect.requiresSelectedOption ?? string.Empty).Trim();
                    effect.requiresNoCardType = (effect.requiresNoCardType ?? string.Empty).Trim();
                    effect.conditionZone = (effect.conditionZone ?? string.Empty).Trim();
                    if (effect.options == null)
                        effect.options = new List<CardChoiceOptionData>();
                    foreach (CardChoiceOptionData option in effect.options)
                    {
                        if (option == null) continue;
                        option.id = (option.id ?? string.Empty).Trim();
                        option.label = (option.label ?? string.Empty).Trim();
                    }
                }
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

                string timing = ability.when.Trim();
                if (!DeclarativeRuleVocabulary.IsSupportedTiming(timing))
                {
                    error = "Card '" + card.id + "' ability[" + abilityIndex + "] uses unsupported timing '" + ability.when + "'.";
                    return false;
                }

                if (!string.IsNullOrWhiteSpace(ability.scope) && !DeclarativeRuleVocabulary.IsSupportedScope(ability.scope))
                {
                    error = "Card '" + card.id + "' ability[" + abilityIndex + "] uses unsupported scope '" + ability.scope + "'.";
                    return false;
                }

                if (!ValidateTriggerFilter(card.id, abilityIndex, ability.filter, out error))
                    return false;

                if (ability.effects == null)
                {
                    error = "Card '" + card.id + "' ability[" + abilityIndex + "] has a null effects list.";
                    return false;
                }

                HashSet<string> availableOptionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int effectIndex = 0; effectIndex < ability.effects.Count; effectIndex++)
                {
                    CardEffectData effect = ability.effects[effectIndex];
                    if (effect == null)
                    {
                        error = "Card '" + card.id + "' ability[" + abilityIndex + "] effect[" + effectIndex + "] is null.";
                        return false;
                    }

                    if (!ValidateEffect(card.id, abilityIndex, effectIndex, timing, effect, out error))
                        return false;

                    if (!string.IsNullOrWhiteSpace(effect.requiresSelectedOption) &&
                        !availableOptionIds.Contains(effect.requiresSelectedOption))
                    {
                        error = "Card '" + card.id + "' ability[" + abilityIndex + "] effect[" + effectIndex +
                                "] references unknown or not-yet-selected option '" + effect.requiresSelectedOption + "'.";
                        return false;
                    }

                    if (string.Equals(effect.op, "choose_options", StringComparison.OrdinalIgnoreCase) && effect.options != null)
                        foreach (CardChoiceOptionData option in effect.options)
                            if (option != null && !string.IsNullOrWhiteSpace(option.id)) availableOptionIds.Add(option.id);
                }
            }
        }

        return true;
    }

    private static bool ValidateTriggerFilter(string cardId, int abilityIndex, CardTriggerFilterData filter, out string error)
    {
        error = string.Empty;
        if (filter == null)
            return true;

        if (!string.IsNullOrWhiteSpace(filter.eventPlayer) && !DeclarativeRuleVocabulary.IsSupportedEventPlayer(filter.eventPlayer))
        {
            error = "Card '" + cardId + "' ability[" + abilityIndex + "] uses unsupported filter.eventPlayer '" + filter.eventPlayer + "'.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(filter.cardId) && !CardDefinitionReference.IsValid(filter.cardId))
        {
            error = "Card '" + cardId + "' ability[" + abilityIndex + "] has malformed filter.cardId '" + filter.cardId + "'.";
            return false;
        }

        return true;
    }

    private static bool ValidateEffect(string cardId, int abilityIndex, int effectIndex, string timing, CardEffectData effect, out string error)
    {
        error = string.Empty;
        string prefix = "Card '" + cardId + "' ability[" + abilityIndex + "] effect[" + effectIndex + "]";

        if (string.IsNullOrWhiteSpace(effect.op))
        {
            error = prefix + " has no operation ('op').";
            return false;
        }

        string op = effect.op.Trim();
        bool isBlockAttack = string.Equals(op, ReactionRules.BlockAttackOperation, StringComparison.OrdinalIgnoreCase);
        if (!DeclarativeRuleVocabulary.IsSupportedOperation(op))
        {
            error = prefix + " uses unsupported operation '" + effect.op + "'.";
            return false;
        }

        bool isAttackReaction = string.Equals(timing, ReactionRules.AttackReactionTiming, StringComparison.OrdinalIgnoreCase);
        if (isAttackReaction && !isBlockAttack)
        {
            error = prefix + " uses operation '" + effect.op + "', but attack_reaction currently supports only '" + ReactionRules.BlockAttackOperation + "'.";
            return false;
        }
        if (isBlockAttack && !isAttackReaction)
        {
            error = prefix + " uses '" + ReactionRules.BlockAttackOperation + "' outside attack_reaction timing.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(effect.target) || !DeclarativeRuleVocabulary.IsSupportedTarget(effect.target))
        {
            error = prefix + " uses unsupported or missing target '" + (effect.target ?? string.Empty) + "'.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(effect.resource) && !DeclarativeRuleVocabulary.IsSupportedResource(effect.resource))
        {
            error = prefix + " uses unsupported resource '" + effect.resource + "'.";
            return false;
        }

        if (!ValidateOptionalZone(prefix, "zone", effect.zone, out error) ||
            !ValidateOptionalZone(prefix, "sourceZone", effect.sourceZone, out error) ||
            !ValidateOptionalZone(prefix, "destinationZone", effect.destinationZone, out error))
            return false;

        if (effect.min < 0 || effect.max < 0)
        {
            error = prefix + " has negative selection bounds.";
            return false;
        }

        if (effect.requiresLastSelection && effect.requiresNoLastSelection)
        {
            error = prefix + " cannot require both an empty and a non-empty previous selection.";
            return false;
        }

        if (effect.requiresLastSelectionCount < 0)
        {
            error = prefix + " has a negative required selection count.";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(effect.requiresNoCardType) &&
            !ValidateOptionalZone(prefix, "conditionZone", effect.conditionZone, out error))
            return false;

        if (string.Equals(op, "choose_options", StringComparison.OrdinalIgnoreCase))
        {
            if (effect.options == null || effect.options.Count == 0)
            {
                error = prefix + " requires at least one choice option.";
                return false;
            }

            HashSet<string> optionIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CardChoiceOptionData option in effect.options)
            {
                if (option == null || string.IsNullOrWhiteSpace(option.id) || string.IsNullOrWhiteSpace(option.label))
                {
                    error = prefix + " contains an option with a missing id or label.";
                    return false;
                }
                if (!optionIds.Add(option.id))
                {
                    error = prefix + " contains duplicate option id '" + option.id + "'.";
                    return false;
                }
            }

            int optionMin = Math.Max(0, effect.min);
            int optionMax = effect.max > 0 ? effect.max : optionMin;
            if (optionMin > optionMax || optionMax > effect.options.Count)
            {
                error = prefix + " has option-selection bounds outside its available options.";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(effect.cardId) && !CardDefinitionReference.IsValid(effect.cardId))
        {
            error = prefix + " has malformed cardId '" + effect.cardId + "'.";
            return false;
        }

        return true;
    }

    private static bool ValidateOptionalZone(string prefix, string fieldName, string value, out string error)
    {
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return true;
        if (CardZoneRules.TryParseZone(value, out _))
            return true;

        error = prefix + " uses unsupported " + fieldName + " '" + value + "'.";
        return false;
    }
}
