
using HarmonyLib;
using UnityEngine;
using static GrimshireTweaks.Utils;

public static class HighlightHarvestableCrops
{
    static Material originalMaterial = null;
    static Material highlightMaterial = null;

    // Also highlight harvestable crops when holding the scythe.
    [HarmonyPatch(typeof(TileMapManager), "ToggleWeedHighlight")]
    [HarmonyPostfix]
    static void HighlightCrops(TileMapManager __instance, bool toggle)
    {
        // Toggle highlight on all harvestable crops
		CropObject[] crops = GameObject.FindObjectsOfType<CropObject>(); // Unfortunately the game does not track this component.
        foreach (CropObject crop in crops)
        {
            bool harvestable = CallMethod<bool>(crop, "IsHarvestable");
            if (harvestable)
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
                        highlightMaterial = GetField<Material>(__instance, "weedHighlightMat");
                    }

                    // Toggle highlight material
                    spriteRenderer.material = toggle ? highlightMaterial : originalMaterial;
                }
            }
        }
    }
}