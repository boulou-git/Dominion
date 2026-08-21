using System;
using System.Collections.Generic;
using Photon.Pun;
using ExitGames.Client.Photon;
using UnityEngine;

[Serializable]
public sealed class GameSetupConfig
{
    public List<ExtensionSetupSelection> extensions = new List<ExtensionSetupSelection>();
}

[Serializable]
public sealed class ExtensionSetupSelection
{
    public string extensionId;
    public bool enabled;
    public List<string> selectedCardIds = new List<string>();
}

/// <summary>
/// Stores the host's pre-game extension/card pool in Photon room properties.
/// The setup is room state: all clients can read it, only the Master Client writes it.
/// </summary>
public static class RoomGameSetup
{
    public const string RoomPropertyKey = "dominion.setup";

    public static GameSetupConfig CreateDefault()
    {
        GameSetupConfig config = new GameSetupConfig();

        foreach (ExtensionPackageData extension in ExtensionCatalog.All)
        {
            if (extension == null || string.IsNullOrEmpty(extension.id))
                continue;

            ExtensionSetupSelection selection = CreateDefaultSelection(extension);
            config.extensions.Add(selection);
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

    public static ExtensionSetupSelection FindExtension(GameSetupConfig config, string extensionId)
    {
        if (config == null || config.extensions == null)
            return null;

        return config.extensions.Find(e => e != null && string.Equals(e.extensionId, extensionId, StringComparison.OrdinalIgnoreCase));
    }

    public static int CountSelectedCards(GameSetupConfig config)
    {
        if (config == null || config.extensions == null)
            return 0;

        int count = 0;
        foreach (ExtensionSetupSelection extension in config.extensions)
        {
            if (extension != null && extension.enabled && extension.selectedCardIds != null)
                count += extension.selectedCardIds.Count;
        }
        return count;
    }

    private static GameSetupConfig Normalise(GameSetupConfig config)
    {
        if (config == null)
            config = new GameSetupConfig();
        if (config.extensions == null)
            config.extensions = new List<ExtensionSetupSelection>();

        foreach (ExtensionPackageData package in ExtensionCatalog.All)
        {
            if (package == null || string.IsNullOrEmpty(package.id))
                continue;

            ExtensionSetupSelection selection = FindExtension(config, package.id);
            if (selection == null)
            {
                // An extension discovered after the config was created gets the same sensible
                // defaults as a fresh room. Existing selections, including empty ones, are preserved.
                config.extensions.Add(CreateDefaultSelection(package));
                continue;
            }

            if (selection.selectedCardIds == null)
                selection.selectedCardIds = new List<string>();
        }

        return config;
    }

    private static ExtensionSetupSelection CreateDefaultSelection(ExtensionPackageData extension)
    {
        ExtensionSetupSelection selection = new ExtensionSetupSelection
        {
            extensionId = extension.id,
            enabled = string.Equals(extension.id, "base", StringComparison.OrdinalIgnoreCase)
        };

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
