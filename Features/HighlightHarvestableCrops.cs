
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static GrimshireTweaks.Utils;

public static class HighlightHarvestableCrops
{
    static Color HIGHLIGHT_COLOR = new Color(0.7f, 0.85f, 0.7f, 1f); // Light teal, somewhat greenish

    static Material originalMaterial = null;
    static Material highlightMaterial = null;
    static HashSet<CropObject> cachedCrops = [];
    static bool isHighlighting = false;

    // Also highlight harvestable crops when holding the scythe.
    [HarmonyPatch(typeof(TileMapManager), "ToggleWeedHighlight")]
    [HarmonyPostfix]
    static void HighlightCrops(TileMapManager __instance, bool toggle)
    {
        isHighlighting = toggle;

        // Toggle highlight on all harvestable crops
        HashSet<CropObject> staleCropReferences = [];
        foreach (CropObject crop in cachedCrops)
        {
            if (crop == null)
            {
                staleCropReferences.Add(crop);
                continue;
            }
            bool harvestable = CallMethod<bool>(crop, "IsHarvestable");
            SetCropHighlighted(crop, harvestable && isHighlighting);
        }

        // Remove stale crop references
        foreach (CropObject staleCrop in staleCropReferences)
        {
            cachedCrops.Remove(staleCrop);
        }
    }

    // Applies or removes the highlight effect from a crop.
    private static void SetCropHighlighted(CropObject crop, bool highlight)
    {
        var persistentCropData = GetField<CropManager.PersistentCropData>(crop, "persistenCropDataContainer"); // Field typo is from the game.
        var subCropObjs = GetField<GameObject[]>(crop, "subCropObjs");

        // Set material for all subsprites of the crop
        // The game uses multiple objects to display bumper crops.
        for (int i = 0; i < persistentCropData.subPlantsList.GetLength(0); i++)
        {
            var subCropObj = subCropObjs[i];
            var spriteRenderer = subCropObj.GetComponent<SpriteRenderer>();

            // Cache materials
            if (originalMaterial == null)
            {
                originalMaterial = spriteRenderer.material;
                highlightMaterial = new Material(Shader.Find("Shader Graphs/HighlightShader"));
                highlightMaterial.SetColor("_OutlineColor", HIGHLIGHT_COLOR);
            }

            // Toggle highlight material
            spriteRenderer.material = highlight ? highlightMaterial : originalMaterial;
        }
    }

    // Track instantiated crops to avoid overhead from FindObjectByType()
    [HarmonyPatch(typeof(CropObject), "Start")]
    [HarmonyPostfix]
    static void OnCropCreated(CropObject __instance)
    {
        cachedCrops.Add(__instance);
    }
}