using System;
using System.Collections.Generic;
using System.Linq;
using Photon.Pun;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class EndGameFlowController
{
    private void ShowFinalRanking()
    {
        if (_scoreRoutine != null)
        {
            StopCoroutine(_scoreRoutine);
            _scoreRoutine = null;
        }

        ClearStage();
        _stageRoot = CreatePanel("FinalRankingStage", _surface, new Vector2(0.06f, 0.055f), new Vector2(0.94f, 0.945f), Window);

        Text title = CreateText("Title", _stageRoot, "CLASSEMENT FINAL", 40, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
        SetAnchors(title.rectTransform, new Vector2(0.22f, 0.89f), new Vector2(0.78f, 0.975f));
        AddPanelBehind(title.rectTransform, "TitleBanner", BlueBanner, 10f);

        List<RankedPlayerScore> ranking = FinalRankingRules.Calculate(_finalState);
        RectTransform rankingPanel = CreatePanel("Ranking", _stageRoot, new Vector2(0.035f, 0.13f), new Vector2(0.46f, 0.84f), InnerPanel);
        RectTransform detailPanel = CreatePanel("Detail", _stageRoot, new Vector2(0.49f, 0.13f), new Vector2(0.965f, 0.84f), InnerPanel);
        BuildRankingList(rankingPanel, detailPanel, ranking);

        CreateButton("ReturnToLobby", _stageRoot, "RETOUR AU SALON", new Vector2(0.38f, 0.025f), new Vector2(0.62f, 0.095f), GreenButton, ReturnToLobby);
    }

    private void BuildRankingList(RectTransform rankingPanel, RectTransform detailPanel, List<RankedPlayerScore> ranking)
    {
        _rankingButtons.Clear();
        Text heading = CreateText("Heading", rankingPanel, "JOUEURS", 25, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
        SetAnchors(heading.rectTransform, new Vector2(0.04f, 0.87f), new Vector2(0.96f, 0.97f));

        float top = 0.82f;
        float rowHeight = ranking.Count > 0 ? Mathf.Min(0.145f, 0.65f / ranking.Count) : 0.145f;
        RankedPlayerScore defaultSelection = null;
        string localId = NetworkGameState.LocalPlayerId;

        for (int i = 0; i < ranking.Count; i++)
        {
            RankedPlayerScore item = ranking[i];
            float yMax = top - (i * rowHeight);
            float yMin = yMax - rowHeight + 0.009f;
            Color background = item.Rank == 1 ? WinnerRow : RowDark;
            Button button = CreateRankingRow(rankingPanel, item, new Vector2(0.04f, yMin), new Vector2(0.96f, yMax), background);
            _rankingButtons.Add(button);
            RankedPlayerScore captured = item;
            button.onClick.AddListener(() =>
            {
                HighlightRankingButton(button);
                RenderPlayerDetail(detailPanel, captured);
            });

            if (string.Equals(item.Score.PlayerId, localId, StringComparison.Ordinal))
                defaultSelection = item;
        }

        if (defaultSelection == null && ranking.Count > 0)
            defaultSelection = ranking[0];

        if (defaultSelection != null)
        {
            int index = ranking.IndexOf(defaultSelection);
            if (index >= 0 && index < _rankingButtons.Count)
                HighlightRankingButton(_rankingButtons[index]);
            RenderPlayerDetail(detailPanel, defaultSelection);
        }
    }

    private Button CreateRankingRow(RectTransform parent, RankedPlayerScore item, Vector2 min, Vector2 max, Color background)
    {
        GameObject obj = new GameObject("Rank_" + item.Rank + "_" + Sanitize(item.Score.PlayerId), typeof(RectTransform), typeof(Image), typeof(Button));
        RectTransform row = obj.GetComponent<RectTransform>();
        row.SetParent(parent, false);
        SetAnchors(row, min, max);
        Image image = obj.GetComponent<Image>();
        image.color = background;
        Button button = obj.GetComponent<Button>();
        button.targetGraphic = image;

        string rankLabel = item.IsTied ? "=" + item.Rank : item.Rank.ToString();
        Text rank = CreateText("Rank", row, rankLabel, 28, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
        SetAnchors(rank.rectTransform, new Vector2(0.02f, 0.08f), new Vector2(0.16f, 0.92f));

        Text name = CreateText("Name", row, string.IsNullOrEmpty(item.Score.PlayerName) ? "Joueur" : item.Score.PlayerName, 22, TextAnchor.MiddleLeft, Color.white, item.Rank == 1 ? FontStyle.Bold : FontStyle.Normal);
        SetAnchors(name.rectTransform, new Vector2(0.18f, 0.08f), new Vector2(0.65f, 0.92f));

        Text scoreText;
        CreatePointValue(row, "Score", item.Score.VictoryPoints, out scoreText, new Vector2(0.68f, 0.08f), new Vector2(0.97f, 0.92f), 26, Gold);
        return button;
    }

    private void HighlightRankingButton(Button selected)
    {
        foreach (Button button in _rankingButtons)
        {
            if (button == null) continue;
            Image image = button.GetComponent<Image>();
            if (image == null) continue;
            bool winner = button.gameObject.name.StartsWith("Rank_1_", StringComparison.Ordinal);
            image.color = button == selected ? RowSelected : winner ? WinnerRow : RowDark;
        }
    }

    private void RenderPlayerDetail(RectTransform panel, RankedPlayerScore ranked)
    {
        if (panel == null || ranked == null)
            return;

        ClearChildren(panel);
        PlayerScoreResult score = ranked.Score;
        Text title = CreateText("DetailTitle", panel, "DÉTAIL DES POINTS — " + (string.IsNullOrEmpty(score.PlayerName) ? "JOUEUR" : score.PlayerName.ToUpperInvariant()), 23, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
        SetAnchors(title.rectTransform, new Vector2(0.04f, 0.87f), new Vector2(0.96f, 0.97f));

        Text meta = CreateText("Meta", panel, score.TotalCards + " cartes possédées  •  " + ranked.TurnsTaken + " tours joués", 16, TextAnchor.MiddleCenter, new Color(0.78f, 0.74f, 0.65f, 1f));
        SetAnchors(meta.rectTransform, new Vector2(0.05f, 0.80f), new Vector2(0.95f, 0.865f));

        List<CardScoreBreakdown> breakdown = OrderedBreakdown(score.Breakdown);
        float top = 0.76f;
        float rowHeight = breakdown.Count > 0 ? Mathf.Min(0.112f, 0.55f / breakdown.Count) : 0.112f;
        for (int i = 0; i < breakdown.Count; i++)
        {
            float yMax = top - (i * rowHeight);
            float yMin = yMax - rowHeight + 0.007f;
            CreateScoreRow(panel, breakdown[i], new Vector2(0.045f, yMin), new Vector2(0.955f, yMax), false);
        }

        RectTransform totalBar = CreatePanel("Total", panel, new Vector2(0.045f, 0.055f), new Vector2(0.955f, 0.15f), new Color(0.085f, 0.075f, 0.06f, 1f));
        Text totalLabel = CreateText("Label", totalBar, "TOTAL", 26, TextAnchor.MiddleLeft, Gold, FontStyle.Bold);
        SetAnchors(totalLabel.rectTransform, new Vector2(0.04f, 0.05f), new Vector2(0.45f, 0.95f));
        Text totalText;
        CreatePointValue(totalBar, "Value", score.VictoryPoints, out totalText, new Vector2(0.61f, 0.05f), new Vector2(0.96f, 0.95f), 31, Gold);
    }

    private void ReturnToLobby()
    {
        if (RoomConnectionHandler.Instance != null)
        {
            RoomConnectionHandler.Instance.LeaveCurrentRoomPermanently();
            return;
        }

        if (PhotonNetwork.InRoom)
            PhotonNetwork.LeaveRoom(false);
    }

    private PlayerStateSnapshot ResolveLocalPlayer(GameStateSnapshot state)
    {
        if (state == null || state.Players == null)
            return null;

        string localId = NetworkGameState.LocalPlayerId;
        PlayerStateSnapshot local = state.Players.Find(player => player != null && player.PlayerId == localId);
        if (local != null)
            return local;

        if (PhotonNetwork.LocalPlayer != null)
        {
            int actor = PhotonNetwork.LocalPlayer.ActorNumber;
            local = state.Players.Find(player => player != null && player.ActorNumber == actor);
            if (local != null)
                return local;
        }

        return state.Players.Count == 1 ? state.Players[0] : null;
    }

    private static List<int> CollectOwnedCardInstances(PlayerStateSnapshot player)
    {
        HashSet<int> unique = new HashSet<int>();
        AddZone(unique, player != null ? player.Deck : null);
        AddZone(unique, player != null ? player.Hand : null);
        AddZone(unique, player != null ? player.Discard : null);
        AddZone(unique, player != null ? player.InPlay : null);
        AddZone(unique, player != null ? player.Inspected : null);
        return unique.ToList();
    }

    private static void AddZone(HashSet<int> destination, List<int> zone)
    {
        if (destination == null || zone == null) return;
        foreach (int instanceId in zone)
            if (instanceId > 0)
                destination.Add(instanceId);
    }

    private static List<CardScoreBreakdown> OrderedBreakdown(IReadOnlyList<CardScoreBreakdown> source)
    {
        List<CardScoreBreakdown> result = source != null ? source.ToList() : new List<CardScoreBreakdown>();
        result.Sort((a, b) =>
        {
            int orderA = BaseDisplayOrder(a != null ? a.DefinitionId : null);
            int orderB = BaseDisplayOrder(b != null ? b.DefinitionId : null);
            if (orderA != orderB) return orderA.CompareTo(orderB);
            return string.Compare(a != null ? a.CardName : string.Empty, b != null ? b.CardName : string.Empty, StringComparison.CurrentCultureIgnoreCase);
        });
        return result;
    }

    private static int BaseDisplayOrder(string definitionId)
    {
        if (string.Equals(definitionId, "base:domaine", StringComparison.OrdinalIgnoreCase)) return 10;
        if (string.Equals(definitionId, "base:duche", StringComparison.OrdinalIgnoreCase)) return 20;
        if (string.Equals(definitionId, "base:province", StringComparison.OrdinalIgnoreCase)) return 30;
        if (string.Equals(definitionId, "base:jardins", StringComparison.OrdinalIgnoreCase)) return 40;
        if (string.Equals(definitionId, "base:malediction", StringComparison.OrdinalIgnoreCase)) return 90;
        return 50;
    }

    private static Sprite LoadCardSprite(string definitionId)
    {
        if (string.IsNullOrEmpty(definitionId)) return null;
        if (!RoomGameSetup.TryResolveCard(definitionId, out ExtensionPackageData extension, out ExtensionCardData definition))
            return null;
        return ExtensionVisualLoader.LoadCardArtwork(extension, definition);
    }

    private string FormatEndReason(GameStateSnapshot state)
    {
        if (state == null) return "Fin de partie";
        if (string.Equals(state.GameEndReason, GameEndRules.ProvinceEmptyReason, StringComparison.Ordinal))
            return "La pile Province est épuisée.";
        if (string.Equals(state.GameEndReason, GameEndRules.ThreePilesEmptyReason, StringComparison.Ordinal))
            return "Trois piles de la Réserve sont épuisées.";
        return "La partie est terminée.";
    }

    private static void Shuffle<T>(IList<T> list, int seed)
    {
        System.Random random = new System.Random(seed);
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            T temp = list[i];
            list[i] = list[j];
            list[j] = temp;
        }
    }

}
