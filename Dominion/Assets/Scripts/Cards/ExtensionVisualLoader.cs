using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Loads extension/card images from StreamingAssets extension packages.
/// In the Unity Editor it prefers AssetDatabase for already-imported project textures,
/// then falls back to direct file loading. Player builds use direct file loading.
/// </summary>
public static class ExtensionVisualLoader
{
    private static readonly Dictionary<string, Sprite> SpriteCache = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> RuntimeOwnedTextures = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static readonly string[] SupportedExtensions = { ".png", ".jpg", ".jpeg" };

    public static Sprite LoadExtensionArtwork(ExtensionPackageData extension)
    {
        if (extension == null)
            return null;

        string path = ResolveExtensionArtworkPath(extension);
        return LoadAbsolute(path);
    }

    public static Sprite LoadCardArtwork(ExtensionPackageData extension, ExtensionCardData card)
    {
        if (extension == null || card == null)
            return null;

        string path = ResolveCardArtworkPath(extension, card);
        if (string.IsNullOrEmpty(path))
        {
            Debug.LogWarning(
                "Dominion card artwork not found for "
                + extension.id
                + ":"
                + card.id
                + " in "
                + extension.packageDirectory);
            return null;
        }

        return LoadAbsolute(path);
    }

    public static string ResolveCardArtworkPath(ExtensionPackageData extension, ExtensionCardData card)
    {
        if (extension == null || card == null || string.IsNullOrWhiteSpace(extension.packageDirectory))
            return null;

        // 1. Explicit JSON path.
        string explicitPath = ResolveExisting(extension.packageDirectory, card.image);
        if (!string.IsNullOrEmpty(explicitPath))
            return explicitPath;

        // A bare filename such as "village.png" historically meant the images folder.
        if (!string.IsNullOrWhiteSpace(card.image)
            && string.IsNullOrEmpty(Path.GetDirectoryName(card.image)))
        {
            string explicitInImages = ResolveExisting(
                extension.packageDirectory,
                Path.Combine("images", card.image));
            if (!string.IsNullOrEmpty(explicitInImages))
                return explicitInImages;
        }

        // 2. Stable convention: images/<id>.*
        string byId = ResolveFirstExisting(
            extension.packageDirectory,
            Path.Combine("images", card.id));
        if (!string.IsNullOrEmpty(byId))
            return byId;

        // 3. Display name convention.
        string byName = ResolveFirstExisting(
            extension.packageDirectory,
            Path.Combine("images", card.name));
        if (!string.IsNullOrEmpty(byName))
            return byName;

        // 4. Forgiving scan for accents/spaces/underscores/case differences.
        return FindNormalisedCardImage(extension.packageDirectory, card);
    }

    public static void ClearCache()
    {
        foreach (KeyValuePair<string, Sprite> pair in SpriteCache)
        {
            Sprite sprite = pair.Value;
            if (sprite == null)
                continue;

            Texture2D texture = sprite.texture;
            UnityEngine.Object.Destroy(sprite);

            // Imported AssetDatabase textures belong to Unity and must never be destroyed here.
            if (texture != null && RuntimeOwnedTextures.Contains(pair.Key))
                UnityEngine.Object.Destroy(texture);
        }

        SpriteCache.Clear();
        RuntimeOwnedTextures.Clear();
    }

    private static string ResolveExtensionArtworkPath(ExtensionPackageData extension)
    {
        if (extension == null || string.IsNullOrWhiteSpace(extension.packageDirectory))
            return null;

        string explicitPath = ResolveExisting(extension.packageDirectory, extension.artwork);
        if (!string.IsNullOrEmpty(explicitPath))
            return explicitPath;

        return ResolveFirstExisting(extension.packageDirectory, "artwork");
    }

    private static string ResolveExisting(string packageDirectory, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(packageDirectory) || string.IsNullOrWhiteSpace(relativePath))
            return null;

        string absolutePath = SafeAbsolutePath(packageDirectory, relativePath);
        return !string.IsNullOrEmpty(absolutePath) && File.Exists(absolutePath)
            ? absolutePath
            : null;
    }

    private static string ResolveFirstExisting(string packageDirectory, string relativePathWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(relativePathWithoutExtension))
            return null;

        foreach (string extension in SupportedExtensions)
        {
            string path = ResolveExisting(packageDirectory, relativePathWithoutExtension + extension);
            if (!string.IsNullOrEmpty(path))
                return path;
        }

        return null;
    }

    private static string FindNormalisedCardImage(string packageDirectory, ExtensionCardData card)
    {
        string imagesDirectory = Path.Combine(packageDirectory, "images");
        if (!Directory.Exists(imagesDirectory))
            return null;

        string idKey = NormaliseKey(card.id);
        string nameKey = NormaliseKey(card.name);
        string explicitKey = NormaliseKey(Path.GetFileNameWithoutExtension(card.image));

        try
        {
            foreach (string file in Directory.GetFiles(imagesDirectory))
            {
                if (!IsSupportedExtension(Path.GetExtension(file)))
                    continue;

                string fileKey = NormaliseKey(Path.GetFileNameWithoutExtension(file));
                if ((!string.IsNullOrEmpty(idKey) && fileKey == idKey)
                    || (!string.IsNullOrEmpty(nameKey) && fileKey == nameKey)
                    || (!string.IsNullOrEmpty(explicitKey) && fileKey == explicitKey))
                    return Path.GetFullPath(file);
            }
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not scan Dominion card visuals in '" + imagesDirectory + "': " + exception.Message);
        }

        return null;
    }

    private static Sprite LoadAbsolute(string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
            return null;

        absolutePath = Path.GetFullPath(absolutePath);

        Sprite cached;
        if (SpriteCache.TryGetValue(absolutePath, out cached))
            return cached;

        if (!File.Exists(absolutePath))
        {
            Debug.LogWarning("Dominion visual not found: " + absolutePath);
            return null;
        }

#if UNITY_EDITOR
        // During development these files already live under Assets/ and are imported by Unity.
        // Loading the imported texture avoids platform/file-decoding differences in Editor tests.
        string assetPath = ToAssetPath(absolutePath);
        if (!string.IsNullOrEmpty(assetPath))
        {
            Texture2D importedTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(assetPath);
            if (importedTexture != null)
            {
                Sprite importedSprite = Sprite.Create(
                    importedTexture,
                    new Rect(0f, 0f, importedTexture.width, importedTexture.height),
                    new Vector2(0.5f, 0.5f),
                    100f);
                importedSprite.name = Path.GetFileNameWithoutExtension(absolutePath);
                SpriteCache[absolutePath] = importedSprite;
                Debug.Log("Loaded Dominion visual via AssetDatabase: " + assetPath);
                return importedSprite;
            }
        }
#endif

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

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, texture.width, texture.height),
                new Vector2(0.5f, 0.5f),
                100f);
            sprite.name = texture.name;
            SpriteCache[absolutePath] = sprite;
            RuntimeOwnedTextures.Add(absolutePath);
            Debug.Log("Loaded Dominion visual from file: " + absolutePath);
            return sprite;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Could not load Dominion visual '" + absolutePath + "': " + exception.Message);
            return null;
        }
    }

    private static string SafeAbsolutePath(string packageDirectory, string relativePath)
    {
        try
        {
            string packageRoot = Path.GetFullPath(packageDirectory)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            string absolutePath = Path.GetFullPath(Path.Combine(packageDirectory, relativePath));

            if (!absolutePath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("Rejected visual outside extension package: " + relativePath);
                return null;
            }

            return absolutePath;
        }
        catch (Exception exception)
        {
            Debug.LogWarning("Invalid Dominion visual path '" + relativePath + "': " + exception.Message);
            return null;
        }
    }

#if UNITY_EDITOR
    private static string ToAssetPath(string absolutePath)
    {
        string assetsRoot = Path.GetFullPath(Application.dataPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string full = Path.GetFullPath(absolutePath);

        if (!full.StartsWith(assetsRoot, StringComparison.OrdinalIgnoreCase))
            return null;

        string relative = full.Substring(assetsRoot.Length)
            .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Replace('\\', '/');
        return "Assets/" + relative;
    }
#endif

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
}
