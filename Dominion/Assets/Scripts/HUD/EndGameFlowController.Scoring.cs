using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class EndGameFlowController
{
    private void BuildAnimatedBreakdown(RectTransform parent, PlayerScoreResult score)
    {
        _animatedRows.Clear();
        _animatedRunningTotal = 0;

        RectTransform rowsRoot = parent.Find("ScoreRows") as RectTransform;
        Transform total = parent.Find("ScoreTotal");
        _scoreTotalText = total != null ? total.Find("Value")?.GetComponent<Text>() : null;
        if (rowsRoot == null || _scoreTotalText == null)
        {
            Debug.LogError("EndGameScoringStage Breakdown contract is incomplete.", parent);
            return;
        }

        List<CardScoreBreakdown> breakdown = OrderedBreakdown(score.Breakdown);
        for (int i = 0; i < breakdown.Count; i++)
        {
            CardScoreBreakdown data = breakdown[i];
            ScoreRowVisual row = CreateScoreRow(rowsRoot, data, true);
            if (row == null) continue;
            _animatedRows[data.DefinitionId] = row;
        }
    }

    private ScoreRowVisual CreateScoreRow(RectTransform parent, CardScoreBreakdown data, bool animated)
    {
        GameObject prefab = Resources.Load<GameObject>(ScoreRowPrefabPath);
        if (prefab == null)
        {
            Debug.LogError("EndGameScoreRow prefab missing.", this);
            return null;
        }
        RectTransform row = Instantiate(prefab, parent).GetComponent<RectTransform>();
        row.gameObject.name = "Score_" + Sanitize(data.DefinitionId);
        Image art = row.Find("Artwork")?.GetComponent<Image>();
        Text name = row.Find("Name")?.GetComponent<Text>();
        Text copies = row.Find("Copies")?.GetComponent<Text>();
        Text points = row.Find("Points/Value")?.GetComponent<Text>();
        if (art == null || name == null || copies == null || points == null)
        {
            Debug.LogError("EndGameScoreRow prefab contract is incomplete.", row);
            Destroy(row.gameObject);
            return null;
        }
        art.sprite = LoadCardSprite(data.DefinitionId);
        art.color = art.sprite != null ? Color.white : new Color(0.28f, 0.24f, 0.18f, 1f);
        name.text = data.CardName;
        copies.text = animated ? "× 0" : "× " + data.Copies;
        points.text = (animated ? 0 : data.TotalPoints).ToString();

        return new ScoreRowVisual
        {
            Data = data,
            Root = row,
            CopiesText = copies,
            PointsText = points,
            RevealedCopies = animated ? 0 : data.Copies
        };
    }

    private IEnumerator AnimateOwnedCards(PlayerStateSnapshot player, PlayerScoreResult score)
    {
        List<int> owned = CollectOwnedCardInstances(player);
        Shuffle(owned, (_finalState != null ? _finalState.TurnNumber : 1) ^ owned.Count);
        _activeCardAnimations = 0;

        Dictionary<string, CardScoreBreakdown> scoring = score.Breakdown.ToDictionary(item => item.DefinitionId, item => item, StringComparer.OrdinalIgnoreCase);

        foreach (int instanceId in owned)
        {
            CardInstance instance = NetworkGameState.FindCardInstance(_finalState, instanceId);
            if (instance == null || string.IsNullOrEmpty(instance.DefinitionId))
                continue;

            bool scores = scoring.ContainsKey(instance.DefinitionId) && _animatedRows.ContainsKey(instance.DefinitionId);
            Sprite sprite = LoadCardSprite(instance.DefinitionId) ?? _cardBack;
            RoomGameSetup.TryResolveCard(instance.DefinitionId, out ExtensionPackageData extension, out ExtensionCardData definition);
            RectTransform destination = scores ? _animatedRows[instance.DefinitionId].Root : _nonScoringSink;
            CardScoreBreakdown scoredCard = scores ? scoring[instance.DefinitionId] : null;
            _activeCardAnimations++;
            StartCoroutine(FlyCard(sprite, definition, destination, scores, scoredCard));
            yield return new WaitForSeconds(0.045f);
        }

        while (_activeCardAnimations > 0)
            yield return null;

        _animatedRunningTotal = score.VictoryPoints;
        if (_scoreTotalText != null) _scoreTotalText.text = score.VictoryPoints.ToString();
        foreach (CardScoreBreakdown data in score.Breakdown)
        {
            if (!_animatedRows.TryGetValue(data.DefinitionId, out ScoreRowVisual row))
                continue;
            row.RevealedCopies = data.Copies;
            if (row.CopiesText != null) row.CopiesText.text = "× " + data.Copies;
            if (row.PointsText != null) row.PointsText.text = data.TotalPoints.ToString();
        }

        if (_scoreStatus != null) _scoreStatus.text = "Décompte terminé";
        if (_continueButton != null) _continueButton.gameObject.SetActive(true);
        _scoreRoutine = null;
    }

    private IEnumerator FlyCard(Sprite sprite, ExtensionCardData definition, RectTransform destination, bool scores, CardScoreBreakdown scoringData)
    {
        if (_animationLayer == null || _sourceDeck == null || destination == null)
        {
            ApplyScoreArrival(scores, scoringData);
            _activeCardAnimations--;
            yield break;
        }

        RuntimeCardView cardView = RuntimeCardView.Create(_animationLayer, "ScoringCard", definition, sprite, false);
        if (cardView == null)
        {
            ApplyScoreArrival(scores, scoringData);
            _activeCardAnimations--;
            yield break;
        }
        GameObject obj = cardView.gameObject;
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(92f, 142f);
        rect.position = _sourceDeck.position;
        rect.localScale = Vector3.one * 0.82f;

        CanvasGroup group = obj.GetComponent<CanvasGroup>();
        if (group == null)
        {
            Debug.LogError("RuntimeCard prefab must contain a CanvasGroup for scoring animations.", obj);
            Destroy(obj);
            ApplyScoreArrival(scores, scoringData);
            _activeCardAnimations--;
            yield break;
        }
        Vector3 start = _sourceDeck.position;
        Vector3 end = destination.position;
        float randomSide = UnityEngine.Random.Range(-55f, 55f);
        end += new Vector3(randomSide, UnityEngine.Random.Range(-24f, 24f), 0f);
        float startRotation = UnityEngine.Random.Range(-10f, 10f);
        float duration = UnityEngine.Random.Range(0.28f, 0.38f);
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            Vector3 position = Vector3.Lerp(start, end, eased);
            position.y += Mathf.Sin(t * Mathf.PI) * 64f;
            rect.position = position;
            rect.localScale = Vector3.one * Mathf.Lerp(0.82f, 0.50f, eased);
            rect.localRotation = Quaternion.Euler(0f, 0f, Mathf.Lerp(startRotation, 0f, eased));
            group.alpha = scores ? 1f : Mathf.Lerp(1f, 0.2f, Mathf.Clamp01((t - 0.7f) / 0.3f));
            yield return null;
        }

        ApplyScoreArrival(scores, scoringData);
        Destroy(obj);
        _activeCardAnimations--;
    }

    private void ApplyScoreArrival(bool scores, CardScoreBreakdown scoringData)
    {
        if (!scores || scoringData == null || !_animatedRows.TryGetValue(scoringData.DefinitionId, out ScoreRowVisual row))
            return;

        row.RevealedCopies = Mathf.Min(row.Data.Copies, row.RevealedCopies + 1);
        if (row.CopiesText != null) row.CopiesText.text = "× " + row.RevealedCopies;
        int subtotal = row.RevealedCopies * row.Data.PointsPerCopy;
        if (row.PointsText != null) row.PointsText.text = subtotal.ToString();
        _animatedRunningTotal += row.Data.PointsPerCopy;
        if (_scoreTotalText != null) _scoreTotalText.text = _animatedRunningTotal.ToString();
    }

}
