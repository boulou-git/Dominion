using System;
using System.Collections.Generic;
using Photon.Pun;
using ExitGames.Client.Photon;
using UnityEngine;

[Serializable]
public sealed class GameSetupConfig
{
    public string stage = RoomGameSetup.SelectionStage;
    public List<ExtensionSetupSelection> extensions = new List<ExtensionSetupSelection>();
    public List<string> kingdomCardIds = new List<string>();
}

[Serializable]
public sealed class ExtensionSetupSelection
{
    public string extensionId;
    public bool enabled;
    public List<string> selectedCardIds = new List<string>();
}

/// <summary>
/// Stores the host's pre-game extension/card pool and the final 10-card Kingdom
/// in Photon room properties. All clients can read it; only the Master Client writes it.
/// </summary>
public static class RoomGameSetup
{
    public const string RoomPropertyKey = "dominion.setup";
    public const string SelectionStage = "Selection";
    public const string RevealStage = "Reveal";
    public const int KingdomCardCount = 10;

    public static GameSetupConfig CreateDefault()
    {
        GameSetupConfig config = new GameSetupConfig();

        foreach (ExtensionPackageData extension in ExtensionCatalog.All)
        {
            if (extension == null || string.IsNullOrEmpty(extension.id))
                continue;

            config.extensions.Add(CreateDefaultSelection(extension));
        }

        return config;
    }

    public static GameSetupConfig ReadCurrent()
    {
        if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom == null)
            return CreateDefault();

        Hashtable properties = PhotonNetwork.CurrentRoom.CustomProperties;
        if (properties == null || !properties.ContainsKey(RoomPropertyKey))
            return CreateDefault();

        string json = properties[RoomPropertyKey] as string;
        if (string.IsNullOrEmpty(json))
            return CreateDefault();

        try
        {
            GameSetupConfig parsed = JsonUtility.FromJson<GameSetupConfig>(json);
            return Normalise(parsed);
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not parse room game setup: " + exception.Message);
            return CreateDefault();
        }
    }

    public static bool Publish(GameSetupConfig config)
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || PhotonNetwork.CurrentRoom == null)
            return false;

        GameSetupConfig normalised = Normalise(config);
        string json = JsonUtility.ToJson(normalised);
        Hashtable properties = new Hashtable
        {
            { RoomPropertyKey, json }
        };
        return PhotonNetwork.CurrentRoom.SetCustomProperties(properties);
    }

    public static bool FinaliseKingdom(GameSetupConfig config)
    {
        if (!PhotonNetwork.InRoom || !PhotonNetwork.IsMasterClient || config == null)
            return false;

        List<string> pool = BuildEnabledCardPool(config);
        if (pool.Count < KingdomCardCount)
        {
            Debug.LogWarning("At least 10 enabled Kingdom cards are required before revealing the setup.");
            return false;
        }

        System.Random random = new System.Random(unchecked(Environment.TickCount * 397 ^ PhotonNetwork.CurrentRoom.Name.GetHashCode()));
        for (int i = pool.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            string temp = pool[i];
            pool[i] = pool[j];
            pool[j] = temp;
        }

        config.kingdomCardIds = pool.GetRange(0, KingdomCardCount);
        config.stage = RevealStage;
        return Publish(config);
    }

    public static ExtensionSetupSelection FindExtension(GameSetupConfig config, string extensionId)
    {
        if (config == null || config.extensions == null)
            return null;

        return config.extensions.Find(e => e != null && string.Equals(e.extensionId, extensionId, StringComparison.OrdinalIgnoreCase));
    }

    public static int CountSelectedCards(GameSetupConfig config)
    {
        return BuildEnabledCardPool(config).Count;
    }

    public static List<string> BuildEnabledCardPool(GameSetupConfig config)
    {
        List<string> result = new List<string>();
        HashSet<string> seenCardRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (config == null || config.extensions == null)
            return result;

        foreach (ExtensionSetupSelection extension in config.extensions)
        {
            if (extension == null || !extension.enabled || string.IsNullOrWhiteSpace(extension.extensionId) || extension.selectedCardIds == null)
                continue;

            ExtensionPackageData package = ExtensionCatalog.Find(extension.extensionId.Trim());
            if (package == null)
                continue;

            foreach (string cardId in extension.selectedCardIds)
            {
                ExtensionCardData kingdomCard = FindKingdomCard(package, cardId);
                if (kingdomCard == null)
                    continue;

                string cardRef = MakeCardRef(package.id, kingdomCard.id);
                if (seenCardRefs.Add(cardRef))
                    result.Add(cardRef);
            }
        }

        return result;
    }

    public static string MakeCardRef(string extensionId, string cardId)
    {
        return (extensionId ?? string.Empty).Trim() + ":" + (cardId ?? string.Empty).Trim();
    }

    /// <summary>
    /// Resolves any qualified card reference belonging to an extension. This includes
    /// both Kingdom cards and base/shared cards. Only extension.cards are ever used to
    /// build the random Kingdom pool, so resolving base cards here cannot affect setup.
    /// </summary>
    public static bool TryResolveCard(string cardRef, out ExtensionPackageData extension, out ExtensionCardData card)
    {
        extension = null;
        card = null;
        if (string.IsNullOrWhiteSpace(cardRef))
            return false;

        int separator = cardRef.IndexOf(':');
        if (separator <= 0 || separator >= cardRef.Length - 1)
            return false;

        string extensionId = cardRef.Substring(0, separator).Trim();
        string cardId = cardRef.Substring(separator + 1).Trim();
        if (extensionId.Length == 0 || cardId.Length == 0)
            return false;

        extension = ExtensionCatalog.Find(extensionId);
        if (extension == null)
            return false;

        card = ExtensionCatalog.FindCard(extension, cardId);
        return card != null;
    }

    private static GameSetupConfig Normalise(GameSetupConfig config)
    {
        if (config == null)
            config = new GameSetupConfig();
        if (string.IsNullOrEmpty(config.stage))
            config.stage = SelectionStage;
        if (config.extensions == null)
            config.extensions = new List<ExtensionSetupSelection>();
        if (config.kingdomCardIds == null)
            config.kingdomCardIds = new List<string>();

        foreach (ExtensionPackageData package in ExtensionCatalog.All)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
                continue;

            ExtensionSetupSelection selection = FindExtension(config, package.id);
            if (selection == null)
            {
                config.extensions.Add(CreateDefaultSelection(package));
                continue;
            }

            if (selection.selectedCardIds == null)
                selection.selectedCardIds = new List<string>();
        }

        config.kingdomCardIds = NormaliseKingdomCardRefs(config.kingdomCardIds);
        return config;
    }

    private static List<string> NormaliseKingdomCardRefs(List<string> cardRefs)
    {
        List<string> result = new List<string>();
        HashSet<string> seenCardRefs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (cardRefs == null)
            return result;

        foreach (string cardRef in cardRefs)
        {
            if (!TryResolveKingdomCard(cardRef, out ExtensionPackageData extension, out ExtensionCardData card))
                continue;

            string canonicalRef = MakeCardRef(extension.id, card.id);
            if (seenCardRefs.Add(canonicalRef))
                result.Add(canonicalRef);
        }

        return result;
    }

    private static bool TryResolveKingdomCard(string cardRef, out ExtensionPackageData extension, out ExtensionCardData card)
    {
        extension = null;
        card = null;
        if (string.IsNullOrWhiteSpace(cardRef))
            return false;

        int separator = cardRef.IndexOf(':');
        if (separator <= 0 || separator >= cardRef.Length - 1)
            return false;

        string extensionId = cardRef.Substring(0, separator).Trim();
        string cardId = cardRef.Substring(separator + 1).Trim();
        if (extensionId.Length == 0 || cardId.Length == 0)
            return false;

        extension = ExtensionCatalog.Find(extensionId);
        card = FindKingdomCard(extension, cardId);
        return card != null;
    }

    private static ExtensionCardData FindKingdomCard(ExtensionPackageData extension, string cardId)
    {
        if (extension == null || extension.cards == null || string.IsNullOrWhiteSpace(cardId))
            return null;

        string trimmedCardId = cardId.Trim();
        return extension.cards.Find(candidate =>
            candidate != null &&
            !string.IsNullOrWhiteSpace(candidate.id) &&
            string.Equals(candidate.id.Trim(), trimmedCardId, StringComparison.OrdinalIgnoreCase));
    }

    private static ExtensionSetupSelection CreateDefaultSelection(ExtensionPackageData extension)
    {
        ExtensionSetupSelection selection = new ExtensionSetupSelection
        {
            extensionId = extension.id,
            enabled = string.Equals(extension.id, "base", StringComparison.OrdinalIgnoreCase)
        };

        // Deliberately Kingdom cards only. baseCards never appear in this list.
        if (extension.cards != null)
        {
            foreach (ExtensionCardData card in extension.cards)
            {
                if (card != null && !string.IsNullOrEmpty(card.id))
                    selection.selectedCardIds.Add(card.id);
            }
        }

        return selection;
    }
}
