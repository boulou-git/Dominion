using UnityEngine;

/// <summary>
/// Resource indirection for the shared Dominion card back.
/// Keeps the source sprite in Assets/2D/Cards while making the reference available in builds.
/// </summary>
public sealed class CardBackReference : ScriptableObject
{
    [SerializeField] private Sprite _sprite;

    public Sprite Sprite => _sprite;

    public static Sprite LoadSprite()
    {
        CardBackReference reference = Resources.Load<CardBackReference>("UI/CardBackReference");
        return reference != null ? reference.Sprite : null;
    }
}
