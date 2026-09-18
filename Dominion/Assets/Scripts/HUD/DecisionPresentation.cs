using System;
using System.Collections.Generic;

/// <summary>Presentation hints only: never infer actions from translated prompt text.</summary>
public static class DecisionPresentation
{
    public static int SourceId(PendingDecisionSnapshot decision) =>
        decision.ListenerCardInstanceId > 0 ? decision.ListenerCardInstanceId : decision.SourceCardInstanceId;

    public static string Destination(PendingDecisionSnapshot decision, ExtensionCardData source)
    {
        string op = decision.Operation ?? string.Empty;
        if (op.StartsWith("move_all_ordered|", StringComparison.Ordinal))
            return ZoneLabel(op.Substring("move_all_ordered|".Length));
        if (op == "discard_down_to" || op.StartsWith("each_other_discard_cards|", StringComparison.Ordinal))
            return "DÉFAUSSE";
        if (op == "repeated_option_trash_from_hand") return "ÉCART";
        if (op != "choose_cards" && op != "choose_cards_per_empty_pile") return "SÉLECTION";
        if (source?.abilities == null || decision.AbilityIndex < 0 || decision.AbilityIndex >= source.abilities.Count)
            return "SÉLECTION";
        List<CardEffectData> effects = source.abilities[decision.AbilityIndex]?.effects;
        if (effects == null || decision.EffectIndex < 0) return "SÉLECTION";
        for (int i = decision.EffectIndex + 1; i < effects.Count; i++)
        {
            CardEffectData effect = effects[i];
            if (effect == null) break;
            switch (effect.op)
            {
                case "remember_selected_card_cost": continue;
                case "trash_selected": return "ÉCART";
                case "discard_selected": return "DÉFAUSSE";
                case "move_selected": return ZoneLabel(effect.destinationZone);
                default: return "SÉLECTION";
            }
        }
        return "SÉLECTION";
    }

    public static string ZoneLabel(string zone)
    {
        switch ((zone ?? string.Empty).ToLowerInvariant())
        {
            case "deck": return "DESSUS DU DECK";
            case "discard": return "DÉFAUSSE";
            case "hand": return "MAIN";
            case "trash": return "ÉCART";
            default: return "SÉLECTION";
        }
    }

    public static bool IsValid(PendingDecisionSnapshot decision, int count) =>
        (count == 0 && decision.AllowPass) || (count >= decision.MinSelections && count <= decision.MaxSelections);

    public static string CountLabel(PendingDecisionSnapshot decision, int count)
    {
        string required = decision.MinSelections == decision.MaxSelections
            ? decision.MaxSelections + " requis"
            : decision.MinSelections + " à " + decision.MaxSelections;
        return count + " sélectionné(s) · " + required +
            (decision.AllowPass || decision.MinSelections == 0 ? " · facultatif" : "");
    }
}
