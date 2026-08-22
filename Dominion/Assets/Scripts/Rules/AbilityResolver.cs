using System;
using System.Collections.Generic;

/// <summary>
/// Result of resolving all abilities for one timing point on a card.
/// The resolver deliberately propagates WaitingForChoice immediately so a future
/// resolution queue can persist its cursor and resume from the exact next effect.
/// </summary>
public readonly struct AbilityResolutionResult
{
    public EffectResolutionStatus Status { get; }
    public int AbilitiesMatched { get; }
    public int EffectsResolved { get; }
    public string Error { get; }

    public bool Succeeded => Status == EffectResolutionStatus.Applied;

    private AbilityResolutionResult(
        EffectResolutionStatus status,
        int abilitiesMatched,
        int effectsResolved,
        string error)
    {
        Status = status;
        AbilitiesMatched = abilitiesMatched;
        EffectsResolved = effectsResolved;
        Error = error ?? string.Empty;
    }

    public static AbilityResolutionResult Applied(int abilitiesMatched, int effectsResolved)
    {
        return new AbilityResolutionResult(
            EffectResolutionStatus.Applied,
            abilitiesMatched,
            effectsResolved,
            string.Empty);
    }

    public static AbilityResolutionResult WaitingForChoice(int abilitiesMatched, int effectsResolved)
    {
        return new AbilityResolutionResult(
            EffectResolutionStatus.WaitingForChoice,
            abilitiesMatched,
            effectsResolved,
            string.Empty);
    }

    public static AbilityResolutionResult Rejected(
        int abilitiesMatched,
        int effectsResolved,
        string error)
    {
        return new AbilityResolutionResult(
            EffectResolutionStatus.Rejected,
            abilitiesMatched,
            effectsResolved,
            error);
    }
}

/// <summary>
/// Resolves a card's declarative abilities at a named timing point ("play" initially).
/// It only orchestrates order; the meaning of each operation remains owned by EffectResolver.
///
/// This class is intentionally independent from Photon, Unity scenes and UI. The caller is
/// responsible for providing an authoritative working-copy state and deciding whether that
/// state is committed. Therefore a Rejected result must never be committed by the caller.
/// </summary>
public static class AbilityResolver
{
    public static AbilityResolutionResult ResolveTiming(
        ExtensionCardData card,
        string timing,
        EffectExecutionContext context)
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

        int matched = 0;
        int resolved = 0;

        for (int abilityIndex = 0; abilityIndex < abilities.Count; abilityIndex++)
        {
            CardAbilityData ability = abilities[abilityIndex];
            if (ability == null ||
                !string.Equals(ability.when, timing, StringComparison.OrdinalIgnoreCase))
                continue;

            matched++;

            if (ability.effects == null || ability.effects.Count == 0)
                continue;

            for (int effectIndex = 0; effectIndex < ability.effects.Count; effectIndex++)
            {
                CardEffectData effect = ability.effects[effectIndex];
                if (effect == null)
                {
                    return AbilityResolutionResult.Rejected(
                        matched,
                        resolved,
                        BuildError(card, timing, abilityIndex, effectIndex, "Effect is null."));
                }

                EffectResolutionResult effectResult = EffectResolver.Resolve(effect, context);

                if (effectResult.Status == EffectResolutionStatus.Rejected)
                {
                    return AbilityResolutionResult.Rejected(
                        matched,
                        resolved,
                        BuildError(card, timing, abilityIndex, effectIndex, effectResult.Error));
                }

                if (effectResult.Status == EffectResolutionStatus.WaitingForChoice)
                {
                    return AbilityResolutionResult.WaitingForChoice(matched, resolved);
                }

                resolved++;
            }
        }

        return AbilityResolutionResult.Applied(matched, resolved);
    }

    public static AbilityResolutionResult ResolvePlay(
        ExtensionCardData card,
        EffectExecutionContext context)
    {
        return ResolveTiming(card, "play", context);
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
