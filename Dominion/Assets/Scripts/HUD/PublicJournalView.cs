using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UI;

/// <summary>Prefab-backed presentation of the replicated activity journal and chat.</summary>
public sealed class PublicJournalView : MonoBehaviour
{
    private const int MaxVisibleEntries = 64;
    private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

    private GameScreenController _screen;
    private Text _legacyJournalText;
    private GameObject _zoomOverlay;
    private Image _zoomImage;
    private GameObject _surface;
    private RectTransform _content;
    private ScrollRect _scroll;
    private InputField _input;
    private Button _sendButton;
    private GameObject _entryPrefab;
    private readonly List<GameObject> _rows = new List<GameObject>();
    private int _lastRenderedVersion = int.MinValue;
    private float _nextLocalSendTime;

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
        if (_sendButton != null) _sendButton.onClick.RemoveListener(SendChat);
        if (_input != null) _input.onSubmit.RemoveListener(SubmitChat);
    }

    private void Update()
    {
        if (_sendButton != null) _sendButton.interactable = Time.unscaledTime >= _nextLocalSendTime;
        Refresh(NetworkGameState.State);
    }

    private void BindExistingUi()
    {
        if (_screen == null) return;
        _legacyJournalText = ReadField<Text>("_journalText");
        _zoomOverlay = ReadField<GameObject>("_zoomOverlay");
        _zoomImage = ReadField<Image>("_zoomImage");
        EnsureSurface();
    }

    private T ReadField<T>(string fieldName) where T : class
    {
        FieldInfo field = typeof(GameScreenController).GetField(fieldName, PrivateInstance);
        return field != null ? field.GetValue(_screen) as T : null;
    }

    private void EnsureSurface()
    {
        if (_surface != null || _legacyJournalText == null) return;
        GameObject prefab = Resources.Load<GameObject>("UI/JournalSurface");
        _entryPrefab = Resources.Load<GameObject>("UI/JournalEntry");
        if (prefab == null || _entryPrefab == null)
        {
            Debug.LogError("Journal prefab contract is incomplete: JournalSurface or JournalEntry is missing.", this);
            return;
        }

        _surface = Instantiate(prefab, _legacyJournalText.transform, false);
        _content = _surface.transform.Find("EntriesScroll/Viewport/Content") as RectTransform;
        _scroll = _surface.transform.Find("EntriesScroll")?.GetComponent<ScrollRect>();
        _input = _surface.transform.Find("Composer/MessageInput")?.GetComponent<InputField>();
        _sendButton = _surface.transform.Find("Composer/SendButton")?.GetComponent<Button>();
        if (_content == null || _scroll == null || _input == null || _sendButton == null)
        {
            Debug.LogError("JournalSurface prefab contract is incomplete.", this);
            Destroy(_surface); _surface = null; return;
        }
        _legacyJournalText.enabled = false;
        _legacyJournalText.raycastTarget = false;
        _input.characterLimit = JournalRules.MaxChatLength;
        _input.onSubmit.AddListener(SubmitChat);
        _sendButton.onClick.AddListener(SendChat);
    }

    private void SubmitChat(string value)
    {
        SendChat();
        if (_input != null) _input.ActivateInputField();
    }

    private void SendChat()
    {
        if (_input == null || Time.unscaledTime < _nextLocalSendTime) return;
        string message = _input.text != null ? _input.text.Trim() : string.Empty;
        if (message.Length == 0) return;
        if (PlayersTurnsHandler.Instance == null)
        {
            Debug.LogError("Cannot send chat message: PlayersTurnsHandler is unavailable.", this);
            return;
        }
        PlayersTurnsHandler.Instance.SendChatMessage(message);
        _input.text = string.Empty;
        _nextLocalSendTime = Time.unscaledTime + 1f;
    }

    private void Refresh(GameStateSnapshot state)
    {
        if (_surface == null) BindExistingUi();
        if (_surface == null || _content == null) return;
        int version = state != null ? state.Version : -1;
        if (_lastRenderedVersion == version) return;
        _lastRenderedVersion = version;
        ClearRows();

        if (state == null || state.Players == null || state.Players.Count == 0)
        {
            CreateRow("En attente de la partie…", string.Empty, true);
            return;
        }

        PlayerStateSnapshot active = state.Players.Find(player => player != null && player.PlayerId == state.ActivePlayerId);
        string activeName = active != null && !string.IsNullOrWhiteSpace(active.NickName) ? active.NickName : "Joueur";
        CreateRow("Tour " + state.TurnNumber + " — " + activeName + " — " + PhaseLabel(state.Phase), string.Empty, true);

        List<GameJournalEntrySnapshot> journal = state.Journal;
        if (journal == null || journal.Count == 0)
            CreateRow("Aucune activité publique.", string.Empty, false);
        else
        {
            int first = Math.Max(0, journal.Count - MaxVisibleEntries);
            for (int index = first; index < journal.Count; index++)
                CreateEntryRow(journal[index]);
        }

        Canvas.ForceUpdateCanvases();
        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        _scroll.verticalNormalizedPosition = 0f;
    }

    private void CreateEntryRow(GameJournalEntrySnapshot entry)
    {
        if (entry == null) return;
        string player = string.IsNullOrWhiteSpace(entry.PlayerName) ? "Joueur" : entry.PlayerName;
        string cardName = ResolveCardName(entry.CardDefinitionId);
        string sourceName = ResolveCardName(entry.SourceCardDefinitionId);
        string text;
        string inspectId = entry.CardDefinitionId;
        switch (entry.Kind)
        {
            case JournalRules.ChatKind:
                text = player + " : " + entry.Message;
                inspectId = string.Empty;
                break;
            case JournalRules.PlayedKind:
                text = "T" + entry.TurnNumber + " · " + player + " joue " + cardName + ".";
                break;
            case JournalRules.GainedKind:
                text = "T" + entry.TurnNumber + " · " + player + " reçoit " + cardName + ".";
                break;
            case JournalRules.ChoiceKind:
                text = "T" + entry.TurnNumber + " · " + player + " choisit « " + entry.Message + " »" +
                       (!string.IsNullOrWhiteSpace(sourceName) ? " pour " + sourceName : string.Empty) + ".";
                inspectId = entry.SourceCardDefinitionId;
                break;
            case JournalRules.RevealKind:
                text = "T" + entry.TurnNumber + " · " + player + " révèle " + cardName + ".";
                break;
            default:
                return;
        }
        CreateRow(text, inspectId, false);
    }

    private void CreateRow(string message, string inspectDefinitionId, bool header)
    {
        GameObject row = Instantiate(_entryPrefab, _content, false);
        row.name = header ? "JournalHeader" : "JournalEntry";
        Text text = row.GetComponent<Text>() ?? row.transform.Find("Text")?.GetComponent<Text>();
        if (text != null)
        {
            text.text = message ?? string.Empty;
            text.fontStyle = header ? FontStyle.Bold : FontStyle.Normal;
        }
        Button button = row.GetComponent<Button>();
        if (button != null)
        {
            string definitionId = inspectDefinitionId;
            button.interactable = !string.IsNullOrWhiteSpace(definitionId);
            if (button.interactable) button.onClick.AddListener(() => InspectCard(definitionId));
        }
        _rows.Add(row);
    }

    private static string ResolveCardName(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return string.Empty;
        ExtensionPackageData extension; ExtensionCardData definition;
        return RoomGameSetup.TryResolveCard(definitionId, out extension, out definition) && definition != null
            ? definition.name : definitionId;
    }

    private void InspectCard(string definitionId)
    {
        ExtensionPackageData extension; ExtensionCardData definition;
        if (!RoomGameSetup.TryResolveCard(definitionId, out extension, out definition) || definition == null) return;
        Sprite sprite = ExtensionVisualLoader.LoadCardArtwork(extension, definition);
        if (sprite == null) return;
        if (_zoomOverlay == null || _zoomImage == null) BindExistingUi();
        if (_zoomOverlay == null || _zoomImage == null) return;
        _zoomImage.sprite = sprite; _zoomImage.preserveAspect = true;
        DynamicCardCostView.Attach(_zoomImage.gameObject, definition);
        _zoomOverlay.SetActive(true); _zoomOverlay.transform.SetAsLastSibling();
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
            default: return phase ?? string.Empty;
        }
    }
}

public sealed class PublicJournalBootstrap : MonoBehaviour
{
    private float _nextScan;
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        if (GameObject.Find("DominionPublicJournalBootstrap") != null) return;
        GameObject installer = new GameObject("DominionPublicJournalBootstrap");
        DontDestroyOnLoad(installer); installer.AddComponent<PublicJournalBootstrap>();
    }

    private void Update()
    {
        if (Time.unscaledTime < _nextScan) return;
        _nextScan = Time.unscaledTime + 0.5f;
        foreach (GameScreenController screen in Resources.FindObjectsOfTypeAll<GameScreenController>())
            if (screen != null && screen.gameObject.scene.IsValid() && screen.GetComponent<PublicJournalView>() == null)
                screen.gameObject.AddComponent<PublicJournalView>();
    }
}
