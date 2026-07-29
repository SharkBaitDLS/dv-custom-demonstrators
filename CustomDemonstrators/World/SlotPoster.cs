using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityModManagerNet;

namespace CustomDemonstrators.World;

// A CCL author supplies a photo for the board by providing `DemonstratorPosters/<livery id>.png` as part of
// their mod's assets. It should be a square 512x512 image to match the game's expectations. The actual shown
// part of the image will be 512x400 because of the nameplate strip at the top.
//
// A player can put one in this mod's own DemonstratorPosters folder to override whatever a mod ships with.
internal static class SlotPoster
{
    private const string Folder = "DemonstratorPosters";

    // Sentinel name so we know if a poster is safe to destroy or not
    private const string Mine = "CustomDemonstrators_Poster";

    private static readonly int MainTex = Shader.PropertyToID("_MainTex");
    private static readonly int MainTexST = Shader.PropertyToID("_MainTex_ST");

    // Shows the loco's own picture or blank if one can't be found
    internal static void Apply(GameObject board, string locoId)
    {
        var posters = Posters(board);
        if (posters.Count == 0) return;

        var picture = Load(locoId);
        var shown = false;

        foreach (var poster in posters)
        {
            Forget(poster);

            var reskinned = picture != null && Reskin(poster, picture);
            poster.enabled = reskinned;
            shown |= reskinned;
        }

        if (picture != null && !shown) UnityEngine.Object.Destroy(picture);
    }

    // Hands a panel back to the museum if we overrode a vanilla one
    internal static void Restore(GameObject board)
    {
        foreach (var poster in Posters(board))
        {
            Forget(poster);
            poster.enabled = true;
        }
    }

    // The pictures are ours alone, and they do not go away with the board.
    internal static void Discard(GameObject board)
    {
        foreach (var poster in Posters(board)) Forget(poster);
    }

    private static List<MeshRenderer> Posters(GameObject board) =>
        [.. board.GetComponentsInChildren<MeshRenderer>(includeInactive: true)
            .Where(r => r != null && r.name.StartsWith("ShopPoster", StringComparison.Ordinal))];

    private static bool Reskin(MeshRenderer poster, Texture2D picture)
    {
        var mesh = poster.GetComponent<MeshFilter>()?.sharedMesh;
        if (mesh == null) return false;

        if (Tile(mesh.name) is not Rect window)
        {
            Main.Logger.Warning($"The museum's poster mesh '{mesh.name}' is not laid out the way this mod "
                + "expects, so we aren't rendering a picture.");
            return false;
        }

        // Vanilla pictures are tiles of one shared atlas, so the panel is told to stretch that tile's window
        // back over the whole of the new one so we don't interfere with the museum's original materials and
        // can just drop the override if the board is destroyed.
        var block = new MaterialPropertyBlock();
        poster.GetPropertyBlock(block);
        block.SetTexture(MainTex, picture);
        block.SetVector(MainTexST, new Vector4(
            1f / window.width, 1f / window.height,
            -window.x / window.width, -window.y / window.height));
        poster.SetPropertyBlock(block);
        return true;
    }

    // Drops whatever picture this mod last put on a panel, leaving the museum's own showing again.
    private static void Forget(MeshRenderer poster)
    {
        var block = new MaterialPropertyBlock();
        poster.GetPropertyBlock(block);

        if (block.GetTexture(MainTex) is Texture2D previous && previous.name == Mine)
            UnityEngine.Object.Destroy(previous);

        poster.SetPropertyBlock(null);
    }

    // The mesh name corresponds to the tile on the atlas
    // A1 | A2
    // B1 | B2
    // etc.
    private static Rect? Tile(string name)
    {
        var mark = name.LastIndexOf('_');
        if (mark < 0 || mark + 2 >= name.Length) return null;

        var row = char.ToUpperInvariant(name[mark + 1]) - 'A';
        if (row < 0 || !int.TryParse(name.Substring(mark + 2), out var column) || column < 1) return null;

        const float size = 0.25f;
        return new Rect((column - 1) * size, 1f - (row + 1) * size, size, size);
    }

    private static Texture2D? Load(string locoId)
    {
        var file = Find(locoId);
        if (file == null) return null;

        var picture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: true)
        {
            name = Mine,
            wrapMode = TextureWrapMode.Clamp,
        };

        try
        {
            if (picture.LoadImage(File.ReadAllBytes(file)))
            {
                Main.Logger.Log($"Additional demonstrator '{locoId}' is showing the restoration poster from {file}.");
                return picture;
            }
            Main.Logger.Warning($"The restoration poster at {file} is not an image the game can read.");
        }
        catch (Exception error)
        {
            Main.Logger.Warning($"Could not read the restoration poster at {file}: {error.Message}");
        }

        UnityEngine.Object.Destroy(picture);
        return null;
    }

    // Finds an image for the mod, following a hierarchy:
    // 1. An image added by the user in this mod's own directory
    // 2. An image provided by the CCL mod that distributed the locomotive
    // 3. An image provided by a different mod that matches the livery ID
    private static string? Find(string locoId)
    {
        if (!string.IsNullOrEmpty(Main.ModPath))
        {
            var mine = Path.Combine(Main.ModPath, Folder, locoId + ".png");
            if (File.Exists(mine)) return mine;
        }

        string? elsewhere = null;
        string? theirs = null;

        foreach (var mod in UnityModManager.modEntries)
        {
            if (mod == null || !mod.Active || string.IsNullOrEmpty(mod.Path)) continue;

            var folder = Path.Combine(mod.Path, Folder);
            var file = Path.Combine(folder, locoId + ".png");
            var defines = Defines(mod, locoId);

            if (File.Exists(file))
            {
                if (defines) return file;
                elsewhere ??= file;
            }
            else if (defines && Directory.Exists(folder))
            {
                theirs = folder;
            }
        }

        if (elsewhere == null && theirs != null) ReportMiss(locoId, theirs);

        return elsewhere;
    }

    // Helpful log message for people like me that typo their image names and get confused
    private static void ReportMiss(string locoId, string folder)
    {
        string held;
        try
        {
            held = string.Join(", ", Directory.GetFiles(folder, "*.png").Select(Path.GetFileName));
        }
        catch (Exception error)
        {
            held = error.Message;
        }

        Main.Logger.Warning($"The mod providing '{locoId}' ships restoration posters, but none named for it: "
            + $"{folder} holds {held}. A poster is named after the livery it belongs to, so this one wants "
            + $"{locoId}.png.");
    }

    // Reflect into the CCL GUI to figure out which liveries come from which folders
    // to break ties in the event two mods provide the same image name.
    private static bool Defines(UnityModManager.ModEntry mod, string locoId)
    {
        if (mod.OnGUI == null) return false;

        foreach (var handler in mod.OnGUI.GetInvocationList())
        {
            var loaded = handler?.Target;
            if (loaded?.GetType().FullName != "CCL.Importer.LoadedModSettings") continue;
            if (loaded.GetType().GetField("Ids")?.GetValue(loaded) is not IEnumerable cars) continue;

            foreach (var car in cars)
            {
                if (car?.GetType().GetField("Item2")?.GetValue(car) is IEnumerable liveries
                    && liveries.Cast<object>().Any(l => (l as string) == locoId))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
