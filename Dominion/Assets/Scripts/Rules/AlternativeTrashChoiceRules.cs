using System;
using System.Collections.Generic;

/// <summary>
/// Recognizes a declarative "supply card or trashed card" branch so the HUD can present
/// both destinations at once. The resolver remains authoritative and still validates each step.
/// </summary>
public static class AlternativeTrashChoiceRules
{
    public static bool IsSupplyStep(PendingDecisionSnapshot decision, ExtensionCardData source)
    {
        if (!MatchesCursor(decision, source, out List<CardEffectData> effects)) return false;
        int i = decision.EffectIndex;
        return IsOptionalSingleSupply(effects[i]) && i + 3 < effects.Count &&
               effects[i + 1]?.op == "trash_selected_supply" && effects[i + 1].requiresLastSelection &&
               IsAlternativeTrashChoice(effects[i + 2]) &&
               effects[i + 3]?.op == "gain_selected_trash";
    }

    public static bool IsTrashStep(PendingDecisionSnapshot decision, ExtensionCardData source)
    {
        if (!MatchesCursor(decision, source, out List<CardEffectData> effects)) return false;
        int i = decision.EffectIndex;
        return i >= 2 && IsAlternativeTrashChoice(effects[i]) &&
               IsOptionalSingleSupply(effects[i - 2]) &&
               effects[i - 1]?.op == "trash_selected_supply" && effects[i - 1].requiresLastSelection &&
               i + 1 < effects.Count && effects[i + 1]?.op == "gain_selected_trash";
    }

    private static bool IsOptionalSingleSupply(CardEffectData effect) =>
        effect != null && effect.op == "choose_supply" && effect.min == 0 && effect.max == 1;

    private static bool IsAlternativeTrashChoice(CardEffectData effect) =>
        effect != null && effect.op == "choose_cards" &&
        string.Equals(effect.zone, "trash", StringComparison.OrdinalIgnoreCase) &&
        effect.min == 1 && effect.max == 1 && effect.requiresNoLastSelection;

    private static bool MatchesCursor(PendingDecisionSnapshot decision, ExtensionCardData source,
        out List<CardEffectData> effects)
    {
        effects = null;
        if (decision == null || source?.abilities == null || decision.AbilityIndex < 0 ||
            decision.AbilityIndex >= source.abilities.Count) return false;
        effects = source.abilities[decision.AbilityIndex]?.effects;
        return effects != null && decision.EffectIndex >= 0 && decision.EffectIndex < effects.Count;
    }
}
