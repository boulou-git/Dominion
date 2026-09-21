using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.InputSystem;

/// <summary>
/// Prefab-owned chat composer and replicated emote interaction for the public journal.
/// </summary>
public sealed class JournalSocialPanel : MonoBehaviour
{
    [SerializeField, Min(0.1f)] private float _emoteDuration = 2.4f;
    [SerializeField] private float _emoteRise = 125f;
    [SerializeField, Min(1)] private int _maxConcurrentEmotes = 8;
    private readonly List<GameObject> _activeEmotes = new List<GameObject>();
    private UnityEngine.Events.UnityAction[] _emoteListeners;

    [SerializeField] private InputField _chatInput;
    [SerializeField] private Button _sendButton;
    [SerializeField] private Button _emoteButton;
    [SerializeField] private GameObject _emoteWheel;
    [SerializeField] private Button[] _emoteButtons;
    [SerializeField] private string[] _emoteIds;
    [SerializeField] private GameObject[] _emoteIconPrefabs;
    [SerializeField] private RectTransform _emoteAnimationRoot;
    [SerializeField] private AudioSource _emoteAudioSource;
    [SerializeField] private AudioClip _emoteSound;

    private int _lastSeenEmoteSequence;
    private bool _emoteCursorInitialised;
    private string _observedMatchId;

    private void Awake()
    {
        if (!HasValidPrefabContract())
        {
            Debug.LogError("JournalSocialPanel.prefab contract is incomplete.", this);
            enabled = false;
            return;
        }

        _chatInput.characterLimit = JournalRules.MaxChatLength;
        _sendButton.onClick.AddListener(SubmitChat);
        _chatInput.onEndEdit.AddListener(HandleChatEndEdit);
        _emoteButton.onClick.AddListener(ToggleEmoteWheel);
        _emoteListeners = new UnityEngine.Events.UnityAction[_emoteButtons.Length];
        for (int index = 0; index < _emoteButtons.Length; index++)
        {
            int capturedIndex = index;
            _emoteListeners[index] = () => SubmitEmote(capturedIndex);
            _emoteButtons[index].onClick.AddListener(_emoteListeners[index]);
        }

        _emoteWheel.SetActive(false);
        NetworkGameState.StateChanged += Refresh;
        Refresh(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= Refresh;
        if (_sendButton != null) _sendButton.onClick.RemoveListener(SubmitChat);
        if (_chatInput != null) _chatInput.onEndEdit.RemoveListener(HandleChatEndEdit);
        if (_emoteButton != null) _emoteButton.onClick.RemoveListener(ToggleEmoteWheel);
        if (_emoteListeners != null)
            for (int index = 0; index < _emoteListeners.Length; index++)
                if (_emoteButtons[index] != null)
                    _emoteButtons[index].onClick.RemoveListener(_emoteListeners[index]);
    }

    private void OnDisable()
    {
        StopAllCoroutines();
        foreach (GameObject instance in _activeEmotes)
            if (instance != null) Destroy(instance);
        _activeEmotes.Clear();
        if (_emoteWheel != null) _emoteWheel.SetActive(false);
    }

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (_emoteWheel != null && _emoteWheel.activeSelf &&
            keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            _emoteWheel.SetActive(false);
    }

    private void OnEnable()
    {
        if (_emoteListeners == null) return;
        _emoteCursorInitialised = false;
        Refresh(NetworkGameState.State);
    }

    private bool HasValidPrefabContract()
    {
        if (_chatInput == null || _sendButton == null || _emoteButton == null ||
            _emoteWheel == null || _emoteAnimationRoot == null || _emoteAudioSource == null)
            return false;
        if (_emoteButtons == null || _emoteIds == null || _emoteIconPrefabs == null ||
            _emoteButtons.Length != 4 || _emoteIds.Length != 4 || _emoteIconPrefabs.Length != 4)
            return false;
        HashSet<string> ids = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        for (int index = 0; index < 4; index++)
            if (_emoteButtons[index] == null || _emoteIconPrefabs[index] == null ||
                !ids.Add(_emoteIds[index]) ||
                _emoteIconPrefabs[index].GetComponent<RectTransform>() == null ||
                _emoteIconPrefabs[index].GetComponent<CanvasGroup>() == null ||
                !JournalRules.IsSupportedEmote(_emoteIds[index])) return false;
        return true;
    }

    private void Refresh(GameStateSnapshot state)
    {
        if (!isActiveAndEnabled) return;
        bool interactable = state != null && state.IsStarted && !state.IsPaused && !state.IsGameOver &&
                            Photon.Pun.PhotonNetwork.InRoom &&
                            !PendingDecisionInputLock.IsActive(state);
        _chatInput.interactable = interactable;
        _sendButton.interactable = interactable;
        _emoteButton.interactable = interactable;
        if (!interactable) _emoteWheel.SetActive(false);

        ProcessNewEmotes(state);
    }

    private void SubmitChat()
    {
        string message = _chatInput != null ? _chatInput.text.Trim() : string.Empty;
        if (message.Length == 0 || PlayersTurnsHandler.Instance == null) return;
        PlayersTurnsHandler.Instance.SendChatMessage(message);
        _chatInput.text = string.Empty;
        _chatInput.ActivateInputField();
    }

    private void HandleChatEndEdit(string value)
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard != null &&
            (keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame))
            SubmitChat();
    }

    private void ToggleEmoteWheel()
    {
        _emoteWheel.SetActive(!_emoteWheel.activeSelf);
    }

    private void SubmitEmote(int index)
    {
        _emoteWheel.SetActive(false);
        if (index < 0 || index >= _emoteIds.Length || PlayersTurnsHandler.Instance == null) return;
        PlayersTurnsHandler.Instance.SendEmote(_emoteIds[index]);
    }

    private void ProcessNewEmotes(GameStateSnapshot state)
    {
        List<GameJournalEntrySnapshot> journal = state != null ? state.Journal : null;
        if (journal == null || !state.IsStarted) return;

        if (!string.Equals(_observedMatchId, state.MatchId, System.StringComparison.Ordinal))
        {
            _observedMatchId = state.MatchId;
            _lastSeenEmoteSequence = 0;
            _emoteCursorInitialised = false;
        }

        int newestSequence = _lastSeenEmoteSequence;
        if (!_emoteCursorInitialised)
        {
            foreach (GameJournalEntrySnapshot entry in journal)
                if (entry != null) newestSequence = Mathf.Max(newestSequence, entry.Sequence);
            _lastSeenEmoteSequence = newestSequence;
            _emoteCursorInitialised = true;
            return;
        }

        foreach (GameJournalEntrySnapshot entry in journal)
        {
            if (entry == null || entry.Sequence <= _lastSeenEmoteSequence) continue;
            newestSequence = Mathf.Max(newestSequence, entry.Sequence);
            if (entry.Kind == JournalRules.EmoteKind)
                PlayEmote(entry.Message);
        }
        _lastSeenEmoteSequence = newestSequence;
    }

    private void PlayEmote(string emoteId)
    {
        _activeEmotes.RemoveAll(instance => instance == null);
        if (_activeEmotes.Count >= Mathf.Max(1, _maxConcurrentEmotes)) return;
        int index = System.Array.FindIndex(_emoteIds,
            candidate => string.Equals(candidate, emoteId, System.StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index >= _emoteIconPrefabs.Length) return;

        GameObject instance = Instantiate(_emoteIconPrefabs[index], _emoteAnimationRoot, false);
        _activeEmotes.Add(instance);
        RectTransform rect = instance.transform as RectTransform;
        if (rect != null)
            rect.anchoredPosition = new Vector2(Random.Range(-22f, 22f), 0f);
        if (_emoteSound != null)
            _emoteAudioSource.PlayOneShot(_emoteSound);
        StartCoroutine(AnimateEmote(instance));
    }

    private IEnumerator AnimateEmote(GameObject instance)
    {
        if (instance == null) yield break;
        RectTransform rect = instance.transform as RectTransform;
        CanvasGroup group = instance.GetComponent<CanvasGroup>();
        Vector2 start = rect != null ? rect.anchoredPosition : Vector2.zero;
        float elapsed = 0f;

        float duration = Mathf.Max(0.1f, _emoteDuration);
        while (instance != null && elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            if (rect != null)
            {
                rect.anchoredPosition = start + Vector2.up * (_emoteRise * progress);
                float scale = Mathf.Lerp(0.72f, 1f, Mathf.Min(1f, progress * 4f));
                rect.localScale = Vector3.one * scale;
            }
            if (group != null)
                group.alpha = 1f - Mathf.SmoothStep(0f, 1f, progress);
            yield return null;
        }

        _activeEmotes.Remove(instance);
        if (instance != null) Destroy(instance);
    }
}
