using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Presents the replicated public journal in the JournalText that already exists
/// in GameScreen.prefab. No UI is created at runtime.
/// Card names are styled from their declarative types and can be clicked to inspect
/// the existing card artwork in GameScreen's zoom overlay.
/// </summary>
[DefaultExecutionOrder(100)]
public sealed class PublicJournalView : MonoBehaviour, IPointerClickHandler
{
    private const int MaxVisibleEntries = 32;

    private sealed class CardLink
    {
        public int Start;
        public int Length;
        public string DefinitionId;
    }

    private readonly List<CardLink> _cardLinks = new List<CardLink>();

    private Text _journalText;
    private GameObject _zoomOverlay;
    private Image _zoomImage;

    private void Awake()
    {
        Transform journalPanel = transform.Find("JournalPanel");
        Transform journalTextTransform = journalPanel != null ? journalPanel.Find("JournalText") : null;
        _journalText = journalTextTransform != null ? journalTextTransform.GetComponent<Text>() : null;

        Transform zoomOverlayTransform = transform.Find("CardZoomOverlay");
        _zoomOverlay = zoomOverlayTransform != null ? zoomOverlayTransform.gameObject : null;
        Transform zoomCardTransform = zoomOverlayTransform != null ? zoomOverlayTransform.Find("Card") : null;
        _zoomImage = zoomCardTransform != null ? zoomCardTransform.GetComponent<Image>() : null;

        if (_journalText == null)
        {
            Debug.LogError("GameScreen.prefab contract is incomplete: JournalPanel/JournalText is missing.", this);
            enabled = false;
            return;
        }

        // The Text itself is the raycast target. Pointer events then bubble to this
        // component on the GameScreen root; no invisible buttons or runtime UI needed.
        _journalText.raycastTarget = true;
        _journalText.supportRichText = true;

        NetworkGameState.StateChanged += Refresh;
        Refresh(NetworkGameState.State);
    }

    private void OnDestroy()
    {
        NetworkGameState.StateChanged -= Refresh;
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (_journalText == null || eventData == null)
            return;
        if (eventData.button != PointerEventData.InputButton.Left &&
            eventData.button != PointerEventData.InputButton.Right)
            return;
        if (eventData.pointerCurrentRaycast.gameObject != _journalText.gameObject)
            return;

        CardLink link = FindLinkAtPointer(eventData.position, eventData.pressEventCamera);
        if (link != null)
            ShowZoom(link.DefinitionId);
    }

    private void Refresh(GameStateSnapshot state)
    {
        if (_journalText == null)
            return;

        _cardLinks.Clear();

        if (state == null || state.Players == null || state.Players.Count == 0)
        {
            _journalText.text = "En attente de la partie…";
            return;
        }

        PlayerStateSnapshot activePlayer = state.Players.Find(
            player => player != null && player.PlayerId == state.ActivePlayerId);
        string activeName = activePlayer != null && !string.IsNullOrWhiteSpace(activePlayer.NickName)
            ? activePlayer.NickName
            : "Joueur";

        StringBuilder text = new StringBuilder();
        int visibleCharacters = 0;
        AppendPlain(text, ref visibleCharacters,
            "Tour " + state.TurnNumber + " — " + activeName + " — " + PhaseLabel(state.Phase));

        int visibleEntries = 0;
        List<GameJournalEntrySnapshot> journal = state.Journal;
        if (journal != null)
        {
            for (int index = journal.Count - 1; index >= 0 && visibleEntries < MaxVisibleEntries; index--)
            {
                GameJournalEntrySnapshot entry = journal[index];
                if (!CanDisplay(entry))
                    continue;

                AppendPlain(text, ref visibleCharacters, "\n");
                AppendEntry(text, ref visibleCharacters, entry);
                visibleEntries++;
            }
        }

        if (visibleEntries == 0)
            AppendPlain(text, ref visibleCharacters, "\nAucune activité publique.");

        _journalText.text = text.ToString();
        Canvas.ForceUpdateCanvases();
    }

    private void AppendEntry(StringBuilder text, ref int visibleCharacters, GameJournalEntrySnapshot entry)
    {
        string player = string.IsNullOrWhiteSpace(entry.PlayerName) ? "Joueur" : entry.PlayerName;
        string prefix = "T" + entry.TurnNumber + " · " + player;

        switch (entry.Kind)
        {
            case JournalRules.ChatKind:
                AppendPlain(text, ref visibleCharacters, player + " : " + (entry.Message ?? string.Empty));
                break;

            case JournalRules.PlayedKind:
                AppendPlain(text, ref visibleCharacters, prefix + " joue ");
                AppendCard(text, ref visibleCharacters, entry.CardDefinitionId);
                AppendPlain(text, ref visibleCharacters, ".");
                break;

            case JournalRules.GainedKind:
                AppendPlain(text, ref visibleCharacters, prefix + " reçoit ");
                AppendCard(text, ref visibleCharacters, entry.CardDefinitionId);
                AppendPlain(text, ref visibleCharacters, ".");
                break;

            case JournalRules.ChoiceKind:
                AppendPlain(text, ref visibleCharacters,
                    prefix + " choisit « " + (entry.Message ?? string.Empty) + " »");
                if (!string.IsNullOrWhiteSpace(entry.SourceCardDefinitionId))
                {
                    AppendPlain(text, ref visibleCharacters, " pour ");
                    AppendCard(text, ref visibleCharacters, entry.SourceCardDefinitionId);
                }
                AppendPlain(text, ref visibleCharacters, ".");
                break;

            case JournalRules.RevealKind:
                AppendPlain(text, ref visibleCharacters, prefix + " révèle ");
                AppendCard(text, ref visibleCharacters, entry.CardDefinitionId);
                AppendPlain(text, ref visibleCharacters, ".");
                break;
        }
    }

    private void AppendCard(StringBuilder text, ref int visibleCharacters, string definitionId)
    {
        ExtensionPackageData extension;
        ExtensionCardData definition;
        if (!RoomGameSetup.TryResolveCard(definitionId, out extension, out definition) || definition == null)
        {
            AppendPlain(text, ref visibleCharacters, definitionId ?? string.Empty);
            return;
        }

        string name = string.IsNullOrWhiteSpace(definition.name) ? definitionId : definition.name;
        int start = visibleCharacters;
        bool bold = HasType(definition, "Action");
        string color = ResolveTypeColor(definition);

        if (bold)
            text.Append("<b>");
        if (!string.IsNullOrEmpty(color))
            text.Append("<color=").Append(color).Append(">");

        text.Append(name);

        if (!string.IsNullOrEmpty(color))
            text.Append("</color>");
        if (bold)
            text.Append("</b>");

        visibleCharacters += name.Length;
        _cardLinks.Add(new CardLink
        {
            Start = start,
            Length = name.Length,
            DefinitionId = definitionId
        });
    }

    private static void AppendPlain(StringBuilder text, ref int visibleCharacters, string value)
    {
        if (string.IsNullOrEmpty(value))
            return;
        text.Append(value);
        visibleCharacters += value.Length;
    }

    private static bool CanDisplay(GameJournalEntrySnapshot entry)
    {
        if (entry == null)
            return false;

        return entry.Kind == JournalRules.ChatKind ||
               entry.Kind == JournalRules.PlayedKind ||
               entry.Kind == JournalRules.GainedKind ||
               entry.Kind == JournalRules.ChoiceKind ||
               entry.Kind == JournalRules.RevealKind;
    }

    private static string ResolveTypeColor(ExtensionCardData definition)
    {
        // Specific subtypes take precedence over the generic Action type.
        if (HasType(definition, "Réaction") || HasType(definition, "Reaction")) return "#5FA8FF";
        if (HasType(definition, "Durée") || HasType(definition, "Duree") || HasType(definition, "Duration")) return "#E7A24B";
        if (HasType(definition, "Attaque") || HasType(definition, "Attack")) return "#E86A6A";
        if (HasType(definition, "Maladie") || HasType(definition, "Disease")) return "#A8B85A";
        if (HasType(definition, "Artefact") || HasType(definition, "Artifact")) return "#65C7C9";
        if (HasType(definition, "Consommable") || HasType(definition, "Consumable")) return "#D88FB8";
        if (HasType(definition, "Trésor") || HasType(definition, "Tresor") || HasType(definition, "Treasure")) return "#E6C25B";
        if (HasType(definition, "Victoire") || HasType(definition, "Victory")) return "#72C777";
        if (HasType(definition, "Malédiction") || HasType(definition, "Malediction") || HasType(definition, "Curse")) return "#B77AE0";
        return null;
    }

    private static bool HasType(ExtensionCardData definition, string type)
    {
        if (definition == null || definition.types == null || string.IsNullOrWhiteSpace(type))
            return false;

        return definition.types.Exists(candidate =>
            !string.IsNullOrWhiteSpace(candidate) &&
            string.Equals(candidate.Trim(), type, StringComparison.OrdinalIgnoreCase));
    }

    private CardLink FindLinkAtPointer(Vector2 screenPosition, Camera eventCamera)
    {
        if (_journalText == null)
            return null;

        RectTransform rect = _journalText.rectTransform;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(rect, screenPosition, eventCamera, out Vector2 localPoint))
            return null;

        TextGenerator generator = _journalText.cachedTextGenerator;
        IList<UICharInfo> characters = generator.characters;
        IList<UILineInfo> lines = generator.lines;
        if (characters == null || lines == null || characters.Count == 0 || lines.Count == 0)
            return null;

        float unitsPerPixel = 1f / Mathf.Max(0.0001f, _journalText.pixelsPerUnit);

        foreach (CardLink link in _cardLinks)
        {
            int first = Mathf.Clamp(link.Start, 0, characters.Count);
            int last = Mathf.Clamp(link.Start + link.Length, first, characters.Count);

            for (int characterIndex = first; characterIndex < last; characterIndex++)
            {
                int lineIndex = FindLineIndex(lines, characterIndex);
                if (lineIndex < 0)
                    continue;

                UICharInfo character = characters[characterIndex];
                UILineInfo line = lines[lineIndex];
                float left = character.cursorPos.x * unitsPerPixel;
                float width = Mathf.Max(1f, character.charWidth * unitsPerPixel);
                float top = line.topY * unitsPerPixel;
                float height = Mathf.Max(1f, line.height * unitsPerPixel);

                Rect characterBounds = Rect.MinMaxRect(
                    left - 1.5f,
                    top - height - 1.5f,
                    left + width + 1.5f,
                    top + 1.5f);

                if (characterBounds.Contains(localPoint))
                    return link;
            }
        }

        return null;
    }

    private static int FindLineIndex(IList<UILineInfo> lines, int characterIndex)
    {
        for (int lineIndex = lines.Count - 1; lineIndex >= 0; lineIndex--)
        {
            if (characterIndex >= lines[lineIndex].startCharIdx)
                return lineIndex;
        }
        return -1;
    }

    private void ShowZoom(string definitionId)
    {
        if (_zoomOverlay == null || _zoomImage == null || string.IsNullOrWhiteSpace(definitionId))
            return;

        ExtensionPackageData extension;
        ExtensionCardData definition;
        if (!RoomGameSetup.TryResolveCard(definitionId, out extension, out definition) || definition == null)
            return;

        Sprite sprite = ExtensionVisualLoader.LoadCardArtwork(extension, definition);
        if (sprite == null)
            return;

        _zoomImage.sprite = sprite;
        _zoomImage.preserveAspect = true;
        DynamicCardCostView.Attach(_zoomImage.gameObject, definition);
        _zoomOverlay.SetActive(true);
        _zoomOverlay.transform.SetAsLastSibling();
    }

    private static string PhaseLabel(string phase)
    {
        switch (phase)
        {
            case NetworkGameState.ActionPhase: return "ACTION";
            case NetworkGameState.BuyPhase: return "ACHAT";
            case NetworkGameState.CleanupPhase: return "AJUSTEMENT";
            default: return string.IsNullOrEmpty(phase) ? "—" : phase.ToUpperInvariant();
        }
    }
}
