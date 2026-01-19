using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using static GrimshireTweaks.Utils;

namespace GrimshireTweaks;

public static class CraftFromContainers
{
    // Utility class for tracking where crafting ingredients are being pulled from.
    class ItemSource
    {
        public Inventory sourceInventory;
        public Inventory.Slot slot;
        public int amountToUse;
    }

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

        Debug.Log("Attempting to craft recipe with ingredients from containers...");

        // If we cannot craft the recipe from items in the inventory,
        // try to pull ingredients from containers on the map
        Inventory playerInventory = GetField<Inventory>(__instance, "playerInventory");
        List<ItemSource> usedItemSources = CanMakeRecipeUsingContainers(__instance, recipe, numToCraft);
        if (usedItemSources == null) return true; // Cannot make recipe even with containers

        // Craft the item; mostly copy-pasted from original method
        if (playerInventory.CanAddStack(recipe, numToCraft))
        {
            // Remove used items from the source containers
            Debug.Log($"Crafting using items from {usedItemSources.Count} containers");
            foreach (var itemSource in usedItemSources)
            {
                Debug.Log($"Using {itemSource.amountToUse} of {itemSource.slot.itemReference.Label} from {(itemSource.sourceInventory == playerInventory ? "player inventory" : "a container")}");
                itemSource.sourceInventory.RemoveItemAmount(itemSource.slot.itemReference, itemSource.amountToUse);
            }

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
                requiredMaterialSlot.Setup(reqItem);
                HashSet<InventoryItem> satisfiedIngredients = [];
                CanMakeRecipeUsingContainers(__instance, recipe, 1, ref satisfiedIngredients);
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

    // Checks whether a recipe can be made using items from the player inventory and containers on the map;
    // if so, will return a list of inventories where items should be fetched from, otherwise null.
    // satisfiedIngredients will contain the ingredients of which the player has enough items for the recipe, populated regardless of whether the recipe is fully craftable.
    static List<ItemSource> CanMakeRecipeUsingContainers(CraftingUI __instance, Recipe recipe, int numToCraft, ref HashSet<InventoryItem> satisfiedIngredients)
    {
        Inventory playerInventory = GetField<Inventory>(__instance, "playerInventory");
        List<Inventory> inventories = [playerInventory];

        // Fetch chest inventories
        // Crafters use a different component (ProcessorObject), so they won't be used
        var chests = UnityEngine.Object.FindObjectsByType<InteractableChest>(FindObjectsSortMode.None); // The maps are all different scenes, so this won't ex. get mine chests
        foreach (var chest in chests)
        {
            if (chest.gameObject.activeInHierarchy && !chest.isRotten) // Sanity check; "isRotten" is used by mine chests
            {
                Inventory chestInventory = chest.GetInventory();
                inventories.Add(chestInventory);
            }
        }

        // Create dict of required items,
        // considering stack amount to craft & profession bonuses
        Dictionary<InventoryItem, int> remainingRequiredItems = [];
        List<ItemSource> usedItemSources = [];
        foreach (ItemAmount requirement in recipe.RequiredItems)
        {
            int amount = CallMethod<int>(__instance, "GetAmountBasedOnSkill", requirement.Amount) * numToCraft;
            remainingRequiredItems.Add(requirement.Item, amount);
        }

        // Search through inventories for required items
        // Items from player inventory will be taken first as it's the first in the list
        foreach (var inventory in inventories)
        {
            Inventory.Slot[] items = GetField<Inventory.Slot[]>(inventory, "itemsInInventory");
            foreach (Inventory.Slot slot in items)
            {
                InventoryItem item = slot.itemReference;
                if (item == null) continue;
                int remainingAmount = remainingRequiredItems.GetValueOrDefault(item, 0); // requiredFoodCategoryItems seems unused for normal crafting (only used for cooking recipes)
                if (remainingAmount > 0)
                {
                    // Track where we'd be getting the items from
                    ItemSource sourcedItem = new ItemSource
                    {
                        slot = slot,
                        sourceInventory = inventory,
                        amountToUse = Math.Min(remainingAmount, slot.GetStackAmount())
                    };
                    usedItemSources.Add(sourcedItem);
                    remainingRequiredItems[item] -= sourcedItem.amountToUse;
                    if (remainingRequiredItems[item] == 0)
                    {
                        // Requirement for this item is fully satisfied
                        remainingRequiredItems.Remove(item);
                        satisfiedIngredients?.Add(item);
                    }
                }
            }
        }

        // If all ingredients were found, return the inventory sources they come from
        return remainingRequiredItems.Count == 0 ? usedItemSources : null;
    }
    // Overload that discards satisfied ingredients
    static List<ItemSource> CanMakeRecipeUsingContainers(CraftingUI __instance, Recipe recipe, int numToCraft)
    {
        HashSet<InventoryItem> dummyOutList = [];
        return CanMakeRecipeUsingContainers(__instance, recipe, numToCraft, ref dummyOutList);
    }

    // Consider ingredients in containers for checking if a recipe can be crafted
    [HarmonyPatch(typeof(CraftingUI), "CanMakeRecipe")]
    [HarmonyPostfix]
    static void PatchCanMakeRecipe(CraftingUI __instance, Recipe recipe, int numberToCraft, ref bool __result)
    {
        if (!__result && !ignoreCanMakeRecipeHook)
        {
            List<ItemSource> usedItemSources = CanMakeRecipeUsingContainers(__instance, recipe, numberToCraft);
            __result = usedItemSources != null;
        }
    }
}