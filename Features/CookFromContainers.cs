using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;
using static GrimshireTweaks.Utils;
using static GrimshireTweaks.CraftingUtils;
using System.Linq;
using Newtonsoft.Json;

namespace GrimshireTweaks;

public static class CookFromContainers
{
    static Dictionary<FoodCategoryType, InventoryItem> chosenCategoryItems = [];

    static bool ignoreSelectItemHook = false;

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
        List<ItemSource> usedItems = CanMakeRecipeUsingContainers(playerInventory, null, recipe, 1, chosenItems);

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
                var storedCategoryItems = GetStoredCategoryItems(GameManager.Instance.Player.Inventory);
                if (storedCategoryItems.ContainsKey(category))
                {
                    var categoryItems = storedCategoryItems[category];
                    ChooseCategoryItem(__instance, ingredOpt, categoryItems.First());
                }
            }
        }
    }

    // Cycle through possible stored items when shift-clicking an ingredient slot that requires a category.
    [HarmonyPatch(typeof(CookingUI), "Interact")]
    [HarmonyPrefix]
    static bool OnCookingUIInteract(CookingUI __instance, UIOption option)
    {
        if (!option || option is not IngredientOption || !Input.GetKey(KeyCode.LeftShift)) return true;

        RecipeOption selectedRecipe = GetField<RecipeOption>(__instance, "selectedRecipe");
        Inventory playerInventory = GameManager.Instance.Player.Inventory;

        // Clear category choices when selecting a different recipe
        if (option is RecipeOption recipeOption && recipeOption != null && recipeOption.isDiscovered && recipeOption.recipeRef != selectedRecipe)
        {
            chosenCategoryItems.Clear();
            return true;
        }
        else if (option is IngredientOption ingredOpt)
        {
            // Only act on category slots
            if (ingredOpt.requiredFoodCategoryType != FoodCategoryType.None)
            {
                Dictionary<FoodCategoryType, List<InventoryItem>> allCategoryChoices = GetStoredCategoryItems(GameManager.Instance.Player.Inventory);
                List<InventoryItem> categoryChoices = allCategoryChoices.GetValueOrDefault(ingredOpt.requiredFoodCategoryType);
                if (categoryChoices != null && categoryChoices.Count > 0)
                {
                    InventoryItem manualItem = ingredOpt.indexOfSelectedItem != 999 ? playerInventory.Items[ingredOpt.indexOfSelectedItem].itemReference : null; // If the player had a specific item selected, we'll try to cycle to the next item after it for better UX
                    InventoryItem currentChoice = manualItem ?? chosenCategoryItems.GetValueOrDefault(ingredOpt.requiredFoodCategoryType, null);
                    InventoryItem newChoice;

                    // Cycle selected category item
                    if (currentChoice == null)
                    {
                        // Pick first item if the slot was empty before (from either manual or category selection)
                        newChoice = categoryChoices[0];
                    }
                    else
                    {
                        // Cycle through possible items
                        // When cycling past the end, empty the slot instead;
                        // this has better UX for having cycled all choices,
                        // especially when there's only 1 ingredient
                        int currentIndex = categoryChoices.IndexOf(currentChoice);
                        if (currentIndex + 1 >= categoryChoices.Count)
                        {
                            newChoice = null;
                        }
                        else
                        {
                            newChoice = categoryChoices[currentIndex + 1]; 
                        }
                    }
                    ChooseCategoryItem(__instance, ingredOpt, newChoice);
                }
            }
            return false;
        }
        
        return true;
    }

    // Clear our selected items for categories when the player manually selects an item.
    [HarmonyPatch(typeof(IngredientOption), "SelectItemFromPlayerInv")]
    [HarmonyPostfix]
    static void AfterIngredientOptionSelectItemFromPlayerInv(IngredientOption __instance, InventoryItem selectedItem, int indx)
    {
        if (ignoreSelectItemHook) return;
        if (chosenCategoryItems.ContainsKey(__instance.requiredFoodCategoryType))
        {
            chosenCategoryItems.Remove(__instance.requiredFoodCategoryType);
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
                    canCraft &= chosenCategoryItems.ContainsKey(category);
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
        chosenCategoryItems.Clear();
        return true;
    }

    // Selects the item type to use for a category requirement.
    static void ChooseCategoryItem(CookingUI ui, IngredientOption ingredOption, InventoryItem item)
    {
        if (item == null)
        {
            chosenCategoryItems.Remove(ingredOption.requiredFoodCategoryType);
            ingredOption.RemoveItem();
            ingredOption.itemImage.color = Color.white;
        }
        else
        {
            chosenCategoryItems[ingredOption.requiredFoodCategoryType] = item;

            // Show the chosen item in the slot
            ignoreSelectItemHook = true;
            ingredOption.SelectItemFromPlayerInv(item, 999); // ID must be kept invalid to avoid vanilla code from thinking this slot is properly filled in
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

                if (chosenCategoryItems.ContainsKey(categoryType)) // TODO verify this item is still valid
                {
                    InventoryItem chosenItem = chosenCategoryItems[categoryType];
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
                if (requiredItem.Amount > 1) Plugin.Logger.LogWarning("Recipe requires >1 of a specific item? Unhandled case");

                chosenItemsList.Add(requiredItem.Item); // Amount is always 1 for cooking recipes
            }
        }

        return chosenItemsList.Count > 0 ? chosenItemsList : null;
    }
    static List<InventoryItem> GetChosenItemsList(IngredientOption[] options, Recipe recipe, bool anyItem = false) // Overload for selected recipe only.
    {
        return GetChosenItemsList(options, recipe, recipe, anyItem);
    }

    static void Log(string msg)
    {
        Plugin.Logger.LogInfo("[CookFromContainers] " + msg);
    }
}