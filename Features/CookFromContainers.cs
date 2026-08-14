using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using static GrimshireTweaks.Utils;
using static GrimshireTweaks.CraftingUtils;
using System.Linq;

namespace GrimshireTweaks;

public static class CookFromContainers
{
    static Dictionary<IngredientOption, ItemSourceUse> chosenCategoryItems = [];

    static bool ignoreSelectItemHook = false;
    static bool showingIngredientsGrid = false;
    static IngredientOption currentIngredOption = null;
    static Dictionary<CursorInventoryOption, ItemSource> currentItemSources = [];

    // Patch recipe craft check to account for being able to use store ingredients.
    // This is used for both checking the craftability of unlocked recipes
    // as well as the selected recipe.
    [HarmonyPatch(typeof(CookingUI), "CanMakeRecipe")]
    [HarmonyPostfix]
    static void OnCookingUICanMakeRecipe(CookingUI __instance, Recipe recipe, ref bool __result)
    {
        if (__result) return;

        Log("Checking if can make recipe " + recipe.name);

        Inventory playerInventory = GameManager.Instance.Player.Inventory;
        IngredientOption[] ingredOptions = GetField<IngredientOption[]>(__instance, "ingredOptions");
        RecipeOption recipeOption = GetField<RecipeOption>(__instance, "selectedRecipe");

        List<InventoryItem> chosenItemsList = GetChosenItemsList(ingredOptions, recipe, recipeOption?.recipeRef, true);
        if (chosenItemsList == null) return;
        __result = CanMakeRecipeUsingContainers(playerInventory, null, recipe, 1, chosenItemsList) != null;

        Log("   " + (__result ? "Can make recipe" : "Cannot make recipe"));
    }

    [HarmonyPatch(typeof(CookingUI), "MakeRecipe")]
    [HarmonyPrefix]
    static bool OnCookingUIMakeRecipe(CookingUI __instance)
    {
        RecipeOption recipeOption = GetField<RecipeOption>(__instance, "selectedRecipe");
        if (recipeOption == null) return true;

        Recipe recipe = recipeOption.recipeRef;
        if (chosenCategoryItems.Count == 0) return true; // All items manually selected (or no selections at all)

        // Check if we have chosen items for all ingredients
        Inventory playerInventory = GameManager.Instance.Player.Inventory;
        IngredientOption[] ingredOptions = GetField<IngredientOption[]>(__instance, "ingredOptions");
        List<InventoryItem> chosenItems = GetChosenItemsList(ingredOptions, recipe);
        if (chosenItems == null) return true;

        // Note: if we've gotten to this point, then we have all ingredients required
        // nothing can change craftability in the middle of using the UI,
        // thus it's not required to re-check it here

        // Gather ingredient sources,
        // both from containers
        // as well as manual player selections from their inventory
        List<ItemSourceUse> usedItems = [];
        int optionIndex = 0;
        foreach (FoodCategoryType categoryRequirement in recipe.RequiredFoodCategoryItems)
        {
            IngredientOption ingredOpt = ingredOptions[optionIndex];
            ItemSourceUse itemSource;
            if (ingredOpt.indexOfSelectedItem != 999)
            {
                // Manually-selected item from player inventory
                itemSource = new ItemSourceUse
                {
                    slot = playerInventory.Items[ingredOpt.indexOfSelectedItem],
                    amountToUse = 1,
                    sourceInventory = playerInventory,  
                };
            }
            else
            {
                // Selection from containers
                itemSource = chosenCategoryItems[ingredOpt];
            }
            usedItems.Add(itemSource);
            optionIndex++;
        }
        foreach (ItemAmount itemRequirement in recipe.RequiredItems)
        {
            IngredientOption ingredOpt = ingredOptions[optionIndex];
            ItemSourceUse itemSource;
            if (ingredOpt.indexOfSelectedItem != 999)
            {
                // Manually-selected item from player inventory
                itemSource = new ItemSourceUse
                {
                    slot = playerInventory.Items[ingredOpt.indexOfSelectedItem],
                    amountToUse = 1,
                    sourceInventory = playerInventory,  
                };
            }
            else
            {
                // Selection from containers
                itemSource = chosenCategoryItems[ingredOpt];
            }
            optionIndex++;
        }

        // Consume items and start cooking; mostly copy-pasted from original code
        CookingProcessor processor = GetField<CookingProcessor>(__instance, "processor");
        processor.HiddenInv.AddItem(recipe, 1f, 1);
		processor.HiddenInv[0].ProcessingMinutesLeft = processor.ProcTime;
        ConsumeItems(playerInventory, usedItems);

        // Update option slots
		foreach (IngredientOption ingredOpt in ingredOptions)
		{
			if (ingredOpt.itemImage.enabled && ingredOpt.indexOfSelectedItem != 999)
			{
				InventoryItem itemRef = playerInventory[ingredOpt.indexOfSelectedItem].itemReference; // Note: we don't need to consume any items here anymore as it's handled by ConsumeItems()
				if (!playerInventory.ContainsItemAmount(itemRef, 1))
				{
					ingredOpt.RemoveItem(); // TODO might need to also support this for category slots?
				}
			}
		}

		GameManager.Instance.Player.UpdateToolBar();
		__instance.Close(false);

        return false;
    }

    // Make the auto-fill button also select ingredients stored in chests,
    // for slots that could not be auto-filled from player inventory.
    [HarmonyPatch(typeof(CookingUI), "PullIngredients")]
    [HarmonyPostfix]
    static void AfterCookingUIPullIngredients(CookingUI __instance)
    {
        foreach (IngredientOption ingredOpt in GetField<IngredientOption[]>(__instance, "ingredOptions"))
        {
            if (ingredOpt.gameObject.activeInHierarchy && (ingredOpt.requiredFoodCategoryType != FoodCategoryType.None) && ingredOpt.indexOfSelectedItem == 999)
            {
                FoodCategoryType category = ingredOpt.requiredFoodCategoryType;
                Log("   Trying to autofill category " + category);

                // Use the first stored item of that category
                var categoryItemSources = GetCategoryItemSources(ingredOpt.requiredFoodCategoryType);
                if (categoryItemSources.Count > 0)
                {
                    ItemSource itemSource = categoryItemSources[0];
                    ChooseCategoryItem(__instance, ingredOpt, new ItemSourceUse
                    {
                        slot = itemSource.slot,
                        sourceInventory = itemSource.sourceInventory,
                        amountToUse = 1,
                    });
                }
            }
        }
    }

    // Cycle through possible stored items when shift-clicking an ingredient slot that requires a category.
    [HarmonyPatch(typeof(CookingUI), "Interact")]
    [HarmonyPrefix]
    static bool OnCookingUIInteract(CookingUI __instance, UIOption option)
    {
        if (!option) return true;

        RecipeOption selectedRecipe = GetField<RecipeOption>(__instance, "selectedRecipe");

        // Clear category choices when selecting a different recipe
        if (option is RecipeOption recipeOption && recipeOption != null && recipeOption.isDiscovered && recipeOption.recipeRef != selectedRecipe)
        {
            chosenCategoryItems.Clear();
            return true;
        }
        else if (option is RecipeOption recipeOption2)
        {
            // Changing recipes hides the inventory grid, so we must reset bookkeeping
            ResetIngredientTracking();
            return true;
        }
        else if (option is IngredientOption ingredOpt && Input.GetKey(KeyCode.LeftShift))
        {
            // Show grid of stored items valid for that ingredient slot
            Dictionary<FoodCategoryType, List<InventoryItem>> allCategoryChoices = GetStoredCategoryItems(GameManager.Instance.Player.Inventory);
            List<InventoryItem> categoryChoices = allCategoryChoices.GetValueOrDefault(ingredOpt.requiredFoodCategoryType);
            if (categoryChoices != null && categoryChoices.Count > 0)
            {

                SetupStoredIngredientsGrid(__instance, ingredOpt);
            }
            return false;
        }
        else if (option is CursorInventoryOption cursorOption && showingIngredientsGrid)
        {
            // InventoryItem item = GetField<InventoryItem>(cursorOption, "myItem");
            if (currentItemSources.ContainsKey(cursorOption))
            {
                ItemSource itemSource = currentItemSources[cursorOption];
                Log(itemSource.slot.itemReference.name);
                ChooseCategoryItem(__instance, currentIngredOption, new ItemSourceUse
                {
                    slot = itemSource.slot,
                    sourceInventory = itemSource.sourceInventory,
                    amountToUse = 1,
                });
                CloseStoredIngredients();
                return false;
            }
        }
        
        return true;
    }

    static void CloseStoredIngredients()
    {
        currentItemSources.Clear();
        currentIngredOption = null;
        showingIngredientsGrid = false;
        GameManager.Instance.PlayerInventoryUIGroup.gameObject.SetActive(false);
    }

    // Clear our selected items for categories when the player manually selects an item.
    [HarmonyPatch(typeof(IngredientOption), "SelectItemFromPlayerInv")]
    [HarmonyPostfix]
    static void AfterIngredientOptionSelectItemFromPlayerInv(IngredientOption __instance, InventoryItem selectedItem, int indx)
    {
        if (ignoreSelectItemHook) return;
        if (chosenCategoryItems.ContainsKey(__instance))
        {
            chosenCategoryItems.Remove(__instance);
        }
    }

    // Patch ingredient selection check to consider our category item selections.
    [HarmonyPatch(typeof(CookingUI), "CheckIfAllIngredSelected")]
    [HarmonyPostfix]
    static void AfterCookingUICheckIfAllIngredSelected(CookingUI __instance)
    {
        UIOption makeButton = GetField<UIOption>(__instance, "makeButton");
        if (!GetField<bool>(makeButton, "available")) // Only act if the player hasn't manually chosen all items
        {
            bool canCraft = true;
            foreach (IngredientOption ingredOpt in GetField<IngredientOption[]>(__instance, "ingredOptions"))
            {
                // Only check enabled slots that don't have a manual player selection.
                if (ingredOpt.gameObject.activeInHierarchy && (ingredOpt.requiredFoodCategoryType != FoodCategoryType.None) && ingredOpt.indexOfSelectedItem == 999)
                {
                    FoodCategoryType category = ingredOpt.requiredFoodCategoryType;
                    canCraft &= chosenCategoryItems.ContainsKey(ingredOpt);
                    if (!canCraft) break;
                }
            }
            makeButton.IsActive(canCraft);
        }
    }

    // Reset chosen items per-category when the recipe changes.
    [HarmonyPatch(typeof(CookingUI), "UpdateRecipeDisplay")]
    [HarmonyPostfix]
    static void AfterCookingUIUpdateRecipeDisplay(CookingUI __instance)
    {
        chosenCategoryItems.Clear();
    }

    // Reset chosen items per-category when the UI is opened.
    [HarmonyPatch(typeof(CookingUI), "Open")]
    [HarmonyPrefix]
    static bool OnCookingUIOpen(CookingUI __instance, CookingProcessor processor)
    {
        ResetIngredientTracking();
        return true;
    }

    static List<ItemSource> GetCategoryItemSources(FoodCategoryType categoryType)
    {
        return GetItemSources((itemSource) =>
        {
            return itemSource.slot.itemReference.FoodCategoryType == categoryType;
        });
    }

    static void SetupStoredIngredientsGrid(CookingUI ui, IngredientOption ingredOpt)
    {
        List<UIOptionsGroup> optionGroups = GetField<List<UIOptionsGroup>>(ui, "optionGroups");
		int itemIndex = 0;
        
        // Get stored items of this category or specific type,
        // based on ingredient slot
        List<ItemSource> itemSources;
        if (ingredOpt.requiredFoodCategoryType != FoodCategoryType.None)
        {
            itemSources = GetCategoryItemSources(ingredOpt.requiredFoodCategoryType);
        }
        else
        {
            itemSources = GetItemSources((itemSource) =>
            {
                return itemSource.slot.itemReference == ingredOpt.requiredFoodItem;
            });
        }

        // Setup slots to show stored ingredients
        foreach (UIOption uioption in optionGroups[1].Options)
		{
			CursorInventoryOption component = uioption.GetComponent<CursorInventoryOption>();
			if (component != null)
			{
                if (itemIndex < itemSources.Count)
                {
                    // Show item source
                    ItemSource itemSource = itemSources[itemIndex];
                    Inventory.Slot slot = new Inventory.Slot(itemSource.slot.itemReference, itemSource.slot.GetStackAmount(), itemSource.slot.spoilageAverageAmount);
                    currentItemSources[component] = itemSource;
                    component.UpdateDisplay(slot, ui);
                }
                else
                {
                    // Clear remaining slots
                    component.UpdateDisplay(null, ui);
                }
			}
			itemIndex++;
		}
        showingIngredientsGrid = true;
        currentIngredOption = ingredOpt;
        GameManager.Instance.PlayerInventoryUIGroup.Setup();
    }

    // Selects the item type to use for a category requirement.
    static void ChooseCategoryItem(CookingUI ui, IngredientOption ingredOption, ItemSourceUse itemSource)
    {
        if (itemSource == null)
        {
            chosenCategoryItems.Remove(ingredOption);
            ingredOption.RemoveItem();
            ingredOption.itemImage.color = Color.white;
        }
        else if (itemSource.sourceInventory == GameManager.Instance.Player.Inventory)
        {
            // Select the item as if it had been picked normally by the player
            // This better distinguishes which items are coming from containers
            // and which ones the player will expect to be consumed from their inventory
            // (so there's no surprises over inventory items disappearing)
            Inventory.Slot[] invSlots = GameManager.Instance.Player.Inventory.Items;
            int slotIndex = 999;
            for (int i = 0; i < invSlots.Length; i++)
            {
                Inventory.Slot slot = invSlots[i];
                if (slot == itemSource.slot)
                {
                    slotIndex = i;
                    break;
                }
            }
            if (slotIndex == 999) LogWarning("Could not find player inventory slot for an item sourced from it? " + itemSource.slot.itemReference.name);

            ingredOption.SelectItemFromPlayerInv(itemSource.slot.itemReference, slotIndex);
        }
        else
        {
            chosenCategoryItems[ingredOption] = itemSource;

            // Show the chosen item in the slot
            ignoreSelectItemHook = true;
            ingredOption.SelectItemFromPlayerInv(itemSource.slot.itemReference, 999); // ID must be kept invalid to avoid vanilla code from thinking this slot is properly filled in (so that the craft button triggers our logic instead)
            ingredOption.itemImage.color = GameManager.Instance.AvailableGreenColor;
            ignoreSelectItemHook = false;
        }

        ui.CheckIfAllIngredSelected();
    }

    // Returns the list of items that the player has chosen to try to use for a recipe.
    static List<InventoryItem> GetChosenItemsList(IngredientOption[] options, Recipe recipe, Recipe selectedRecipe, bool anyItem = false)
    {
        Log("Getting chosen items");

        Inventory playerInventory = GameManager.Instance.Player.Inventory;
        List<InventoryItem> chosenItemsList = [];
        var storedCategoryItems = GetStoredCategoryItems(playerInventory);
        for (int i = 0; i < options.Length; i++) // Note: Cooking UI is hardcoded for 4 slots
        {
            Log("Ingredient index " + i);

            // Category items are always displayed before specific item requirements
            if (i < recipe.RequiredFoodCategoryItems.Count)
            {
                FoodCategoryType categoryType = recipe.RequiredFoodCategoryItems[i];
                IngredientOption ingredOpt = options[i];
                Log("   Is category: " + categoryType);

                // Consider manually selected items if this recipe is the selected one
                if (recipe == selectedRecipe && ingredOpt.indexOfSelectedItem != 999)
                {
                    chosenItemsList.Add(playerInventory.Items[ingredOpt.indexOfSelectedItem].itemReference);
                    continue;            
                }

                if (!storedCategoryItems.ContainsKey(categoryType)) // Note: if present, the set won't be empty
                {
                    Log("   Failed: no item of the category is stored");
                    return null;
                }

                if (chosenCategoryItems.ContainsKey(ingredOpt))
                {
                    InventoryItem chosenItem = chosenCategoryItems[ingredOpt].slot.itemReference;
                    chosenItemsList.Add(chosenItem);

                    Log("   Chosen item: " + chosenItem.name);
                }
                else if (anyItem)
                {
                    InventoryItem firstItem = storedCategoryItems[categoryType].First();
                    chosenItemsList.Add(firstItem);

                    Log("   Chose any item: " + firstItem.name);
                }
                else
                {
                    Log("   Failed to choose item");
                    return null;
                }
            }
            else if ((i - recipe.RequiredFoodCategoryItems.Count) < recipe.RequiredItems.Count)
            {
                int j = i - recipe.RequiredFoodCategoryItems.Count;
                ItemAmount requiredItem = recipe.RequiredItems[j];
                Log("   Is specific item: " + requiredItem.Item.name);
                if (requiredItem.Amount > 1) LogWarning("Recipe requires >1 of a specific item? Unhandled case");

                chosenItemsList.Add(requiredItem.Item); // Amount is always 1 for cooking recipes
            }
        }

        return chosenItemsList.Count > 0 ? chosenItemsList : null;
    }
    static List<InventoryItem> GetChosenItemsList(IngredientOption[] options, Recipe recipe, bool anyItem = false) // Overload for selected recipe only.
    {
        return GetChosenItemsList(options, recipe, recipe, anyItem);
    }
    
    // Resets bookkeeping for ingredients chosen from containers.
    static void ResetIngredientTracking()
    {
        chosenCategoryItems.Clear();
        currentItemSources.Clear();
        showingIngredientsGrid = false;
        currentIngredOption = null;
    }

    static void Log(string msg)
    {
        Plugin.DebugLog("[CookFromContainers] " + msg);
    }

    static void LogWarning(string msg)
    {
        Plugin.Logger.LogWarning("[CookFromContainers] " + msg);
    }
}