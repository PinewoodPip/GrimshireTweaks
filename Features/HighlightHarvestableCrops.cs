
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static GrimshireTweaks.Utils;

public static class HighlightHarvestableCrops
{
    static Color HIGHLIGHT_COLOR = new Color(0.7f, 0.85f, 0.7f, 1f); // Light teal, somewhat greenish

    static Material originalMaterial = null;
    static Material highlightMaterial = null;
    static Dictionary<CropObject, List<SpriteRenderer>> cachedSubPlantSpriteRenderers = [];
    static bool isHighlighting = false;

    // Also highlight harvestable crops when holding the scythe.
    [HarmonyPatch(typeof(TileMapManager), "ToggleWeedHighlight")]
    [HarmonyPostfix]
    static void HighlightCrops(TileMapManager __instance, bool toggle)
    {
        isHighlighting = toggle;

        // Toggle highlight on all harvestable crops
        HashSet<CropObject> staleCropReferences = [];
        foreach (CropObject crop in cachedSubPlantSpriteRenderers.Keys)
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
            cachedSubPlantSpriteRenderers.Remove(staleCrop);
        }
    }

    // Applies or removes the highlight effect from a crop.
    private static void SetCropHighlighted(CropObject crop, bool highlight)
    {
        // Set material for all subsprites of the crop
        // The game uses multiple objects to display bumper crops.
        List<SpriteRenderer> subCropSpriteRenderers = cachedSubPlantSpriteRenderers[crop];
        foreach (var spriteRenderer in subCropSpriteRenderers)
        {
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

    // Restore original material when targeting crops with the cursor
    // Necessary as IInteractable caches the "original" materials
    // when highlighting, which would otherwise cache the wrong material
    // while harvestable crops are being highlighted by this feature.
    [HarmonyPatch(typeof(IInteractable), "Highlight")]
    [HarmonyPrefix]
    static void OnCropTargeted(IInteractable __instance, Material highlightMat)
    {
        // Restore original material so that IInteractable does not confuse ours for it.
        if (isHighlighting && __instance is CropObject crop)
        {
            SetCropHighlighted(crop, false);
        }
    }
    [HarmonyPatch(typeof(IInteractable), "Highlight")]
    [HarmonyPostfix]
    static void AfterCropTargeted(IInteractable __instance, Material highlightMat)
    {
        // Restore our highlight when untargeting a harvestable crop.
        if (isHighlighting && highlightMat == null && __instance is CropObject crop)
        {
            SetCropHighlighted(crop, CallMethod<bool>(crop, "IsHarvestable"));
        }
    }

    // Remove highlight when crop sprites are updated
    // and the crop is no longer harvestable (ex. when harvesting a regrowable crop)
    [HarmonyPatch(typeof(CropObject), "UpdateSprite")]
    [HarmonyPostfix]
    static void OnCropSpritesUpdated(CropObject __instance)
    {
        if (isHighlighting && !CallMethod<bool>(__instance, "IsHarvestable"))
        {
            SetCropHighlighted(__instance, false);
        }
    }

    // Track instantiated crops to avoid overhead from FindObjectByType() and GetComponent() for their sprites
    [HarmonyPatch(typeof(CropObject), "Start")]
    [HarmonyPostfix]
    static void OnCropCreated(CropObject __instance)
    {
        CacheCrop(__instance);
    }
    [HarmonyPatch(typeof(CropObject), "UpdateSprite")]
    [HarmonyPrefix]
    static void OnBeforeCropSpritesUpdated(CropObject __instance)
    {
        CacheCrop(__instance); // Necessary as this is sometimes called before Start()
    }

    // Caches the SpriteRenderers of a crop.
    static void CacheCrop(CropObject crop)
    {
        // Cache SpriteRenderers
        List<SpriteRenderer> cropSprites = [];
        cachedSubPlantSpriteRenderers[crop] = cropSprites;
        var persistentCropData = GetField<CropManager.PersistentCropData>(crop, "persistenCropDataContainer"); // Field typo is from the game.
        var subCropObjs = GetField<GameObject[]>(crop, "subCropObjs");
        for (int i = 0; i < persistentCropData.subPlantsList.GetLength(0); i++)
        {
            var subCropObj = subCropObjs[i];
            var spriteRenderer = subCropObj.GetComponent<SpriteRenderer>();
            cropSprites.Add(spriteRenderer);
        }
    }
}