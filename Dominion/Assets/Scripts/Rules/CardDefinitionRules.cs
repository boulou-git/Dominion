using System;

/// <summary>
/// Shared helpers for querying immutable card-definition data.
/// Rules and presentation should use this instead of duplicating type parsing.
/// </summary>
public static class CardDefinitionRules
{
    public static bool HasType(ExtensionCardData definition, string type)
    {
        if (definition == null || definition.types == null || string.IsNullOrWhiteSpace(type))
            return false;

        foreach (string declaredType in definition.types)
        {
            if (string.Equals(declaredType, type, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}
