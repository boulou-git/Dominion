using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Renders replicated public journal entries over the existing journal Text slot.
/// Card names are separate raycast targets so left/right-click inspection is precise.
/// </summary>
public sealed class PublicJournalView : MonoBehaviour
{
    private const int MaxVisibleEntries = 16;

    private GameScreenController _screen;
    private Text _journalText;
    private GameObject _zoomOverlay;
    private Image _zoomImage;
    private RectTransform _root;
    private readonly List<GameObject> _rows = new List<GameObject>();
    private int _lastRenderedVersion = -1;

    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private void Awake()
    {
        _screen = GetComponent<GameScreenController>();
        BindExistingUi();
        NetworkGameState.StateChanged += Refresh;
        Refresh(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= Refresh;
    }

    private void BindExistingUi()
    {
        if (_screen == null) return;
        _journalText = ReadField<Text>("_journalText");
        _zoomOverlay = ReadField<GameObject>("_zoomOverlay");
        _zoomImage = ReadField<Image>("_zoomImage");
        EnsureRoot();
    }

    private T ReadField<T>(string fieldName) where T : class
    {
        FieldInfo field = typeof(GameScreenController).GetField(fieldName, PrivateInstance);
        return field != null ? field.GetValue(_screen) as T : null;
    }

    private void EnsureRoot()
    {
        if (_root != null || _journalText == null) return;

        _journalText.enabled = false;
        _journalText.raycastTarget = false;

        GameObject rootObject = new GameObject("PublicJournalEntries", typeof(RectTransform), typeof(VerticalLayoutGroup));
        rootObject.transform.SetParent(_journalText.transform, false);
        _root = rootObject.GetComponent<RectTransform>();
        _root.anchorMin = Vector2.zero;
        _root.anchorMax = Vector2.one;
        _root.offsetMin = Vector2.zero;
        _root.offsetMax = Vector2.zero;

        VerticalLayoutGroup layout = rootObject.GetComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 3f;
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private void Refresh(GameStateSnapshot state)
    {
        if (_journalText == null || _root == null) BindExistingUi();
        if (_root == null) return;
        if (_journalText != null) _journalText.enabled = false;

        int version = state != null ? state.Version : -1;
        if (_lastRenderedVersion == version) return;
        _lastRenderedVersion = version;

        ClearRows();
        if (state == null || state.Players == null || state.Players.Count == 0)
        {
            CreatePlainRow("En attente de la partie…", true);
            return;
        }

        PlayerStateSnapshot active = state.Players.Find(p => p != null && p.PlayerId == state.ActivePlayerId);
        string activeName = active != null && !string.IsNullOrWhiteSpace(active.NickName) ? active.NickName : "Joueur";
        CreatePlainRow("Tour " + state.TurnNumber + " — " + activeName + " — " + PhaseLabel(state.Phase), true);

        List<GameJournalEntrySnapshot> journal = state.Journal;
        if (journal == null || journal.Count == 0)
        {
            CreatePlainRow("Aucune révélation publique.", false);
            return;
        }

        int first = Math.Max(0, journal.Count - MaxVisibleEntries);
        for (int i = first; i < journal.Count; i++)
        {
            GameJournalEntrySnapshot entry = journal[i];
            if (entry == null || !string.Equals(entry.Kind, JournalRules.RevealKind, StringComparison.OrdinalIgnoreCase)) continue;
            CreateRevealRow(entry);
        }

        LayoutRebuilder.ForceRebuildLayoutImmediate(_root);
    }

    private void CreatePlainRow(string message, bool bold)
    {
        GameObject row = CreateRow("JournalLine");
        CreateInlineText(row.transform, message, Color.white, bold, false, null);
    }

    private void CreateRevealRow(GameJournalEntrySnapshot entry)
    {
        GameObject row = CreateRow("Reveal_" + entry.Sequence);
        string playerName = string.IsNullOrWhiteSpace(entry.PlayerName) ? "Joueur" : entry.PlayerName;
        CreateInlineText(row.transform, "T" + entry.TurnNumber + " · " + playerName + " révèle ", Color.white, false, false, null);

        ExtensionPackageData extension;
        ExtensionCardData definition;
        if (!RoomGameSetup.TryResolveCard(entry.CardDefinitionId, out extension, out definition) || definition == null)
        {
            CreateInlineText(row.transform, entry.CardDefinitionId ?? "carte inconnue", Color.white, true, false, null);
        }
        else
        {
            Color cardColor = ResolveCardNameColor(entry.CardDefinitionId, definition);
            string definitionId = entry.CardDefinitionId;
            CreateInlineText(row.transform, definition.name, cardColor, true, true, () => InspectCard(definitionId));
        }

        CreateInlineText(row.transform, ".", Color.white, false, false, null);
    }

    private GameObject CreateRow(string name)
    {
        GameObject row = new GameObject(name, typeof(RectTransform), typeof(HorizontalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(_root, false);

        HorizontalLayoutGroup layout = row.GetComponent<HorizontalLayoutGroup>();
        layout.padding = new RectOffset(0, 0, 0, 0);
        layout.spacing = 0f;
        layout.childAlignment = TextAnchor.MiddleLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = false;
        layout.childForceExpandHeight = false;

        LayoutElement element = row.GetComponent<LayoutElement>();
        element.preferredHeight = Math.Max(18f, JournalFontSize() + 4f);
        element.minHeight = element.preferredHeight;
        _rows.Add(row);
        return row;
    }

    private Text CreateInlineText(Transform parent, string content, Color color, bool bold, bool clickable, Action inspect)
    {
        GameObject textObject = new GameObject(clickable ? "CardName" : "Text", typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        textObject.transform.SetParent(parent, false);

        Text text = textObject.GetComponent<Text>();
        text.font = _journalText != null && _journalText.font != null
            ? _journalText.font
            : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = JournalFontSize();
        text.fontStyle = bold ? FontStyle.Bold : FontStyle.Normal;
        text.alignment = TextAnchor.MiddleLeft;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.color = color;
        text.raycastTarget = clickable;
        text.text = content ?? string.Empty;

        LayoutElement element = textObject.GetComponent<LayoutElement>();
        element.preferredWidth = text.preferredWidth + 1f;
        element.minWidth = element.preferredWidth;
        element.preferredHeight = Math.Max(18f, text.preferredHeight);
        element.flexibleWidth = 0f;

        if (clickable && inspect != null)
        {
            CardPointerInteraction pointer = textObject.AddComponent<CardPointerInteraction>();
            pointer.InspectOnLongPress = false;
            pointer.PrimaryActionRequested += inspect;
            pointer.InspectRequested += inspect;
        }

        return text;
    }

    private int JournalFontSize()
    {
        return _journalText != null && _journalText.fontSize > 0 ? _journalText.fontSize : 14;
    }

    private static Color ResolveCardNameColor(string definitionId, ExtensionCardData definition)
    {
        if (definition != null && CardDefinitionRules.HasType(definition, "Réaction"))
            return new Color32(46, 74, 112, 255); // dark blue
        if (string.Equals(definitionId, "base:cuivre", StringComparison.OrdinalIgnoreCase))
            return new Color32(184, 115, 51, 255);
        if (string.Equals(definitionId, "base:argent", StringComparison.OrdinalIgnoreCase))
            return new Color32(192, 192, 192, 255);
        if (string.Equals(definitionId, "base:or", StringComparison.OrdinalIgnoreCase))
            return new Color32(212, 175, 55, 255);
        if (definition != null && CardDefinitionRules.HasType(definition, "Victoire"))
            return new Color32(76, 139, 87, 255);
        if (definition != null && CardDefinitionRules.HasType(definition, "Action"))
            return new Color32(160, 160, 160, 255);
        return Color.white;
    }

    private void InspectCard(string definitionId)
    {
        ExtensionPackageData extension;
        ExtensionCardData definition;
        if (!RoomGameSetup.TryResolveCard(definitionId, out extension, out definition) || definition == null) return;
        Sprite sprite = ExtensionVisualLoader.LoadCardArtwork(extension, definition);
        if (sprite == null) return;

        if (_zoomOverlay == null || _zoomImage == null) BindExistingUi();
        if (_zoomOverlay != null && _zoomImage != null)
        {
            _zoomImage.sprite = sprite;
            _zoomImage.preserveAspect = true;
            _zoomOverlay.SetActive(true);
            _zoomOverlay.transform.SetAsLastSibling();
            return;
        }

        ShowFallbackZoom(sprite);
    }

    private void ShowFallbackZoom(Sprite sprite)
    {
        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        Transform existing = canvas.transform.Find("JournalZoomFallback");
        if (existing != null) Destroy(existing.gameObject);

        GameObject overlay = new GameObject("JournalZoomFallback", typeof(RectTransform), typeof(Image), typeof(Button));
        overlay.transform.SetParent(canvas.transform, false);
        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.anchorMin = Vector2.zero;
        overlayRect.anchorMax = Vector2.one;
        overlayRect.offsetMin = Vector2.zero;
        overlayRect.offsetMax = Vector2.zero;
        overlay.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.78f);
        overlay.GetComponent<Button>().onClick.AddListener(() => Destroy(overlay));

        GameObject imageObject = new GameObject("Card", typeof(RectTransform), typeof(Image));
        imageObject.transform.SetParent(overlay.transform, false);
        RectTransform cardRect = imageObject.GetComponent<RectTransform>();
        cardRect.anchorMin = new Vector2(0.37f, 0.13f);
        cardRect.anchorMax = new Vector2(0.63f, 0.87f);
        cardRect.offsetMin = Vector2.zero;
        cardRect.offsetMax = Vector2.zero;
        Image image = imageObject.GetComponent<Image>();
        image.sprite = sprite;
        image.preserveAspect = true;
        image.raycastTarget = false;
        overlay.transform.SetAsLastSibling();
    }

    private void ClearRows()
    {
        foreach (GameObject row in _rows) if (row != null) Destroy(row);
        _rows.Clear();
    }

    private static string PhaseLabel(string phase)
    {
        switch (phase)
        {
            case NetworkGameState.ActionPhase: return "phase ACTION";
            case NetworkGameState.BuyPhase: return "phase ACHAT";
            case NetworkGameState.CleanupPhase: return "phase AJUSTEMENT";
            default: return string.IsNullOrWhiteSpace(phase) ? string.Empty : phase;
        }
    }
}

/// <summary>
/// Installs PublicJournalView even when the editable GameScreen is instantiated after scene load.
/// </summary>
public sealed class PublicJournalBootstrap : MonoBehaviour
{
    private float _nextScan;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (GameObject.Find("DominionPublicJournalBootstrap") != null) return;
        GameObject installer = new GameObject("DominionPublicJournalBootstrap");
        DontDestroyOnLoad(installer);
        installer.AddComponent<PublicJournalBootstrap>();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 0.5f;

        GameScreenController[] screens = Resources.FindObjectsOfTypeAll<GameScreenController>();
        foreach (GameScreenController screen in screens)
        {
            if (screen == null || !screen.gameObject.scene.IsValid()) continue;
            if (screen.GetComponent<PublicJournalView>() == null)
                screen.gameObject.AddComponent<PublicJournalView>();
        }
    }
}
