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

    public static bool HasAnyType(ExtensionCardData definition, string types)
    {
        if (string.IsNullOrWhiteSpace(types)) return false;
        string[] candidates = types.Split('|');
        foreach (string candidate in candidates)
            if (HasType(definition, candidate.Trim())) return true;
        return false;
    }
}
