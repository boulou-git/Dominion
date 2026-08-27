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
        GameObject prefab = Resources.Load<GameObject>(RankingStagePrefabPath);
        if (prefab == null)
        {
            Debug.LogError("EndGameRankingStage prefab missing.", this);
            return;
        }
        _stageRoot = Instantiate(prefab, _surface).GetComponent<RectTransform>();

        List<RankedPlayerScore> ranking = FinalRankingRules.Calculate(_finalState);
        RectTransform rankingPanel = _stageRoot.Find("Ranking") as RectTransform;
        RectTransform detailPanel = _stageRoot.Find("Detail") as RectTransform;
        Button returnButton = _stageRoot.Find("ReturnToLobby")?.GetComponent<Button>();
        if (rankingPanel == null || detailPanel == null || returnButton == null)
        {
            Debug.LogError("EndGameRankingStage prefab contract is incomplete.", _stageRoot);
            return;
        }
        BuildRankingList(rankingPanel, detailPanel, ranking);
        returnButton.onClick.AddListener(ReturnToLobby);
    }

    private void BuildRankingList(RectTransform rankingPanel, RectTransform detailPanel, List<RankedPlayerScore> ranking)
    {
        _rankingButtons.Clear();
        RectTransform rowsRoot = rankingPanel.Find("RankingRows") as RectTransform;
        if (rowsRoot == null)
        {
            Debug.LogError("EndGameRankingStage Ranking/Rows is missing.", rankingPanel);
            return;
        }
        RankedPlayerScore defaultSelection = null;
        string localId = NetworkGameState.LocalPlayerId;

        for (int i = 0; i < ranking.Count; i++)
        {
            RankedPlayerScore item = ranking[i];
            Color background = item.Rank == 1 ? WinnerRow : RowDark;
            Button button = CreateRankingRow(rowsRoot, item, background);
            if (button == null) continue;
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

    private Button CreateRankingRow(RectTransform parent, RankedPlayerScore item, Color background)
    {
        GameObject prefab = Resources.Load<GameObject>(RankingRowPrefabPath);
        if (prefab == null)
        {
            Debug.LogError("EndGameRankingRow prefab missing.", this);
            return null;
        }
        GameObject obj = Instantiate(prefab, parent);
        obj.name = "Rank_" + item.Rank + "_" + Sanitize(item.Score.PlayerId);
        Image image = obj.GetComponent<Image>();
        Button button = obj.GetComponent<Button>();
        Text rank = obj.transform.Find("Rank")?.GetComponent<Text>();
        Text name = obj.transform.Find("Name")?.GetComponent<Text>();
        Text scoreText = obj.transform.Find("Score/Value")?.GetComponent<Text>();
        if (image == null || button == null || rank == null || name == null || scoreText == null)
        {
            Debug.LogError("EndGameRankingRow prefab contract is incomplete.", obj);
            Destroy(obj);
            return null;
        }
        image.color = background;

        string rankLabel = item.IsTied ? "=" + item.Rank : item.Rank.ToString();
        rank.text = rankLabel;
        name.text = string.IsNullOrEmpty(item.Score.PlayerName) ? "Joueur" : item.Score.PlayerName;
        name.fontStyle = item.Rank == 1 ? FontStyle.Bold : FontStyle.Normal;
        scoreText.text = item.Score.VictoryPoints.ToString();
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

        RectTransform rowsRoot = panel.Find("DetailRows") as RectTransform;
        Text title = panel.Find("DetailTitle")?.GetComponent<Text>();
        Text meta = panel.Find("Meta")?.GetComponent<Text>();
        Text totalText = panel.Find("DetailTotal/Value")?.GetComponent<Text>();
        if (rowsRoot == null || title == null || meta == null || totalText == null)
        {
            Debug.LogError("EndGameRankingStage Detail contract is incomplete.", panel);
            return;
        }
        ClearChildren(rowsRoot);
        PlayerScoreResult score = ranked.Score;
        title.text = "DÉTAIL DES POINTS — " + (string.IsNullOrEmpty(score.PlayerName) ? "JOUEUR" : score.PlayerName.ToUpperInvariant());
        meta.text = score.TotalCards + " cartes possédées  •  " + ranked.TurnsTaken + " tours joués";

        List<CardScoreBreakdown> breakdown = OrderedBreakdown(score.Breakdown);
        for (int i = 0; i < breakdown.Count; i++)
            CreateScoreRow(rowsRoot, breakdown[i], false);
        totalText.text = score.VictoryPoints.ToString();
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
