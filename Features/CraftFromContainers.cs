using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using static GrimshireTweaks.Utils;
using static GrimshireTweaks.CraftingUtils;

namespace GrimshireTweaks;

public static class CraftFromContainers
{
    static private bool ignoreCanMakeRecipeHook = false;

    // Allow crafting recipes using items from containers on the map
    [HarmonyPatch(typeof(CraftingUI), "MakeRecipe")]
    [HarmonyPrefix]
    static bool MakeRecipeWithIngredientsFromContainers(CraftingUI __instance, int numToCraft)
    {
        CraftingRecipeOption selectedRecipe = GetField<CraftingRecipeOption>(__instance, "selectedRecipe");
        Recipe recipe = selectedRecipe.craftingRecipeRef;

        // Check if the recipe can be made with items from just player inventory;
        // we must call CanMakeRecipe without our hook
        ignoreCanMakeRecipeHook = true;
        bool canMakeWithPlayerInventory = CallMethod<bool>(__instance, "CanMakeRecipe", recipe, numToCraft);
        ignoreCanMakeRecipeHook = false;
        if (canMakeWithPlayerInventory) return true;

        Plugin.DebugLog("Attempting to craft recipe with ingredients from containers...");

        // If we cannot craft the recipe from items in the inventory,
        // try to pull ingredients from containers on the map
        Inventory playerInventory = GetField<Inventory>(__instance, "playerInventory");
        List<ItemSourceUse> usedItemSources = CanMakeRecipeUsingContainers(playerInventory, __instance, recipe, numToCraft, null);
        if (usedItemSources == null) return true; // Cannot make recipe even with containers

        // Craft the item; mostly copy-pasted from original method
        if (playerInventory.CanAddStack(recipe, numToCraft))
        {
            // Remove used items from the source containers
            Plugin.DebugLog($"Crafting using items from {usedItemSources.Count} containers");
            ConsumeItems(playerInventory, usedItemSources);

            // Add crafted item
            playerInventory.AddItem(recipe, 1f, numToCraft);

            // Update UI
            CallMethod(__instance, "UpdateRecipeDisplay");
            CallMethod(__instance, "UpdateInventoryDisplay");
            __instance.UpdateDetailsPanel(selectedRecipe);
            GameManager.Instance.Player.PlayGotItemSFX();
            InventoryItem item = ResourceManager.Instance.GetInventoryItemByID(recipe.ID);
            GameManager.Instance.NotificationPanel.DisplayNotification(item.Label, 2f, item.InventoryDisplayIcon, numToCraft);
            GameManager.Instance.Player.UpdateToolBar();
            CallMethod(__instance, "UpdateCraftingButtons");

            // For some reason UpdateDetailsPanel() is no-op if lockRecipeSelected is true;
            // we must temporarily unset it to properly update the ingredient slots
            bool recipeWasLocked = GetField<bool>(__instance, "lockRecipeSelected");
            SetField(__instance, "lockRecipeSelected", false);
            __instance.UpdateDetailsPanel(selectedRecipe);
            SetField(__instance, "lockRecipeSelected", recipeWasLocked);
        }
        else
        {
            UnityEngine.Object.FindObjectOfType<UIController>().PlayNegativeSFX();
            GameManager.Instance.PopUpDialogBox.DisplayMsg("Inventory Full!", 3f);
        }
        return false;
    }

    // Patch the details panel to highlight ingredients if the recipe requirements are met using containers
    [HarmonyPatch(typeof(CraftingUI), "UpdateDetailsPanel")]
    [HarmonyPostfix]
    public static void PatchUpdateDetailsPanel(CraftingUI __instance, UIOption option)
    {
        if (GetField<bool>(__instance, "lockRecipeSelected")) return;

        Inventory playerInventory = GetField<Inventory>(__instance, "playerInventory");

        // Highlight slots whose item requirements are met
        // through items in containers
        RequiredMaterialSlot[] requiredMaterialSlots = GetField<RequiredMaterialSlot[]>(__instance, "requiredMaterialSlots");
        Recipe recipe = option.GetComponent<CraftingRecipeOption>().craftingRecipeRef;
        for (int j = 0; j < requiredMaterialSlots.Length; j++)
        {
            RequiredMaterialSlot requiredMaterialSlot = requiredMaterialSlots[j];
            Image itemImage = GetField<Image>(requiredMaterialSlot, "itemImage");
            bool isSlotUsed = recipe.RequiredItems.Count > j && requiredMaterialSlot.gameObject.activeInHierarchy;
            if (isSlotUsed && itemImage.color != Color.white)
            {
                // Check if the requirement is met through items in containers
                ItemAmount reqItem = recipe.RequiredItems[j];
                requiredMaterialSlot.Setup(reqItem, __instance);
                HashSet<InventoryItem> satisfiedIngredients = [];
                CanMakeRecipeUsingContainers(playerInventory, __instance, recipe, 1, null, ref satisfiedIngredients);
                if (satisfiedIngredients.Contains(reqItem.Item))
                {
                    // Highlight the slot in a different color
                    // to distinguish that the items would be pulled
                    // from containers
                    requiredMaterialSlot.SetItemColor(GameManager.Instance.AvailableGreenColor);
                }
            }
        }
    }

    // Consider ingredients in containers for checking if a recipe can be crafted
    [HarmonyPatch(typeof(CraftingUI), "CanMakeRecipe")]
    [HarmonyPostfix]
    static void PatchCanMakeRecipe(CraftingUI __instance, Recipe recipe, int numberToCraft, ref bool __result)
    {
        if (!__result && !ignoreCanMakeRecipeHook)
        {
            Inventory playerInventory = GetField<Inventory>(__instance, "playerInventory");
            List<ItemSourceUse> usedItemSources = CanMakeRecipeUsingContainers(playerInventory, __instance, recipe, numberToCraft, null);
            __result = usedItemSources != null;
        }
    }
}