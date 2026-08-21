using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Static, authoring-time definition of a Dominion card.
/// The visual sprite already contains the artwork, card name and rules text.
/// Only values that must remain dynamic in-game (notably the cost) are rendered by Unity.
/// </summary>
[CreateAssetMenu(menuName = "Dominion/Card Definition", fileName = "card_definition")]
public sealed class CardDefinition : ScriptableObject
{
    [SerializeField] private string _id;
    [SerializeField] private string _displayName;
    [SerializeField, Min(0)] private int _baseCoinCost;
    [SerializeField] private Sprite _cardSprite;
    [SerializeField] private List<string> _types = new List<string>();

    public string Id => _id;
    public string DisplayName => _displayName;
    public int BaseCoinCost => _baseCoinCost;
    public Sprite CardSprite => _cardSprite;
    public IReadOnlyList<string> Types => _types;

#if UNITY_EDITOR
    private void OnValidate()
    {
        _id = (_id ?? string.Empty).Trim().ToLowerInvariant().Replace(' ', '_');
        _baseCoinCost = Mathf.Max(0, _baseCoinCost);
    }
#endif
}
