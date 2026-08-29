using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Binds extension-owned Reserve elements to prefab-authored compact visuals.
/// Special piles and available Artefacts occupy a right rail only when present;
/// the Kingdom grid is then reduced just enough to keep all ten piles visible.
/// </summary>
public sealed class ReserveExtrasController : MonoBehaviour
{
    private const string ExtrasPrefabPath = "UI/ReserveExtrasUi";
    private const string SpecialPilePrefabPath = "UI/SpecialPileTile";
    private const string ArtifactPrefabPath = "UI/ArtifactTile";

    [SerializeField] private RectTransform _supplyPanel;
    [SerializeField] private GridLayoutGroup _kingdomGrid;
    [SerializeField] private GameObject _zoomOverlay;
    [SerializeField] private Image _zoomImage;

    private GameObject _extrasUi;
    private RectTransform _extrasRect;
    private RectTransform _specialPilesRoot;
    private RectTransform _artifactsRoot;
    private GameObject _specialPilesLabel;
    private GameObject _artifactsLabel;
    private GameObject _specialPilePrefab;
    private GameObject _artifactPrefab;
    private Vector2 _prefabKingdomCellSize;
    private string _renderedSignature;
    private string _kingdomSignature;
    private ExtensionComponentUsage _componentUsage = new ExtensionComponentUsage();
    private Coroutine _layoutRoutine;

    private void Awake()
    {
        if (_kingdomGrid != null)
            _prefabKingdomCellSize = _kingdomGrid.cellSize;

        BuildUi();
        NetworkGameState.StateChanged += Refresh;
        Refresh(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= Refresh;
        if (_layoutRoutine != null)
            StopCoroutine(_layoutRoutine);
    }

    private void OnRectTransformDimensionsChange()
    {
        if (_extrasUi != null && _extrasUi.activeSelf)
            ScheduleLayoutRefresh();
    }

    private void BuildUi()
    {
        if (_extrasUi != null || _supplyPanel == null)
            return;

        GameObject extrasPrefab = Resources.Load<GameObject>(ExtrasPrefabPath);
        _specialPilePrefab = Resources.Load<GameObject>(SpecialPilePrefabPath);
        _artifactPrefab = Resources.Load<GameObject>(ArtifactPrefabPath);
        if (extrasPrefab == null || _specialPilePrefab == null || _artifactPrefab == null)
        {
            Debug.LogError("Reserve extension UI prefabs are missing from Resources/UI.", this);
            return;
        }

        _extrasUi = Instantiate(extrasPrefab, _supplyPanel, false);
        _extrasUi.name = "ReserveExtras";
        _extrasRect = _extrasUi.GetComponent<RectTransform>();
        _specialPilesRoot = _extrasUi.transform.Find("SpecialPiles") as RectTransform;
        _artifactsRoot = _extrasUi.transform.Find("AvailableArtifacts") as RectTransform;
        Transform specialLabel = _extrasUi.transform.Find("SpecialPilesLabel");
        Transform artifactLabel = _extrasUi.transform.Find("ArtifactsLabel");
        _specialPilesLabel = specialLabel != null ? specialLabel.gameObject : null;
        _artifactsLabel = artifactLabel != null ? artifactLabel.gameObject : null;
        if (_extrasRect == null || _specialPilesRoot == null || _artifactsRoot == null)
        {
            Debug.LogError("ReserveExtrasUi.prefab contract is incomplete.", _extrasUi);
            Destroy(_extrasUi);
            _extrasUi = null;
            return;
        }

        _extrasUi.SetActive(false);
    }

    private void Refresh(GameStateSnapshot state)
    {
        BuildUi();
        if (_extrasUi == null)
            return;

        RefreshComponentUsage(state);
        bool hasSpecialPiles = HasRelevantSpecialPiles(state);
        bool hasArtifacts = HasRelevantArtifacts(state);
        bool visible = hasSpecialPiles || hasArtifacts;

        _extrasUi.SetActive(visible);
        _specialPilesRoot.gameObject.SetActive(hasSpecialPiles);
        _artifactsRoot.gameObject.SetActive(hasArtifacts);
        if (_specialPilesLabel != null)
            _specialPilesLabel.SetActive(hasSpecialPiles);
        if (_artifactsLabel != null)
            _artifactsLabel.SetActive(hasArtifacts);
        if (!visible)
        {
            _renderedSignature = null;
            RestoreKingdomCellSize();
            return;
        }

        string signature = BuildSignature(state);
        if (!string.Equals(signature, _renderedSignature, StringComparison.Ordinal))
        {
            _renderedSignature = signature;
            RebuildSpecialPiles(state);
            RebuildArtifacts(state);
        }

        ScheduleLayoutRefresh();
    }

    private void RebuildSpecialPiles(GameStateSnapshot state)
    {
        ClearChildren(_specialPilesRoot);
        if (state == null || state.SpecialPiles == null)
            return;

        foreach (SpecialPileSnapshot pile in state.SpecialPiles)
        {
            if (pile == null || !_componentUsage.UsesSpecialPile(pile.PileId))
                continue;

            GameObject tile = Instantiate(_specialPilePrefab, _specialPilesRoot, false);
            tile.name = "SpecialPile_" + SafeObjectName(pile.PileId);
            Text nameText = tile.transform.Find("Name")?.GetComponent<Text>();
            Text countText = tile.transform.Find("Count")?.GetComponent<Text>();
            int count = pile.CardInstanceIds != null ? pile.CardInstanceIds.Count : 0;
            if (nameText != null)
                nameText.text = string.IsNullOrWhiteSpace(pile.DisplayName) ? pile.PileId : pile.DisplayName;
            if (countText != null)
                countText.text = count.ToString();

            if (count <= 0)
                continue;

            int instanceId = pile.CardInstanceIds[count - 1];
            CardInstance instance = NetworkGameState.FindCardInstance(state, instanceId);
            if (!TryResolveVisual(instance, out ExtensionCardData definition, out Sprite sprite))
                continue;

            CardPointerInteraction pointer = tile.GetComponent<CardPointerInteraction>();
            if (pointer != null)
            {
                pointer.InspectOnLongPress = false;
                Sprite capturedSprite = sprite;
                ExtensionCardData capturedDefinition = definition;
                pointer.InspectRequested += () => ShowDetail(capturedSprite, capturedDefinition, true);
            }
        }
    }

    private void RebuildArtifacts(GameStateSnapshot state)
    {
        ClearChildren(_artifactsRoot);
        if (state == null || state.UnownedArtifacts == null)
            return;

        foreach (int instanceId in state.UnownedArtifacts)
        {
            CardInstance instance = NetworkGameState.FindCardInstance(state, instanceId);
            if (instance == null || !_componentUsage.UsesArtifact(instance.DefinitionId))
                continue;
            if (!TryResolveVisual(instance, out ExtensionCardData definition, out Sprite sprite))
                continue;

            GameObject tile = Instantiate(_artifactPrefab, _artifactsRoot, false);
            tile.name = "AvailableArtifact_" + SafeObjectName(definition.id);
            Text label = tile.transform.Find("Label")?.GetComponent<Text>();
            Text kind = tile.transform.Find("Kind")?.GetComponent<Text>();
            Image artwork = tile.transform.Find("Artwork")?.GetComponent<Image>();
            if (label != null)
                label.text = definition.name;
            if (kind != null)
                kind.text = "DISPONIBLE";
            if (artwork != null)
            {
                artwork.sprite = sprite;
                artwork.gameObject.SetActive(sprite != null);
            }

            Sprite capturedSprite = sprite;
            ExtensionCardData capturedDefinition = definition;
            Button button = tile.GetComponent<Button>();
            if (button != null)
            {
                button.interactable = sprite != null;
                if (sprite != null)
                    button.onClick.AddListener(() => ShowDetail(capturedSprite, capturedDefinition, false));
            }

            CardPointerInteraction pointer = tile.GetComponent<CardPointerInteraction>();
            if (pointer != null && sprite != null)
            {
                pointer.InspectOnLongPress = false;
                pointer.InspectRequested += () => ShowDetail(capturedSprite, capturedDefinition, false);
            }
        }
    }

    private static bool TryResolveVisual(CardInstance instance, out ExtensionCardData definition, out Sprite sprite)
    {
        definition = null;
        sprite = null;
        if (instance == null || string.IsNullOrWhiteSpace(instance.DefinitionId))
            return false;

        if (!RoomGameSetup.TryResolveCard(instance.DefinitionId,
                out ExtensionPackageData extension, out definition) || definition == null)
            return false;

        sprite = ExtensionVisualLoader.LoadCardArtwork(extension, definition);
        return sprite != null;
    }

    private void ShowDetail(Sprite sprite, ExtensionCardData definition, bool showCost)
    {
        if (sprite == null || _zoomOverlay == null || _zoomImage == null)
            return;

        _zoomImage.sprite = sprite;
        _zoomImage.preserveAspect = true;
        DynamicCardCostView costView = DynamicCardCostView.Attach(_zoomImage.gameObject,
            showCost ? definition : null);
        if (costView != null)
            costView.Bind(showCost ? definition : null);
        _zoomOverlay.SetActive(true);
        _zoomOverlay.transform.SetAsLastSibling();
    }

    private void ScheduleLayoutRefresh()
    {
        if (!isActiveAndEnabled || _layoutRoutine != null)
            return;
        _layoutRoutine = StartCoroutine(RefreshLayoutNextFrame());
    }

    private IEnumerator RefreshLayoutNextFrame()
    {
        yield return null;
        Canvas.ForceUpdateCanvases();
        FitKingdomGridBeforeExtras();
        _layoutRoutine = null;
    }

    private void FitKingdomGridBeforeExtras()
    {
        if (_kingdomGrid == null || _extrasRect == null || !_extrasUi.activeSelf)
            return;

        RectTransform gridRect = _kingdomGrid.transform as RectTransform;
        if (gridRect == null || gridRect.rect.width <= 0f || gridRect.rect.height <= 0f)
            return;

        Vector3 extrasLeftWorld = _extrasRect.TransformPoint(new Vector3(_extrasRect.rect.xMin, 0f, 0f));
        float extrasLeftInGrid = gridRect.InverseTransformPoint(extrasLeftWorld).x;
        float rightEdge = Mathf.Min(gridRect.rect.xMax, extrasLeftInGrid);
        float availableWidth = rightEdge - gridRect.rect.xMin -
                               _kingdomGrid.padding.left - _kingdomGrid.padding.right;
        float availableHeight = gridRect.rect.height -
                                _kingdomGrid.padding.top - _kingdomGrid.padding.bottom;
        int columns = Mathf.Max(1, _kingdomGrid.constraintCount);
        const int rows = 2;
        float widthSize = (availableWidth - _kingdomGrid.spacing.x * (columns - 1)) / columns;
        float heightSize = (availableHeight - _kingdomGrid.spacing.y * (rows - 1)) / rows;
        float prefabSize = Mathf.Min(_prefabKingdomCellSize.x, _prefabKingdomCellSize.y);
        float size = Mathf.Max(1f, Mathf.Min(prefabSize, widthSize, heightSize));
        _kingdomGrid.cellSize = new Vector2(size, size);
        LayoutRebuilder.ForceRebuildLayoutImmediate(gridRect);
    }

    private void RestoreKingdomCellSize()
    {
        if (_kingdomGrid == null || _prefabKingdomCellSize == Vector2.zero)
            return;
        _kingdomGrid.cellSize = _prefabKingdomCellSize;
        RectTransform rect = _kingdomGrid.transform as RectTransform;
        if (rect != null)
            LayoutRebuilder.ForceRebuildLayoutImmediate(rect);
    }

    private void RefreshComponentUsage(GameStateSnapshot state)
    {
        string signature = BuildKingdomSignature(state);
        if (string.Equals(signature, _kingdomSignature, StringComparison.Ordinal))
            return;

        _kingdomSignature = signature;
        _renderedSignature = null;
        List<string> kingdomCards = new List<string>();
        if (state != null && state.SupplyPiles != null)
            foreach (SupplyPileSnapshot pile in state.SupplyPiles)
                if (pile != null && pile.IsKingdom && !string.IsNullOrWhiteSpace(pile.DefinitionId))
                    kingdomCards.Add(pile.DefinitionId);
        _componentUsage = ExtensionComponentUsageResolver.Resolve(kingdomCards);
    }

    private bool HasRelevantSpecialPiles(GameStateSnapshot state)
    {
        if (state == null || state.SpecialPiles == null)
            return false;
        foreach (SpecialPileSnapshot pile in state.SpecialPiles)
            if (pile != null && _componentUsage.UsesSpecialPile(pile.PileId))
                return true;
        return false;
    }

    private bool HasRelevantArtifacts(GameStateSnapshot state)
    {
        if (state == null || state.UnownedArtifacts == null)
            return false;
        foreach (int instanceId in state.UnownedArtifacts)
        {
            CardInstance instance = NetworkGameState.FindCardInstance(state, instanceId);
            if (instance != null && _componentUsage.UsesArtifact(instance.DefinitionId))
                return true;
        }
        return false;
    }

    private static string BuildKingdomSignature(GameStateSnapshot state)
    {
        StringBuilder builder = new StringBuilder();
        if (state != null && state.SupplyPiles != null)
            foreach (SupplyPileSnapshot pile in state.SupplyPiles)
                if (pile != null && pile.IsKingdom)
                    builder.Append(pile.DefinitionId).Append('|');
        return builder.ToString();
    }

    private string BuildSignature(GameStateSnapshot state)
    {
        StringBuilder builder = new StringBuilder();
        if (state.SpecialPiles != null)
        {
            foreach (SpecialPileSnapshot pile in state.SpecialPiles)
            {
                if (pile == null || !_componentUsage.UsesSpecialPile(pile.PileId))
                    continue;
                int count = pile.CardInstanceIds != null ? pile.CardInstanceIds.Count : 0;
                int top = count > 0 ? pile.CardInstanceIds[count - 1] : 0;
                builder.Append(pile.PileId).Append(':').Append(count).Append(':').Append(top).Append('|');
            }
        }
        builder.Append('#');
        if (state.UnownedArtifacts != null)
            foreach (int instanceId in state.UnownedArtifacts)
            {
                CardInstance instance = NetworkGameState.FindCardInstance(state, instanceId);
                if (instance != null && _componentUsage.UsesArtifact(instance.DefinitionId))
                    builder.Append(instanceId).Append(',');
            }
        return builder.ToString();
    }

    private static string SafeObjectName(string value)
    {
        return string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Replace(':', '_').Replace('/', '_');
    }

    private static void ClearChildren(RectTransform root)
    {
        if (root == null)
            return;
        for (int i = root.childCount - 1; i >= 0; i--)
        {
            GameObject child = root.GetChild(i).gameObject;
            child.SetActive(false);
            Destroy(child);
        }
    }
}
