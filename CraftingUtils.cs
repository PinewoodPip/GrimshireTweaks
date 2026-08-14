using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static GrimshireTweaks.Utils;

namespace GrimshireTweaks;

public static class CraftingUtils
{
    // Utility classes for tracking where crafting ingredients are being pulled from.
    public class ItemSource
    {
        public Inventory sourceInventory;
        public Inventory.Slot slot;
    }
    public class ItemSourceUse : ItemSource
    {
        public int amountToUse;
    }

    // Returns all player-owned inventories, including chests on the map and their own.
    static List<Inventory> GetInventories(Inventory playerInventory)
    {
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

        return inventories;
    }

    // Checks whether a recipe can be made using items from the player inventory and containers on the map;
    // if so, will return a list of inventories where items should be fetched from, otherwise null.
    // satisfiedIngredients will contain the ingredients of which the player has enough items for the recipe, populated regardless of whether the recipe is fully craftable.
    // chosenItems should be provided & filled in for recipes with RequiredFoodCategoryItems - including the required specific items after them as well.
    public static List<ItemSourceUse> CanMakeRecipeUsingContainers(Inventory playerInventory, CraftingUI __instance, Recipe recipe, int numToCraft, List<InventoryItem> chosenItems, ref HashSet<InventoryItem> satisfiedIngredients)
    {
        // Sanity check for recipes with category requirements
        if (chosenItems != null && chosenItems.Count != (recipe.RequiredFoodCategoryItems.Count + recipe.RequiredItems.Count))
        {
            Plugin.Logger.LogError("Not all categories & items were chosen? " + chosenItems.Count + " " + recipe.RequiredFoodCategoryItems.Count + " " + recipe.RequiredItems.Count);
            return null;
        }

        List<Inventory> inventories = GetInventories(playerInventory);

        // Create dict of required items,
        // considering stack amount to craft & profession bonuses
        Dictionary<InventoryItem, int> remainingRequiredItems = [];
        for (int i = 0; i < recipe.RequiredFoodCategoryItems.Count; i++)
        {
            FoodCategoryType categoryType = recipe.RequiredFoodCategoryItems[i];
            InventoryItem chosenItemKind = chosenItems[i];
            if (chosenItemKind.FoodCategoryType != categoryType)
            {
                Plugin.Logger.LogError("Item of wrong category was chosen?");
                return null;
            }

            Plugin.DebugLog("Adding item for category " + categoryType.ToString() + " " + chosenItemKind.name);
            int amount = numToCraft; // Category requirements only ever 1 of the item, unaffected by perks
            if (remainingRequiredItems.ContainsKey(chosenItemKind))
            {
                remainingRequiredItems[chosenItemKind] += amount;
            }
            else
            {
                remainingRequiredItems.Add(chosenItemKind, amount);
            }
        }
        foreach (ItemAmount requirement in recipe.RequiredItems)
        {
            int baseAmount = __instance ? CallMethod<int>(__instance, "GetAmountBasedOnSkill", requirement.Amount) : requirement.Amount;
            int amount = baseAmount * numToCraft;

            if (remainingRequiredItems.ContainsKey(requirement.Item))
            {
                remainingRequiredItems[requirement.Item] += amount;
            }
            else
            {
                remainingRequiredItems.Add(requirement.Item, amount);
            }
        }

        // Search through inventories for required items
        // Items from player inventory will be taken first as it's the first in the list
        HashSet<InventoryItem> tempSatisfiedIngredients = [];
        List<ItemSourceUse> usedItems = [];
        List<ItemSource> itemSources = GetItemSources((item) =>
        {
            int remainingAmount = remainingRequiredItems.GetValueOrDefault(item.slot.itemReference, 0);
            if (remainingAmount > 0)
            {
                int usedAmount = Math.Min(remainingAmount, item.slot.GetStackAmount());

                remainingRequiredItems[item.slot.itemReference] -= usedAmount;
                usedItems.Add(new ItemSourceUse
                {
                    sourceInventory = item.sourceInventory,
                    slot = item.slot,
                    amountToUse = usedAmount
                });

                // Check if requirement for this item is fully satisfied
                if (remainingRequiredItems[item.slot.itemReference] == 0)
                {
                    remainingRequiredItems.Remove(item.slot.itemReference);
                    tempSatisfiedIngredients.Add(item.slot.itemReference);
                }

                return true;
            }
            return false;
        });

        if (satisfiedIngredients != null)
        {
            foreach (var item in tempSatisfiedIngredients)
            {
                satisfiedIngredients.Add(item);
            }
        }

        // If all ingredients were found, return the inventory sources they come from
        return remainingRequiredItems.Count == 0 ? usedItems : null;
    }
    // Overload that discards satisfied ingredients
    public static List<ItemSourceUse> CanMakeRecipeUsingContainers(Inventory playerInventory, CraftingUI __instance, Recipe recipe, int numToCraft, List<InventoryItem> chosenCategoryItems)
    {
        HashSet<InventoryItem> dummyOutList = [];
        return CanMakeRecipeUsingContainers(playerInventory, __instance, recipe, numToCraft, chosenCategoryItems, ref dummyOutList);
    }

    public static List<ItemSource> GetItemSources(Func<ItemSource, bool> filter)
    {
        Inventory playerInventory = GameManager.Instance.Player.Inventory;
        List<Inventory> inventories = GetInventories(playerInventory);
        List<ItemSource> usedItemSources = [];

        foreach (var inventory in inventories)
        {
            Inventory.Slot[] items = GetField<Inventory.Slot[]>(inventory, "itemsInInventory");
            foreach (Inventory.Slot slot in items)
            {
                InventoryItem item = slot.itemReference;
                if (item == null) continue;

                ItemSource sourcedItem = new ItemSource
                {
                    slot = slot,
                    sourceInventory = inventory
                };

                // Track where we'd be getting the items from
                if (filter(sourcedItem))
                {
                    usedItemSources.Add(sourcedItem);
                }
            }
        }

        return usedItemSources;
    }

    public static Dictionary<FoodCategoryType, List<InventoryItem>> GetStoredCategoryItems(Inventory playerInventory)
    {
        Dictionary<FoodCategoryType, List<InventoryItem>> categoryItems = new Dictionary<FoodCategoryType, List<InventoryItem>>();

        foreach (var inventory in GetInventories(playerInventory))
        {
            foreach (var slot in inventory.Items)
            {
                InventoryItem item = slot.itemReference;
                if (item == null) continue;

                FoodCategoryType categoryType = item.foodCategoryType;
                if (categoryType != FoodCategoryType.None)
                {
                    if (!categoryItems.ContainsKey(categoryType))
                    {
                        categoryItems.Add(categoryType, new List<InventoryItem>());
                    }
                    if (!categoryItems[categoryType].Contains(item))
                    {
                        categoryItems[categoryType].Add(item);
                    }
                }
            }
        }

        return categoryItems;
    }

    public static void ConsumeItems(Inventory playerInventory, List<ItemSourceUse> itemSources)
    {
        foreach (var itemSource in itemSources)
        {
            Plugin.DebugLog($"Using {itemSource.amountToUse} of {itemSource.slot.itemReference.Label} from {(itemSource.sourceInventory == playerInventory ? "player inventory" : "a container")}");
            itemSource.sourceInventory.RemoveItemAmount(itemSource.slot.itemReference, itemSource.amountToUse);
        }
    }
}
