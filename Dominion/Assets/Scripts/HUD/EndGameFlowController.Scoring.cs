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

        Text heading = CreateText("Heading", parent, "CARTES DE POINTS", 24, TextAnchor.MiddleCenter, Gold, FontStyle.Bold);
        SetAnchors(heading.rectTransform, new Vector2(0.05f, 0.86f), new Vector2(0.95f, 0.97f));

        List<CardScoreBreakdown> breakdown = OrderedBreakdown(score.Breakdown);
        float top = 0.82f;
        float rowHeight = breakdown.Count > 0 ? Mathf.Min(0.115f, 0.60f / breakdown.Count) : 0.115f;
        for (int i = 0; i < breakdown.Count; i++)
        {
            CardScoreBreakdown data = breakdown[i];
            float yMax = top - (i * rowHeight);
            float yMin = yMax - rowHeight + 0.008f;
            ScoreRowVisual row = CreateScoreRow(parent, data, new Vector2(0.055f, yMin), new Vector2(0.945f, yMax), true);
            _animatedRows[data.DefinitionId] = row;
        }

        RectTransform totalBar = CreatePanel("Total", parent, new Vector2(0.055f, 0.055f), new Vector2(0.945f, 0.15f), new Color(0.085f, 0.075f, 0.06f, 1f));
        Text totalLabel = CreateText("Label", totalBar, "TOTAL", 27, TextAnchor.MiddleLeft, Gold, FontStyle.Bold);
        SetAnchors(totalLabel.rectTransform, new Vector2(0.04f, 0.05f), new Vector2(0.43f, 0.95f));
        CreatePointValue(totalBar, "TotalValue", 0, out _scoreTotalText, new Vector2(0.60f, 0.05f), new Vector2(0.96f, 0.95f), 31, Gold);
    }

    private ScoreRowVisual CreateScoreRow(RectTransform parent, CardScoreBreakdown data, Vector2 min, Vector2 max, bool animated)
    {
        RectTransform row = CreatePanel("Score_" + Sanitize(data.DefinitionId), parent, min, max, Parchment);
        Image rowImage = row.GetComponent<Image>();
        rowImage.raycastTarget = false;

        RectTransform artRoot = CreateEmptyRect("Artwork", row, new Vector2(0.015f, 0.08f), new Vector2(0.145f, 0.92f));
        Image art = artRoot.gameObject.AddComponent<Image>();
        art.sprite = LoadCardSprite(data.DefinitionId);
        art.preserveAspect = true;
        art.color = art.sprite != null ? Color.white : new Color(0.28f, 0.24f, 0.18f, 1f);
        art.raycastTarget = false;

        Text name = CreateText("Name", row, data.CardName, 20, TextAnchor.MiddleLeft, ParchmentDark, FontStyle.Bold);
        SetAnchors(name.rectTransform, new Vector2(0.17f, 0.05f), new Vector2(0.58f, 0.95f));

        Text copies = CreateText("Copies", row, animated ? "× 0" : "× " + data.Copies, 19, TextAnchor.MiddleCenter, ParchmentDark);
        SetAnchors(copies.rectTransform, new Vector2(0.58f, 0.05f), new Vector2(0.73f, 0.95f));

        Text points;
        CreatePointValue(row, "Points", animated ? 0 : data.TotalPoints, out points, new Vector2(0.74f, 0.05f), new Vector2(0.985f, 0.95f), 21, ParchmentDark);

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
            RectTransform destination = scores ? _animatedRows[instance.DefinitionId].Root : _nonScoringSink;
            CardScoreBreakdown scoredCard = scores ? scoring[instance.DefinitionId] : null;
            _activeCardAnimations++;
            StartCoroutine(FlyCard(sprite, destination, scores, scoredCard));
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

    private IEnumerator FlyCard(Sprite sprite, RectTransform destination, bool scores, CardScoreBreakdown scoringData)
    {
        if (_animationLayer == null || _sourceDeck == null || destination == null)
        {
            ApplyScoreArrival(scores, scoringData);
            _activeCardAnimations--;
            yield break;
        }

        GameObject obj = new GameObject("ScoringCard", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
        RectTransform rect = obj.GetComponent<RectTransform>();
        rect.SetParent(_animationLayer, false);
        rect.sizeDelta = new Vector2(92f, 142f);
        rect.position = _sourceDeck.position;
        rect.localScale = Vector3.one * 0.82f;

        Image image = obj.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.color = sprite != null ? Color.white : new Color(0.3f, 0.25f, 0.18f, 1f);
        image.raycastTarget = false;

        CanvasGroup group = obj.GetComponent<CanvasGroup>();
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
