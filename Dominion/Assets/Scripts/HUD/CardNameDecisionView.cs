using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Search/autocomplete presentation for naming a card. It deliberately renders at
/// most four prefab-backed suggestions, regardless of the number of known cards.
/// </summary>
public sealed class CardNameDecisionView : MonoBehaviour
{
    public const int MaximumVisibleSuggestions = 4;

    private InputField _searchField;
    private RectTransform _suggestionsRoot;
    private Text _statusText;
    private GameObject _optionPrefab;
    private IReadOnlyList<string> _optionIds;
    private IReadOnlyList<string> _optionLabels;
    private Action<string> _selectionChanged;
    private readonly List<GameObject> _suggestions = new List<GameObject>();
    private string _selectedId = string.Empty;

    private void Awake()
    {
        BindPrefab();
    }

    public bool Configure(IReadOnlyList<string> optionIds, IReadOnlyList<string> optionLabels,
        GameObject optionPrefab, Action<string> selectionChanged)
    {
        if (!BindPrefab() || optionIds == null || optionLabels == null ||
            optionIds.Count == 0 || optionIds.Count != optionLabels.Count || optionPrefab == null)
            return false;

        _optionIds = optionIds;
        _optionLabels = optionLabels;
        _optionPrefab = optionPrefab;
        _selectionChanged = selectionChanged;
        _selectedId = string.Empty;
        _searchField.onValueChanged.RemoveListener(OnSearchChanged);
        _searchField.onEndEdit.RemoveListener(OnSearchSubmitted);
        _searchField.SetTextWithoutNotify(string.Empty);
        _searchField.onValueChanged.AddListener(OnSearchChanged);
        _searchField.onEndEdit.AddListener(OnSearchSubmitted);
        RenderMatches(string.Empty);
        return true;
    }

    public void ResetView()
    {
        if (_searchField != null)
        {
            _searchField.onValueChanged.RemoveListener(OnSearchChanged);
            _searchField.onEndEdit.RemoveListener(OnSearchSubmitted);
            _searchField.SetTextWithoutNotify(string.Empty);
        }
        ClearSuggestions();
        _optionIds = null;
        _optionLabels = null;
        _optionPrefab = null;
        _selectionChanged = null;
        _selectedId = string.Empty;
    }

    public static List<int> FindMatches(string query, IReadOnlyList<string> labels, int maximum)
    {
        List<int> matches = new List<int>();
        if (labels == null || maximum <= 0)
            return matches;

        string normalizedQuery = Normalize(query);
        List<int> prefix = new List<int>();
        List<int> contains = new List<int>();
        for (int index = 0; index < labels.Count; index++)
        {
            string normalizedLabel = Normalize(labels[index]);
            if (normalizedQuery.Length == 0 || normalizedLabel.StartsWith(normalizedQuery, StringComparison.Ordinal))
                prefix.Add(index);
            else if (normalizedLabel.Contains(normalizedQuery))
                contains.Add(index);
        }

        Comparison<int> byLabel = (left, right) => string.Compare(labels[left], labels[right], StringComparison.CurrentCultureIgnoreCase);
        prefix.Sort(byLabel);
        contains.Sort(byLabel);
        AppendUntil(matches, prefix, maximum);
        AppendUntil(matches, contains, maximum);
        return matches;
    }

    private void OnSearchChanged(string query)
    {
        _selectedId = string.Empty;
        _selectionChanged?.Invoke(string.Empty);
        RenderMatches(query);
    }

    private void OnSearchSubmitted(string query)
    {
        List<int> matches = FindMatches(query, _optionLabels, MaximumVisibleSuggestions);
        if (matches.Count == 1 || (matches.Count > 0 &&
            string.Equals(Normalize(_optionLabels[matches[0]]), Normalize(query), StringComparison.Ordinal)))
            Select(matches[0]);
    }

    private void RenderMatches(string query)
    {
        ClearSuggestions();
        if (_optionIds == null || _optionLabels == null || _optionPrefab == null)
            return;

        List<int> matches = FindMatches(query, _optionLabels, MaximumVisibleSuggestions);
        foreach (int index in matches)
        {
            GameObject suggestion = Instantiate(_optionPrefab, _suggestionsRoot);
            suggestion.name = "CardSuggestion_" + _optionIds[index];
            Button button = suggestion.GetComponent<Button>();
            Text label = suggestion.transform.Find("Label")?.GetComponent<Text>();
            if (button == null || label == null)
            {
                Destroy(suggestion);
                continue;
            }
            label.text = _optionLabels[index];
            int capturedIndex = index;
            button.onClick.AddListener(() => Select(capturedIndex));
            _suggestions.Add(suggestion);
        }

        if (_statusText != null)
            _statusText.text = matches.Count == 0
                ? "Aucune carte trouvée"
                : string.IsNullOrEmpty(_selectedId) ? "Choisissez une suggestion" : "Carte sélectionnée";
    }

    private void Select(int index)
    {
        if (_optionIds == null || _optionLabels == null || index < 0 || index >= _optionIds.Count)
            return;
        _selectedId = _optionIds[index];
        _searchField.SetTextWithoutNotify(_optionLabels[index]);
        _selectionChanged?.Invoke(_selectedId);
        RenderMatches(_optionLabels[index]);
    }

    private void ClearSuggestions()
    {
        foreach (GameObject suggestion in _suggestions)
            if (suggestion != null)
                Destroy(suggestion);
        _suggestions.Clear();
    }

    private bool BindPrefab()
    {
        if (_searchField == null)
            _searchField = transform.Find("SearchField")?.GetComponent<InputField>();
        if (_suggestionsRoot == null)
            _suggestionsRoot = transform.Find("Suggestions") as RectTransform;
        if (_statusText == null)
            _statusText = transform.Find("Status")?.GetComponent<Text>();
        return _searchField != null && _suggestionsRoot != null && _statusText != null;
    }

    private static void AppendUntil(List<int> destination, List<int> source, int maximum)
    {
        foreach (int index in source)
        {
            if (destination.Count >= maximum)
                return;
            destination.Add(index);
        }
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        string decomposed = value.Trim().Normalize(NormalizationForm.FormD);
        StringBuilder builder = new StringBuilder(decomposed.Length);
        foreach (char character in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
                builder.Append(char.ToLowerInvariant(character));
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }
}
