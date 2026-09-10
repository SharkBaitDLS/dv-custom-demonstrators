using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using CustomDemonstrators.Slots;

namespace CustomDemonstrators.World;

// A CCL author supplies a photo for the board on the livery itself, as its DemonstratorPoster texture.
// It should be a square 512x512 image to match the game's expectations. The actual shown part of the image
// will be 512x400 because of the nameplate strip at the top.
//
// A player overrides whatever a car's mod ships by dropping `<livery id>.png` into this mod's own
// DemonstratorPosters folder.
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

        var (picture, owned) = Load(locoId);
        var shown = false;

        foreach (var poster in posters)
        {
            Forget(poster);

            var reskinned = picture != null && Reskin(poster, picture!);
            poster.enabled = reskinned;
            shown |= reskinned;
        }

        // A texture read from a file is ours to clean up
        if (picture != null && owned && !shown) UnityEngine.Object.Destroy(picture);
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

    // The player's own override first, then whatever the car's mod put on the livery.
    private static (Texture2D? Picture, bool Owned) Load(string locoId)
    {
        if (!string.IsNullOrEmpty(Main.ModPath))
        {
            var file = Path.Combine(Main.ModPath, Folder, locoId + ".png");
            if (File.Exists(file) && Read(locoId, file) is Texture2D own) return (own, true);
        }

        if (CustomCarLoaderHelper.PosterFor(DemonstratorSetup.GetLivery(locoId)) is Texture2D authored)
        {
            Main.Logger.Log($"Additional demonstrator '{locoId}' is showing the restoration poster its own "
                + "mod ships on the livery.");
            return (authored, false);
        }

        return (null, false);
    }

    private static Texture2D? Read(string locoId, string file)
    {
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
}
