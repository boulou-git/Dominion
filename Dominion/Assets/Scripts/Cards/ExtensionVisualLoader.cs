using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// Loads extension/card images from a package directory in StreamingAssets.
/// Explicit JSON paths are supported, but conventional filenames are also auto-discovered.
/// Card lookup is deliberately tolerant: id, display name and normalised filenames are accepted.
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

        // 1. Exact path written in extension.json always wins.
        Sprite explicitSprite = LoadRelative(extension.packageDirectory, card.image, false);
        if (explicitSprite != null)
            return explicitSprite;

        // 2. Stable convention: images/<card id>.*
        if (!string.IsNullOrWhiteSpace(card.id))
        {
            Sprite byId = LoadFirstExisting(extension.packageDirectory, Path.Combine("images", card.id));
            if (byId != null)
                return byId;
        }

        // 3. Also accept images/<display name>.* for locally renamed cards.
        if (!string.IsNullOrWhiteSpace(card.name))
        {
            Sprite byName = LoadFirstExisting(extension.packageDirectory, Path.Combine("images", card.name));
            if (byName != null)
                return byName;
        }

        // 4. Last-resort discovery: compare filenames after removing accents, spaces,
        // punctuation, underscores and case differences. This keeps local asset naming forgiving.
        return FindNormalisedCardImage(extension.packageDirectory, card);
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

    private static Sprite FindNormalisedCardImage(string packageDirectory, ExtensionCardData card)
    {
        if (string.IsNullOrWhiteSpace(packageDirectory))
            return null;

        string imagesDirectory = Path.Combine(packageDirectory, "images");
        if (!Directory.Exists(imagesDirectory))
            return null;

        string idKey = NormaliseKey(card.id);
        string nameKey = NormaliseKey(card.name);

        try
        {
            foreach (string file in Directory.GetFiles(imagesDirectory))
            {
                string extension = Path.GetExtension(file);
                if (!IsSupportedExtension(extension))
                    continue;

                string fileKey = NormaliseKey(Path.GetFileNameWithoutExtension(file));
                if ((!string.IsNullOrEmpty(idKey) && fileKey == idKey) ||
                    (!string.IsNullOrEmpty(nameKey) && fileKey == nameKey))
                {
                    string relative = Path.Combine("images", Path.GetFileName(file));
                    return LoadRelative(packageDirectory, relative, false);
                }
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not scan Dominion card visuals in '" + imagesDirectory + "': " + exception.Message);
        }

        return null;
    }

    private static bool IsSupportedExtension(string extension)
    {
        foreach (string supported in SupportedExtensions)
        {
            if (string.Equals(extension, supported, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    private static string NormaliseKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string decomposed = value.Normalize(NormalizationForm.FormD);
        StringBuilder builder = new StringBuilder(decomposed.Length);

        foreach (char character in decomposed)
        {
            System.Globalization.UnicodeCategory category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == System.Globalization.UnicodeCategory.NonSpacingMark)
                continue;

            if (char.IsLetterOrDigit(character))
                builder.Append(char.ToLowerInvariant(character));
        }

        return builder.ToString();
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
