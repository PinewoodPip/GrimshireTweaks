
using HarmonyLib;
using UnityEngine;
using static GrimshireTweaks.Utils;

public static class EfficientAutoCook
{
    // Cooking UI: when auto-selecting ingredients,
    // prioritize the ones with less stamina value (average of player & villager values)
    [HarmonyPatch(typeof(CookingUI), "PullIngredients")]
    [HarmonyPrefix]
    static bool PrioritizeLowStaminaIngredients(CookingUI __instance)
    {
        var ui = __instance;
        IngredientOption[] ingredOptions = GetField<IngredientOption[]>(ui, "ingredOptions");
        Inventory playerInventory = GetField<Inventory>(ui, "playerInventory");

        // For each ingredient the recipe requires
        foreach (IngredientOption option in ingredOptions)
        {
            if (!option.gameObject.activeSelf) continue; // Skip unused ingredient slots

            Debug.Log($"Finding best ingredient for category {option.requiredFoodCategoryType}, item {option.requiredFoodItem?.Label}");

            // Find the valid ingredient in the player's inventory that restores the least stamina
            float lowestStamina = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < playerInventory.Items.Length; i++)
            {
                InventoryItem item = playerInventory[i].itemReference;
                if (item != null && ((option.requiredFoodCategoryType != FoodCategoryType.None && option.requiredFoodCategoryType == item.FoodCategoryType) || option.requiredFoodItem == item))
                {
                    float staminaValue = (item.PlayerDietCaloricValue + item.CaloricValue) / 2f; // Use average of player & villager stamina values
                    if (staminaValue < lowestStamina)
                    {
                        Debug.Log($"Found better ingredient for recipe slot: {item.Label} (stamina {staminaValue})");
                        lowestStamina = staminaValue;
                        bestIndex = i;
                    }
                }
            }
            // Select the item
            if (bestIndex != -1)
            {
                option.SelectItemFromPlayerInv(playerInventory[bestIndex].itemReference, bestIndex);
            }
        }
        ui.CheckIfAllIngredSelected();
        return false; // Skip original method
    }
}