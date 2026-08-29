using System;
using System.Collections.Generic;
using System.Linq;

public static class JournalRules
{
    public const string RevealKind = "reveal";
    public const string PlayedKind = "played";
    public const string GainedKind = "gained";
    public const string ChoiceKind = "choice";
    public const string ChatKind = "chat";
    public const int ChatCooldownMilliseconds = 1000;
    public const int MaxChatLength = 200;
    private const int MaxEntries = 128;

    public static void RecordReveal(GameStateSnapshot state, PlayerStateSnapshot player, int cardInstanceId)
    {
        CardInstance card = FindCard(state, cardInstanceId);
        if (card != null) RecordReveal(state, player, card.DefinitionId);
    }

    public static void RecordReveal(GameStateSnapshot state, PlayerStateSnapshot player, string definitionId) =>
        Append(state, RevealKind, player, definitionId, string.Empty, string.Empty);

    public static void RecordRevealZone(GameStateSnapshot state, PlayerStateSnapshot player, CardZone zone)
    {
        List<int> cards = CardZoneRules.ResolveZone(player, zone);
        if (cards == null) return;
        foreach (int id in new List<int>(cards)) RecordReveal(state, player, id);
    }

    public static void PublishReveal(GameStateSnapshot state, PlayerStateSnapshot player, int cardInstanceId,
        CardZone sourceZone, int sourceCardInstanceId, GameEventBus eventBus)
    {
        CardInstance card = FindCard(state, cardInstanceId);
        if (card == null) return;
        RecordReveal(state, player, card.DefinitionId);
        eventBus?.Publish(GameEvent.CardRevealed(player != null ? player.PlayerId : string.Empty,
            cardInstanceId, card.DefinitionId, sourceZone, sourceCardInstanceId));
    }

    public static void PublishRevealZone(GameStateSnapshot state, PlayerStateSnapshot player, CardZone zone,
        int sourceCardInstanceId, GameEventBus eventBus)
    {
        List<int> cards = CardZoneRules.ResolveZone(state, player, zone);
        if (cards == null) return;
        foreach (int id in new List<int>(cards))
            PublishReveal(state, player, id, zone, sourceCardInstanceId, eventBus);
    }

    public static void RecordEvents(GameStateSnapshot state, IEnumerable<GameEvent> events)
    {
        if (state == null || events == null) return;
        foreach (GameEvent gameEvent in events)
        {
            if (gameEvent == null) continue;
            string kind;
            if (gameEvent.Type == GameEventType.CardPlayed) kind = PlayedKind;
            else if (gameEvent.Type == GameEventType.CardGained || gameEvent.Type == GameEventType.ArtifactGained) kind = GainedKind;
            else continue;
            string definitionId = gameEvent.CardDefinitionId;
            if (string.IsNullOrWhiteSpace(definitionId))
                definitionId = FindCard(state, gameEvent.CardInstanceId)?.DefinitionId ?? string.Empty;
            Append(state, kind, FindPlayer(state, gameEvent.PlayerId), definitionId,
                string.Empty, string.Empty);
        }
    }

    public static void RecordInstanceChoice(GameStateSnapshot state, PendingDecisionSnapshot decision, IEnumerable<int> selected)
    {
        List<string> labels = new List<string>();
        if (selected != null)
            foreach (int id in selected)
            {
                CardInstance card = FindCard(state, id);
                labels.Add(CardName(card != null ? card.DefinitionId : string.Empty));
            }
        RecordChoice(state, decision, labels);
    }

    public static void RecordDefinitionChoice(GameStateSnapshot state, PendingDecisionSnapshot decision, IEnumerable<string> selected) =>
        RecordChoice(state, decision, selected != null ? selected.Select(CardName) : null);

    public static void RecordOptionChoice(GameStateSnapshot state, PendingDecisionSnapshot decision, IEnumerable<string> selected)
    {
        List<string> labels = new List<string>();
        if (selected != null)
            foreach (string id in selected)
            {
                int index = decision != null && decision.CandidateDefinitionIds != null
                    ? decision.CandidateDefinitionIds.FindIndex(candidate => string.Equals(candidate, id, StringComparison.Ordinal)) : -1;
                labels.Add(index >= 0 && decision.CandidateOptionLabels != null && index < decision.CandidateOptionLabels.Count
                    ? decision.CandidateOptionLabels[index] : id);
            }
        RecordChoice(state, decision, labels);
    }

    public static bool TryRecordChat(GameStateSnapshot state, string playerId, string rawMessage,
        long nowUnixMilliseconds, out string error)
    {
        error = string.Empty;
        PlayerStateSnapshot player = FindPlayer(state, playerId);
        if (state == null || player == null || !player.IsConnected)
        { error = "Le joueur n’est pas connecté à cette partie."; return false; }
        string message = SanitiseChat(rawMessage);
        if (message.Length == 0) { error = "Le message est vide."; return false; }
        if (player.LastChatMessageUnixMilliseconds > 0 &&
            nowUnixMilliseconds - player.LastChatMessageUnixMilliseconds < ChatCooldownMilliseconds)
        { error = "Veuillez attendre une seconde avant d’envoyer un autre message."; return false; }
        player.LastChatMessageUnixMilliseconds = nowUnixMilliseconds;
        Append(state, ChatKind, player, string.Empty, string.Empty, message);
        return true;
    }

    private static void RecordChoice(GameStateSnapshot state, PendingDecisionSnapshot decision, IEnumerable<string> values)
    {
        if (state == null || decision == null) return;
        string sourceDefinitionId = FindCard(state, decision.SourceCardInstanceId)?.DefinitionId ?? string.Empty;
        List<string> clean = values != null
            ? values.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value.Trim()).ToList()
            : new List<string>();
        Append(state, ChoiceKind, FindPlayer(state, decision.PlayerId), string.Empty, sourceDefinitionId,
            clean.Count > 0 ? string.Join(", ", clean) : "Passe");
    }

    private static void Append(GameStateSnapshot state, string kind, PlayerStateSnapshot player,
        string definitionId, string sourceDefinitionId, string message)
    {
        if (state == null || player == null || string.IsNullOrWhiteSpace(kind)) return;
        if (state.Journal == null) state.Journal = new List<GameJournalEntrySnapshot>();
        EnsureSequence(state);
        state.Journal.Add(new GameJournalEntrySnapshot
        {
            Sequence = state.NextJournalSequence++, TurnNumber = state.TurnNumber, Kind = kind,
            PlayerId = player.PlayerId,
            PlayerName = string.IsNullOrWhiteSpace(player.NickName) ? "Joueur" : player.NickName,
            CardDefinitionId = definitionId ?? string.Empty,
            SourceCardDefinitionId = sourceDefinitionId ?? string.Empty,
            Message = message ?? string.Empty
        });
        while (state.Journal.Count > MaxEntries) state.Journal.RemoveAt(0);
    }

    private static void EnsureSequence(GameStateSnapshot state)
    {
        if (state.NextJournalSequence > 0) return;
        state.NextJournalSequence = state.Journal.Count > 0
            ? state.Journal.Max(entry => entry != null ? entry.Sequence : 0) + 1 : 1;
    }

    private static string SanitiseChat(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string clean = value.Replace('\r', ' ').Replace('\n', ' ').Trim();
        while (clean.Contains("  ")) clean = clean.Replace("  ", " ");
        return clean.Length <= MaxChatLength ? clean : clean.Substring(0, MaxChatLength).TrimEnd();
    }

    private static PlayerStateSnapshot FindPlayer(GameStateSnapshot state, string playerId) =>
        state != null && state.Players != null
            ? state.Players.Find(player => player != null && player.PlayerId == playerId) : null;

    private static CardInstance FindCard(GameStateSnapshot state, int instanceId) =>
        state != null && instanceId > 0 && state.CardInstances != null
            ? state.CardInstances.Find(card => card != null && card.InstanceId == instanceId) : null;

    private static string CardName(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return "carte inconnue";
        ExtensionPackageData extension; ExtensionCardData definition;
        return RoomGameSetup.TryResolveCard(definitionId, out extension, out definition) && definition != null
            ? definition.name : definitionId;
    }
}
