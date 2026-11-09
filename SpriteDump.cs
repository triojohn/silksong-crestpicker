using System;
using System.IO;
using UnityEngine;

namespace CrestPicker;

/// <summary>
/// One-shot dump of every crest sprite to PNG on hotkey, so the art can be
/// inspected outside the game. Read-only: no game state is changed, nothing throws.
/// </summary>
internal static class SpriteDump
{
    /// <summary>Output directory of the dumped PNG files.</summary>
    internal const string Dir = @"C:\Users\trioj\Downloads\silksong_sprites";

    /// <summary>
    /// Saves every loaded sprite whose name contains "crest" (case-insensitive) to
    /// <see cref="Dir"/> and logs one line per sprite plus a closing summary.
    /// </summary>
    internal static void DumpAll()
    {
        int count = 0;
        try
        {
            Directory.CreateDirectory(Dir);
        }
        catch (Exception ex)
        {
            Plugin.Log.LogWarning($"[DUMP] cannot create '{Dir}': {ex.Message}");
            return;
        }

        foreach (Sprite sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite == null || sprite.name.IndexOf("crest", StringComparison.OrdinalIgnoreCase) < 0)
            {
                continue;
            }

            try
            {
                if (Save(sprite))
                {
                    count++;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogWarning($"[DUMP] failed '{sprite.name}': {ex.Message}");
            }
        }

        Plugin.Log.LogInfo($"[DUMP] done count={count} dir={Dir}");
    }

    /// <summary>
    /// Writes the sprite's own rect (not the whole atlas it may share) to a PNG through a
    /// readable <see cref="RenderTexture"/> copy. True when the file was written.
    /// </summary>
    private static bool Save(Sprite sprite)
    {
        Texture2D source = sprite.texture;
        if (source == null)
        {
            Plugin.Log.LogWarning($"[DUMP] failed '{sprite.name}': no texture");
            return false;
        }

        Rect rect = sprite.textureRect;
        int width = Mathf.Max(1, Mathf.RoundToInt(rect.width));
        int height = Mathf.Max(1, Mathf.RoundToInt(rect.height));
        string file = Path.Combine(Dir, FileName(sprite.name) + ".png");

        RenderTexture render = RenderTexture.GetTemporary(width, height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        RenderTexture? previous = RenderTexture.active;
        Texture2D copy = new Texture2D(width, height, TextureFormat.RGBA32, false);
        try
        {
            Graphics.Blit(source, render,
                new Vector2(rect.width / source.width, rect.height / source.height),
                new Vector2(rect.x / source.width, rect.y / source.height));
            RenderTexture.active = render;
            copy.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
            copy.Apply();
            RenderTexture.active = previous;
            File.WriteAllBytes(file, copy.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(render);
            UnityEngine.Object.Destroy(copy);
        }

        Plugin.Log.LogInfo($"[DUMP] {sprite.name} rect=({width},{height})" +
            $" ppu={sprite.pixelsPerUnit:0.##} path={file}");
        return true;
    }

    /// <summary>Sprite name made safe as a Windows file name.</summary>
    private static string FileName(string name)
    {
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(invalid, '_');
        }

        return name;
    }
}
