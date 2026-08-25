using System;
using System.Collections.Generic;

/// <summary>
/// Shared vocabulary for declarative Dominion card data. Runtime rules and package validation
/// should consult this class instead of maintaining parallel string lists.
/// </summary>
public static class DeclarativeRuleVocabulary
{
    public const string PlayTiming = "play";
    public const string CardGainedTiming = "card_gained";
    public const string CardDiscardedTiming = "card_discarded";
    public const string CardTrashedTiming = "card_trashed";
    public const string TurnStartedTiming = "turn_started";
    public const string TurnEndedTiming = "turn_ended";
    public const string BuyStartedTiming = "buy_started";
    public const string PileEmptiedTiming = "pile_emptied";
    public const string ArtifactGainedTiming = "artifact_gained";
    public const string DiseaseGainedTiming = "disease_gained";

    public const string SubjectScope = "subject";
    public const string InHandScope = "in_hand";
    public const string InPlayScope = "in_play";

    public const string AnyEventPlayer = "any";
    public const string SelfEventPlayer = "self";
    public const string OtherEventPlayer = "other";

    public const string SelfTarget = "self";
    public const string OthersTarget = "others";

    public const string ActionsResource = "actions";
    public const string BuysResource = "buys";
    public const string CoinsResource = "coins";

    private static readonly HashSet<string> Timings = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        PlayTiming,
        CardGainedTiming,
        CardDiscardedTiming,
        CardTrashedTiming,
        TurnStartedTiming,
        TurnEndedTiming,
        BuyStartedTiming,
        PileEmptiedTiming,
        ArtifactGainedTiming,
        DiseaseGainedTiming,
        ReactionRules.AttackReactionTiming
    };

    private static readonly HashSet<string> Scopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SubjectScope,
        InHandScope,
        InPlayScope
    };

    private static readonly HashSet<string> EventPlayers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        AnyEventPlayer,
        SelfEventPlayer,
        OtherEventPlayer
    };

    private static readonly HashSet<string> Targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        SelfTarget,
        OthersTarget
    };

    private static readonly HashSet<string> Resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ActionsResource,
        BuysResource,
        CoinsResource
    };

    public static bool IsSupportedTiming(string value) => Contains(Timings, value);
    public static bool IsSupportedScope(string value) => Contains(Scopes, value);
    public static bool IsSupportedEventPlayer(string value) => Contains(EventPlayers, value);
    public static bool IsSupportedTarget(string value) => Contains(Targets, value);
    public static bool IsSupportedResource(string value) => Contains(Resources, value);

    public static bool IsSupportedOperation(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        string operation = value.Trim();
        return EffectResolver.IsSupported(operation) ||
               string.Equals(operation, ReactionRules.BlockAttackOperation, StringComparison.OrdinalIgnoreCase);
    }

    private static bool Contains(HashSet<string> values, string candidate)
    {
        return !string.IsNullOrWhiteSpace(candidate) && values.Contains(candidate.Trim());
    }
}
