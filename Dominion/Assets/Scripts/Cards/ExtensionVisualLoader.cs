using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Loads extension/card images from a package directory in StreamingAssets.
/// Explicit JSON paths are supported, but conventional filenames are also auto-discovered:
/// artwork.(png|jpg|jpeg) for an extension and images/&lt;cardId&gt;.(png|jpg|jpeg) for a card.
/// Sprites are cached per absolute path.
/// </summary>
public static class ExtensionVisualLoader
{
    private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg" };

    public static Sprite LoadExtensionArtwork(ExtensionPackageData extension)
    {
        if (extension == null)
            return null;

        Sprite explicitSprite = LoadRelative(extension.packageDirectory, extension.artwork, false);
        if (explicitSprite != null)
            return explicitSprite;

        return LoadFirstExisting(extension.packageDirectory, "artwork");
    }

    public static Sprite LoadCardArtwork(ExtensionPackageData extension, ExtensionCardData card)
    {
        if (extension == null || card == null)
            return null;

        Sprite explicitSprite = LoadRelative(extension.packageDirectory, card.image, false);
        if (explicitSprite != null)
            return explicitSprite;

        if (string.IsNullOrWhiteSpace(card.id))
            return null;

        return LoadFirstExisting(extension.packageDirectory, Path.Combine("images", card.id));
    }

    public static void ClearCache()
    {
        foreach (Sprite sprite in SpriteCache.Values)
        {
            if (sprite == null)
                continue;

            Texture2D texture = sprite.texture;
            UnityEngine.Object.Destroy(sprite);
            if (texture != null)
                UnityEngine.Object.Destroy(texture);
        }

        SpriteCache.Clear();
    }

    private static Sprite LoadFirstExisting(string packageDirectory, string relativePathWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(packageDirectory) || string.IsNullOrWhiteSpace(relativePathWithoutExtension))
            return null;

        foreach (string extension in SupportedExtensions)
        {
            string candidate = relativePathWithoutExtension + extension;
            Sprite sprite = LoadRelative(packageDirectory, candidate, false);
            if (sprite != null)
                return sprite;
        }

        return null;
    }

    private static Sprite LoadRelative(string packageDirectory, string relativePath, bool warnIfMissing)
    {
        if (string.IsNullOrWhiteSpace(packageDirectory) || string.IsNullOrWhiteSpace(relativePath))
            return null;

        string absolutePath = Path.GetFullPath(Path.Combine(packageDirectory, relativePath));
        string packageRoot = Path.GetFullPath(packageDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;

        if (!absolutePath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
        {
            Debug.LogWarning("Rejected visual outside extension package: " + relativePath);
            return null;
        }

        Sprite cached;
        if (SpriteCache.TryGetValue(absolutePath, out cached))
            return cached;

        if (!File.Exists(absolutePath))
        {
            if (warnIfMissing)
                Debug.LogWarning("Dominion visual not found: " + absolutePath);
            return null;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(absolutePath);
            Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.name = Path.GetFileNameWithoutExtension(absolutePath);

            if (!texture.LoadImage(bytes, false))
            {
                UnityEngine.Object.Destroy(texture);
                Debug.LogWarning("Could not decode Dominion visual: " + absolutePath);
                return null;
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;

            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 100f);
            sprite.name = texture.name;
            SpriteCache[absolutePath] = sprite;
            return sprite;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not load Dominion visual '" + absolutePath + "': " + exception.Message);
            return null;
        }
    }
}
