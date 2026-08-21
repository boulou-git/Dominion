using System;
using System.Collections.Generic;
using UnityEngine;

public static class GenericEffectKinds
{
    public const string Draw = "Draw";
    public const string AddActions = "AddActions";
    public const string AddBuys = "AddBuys";
    public const string AddCoins = "AddCoins";
    public const string DiscardFromHand = "DiscardFromHand";
    public const string TrashFromHand = "TrashFromHand";
}

/// <summary>
/// Data-only description of one atomic Dominion effect. Complex cards are composed by
/// chaining several of these specs instead of implementing a bespoke MonoBehaviour.
/// </summary>
[Serializable]
public class GenericCardEffect
{
    public string kind;
    public int amount;
    public int min;
    public int max;
    public bool optional;
    public string prompt;

    public GenericCardEffect()
    {
    }

    public GenericCardEffect(string kind, int amount)
    {
        this.kind = kind;
        this.amount = amount;
    }

    public static GenericCardEffect Draw(int amount) => new GenericCardEffect(GenericEffectKinds.Draw, amount);
    public static GenericCardEffect Actions(int amount) => new GenericCardEffect(GenericEffectKinds.AddActions, amount);
    public static GenericCardEffect Buys(int amount) => new GenericCardEffect(GenericEffectKinds.AddBuys, amount);
    public static GenericCardEffect Coins(int amount) => new GenericCardEffect(GenericEffectKinds.AddCoins, amount);

    public static GenericCardEffect Discard(int min, int max, string prompt = null, bool optional = false)
    {
        return new GenericCardEffect
        {
            kind = GenericEffectKinds.DiscardFromHand,
            min = Mathf.Max(0, min),
            max = Mathf.Max(min, max),
            optional = optional,
            prompt = prompt
        };
    }

    public static GenericCardEffect Trash(int min, int max, string prompt = null, bool optional = false)
    {
        return new GenericCardEffect
        {
            kind = GenericEffectKinds.TrashFromHand,
            min = Mathf.Max(0, min),
            max = Mathf.Max(min, max),
            optional = optional,
            prompt = prompt
        };
    }
}

/// <summary>
/// Pure rules helper. It mutates the supplied authoritative snapshot but never performs
/// networking itself. NetworkGameState remains responsible for cloning and committing.
/// </summary>
public static class GenericEffectResolver
{
    public static bool ApplyImmediateOrCreateChoice(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        GenericCardEffect effect,
        int sourceCardInstanceId = 0,
        System.Random random = null)
    {
        if (state == null || player == null || effect == null || string.IsNullOrEmpty(effect.kind))
            return false;

        if (state.PendingChoice != null)
            return false;

        switch (effect.kind)
        {
            case GenericEffectKinds.Draw:
                DrawCards(state, player, Mathf.Max(0, effect.amount), random ?? NewRandom());
                return true;

            case GenericEffectKinds.AddActions:
                player.Actions += Mathf.Max(0, effect.amount);
                return true;

            case GenericEffectKinds.AddBuys:
                player.Buys += Mathf.Max(0, effect.amount);
                return true;

            case GenericEffectKinds.AddCoins:
                player.Coins += Mathf.Max(0, effect.amount);
                return true;

            case GenericEffectKinds.DiscardFromHand:
            case GenericEffectKinds.TrashFromHand:
                CreateHandChoice(state, player, effect, sourceCardInstanceId);
                return true;

            default:
                Debug.LogWarning("Unknown generic effect kind: " + effect.kind);
                return false;
        }
    }

    public static bool ToggleChoiceSelection(GameStateSnapshot state, string playerId, int instanceId)
    {
        PendingChoiceSnapshot choice = state != null ? state.PendingChoice : null;
        if (choice == null || !choice.IsFor(playerId) || choice.ValidInstanceIds == null ||
            !choice.ValidInstanceIds.Contains(instanceId))
            return false;

        if (choice.SelectedInstanceIds == null)
            choice.SelectedInstanceIds = new List<int>();

        if (choice.SelectedInstanceIds.Contains(instanceId))
        {
            choice.SelectedInstanceIds.Remove(instanceId);
            return true;
        }

        int maximum = Mathf.Max(0, choice.MaxSelections);
        if (maximum > 0 && choice.SelectedInstanceIds.Count >= maximum)
            return false;

        choice.SelectedInstanceIds.Add(instanceId);
        return true;
    }

    public static bool ResolvePendingChoice(GameStateSnapshot state, string playerId)
    {
        PendingChoiceSnapshot choice = state != null ? state.PendingChoice : null;
        if (choice == null || !choice.IsFor(playerId))
            return false;

        PlayerStateSnapshot player = state.Players != null
            ? state.Players.Find(candidate => candidate != null && candidate.PlayerId == playerId)
            : null;
        if (player == null)
            return false;

        if (choice.SelectedInstanceIds == null)
            choice.SelectedInstanceIds = new List<int>();

        int count = choice.SelectedInstanceIds.Count;
        int minimum = choice.Optional ? 0 : Mathf.Max(0, choice.MinSelections);
        int maximum = Mathf.Max(minimum, choice.MaxSelections);
        if (count < minimum || (maximum > 0 && count > maximum))
            return false;

        foreach (int instanceId in new List<int>(choice.SelectedInstanceIds))
        {
            if (!player.Hand.Remove(instanceId))
                continue;

            if (string.Equals(choice.Kind, GenericEffectKinds.DiscardFromHand, StringComparison.Ordinal))
                player.Discard.Add(instanceId);
            else if (string.Equals(choice.Kind, GenericEffectKinds.TrashFromHand, StringComparison.Ordinal))
            {
                if (state.TrashedCards == null)
                    state.TrashedCards = new List<int>();
                state.TrashedCards.Add(instanceId);
            }
        }

        state.PendingChoice = null;
        return true;
    }

    private static void CreateHandChoice(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        GenericCardEffect effect,
        int sourceCardInstanceId)
    {
        int available = player.Hand != null ? player.Hand.Count : 0;
        int max = effect.max > 0 ? Mathf.Min(effect.max, available) : available;
        int min = Mathf.Min(Mathf.Max(0, effect.min), max);

        string defaultPrompt = string.Equals(effect.kind, GenericEffectKinds.TrashFromHand, StringComparison.Ordinal)
            ? "Choisissez les cartes à écarter."
            : "Choisissez les cartes à défausser.";

        state.PendingChoice = new PendingChoiceSnapshot
        {
            ChoiceId = Guid.NewGuid().ToString("N"),
            PlayerId = player.PlayerId,
            Kind = effect.kind,
            Prompt = string.IsNullOrWhiteSpace(effect.prompt) ? defaultPrompt : effect.prompt,
            SourceCardInstanceId = sourceCardInstanceId,
            MinSelections = min,
            MaxSelections = max,
            Optional = effect.optional,
            ValidInstanceIds = player.Hand != null ? new List<int>(player.Hand) : new List<int>(),
            SelectedInstanceIds = new List<int>()
        };
    }

    private static void DrawCards(
        GameStateSnapshot state,
        PlayerStateSnapshot player,
        int count,
        System.Random random)
    {
        if (state == null || player == null || count <= 0)
            return;

        if (player.Deck == null) player.Deck = new List<int>();
        if (player.Discard == null) player.Discard = new List<int>();
        if (player.Hand == null) player.Hand = new List<int>();

        for (int i = 0; i < count; i++)
        {
            if (player.Deck.Count == 0)
            {
                if (player.Discard.Count == 0)
                    break;

                player.Deck.AddRange(player.Discard);
                player.Discard.Clear();
                Shuffle(player.Deck, random);
            }

            int top = player.Deck.Count - 1;
            int instanceId = player.Deck[top];
            player.Deck.RemoveAt(top);
            player.Hand.Add(instanceId);
        }
    }

    private static void Shuffle(List<int> cards, System.Random random)
    {
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            int tmp = cards[i];
            cards[i] = cards[j];
            cards[j] = tmp;
        }
    }

    private static System.Random NewRandom()
    {
        return new System.Random(Guid.NewGuid().GetHashCode());
    }
}
