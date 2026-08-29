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
    private GameObject _surface;
    private RectTransform _content;
    private Text _logText;
    private ScrollRect _scroll;
    private InputField _input;
    private Button _sendButton;
    private int _lastRenderedVersion = int.MinValue;
    private float _lastLayoutWidth = -1f;
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
        RefreshLogLayout();
    }

    private void BindExistingUi()
    {
        if (_screen == null) return;
        _legacyJournalText = ReadField<Text>("_journalText");
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

        RectTransform legacyRect = _legacyJournalText.rectTransform;
        RectTransform host = legacyRect.parent as RectTransform;
        if (host == null)
        {
            Debug.LogError("Journal prefab contract is incomplete: JournalText has no RectTransform parent.", this);
            return;
        }

        GameObject prefab = Resources.Load<GameObject>("UI/JournalSurface");
        if (prefab == null)
        {
            Debug.LogError("Journal prefab contract is incomplete: JournalSurface is missing.", this);
            return;
        }

        _surface = Instantiate(prefab, host, false);
        RectTransform surfaceRect = _surface.GetComponent<RectTransform>();
        if (surfaceRect == null)
        {
            Debug.LogError("Journal prefab contract is incomplete: JournalSurface has no RectTransform.", this);
            Destroy(_surface);
            _surface = null;
            return;
        }

        CopyRectTransform(legacyRect, surfaceRect);
        _surface.transform.SetSiblingIndex(legacyRect.GetSiblingIndex() + 1);
        SetLayerRecursively(_surface, _legacyJournalText.gameObject.layer);

        _content = _surface.transform.Find("EntriesScroll/Viewport/Content") as RectTransform;
        _logText = _content != null ? _content.GetComponent<Text>() : null;
        _scroll = _surface.transform.Find("EntriesScroll")?.GetComponent<ScrollRect>();
        _input = _surface.transform.Find("Composer/MessageInput")?.GetComponent<InputField>();
        _sendButton = _surface.transform.Find("Composer/SendButton")?.GetComponent<Button>();
        if (_content == null || _logText == null || _scroll == null || _input == null || _sendButton == null)
        {
            Debug.LogError("JournalSurface prefab contract is incomplete.", this);
            Destroy(_surface);
            _surface = null;
            return;
        }

        _legacyJournalText.enabled = false;
        _legacyJournalText.raycastTarget = false;
        _input.characterLimit = JournalRules.MaxChatLength;
        _input.onSubmit.AddListener(SubmitChat);
        _sendButton.onClick.AddListener(SendChat);
    }

    private static void CopyRectTransform(RectTransform source, RectTransform destination)
    {
        destination.anchorMin = source.anchorMin;
        destination.anchorMax = source.anchorMax;
        destination.anchoredPosition = source.anchoredPosition;
        destination.sizeDelta = source.sizeDelta;
        destination.pivot = source.pivot;
        destination.localRotation = source.localRotation;
        destination.localScale = source.localScale;
    }

    private static void SetLayerRecursively(GameObject root, int layer)
    {
        if (root == null) return;
        root.layer = layer;
        foreach (Transform child in root.transform)
            SetLayerRecursively(child.gameObject, layer);
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
        List<string> lines = new List<string>();

        if (state == null || state.Players == null || state.Players.Count == 0)
        {
            lines.Add("En attente de la partie…");
            SetLog(lines);
            return;
        }

        PlayerStateSnapshot active = state.Players.Find(player => player != null && player.PlayerId == state.ActivePlayerId);
        string activeName = active != null && !string.IsNullOrWhiteSpace(active.NickName) ? active.NickName : "Joueur";
        lines.Add("Tour " + state.TurnNumber + " — " + activeName + " — " + PhaseLabel(state.Phase));

        List<GameJournalEntrySnapshot> journal = state.Journal;
        if (journal == null || journal.Count == 0)
            lines.Add("Aucune activité publique.");
        else
        {
            int first = Math.Max(0, journal.Count - MaxVisibleEntries);
            for (int index = first; index < journal.Count; index++)
            {
                string line = FormatEntry(journal[index]);
                if (!string.IsNullOrWhiteSpace(line)) lines.Add(line);
            }
        }

        SetLog(lines);
    }

    private static string FormatEntry(GameJournalEntrySnapshot entry)
    {
        if (entry == null) return string.Empty;
        string player = string.IsNullOrWhiteSpace(entry.PlayerName) ? "Joueur" : entry.PlayerName;
        string cardName = ResolveCardName(entry.CardDefinitionId);
        string sourceName = ResolveCardName(entry.SourceCardDefinitionId);
        switch (entry.Kind)
        {
            case JournalRules.ChatKind:
                return player + " : " + entry.Message;
            case JournalRules.PlayedKind:
                return "T" + entry.TurnNumber + " · " + player + " joue " + cardName + ".";
            case JournalRules.GainedKind:
                return "T" + entry.TurnNumber + " · " + player + " reçoit " + cardName + ".";
            case JournalRules.ChoiceKind:
                return "T" + entry.TurnNumber + " · " + player + " choisit « " + entry.Message + " »" +
                       (!string.IsNullOrWhiteSpace(sourceName) ? " pour " + sourceName : string.Empty) + ".";
            case JournalRules.RevealKind:
                return "T" + entry.TurnNumber + " · " + player + " révèle " + cardName + ".";
            default:
                return string.Empty;
        }
    }

    private void SetLog(List<string> lines)
    {
        if (_logText == null) return;
        _logText.text = lines != null ? string.Join("\n", lines) : string.Empty;
        _lastLayoutWidth = -1f;
        RefreshLogLayout();
    }

    private void RefreshLogLayout()
    {
        if (_content == null || _logText == null || _scroll == null || _scroll.viewport == null) return;
        float width = _scroll.viewport.rect.width;
        if (width <= 1f || Mathf.Approximately(width, _lastLayoutWidth)) return;

        _lastLayoutWidth = width;
        _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
        Canvas.ForceUpdateCanvases();
        float height = Mathf.Max(_scroll.viewport.rect.height, _logText.preferredHeight + 4f);
        _content.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
        _scroll.verticalNormalizedPosition = 0f;
    }

    private static string ResolveCardName(string definitionId)
    {
        if (string.IsNullOrWhiteSpace(definitionId)) return string.Empty;
        ExtensionPackageData extension; ExtensionCardData definition;
        return RoomGameSetup.TryResolveCard(definitionId, out extension, out definition) && definition != null
            ? definition.name : definitionId;
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
        DontDestroyOnLoad(installer);
        installer.AddComponent<PublicJournalBootstrap>();
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
