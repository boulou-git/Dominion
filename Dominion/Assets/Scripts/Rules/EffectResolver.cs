using System;
using System.Collections.Generic;

/// <summary>
/// Result returned by one declarative effect resolution.
/// WaitingForChoice is reserved now so the resolver API does not need to change later
/// when interactive effects (discard, trash, gain, choose-one, etc.) are introduced.
/// </summary>
public enum EffectResolutionStatus
{
    Applied,
    WaitingForChoice,
    Rejected
}

public readonly struct EffectResolutionResult
{
    public EffectResolutionStatus Status { get; }
    public string Error { get; }

    public bool Succeeded => Status == EffectResolutionStatus.Applied;

    private EffectResolutionResult(EffectResolutionStatus status, string error)
    {
        Status = status;
        Error = error ?? string.Empty;
    }

    public static EffectResolutionResult Applied()
    {
        return new EffectResolutionResult(EffectResolutionStatus.Applied, string.Empty);
    }

    public static EffectResolutionResult WaitingForChoice()
    {
        return new EffectResolutionResult(EffectResolutionStatus.WaitingForChoice, string.Empty);
    }

    public static EffectResolutionResult Rejected(string error)
    {
        return new EffectResolutionResult(EffectResolutionStatus.Rejected, error);
    }
}

/// <summary>
/// Minimal context required by the rules engine to resolve one effect.
/// The state passed here is expected to be an authoritative working copy; the resolver
/// deliberately knows nothing about Photon, scenes, MonoBehaviours or UI.
///
/// Random is injected so tests can use a deterministic seed and the future authoritative
/// rules layer can own every shuffle instead of hiding randomness inside an effect handler.
/// </summary>
public sealed class EffectExecutionContext
{
    public GameStateSnapshot State { get; }
    public PlayerStateSnapshot Actor { get; }
    public int SourceCardInstanceId { get; }
    public System.Random Random { get; }

    public EffectExecutionContext(
        GameStateSnapshot state,
        PlayerStateSnapshot actor,
        int sourceCardInstanceId = 0,
        System.Random random = null)
    {
        State = state;
        Actor = actor;
        SourceCardInstanceId = sourceCardInstanceId;
        Random = random;
    }
}

/// <summary>
/// Central registry/dispatcher for declarative card effects.
/// Adding a new generic operation means registering one handler here rather than adding
/// card-id conditionals to PlayTreasure/PlayActionCard/NetworkGameState.
///
/// IMPORTANT: this resolver is intentionally not wired into live gameplay yet.
/// Current Treasure behaviour remains unchanged until this layer has been validated.
/// </summary>
public static class EffectResolver
{
    private delegate EffectResolutionResult EffectHandler(
        CardEffectData effect,
        EffectExecutionContext context);

    private static readonly Dictionary<string, EffectHandler> Handlers =
        new Dictionary<string, EffectHandler>(StringComparer.OrdinalIgnoreCase)
        {
            { "add_resource", ResolveAddResource },
            { "draw", ResolveDraw }
        };

    public static bool IsSupported(string operation)
    {
        return !string.IsNullOrWhiteSpace(operation) && Handlers.ContainsKey(operation);
    }

    public static EffectResolutionResult Resolve(
        CardEffectData effect,
        EffectExecutionContext context)
    {
        if (effect == null)
            return EffectResolutionResult.Rejected("Effect is null.");

        if (context == null || context.State == null || context.Actor == null)
            return EffectResolutionResult.Rejected("Effect execution context is incomplete.");

        if (string.IsNullOrWhiteSpace(effect.op))
            return EffectResolutionResult.Rejected("Effect operation is missing.");

        if (!Handlers.TryGetValue(effect.op, out EffectHandler handler))
            return EffectResolutionResult.Rejected("Unsupported effect operation: " + effect.op);

        return handler(effect, context);
    }

    private static EffectResolutionResult ResolveAddResource(
        CardEffectData effect,
        EffectExecutionContext context)
    {
        if (!TargetsSelf(effect))
            return EffectResolutionResult.Rejected(
                "add_resource currently supports target 'self' only.");

        if (effect.amount < 0)
            return EffectResolutionResult.Rejected(
                "add_resource amount cannot be negative. Use a dedicated spend/remove effect later.");

        if (string.IsNullOrWhiteSpace(effect.resource))
            return EffectResolutionResult.Rejected("add_resource resource is missing.");

        switch (effect.resource.Trim().ToLowerInvariant())
        {
            case "actions":
                context.Actor.Actions += effect.amount;
                break;

            case "buys":
                context.Actor.Buys += effect.amount;
                break;

            case "coins":
                context.Actor.Coins += effect.amount;
                break;

            default:
                return EffectResolutionResult.Rejected(
                    "Unsupported add_resource resource: " + effect.resource);
        }

        return EffectResolutionResult.Applied();
    }

    private static EffectResolutionResult ResolveDraw(
        CardEffectData effect,
        EffectExecutionContext context)
    {
        if (!TargetsSelf(effect))
            return EffectResolutionResult.Rejected("draw currently supports target 'self' only.");

        if (effect.amount < 0)
            return EffectResolutionResult.Rejected("draw amount cannot be negative.");

        PlayerStateSnapshot player = context.Actor;
        if (player.Deck == null || player.Hand == null || player.Discard == null)
            return EffectResolutionResult.Rejected("draw requires deck, hand and discard zones.");

        for (int i = 0; i < effect.amount; i++)
        {
            if (player.Deck.Count == 0)
            {
                if (player.Discard.Count == 0)
                    break;

                if (context.Random == null)
                    return EffectResolutionResult.Rejected(
                        "draw requires an injected random source when the discard pile must be shuffled.");

                player.Deck.AddRange(player.Discard);
                player.Discard.Clear();
                Shuffle(player.Deck, context.Random);
            }

            int topIndex = player.Deck.Count - 1;
            int instanceId = player.Deck[topIndex];
            player.Deck.RemoveAt(topIndex);
            player.Hand.Add(instanceId);
        }

        return EffectResolutionResult.Applied();
    }

    private static bool TargetsSelf(CardEffectData effect)
    {
        return effect != null &&
               string.Equals(effect.target, "self", StringComparison.OrdinalIgnoreCase);
    }

    private static void Shuffle(List<int> cards, System.Random random)
    {
        for (int i = cards.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            int temp = cards[i];
            cards[i] = cards[j];
            cards[j] = temp;
        }
    }
}
