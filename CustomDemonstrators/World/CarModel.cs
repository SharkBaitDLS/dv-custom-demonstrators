using System.Collections.Generic;
using DV.ThingTypes;
using UnityEngine;

namespace CustomDemonstrators.World;

// A static dummy render of a train car
internal static class CarModel
{
    internal static GameObject? Build(TrainCarLivery? livery, Transform parent)
    {
        var prefab = livery?.prefab;
        if (prefab == null)
        {
            Main.Logger.Warning($"Car model: '{livery?.id}' has no prefab to copy from.");
            return null;
        }

        var car = new GameObject(livery!.id);
        car.transform.SetParent(parent, worldPositionStays: false);

        CopyPrefab(prefab, car);

        // Doors, windows, etc.
        var interactables = livery.externalInteractablesPrefab;
        if (interactables != null)
        {
            var fittings = new GameObject(interactables.name);
            fittings.transform.SetParent(car.transform, worldPositionStays: false);

            CopyPrefab(interactables, fittings);
            if (fittings.GetComponentInChildren<MeshRenderer>(includeInactive: true) == null)
            {
                Object.DestroyImmediate(fittings);
            }
        }

        if (car.GetComponentInChildren<MeshRenderer>(includeInactive: true) != null) return car;

        Main.Logger.Warning($"Car model: nothing drawable found on the '{livery.id}' prefab.");
        Object.DestroyImmediate(car);
        return null;
    }

    // Measure the meshes directly because Renderer.bounds is useless while the object is still inactive
    internal static Bounds LocalBounds(GameObject car)
    {
        var bounds = new Bounds();
        bool any = false;

        foreach (var filter in car.GetComponentsInChildren<MeshFilter>(includeInactive: true))
        {
            var mesh = filter.sharedMesh;
            if (mesh == null) continue;

            var toCar = car.transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            foreach (var corner in Corners)
            {
                var point = toCar.MultiplyPoint3x4(
                    mesh.bounds.center + Vector3.Scale(mesh.bounds.extents, corner));
                if (any)
                {
                    bounds.Encapsulate(point);
                }
                else
                {
                    bounds = new Bounds(point, Vector3.zero);
                    any = true;
                }
            }
        }

        return bounds;
    }

    private static void CopyPrefab(GameObject prefab, GameObject into)
    {
        var lowerLods = LowerLods(prefab);
        CopyRenderers(prefab.transform, into, lowerLods);
        for (int i = 0; i < prefab.transform.childCount; i++)
        {
            CopyVisuals(prefab.transform.GetChild(i), into.transform, lowerLods);
        }
    }

    private static GameObject? CopyVisuals(Transform source, Transform parent, HashSet<Renderer> lowerLods)
    {
        if (!source.gameObject.activeSelf || source.name.Contains("highlight")) return null;

        var copy = new GameObject(source.name);
        copy.transform.SetParent(parent, worldPositionStays: false);
        copy.transform.localPosition = source.localPosition;
        copy.transform.localRotation = source.localRotation;
        copy.transform.localScale = source.localScale;

        bool drawn = CopyRenderers(source, copy, lowerLods);
        for (int i = 0; i < source.childCount; i++)
        {
            if (CopyVisuals(source.GetChild(i), copy.transform, lowerLods) != null) drawn = true;
        }

        if (drawn) return copy;

        Object.DestroyImmediate(copy);
        return null;
    }

    private static bool CopyRenderers(Transform source, GameObject copy, HashSet<Renderer> lowerLods)
    {
        var filter = source.GetComponent<MeshFilter>();
        var renderer = source.GetComponent<MeshRenderer>();
        if (filter != null && renderer != null && filter.sharedMesh != null && Drawable(renderer, lowerLods))
        {
            copy.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            Dress(copy.AddComponent<MeshRenderer>(), renderer);
            return true;
        }

        var skinned = source.GetComponent<SkinnedMeshRenderer>();
        if (skinned != null && skinned.sharedMesh != null && Drawable(skinned, lowerLods))
        {
            copy.AddComponent<MeshFilter>().sharedMesh = skinned.sharedMesh;
            Dress(copy.AddComponent<MeshRenderer>(), skinned);
            return true;
        }

        return false;
    }

    // The raindrop shader reads a screen grab we aren't including on our mesh object and produces
    // black windows if we keep it
    private static bool Drawable(Renderer renderer, HashSet<Renderer> lowerLods) =>
        !lowerLods.Contains(renderer) && !IsRaindropShader(renderer);

    private static bool IsRaindropShader(Renderer renderer)
    {
        foreach (var material in renderer.sharedMaterials)
        {
            if (material == null || material.shader == null) continue;
            if (material.shader.name.Contains("Droplet")) return true;
        }

        return false;
    }

    private static void Dress(MeshRenderer copy, Renderer source)
    {
        copy.sharedMaterials = source.sharedMaterials;
        copy.shadowCastingMode = source.shadowCastingMode;
        copy.receiveShadows = source.receiveShadows;
        copy.lightProbeUsage = source.lightProbeUsage;
        copy.reflectionProbeUsage = source.reflectionProbeUsage;
    }

    private static HashSet<Renderer> LowerLods(GameObject prefab)
    {
        var lower = new HashSet<Renderer>();
        var top = new HashSet<Renderer>();

        foreach (var group in prefab.GetComponentsInChildren<LODGroup>(includeInactive: true))
        {
            var lods = group.GetLODs();
            for (int level = 0; level < lods.Length; level++)
            {
                foreach (var renderer in lods[level].renderers)
                {
                    if (renderer == null) continue;
                    (level == 0 ? top : lower).Add(renderer);
                }
            }
        }

        lower.ExceptWith(top);
        return lower;
    }

    private static readonly Vector3[] Corners =
    [
        new(-1f, -1f, -1f), new(-1f, -1f, 1f), new(-1f, 1f, -1f), new(-1f, 1f, 1f),
        new(1f, -1f, -1f), new(1f, -1f, 1f), new(1f, 1f, -1f), new(1f, 1f, 1f),
    ];
}
