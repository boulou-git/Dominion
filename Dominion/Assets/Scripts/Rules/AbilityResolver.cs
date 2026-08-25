using System;
using System.Collections.Generic;

public readonly struct AbilityResolutionResult
{
    public EffectResolutionStatus Status { get; }
    public int AbilitiesMatched { get; }
    public int EffectsResolved { get; }
    public string Error { get; }
    public bool Succeeded => Status == EffectResolutionStatus.Applied;

    private AbilityResolutionResult(EffectResolutionStatus status, int abilitiesMatched, int effectsResolved, string error)
    {
        Status = status;
        AbilitiesMatched = abilitiesMatched;
        EffectsResolved = effectsResolved;
        Error = error ?? string.Empty;
    }

    public static AbilityResolutionResult Applied(int abilitiesMatched, int effectsResolved) =>
        new AbilityResolutionResult(EffectResolutionStatus.Applied, abilitiesMatched, effectsResolved, string.Empty);

    public static AbilityResolutionResult WaitingForChoice(int abilitiesMatched, int effectsResolved) =>
        new AbilityResolutionResult(EffectResolutionStatus.WaitingForChoice, abilitiesMatched, effectsResolved, string.Empty);

    public static AbilityResolutionResult Rejected(int abilitiesMatched, int effectsResolved, string error) =>
        new AbilityResolutionResult(EffectResolutionStatus.Rejected, abilitiesMatched, effectsResolved, error);
}

/// <summary>
/// Executes declarative abilities in deterministic list order. The optional cursor lets a
/// suspended ResolutionQueue resume after the exact effect that requested a player decision.
/// </summary>
public static class AbilityResolver
{
    public static AbilityResolutionResult ResolveTiming(
        ExtensionCardData card,
        string timing,
        EffectExecutionContext context,
        Func<CardAbilityData, bool> abilityPredicate = null)
    {
        return ResolveTimingFromCursor(card, timing, context, abilityPredicate, 0, 0);
    }

    public static AbilityResolutionResult ResolveTimingFromCursor(
        ExtensionCardData card,
        string timing,
        EffectExecutionContext context,
        Func<CardAbilityData, bool> abilityPredicate,
        int startAbilityIndex,
        int startEffectIndex)
    {
        if (card == null)
            return AbilityResolutionResult.Rejected(0, 0, "Card definition is null.");
        if (string.IsNullOrWhiteSpace(timing))
            return AbilityResolutionResult.Rejected(0, 0, "Ability timing is missing.");
        if (context == null || context.State == null || context.Actor == null)
            return AbilityResolutionResult.Rejected(0, 0, "Effect execution context is incomplete.");

        List<CardAbilityData> abilities = card.abilities;
        if (abilities == null || abilities.Count == 0)
            return AbilityResolutionResult.Applied(0, 0);

        startAbilityIndex = Math.Max(0, startAbilityIndex);
        startEffectIndex = Math.Max(0, startEffectIndex);
        int matched = 0;
        int resolved = 0;

        for (int abilityIndex = startAbilityIndex; abilityIndex < abilities.Count; abilityIndex++)
        {
            CardAbilityData ability = abilities[abilityIndex];
            if (ability == null ||
                !string.Equals(ability.when, timing, StringComparison.OrdinalIgnoreCase) ||
                (abilityPredicate != null && !abilityPredicate(ability)))
                continue;

            if (ability.oncePerTurn)
            {
                if (context.SourceCardInstanceId <= 0)
                    return AbilityResolutionResult.Rejected(matched, resolved, "oncePerTurn ability requires a physical source card instance.");
                if (WasUsedThisTurn(context.State, context.SourceCardInstanceId, abilityIndex))
                    continue;
            }

            matched++;
            if (ability.effects == null || ability.effects.Count == 0)
            {
                if (ability.oncePerTurn)
                    MarkUsedThisTurn(context.State, context.SourceCardInstanceId, abilityIndex);
                continue;
            }

            int firstEffect = abilityIndex == startAbilityIndex ? startEffectIndex : 0;
            for (int effectIndex = firstEffect; effectIndex < ability.effects.Count; effectIndex++)
            {
                CardEffectData effect = ability.effects[effectIndex];
                if (effect == null)
                    return AbilityResolutionResult.Rejected(
                        matched,
                        resolved,
                        BuildError(card, timing, abilityIndex, effectIndex, "Effect is null."));

                EffectExecutionContext effectContext = context.WithCursor(
                    timing,
                    context.SourceCardInstanceId,
                    abilityIndex,
                    effectIndex);
                EffectResolutionResult effectResult = EffectResolver.Resolve(effect, effectContext);

                if (effectResult.Status == EffectResolutionStatus.Rejected)
                    return AbilityResolutionResult.Rejected(
                        matched,
                        resolved,
                        BuildError(card, timing, abilityIndex, effectIndex, effectResult.Error));

                if (effectResult.Status == EffectResolutionStatus.WaitingForChoice)
                    return AbilityResolutionResult.WaitingForChoice(matched, resolved);

                resolved++;
            }

            if (ability.oncePerTurn)
                MarkUsedThisTurn(context.State, context.SourceCardInstanceId, abilityIndex);
        }

        return AbilityResolutionResult.Applied(matched, resolved);
    }

    public static AbilityResolutionResult ResolvePlay(ExtensionCardData card, EffectExecutionContext context)
    {
        return ResolveTiming(card, "play", context);
    }

    private static bool WasUsedThisTurn(GameStateSnapshot state, int cardInstanceId, int abilityIndex)
    {
        EnsureCurrentTurnUsages(state);
        return state.AbilityUsages.Exists(usage =>
            usage != null &&
            usage.CardInstanceId == cardInstanceId &&
            usage.AbilityIndex == abilityIndex &&
            usage.TurnNumber == state.TurnNumber);
    }

    private static void MarkUsedThisTurn(GameStateSnapshot state, int cardInstanceId, int abilityIndex)
    {
        EnsureCurrentTurnUsages(state);
        if (state.AbilityUsages.Exists(usage =>
                usage != null &&
                usage.CardInstanceId == cardInstanceId &&
                usage.AbilityIndex == abilityIndex &&
                usage.TurnNumber == state.TurnNumber))
            return;

        state.AbilityUsages.Add(new AbilityUsageSnapshot(cardInstanceId, abilityIndex, state.TurnNumber));
    }

    private static void EnsureCurrentTurnUsages(GameStateSnapshot state)
    {
        if (state.AbilityUsages == null)
            state.AbilityUsages = new List<AbilityUsageSnapshot>();
        state.AbilityUsages.RemoveAll(usage => usage == null || usage.TurnNumber != state.TurnNumber);
    }

    private static string BuildError(
        ExtensionCardData card,
        string timing,
        int abilityIndex,
        int effectIndex,
        string detail)
    {
        string cardId = !string.IsNullOrWhiteSpace(card.id) ? card.id : "<unknown>";
        return "Card '" + cardId + "', timing '" + timing + "', ability " + abilityIndex +
               ", effect " + effectIndex + ": " + (detail ?? "Unknown effect error.");
    }
}
