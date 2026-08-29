using System;
using System.Collections.Generic;

/// <summary>
/// Additional extension components required by the Kingdom cards selected for one match.
/// References are qualified (for example "fleaux:maladies") and compared case-insensitively.
/// </summary>
public sealed class ExtensionComponentUsage
{
    private readonly HashSet<string> _specialPileIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _artifactIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public int SpecialPileCount => _specialPileIds.Count;
    public int ArtifactCount => _artifactIds.Count;

    public bool UsesSpecialPile(string qualifiedPileId) =>
        !string.IsNullOrWhiteSpace(qualifiedPileId) && _specialPileIds.Contains(qualifiedPileId);

    public bool UsesArtifact(string qualifiedArtifactId) =>
        !string.IsNullOrWhiteSpace(qualifiedArtifactId) && _artifactIds.Contains(qualifiedArtifactId);

    internal bool AddSpecialPile(string qualifiedPileId) => _specialPileIds.Add(qualifiedPileId);
    internal bool AddArtifact(string qualifiedArtifactId) => _artifactIds.Add(qualifiedArtifactId);
}

/// <summary>
/// Follows declarative card-effect references so setup and UI only include extension
/// piles and Artefacts that can actually be reached during the selected match.
/// </summary>
public static class ExtensionComponentUsageResolver
{
    public static ExtensionComponentUsage Resolve(IEnumerable<string> kingdomCardIds)
    {
        ExtensionComponentUsage usage = new ExtensionComponentUsage();
        Queue<string> cardsToInspect = new Queue<string>();
        HashSet<string> inspectedCards = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (kingdomCardIds != null)
            foreach (string cardId in kingdomCardIds)
                EnqueueQualifiedCard(cardId, cardsToInspect);

        while (cardsToInspect.Count > 0)
        {
            string cardReference = cardsToInspect.Dequeue();
            if (!inspectedCards.Add(cardReference) ||
                !TryResolveCard(cardReference, out _, out ExtensionCardData card))
                continue;

            if (card.abilities == null)
                continue;

            foreach (CardAbilityData ability in card.abilities)
            {
                if (ability == null || ability.effects == null)
                    continue;
                foreach (CardEffectData effect in ability.effects)
                    AddEffectDependencies(effect, usage, cardsToInspect);
            }
        }

        return usage;
    }

    private static void AddEffectDependencies(CardEffectData effect, ExtensionComponentUsage usage,
        Queue<string> cardsToInspect)
    {
        if (effect == null)
            return;

        AddSpecialPile(effect.specialPileId, usage, cardsToInspect);
        AddArtifact(effect.artifactId, usage, cardsToInspect);

        if (effect.requiresArtifactIds != null)
            foreach (string artifactId in effect.requiresArtifactIds)
                AddArtifact(artifactId, usage, cardsToInspect);

        if (effect.options != null)
            foreach (CardChoiceOptionData option in effect.options)
                if (option != null)
                    AddArtifact(option.artifactId, usage, cardsToInspect);
    }

    private static void AddSpecialPile(string pileReference, ExtensionComponentUsage usage,
        Queue<string> cardsToInspect)
    {
        if (!TryResolveSpecialPile(pileReference, out ExtensionPackageData extension,
                out ExtensionSpecialPileData pile, out string qualifiedPileId) ||
            !usage.AddSpecialPile(qualifiedPileId) || pile.cardIds == null)
            return;

        foreach (string cardId in pile.cardIds)
            EnqueueQualifiedCard(extension.id + ":" + cardId, cardsToInspect);
    }

    private static void AddArtifact(string artifactReference, ExtensionComponentUsage usage,
        Queue<string> cardsToInspect)
    {
        if (!TryResolveArtifact(artifactReference, out ExtensionPackageData extension,
                out ExtensionCardData artifact))
            return;

        string qualifiedArtifactId = extension.id + ":" + artifact.id;
        if (usage.AddArtifact(qualifiedArtifactId))
            EnqueueQualifiedCard(qualifiedArtifactId, cardsToInspect);
    }

    private static bool TryResolveSpecialPile(string pileReference, out ExtensionPackageData extension,
        out ExtensionSpecialPileData pile, out string qualifiedPileId)
    {
        extension = null;
        pile = null;
        qualifiedPileId = string.Empty;
        if (!CardDefinitionReference.TryParseQualified(pileReference, out string extensionId, out string pileId))
            return false;

        extension = ExtensionCatalog.Find(extensionId);
        if (extension == null || extension.specialPiles == null)
            return false;

        pile = extension.specialPiles.Find(candidate => candidate != null &&
            string.Equals(candidate.id, pileId, StringComparison.OrdinalIgnoreCase));
        if (pile == null)
            return false;

        qualifiedPileId = extension.id + ":" + pile.id;
        return true;
    }

    private static bool TryResolveCard(string cardReference, out ExtensionPackageData extension,
        out ExtensionCardData card)
    {
        extension = null;
        card = null;
        if (!CardDefinitionReference.TryParseQualified(cardReference, out string extensionId, out string cardId))
            return false;

        extension = ExtensionCatalog.Find(extensionId);
        card = ExtensionCatalog.FindCard(extension, cardId);
        return extension != null && card != null;
    }

    private static bool TryResolveArtifact(string artifactReference, out ExtensionPackageData extension,
        out ExtensionCardData artifact)
    {
        extension = null;
        artifact = null;
        if (!CardDefinitionReference.TryParseQualified(artifactReference,
                out string extensionId, out string artifactId))
            return false;

        extension = ExtensionCatalog.Find(extensionId);
        if (extension == null || extension.artifacts == null)
            return false;

        artifact = extension.artifacts.Find(candidate => candidate != null &&
            string.Equals(candidate.id, artifactId, StringComparison.OrdinalIgnoreCase));
        return artifact != null;
    }

    private static void EnqueueQualifiedCard(string cardReference, Queue<string> cardsToInspect)
    {
        if (CardDefinitionReference.TryParseQualified(cardReference, out string extensionId, out string cardId))
            cardsToInspect.Enqueue(extensionId + ":" + cardId);
    }
}
