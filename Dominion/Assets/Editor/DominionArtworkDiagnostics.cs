#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

public static class DominionArtworkDiagnostics
{
    [MenuItem("Dominion/UI/Diagnose Extension Artworks")]
    public static void Diagnose()
    {
        ExtensionCatalog.Reload();

        int extensionCount = 0;
        int extensionArtworkCount = 0;
        int cardCount = 0;
        int cardArtworkCount = 0;

        foreach (ExtensionPackageData extension in ExtensionCatalog.All)
        {
            if (extension == null)
                continue;

            extensionCount++;
            Sprite extensionSprite = ExtensionVisualLoader.LoadExtensionArtwork(extension);
            if (extensionSprite != null)
                extensionArtworkCount++;

            Debug.Log(
                "[Dominion Artwork] Extension " + extension.id
                + " | folder=" + extension.packageDirectory
                + " | artwork=" + (extensionSprite != null ? "OK" : "MISSING"));

            if (extension.cards == null)
                continue;

            foreach (ExtensionCardData card in extension.cards)
            {
                if (card == null)
                    continue;

                cardCount++;
                string resolvedPath = ExtensionVisualLoader.ResolveCardArtworkPath(extension, card);
                Sprite sprite = ExtensionVisualLoader.LoadCardArtwork(extension, card);
                if (sprite != null)
                    cardArtworkCount++;

                Debug.Log(
                    "[Dominion Artwork] " + extension.id + ":" + card.id
                    + " | json=" + card.image
                    + " | resolved=" + (string.IsNullOrEmpty(resolvedPath) ? "<none>" : resolvedPath)
                    + " | " + (sprite != null ? "OK" : "MISSING"));
            }
        }

        string summary =
            "Extensions: " + extensionArtworkCount + "/" + extensionCount + " artworks\n"
            + "Cards: " + cardArtworkCount + "/" + cardCount + " artworks";

        Debug.Log("[Dominion Artwork] DIAGNOSTIC COMPLETE — " + summary.Replace("\n", " | "));
        EditorUtility.DisplayDialog("Dominion Artwork Diagnostic", summary, "OK");
    }
}
#endif
