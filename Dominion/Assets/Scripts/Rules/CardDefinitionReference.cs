using System;

/// <summary>
/// Canonical helpers for serialized card-definition references.
/// Qualified references keep the existing "extension:card" format; unqualified card ids
/// are accepted only where declarative filters intentionally allow them.
/// </summary>
public static class CardDefinitionReference
{
    public const char Separator = ':';

    public static string Format(string extensionId, string cardId)
    {
        return TryFormat(extensionId, cardId, out string reference) ? reference : string.Empty;
    }

    public static bool TryFormat(string extensionId, string cardId, out string reference)
    {
        reference = string.Empty;
        if (!TryNormaliseSimpleId(extensionId, out string extension) ||
            !TryNormaliseSimpleId(cardId, out string card))
            return false;

        reference = extension + Separator + card;
        return true;
    }

    public static bool TryParseQualified(string value, out string extensionId, out string cardId)
    {
        extensionId = string.Empty;
        cardId = string.Empty;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        string reference = value.Trim();
        int separator = reference.IndexOf(Separator);
        if (separator <= 0 || separator >= reference.Length - 1 ||
            reference.IndexOf(Separator, separator + 1) >= 0)
            return false;

        return TryNormaliseSimpleId(reference.Substring(0, separator), out extensionId) &&
               TryNormaliseSimpleId(reference.Substring(separator + 1), out cardId);
    }

    public static bool TryGetCardId(string value, out string cardId)
    {
        cardId = string.Empty;
        if (TryParseQualified(value, out _, out string qualifiedCardId))
        {
            cardId = qualifiedCardId;
            return true;
        }

        return TryNormaliseSimpleId(value, out cardId);
    }

    public static bool IsValid(string value)
    {
        return TryParseQualified(value, out _, out _) || TryNormaliseSimpleId(value, out _);
    }

    /// <summary>
    /// Matches a filter/reference against an actual card reference. A qualified expected
    /// reference must match both extension and card; an unqualified expected id matches
    /// the local card id regardless of extension.
    /// </summary>
    public static bool Matches(string expected, string actual)
    {
        if (TryParseQualified(expected, out string expectedExtension, out string expectedCard))
        {
            return TryParseQualified(actual, out string actualExtension, out string actualCard) &&
                   string.Equals(expectedExtension, actualExtension, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(expectedCard, actualCard, StringComparison.OrdinalIgnoreCase);
        }

        if (!TryNormaliseSimpleId(expected, out string expectedLocal) ||
            !TryGetCardId(actual, out string actualLocal))
            return false;

        return string.Equals(expectedLocal, actualLocal, StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryNormaliseSimpleId(string value, out string id)
    {
        id = (value ?? string.Empty).Trim();
        return id.Length > 0 && id.IndexOf(Separator) < 0;
    }
}
