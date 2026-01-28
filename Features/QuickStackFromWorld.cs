
using System;
using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using Newtonsoft.Json;
using TMPro;
using UnityEngine;
using static GrimshireTweaks.Utils;

namespace GrimshireTweaks;

public static class QuickStackFromWorld
{
    public static int INVENTORY_SLOTS_PER_ROW = 9;
    private static HashSet<int> favoritedItems = new HashSet<int>(); // Set of favorited item IDs.
    private static string FavoritedItemsSaveFilePath => Path.Combine(Application.persistentDataPath, "GrimshireTweaks_FavoritedItems.json");

    // Allow quick-stacking from world containers through a hotkey.
    [HarmonyPatch(typeof(PlayerController), "ProcessInput")]
    [HarmonyPostfix]
    static void QuickStackToNearbyContainers(PlayerController __instance)
    {
        if (!GrimshireTweaks.Plugin.QuickStackKeybind.Value.IsDown()) return;

        // Check whether quick-stack is usable
        if (TimeControl.Instance.Paused || TimeControl.Instance.TimeScale <= 0f || Time.timeScale <= 0f) // Pause/timescale checks should cover most UIs. TimeControl timescale is *not* same as Unity's
        {
            return;
        }
        if (!GameManager.Instance.IsCurrentlyAtFarm())
        {
            GameManager.Instance.PopUpDialogBox.DisplayMsg("I can't quick-stack outside my farm!", 1f);
            return;
        }

        PlayerController player = GameManager.Instance.Player;
        Inventory playerInventory = GameManager.Instance.Player.Inventory;
        InteractableChest[] chests = UnityEngine.Object.FindObjectsByType<InteractableChest>(FindObjectsSortMode.None);
        List<InteractableChest> nearbyChests = new List<InteractableChest>();

        // Find nearby chests
        for (int i = 0; i < chests.Length; i++)
        {
            // Check Chebyshev distance to player (ie. tile distance where diagonals count as 1)
            Vector3 playerPos = player.transform.position;
            Vector3 chestPos = chests[i].transform.position;
            float distance = Mathf.Max(Mathf.Abs(chestPos.x - playerPos.x), Mathf.Abs(chestPos.y - playerPos.y));
            if (distance <= GrimshireTweaks.Plugin.QuickStackRange.Value)
            {
                nearbyChests.Add(chests[i]);
            }
        }
        if (nearbyChests.Count == 0)
        {
            GameManager.Instance.PopUpDialogBox.DisplayMsg("No nearby chests to quick-stack to!", 1f);
            return;
        }
        Plugin.Logger.LogInfo($"Quick-stacking to {nearbyChests.Count} nearby chests.");

        // Try to stack items from player inventory to chest inventories
        int transferedStacks = 0;
        InventoryItem firstStackedItem = null;
        foreach (var chest in nearbyChests)
        {
            Inventory chestInventory = chest.GetInventory();
            for (int i = 0; i < playerInventory.Items.Length; i++)
            {
                Inventory.Slot slot = playerInventory.Items[i];
                InventoryItem item = slot.itemReference;
                if (item == null || !CanQuickStackItem(slot, i)) continue;
                int stackAmount = slot.GetStackAmount();
                int stacksToMove = Math.Min(stackAmount, chestInventory.SpaceAvailable(item, stackAmount)); // 2nd param is actually unused
                if (chestInventory.ContainsItemAmount(item, 1) && chestInventory.CanAddStack(item, stacksToMove))
                {
                    // Transfer the stack
                    chestInventory.AddItem(item, slot.spoilageAverageAmount, stacksToMove);
                    playerInventory.RemoveItemAmount(item, stacksToMove);
                    Plugin.Logger.LogInfo($"Quick-stacked {stacksToMove} of {item.Label} to chest at {chest.transform.position}.");

                    // Track how many stacks were fully moved
                    // This is somewhat flawed, in the sense that stacks that are only partially moved won't be reported - not sure what the best feedback for this would be
                    if (stackAmount == stacksToMove)
                    {
                        transferedStacks += 1;
                    }
                    firstStackedItem ??= item;
                }
            }
        }

        // Update UI
        // Should be done even if no full stacks were moved (as a stack might've been partially moved)
        GameManager.Instance.ToolBar.UpdateToolBarDisplay();

        // Play sound and show notification
        GameManager.Instance.Player.PlayChestClosedSFX();
        if (transferedStacks > 0)
        {
            GameManager.Instance.PopUpDialogBox.DisplayFancy($"Quick-stacked {transferedStacks} item stacks", firstStackedItem?.InventoryDisplayIcon, 1.5f); // Show icon of first stacked item as a reference
        }
    }

    // Mark items as "favorite" when alt-clicking them in the inventory.
    [HarmonyPatch(typeof(InventoryMenuUI), "Interact")]
    [HarmonyPrefix]
    public static bool ToggleFavoriteItem(InventoryMenuUI __instance, UIOption option)
    {
        int optionIndex = option.ParentUIOptionsGroup.GetOptionIndex(option);
        List<UIOptionsGroup> optionGroups = GetField<List<UIOptionsGroup>>(__instance, "optionGroups");
        Inventory playerInventory = GetField<Inventory>(__instance, "playerInventory");
        Inventory.Slot slot = (optionGroups[1].Options[0] == option) ? new Inventory.Slot() : playerInventory[optionIndex];
        if (slot == null || slot.itemReference == null) return true;

        // Toggle favorite state when alt-clicking
        if (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt))
        {
            InventoryItem item = slot.itemReference;
            int itemId = item.ID;
            if (favoritedItems.Contains(itemId))
            {
                favoritedItems.Remove(itemId);
            }
            else
            {
                favoritedItems.Add(itemId);
            }

            // Update UI and save favorites
            __instance.UpdateDetailsDisplay(slot.itemReference, slot.spoilageAverageAmount);
            SaveFavoritedItems();

            return false;
        }
        return true;
    }

    // Denote favorited items in the inventory UI and tooltips.
    [HarmonyPatch(typeof(ItemDetailsPanel), "UpdateDetailsDisplay", new Type[] { typeof(InventoryItem), typeof(float) })] // Inventory tab
    [HarmonyPostfix]
    public static void ShowFavoriteIndicator(ItemDetailsPanel __instance, InventoryItem itemRef, float slotSpoilageAmount)
    {
        if (itemRef == null) return;

        // Append icon to names of favorited items
        if (IsItemFavorited(itemRef))
        {
            var nameText = GetField<TextMeshProUGUI>(__instance, "nameTMP");
            nameText.text = $"{itemRef.Label} ★";
        }
    }
    [HarmonyPatch(typeof(ItemInfoDisplay), "Display")] // Tooltips (ex. in toolbar)
    [HarmonyPostfix]
    static void ShowComposterValue(ItemInfoDisplay __instance, bool enabled, InventoryItem itemRef, float itemSpoilage, RectTransform parent)
    {
        if (itemRef == null) return;

        // Prepend icon to names of favorited items
        // The tooltip UI uses a single text field,
        // thus we cannot cleanly add a suffix after the name (as other info such as spoilage is already there)
        if (IsItemFavorited(itemRef))
        {
            var nameText = GetField<TextMeshProUGUI>(__instance, "infoTMP");
            nameText.text = "★ " + nameText.text;
        }
    }

    // Returns whether an item can be quick-stacked based on user settings.
    public static bool CanQuickStackItem(Inventory.Slot slot, int inventoryIndex)
    {
        var item = slot.itemReference;
        if (!GrimshireTweaks.Plugin.QuickStackSpoilableItems.Value && item.IsEdible() && item.decayRate > 0) return false;
        if (!GrimshireTweaks.Plugin.QuickStackToolbarItems.Value && inventoryIndex < INVENTORY_SLOTS_PER_ROW) return false;
        if (IsItemFavorited(item)) return false;
        return true;
    }

    // Returns whether an item is marked as favorited.
    // Favorited items cannot be quick-stacked.
    public static bool IsItemFavorited(InventoryItem item)
    {
        return item != null && favoritedItems.Contains(item.ID);
    }

    // Load favorited items when game starts
    [HarmonyPatch(typeof(GameManager), "Awake")]
    [HarmonyPostfix]
    static void InitializeFavoritedItems()
    {
        LoadFavoritedItems();
    }

    private static void SaveFavoritedItems()
    {
        try
        {
            string json = JsonConvert.SerializeObject(favoritedItems, Formatting.None);
            File.WriteAllText(FavoritedItemsSaveFilePath, json);
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Quick-stack: failed to save favorited items: {ex.Message}");
        }
    }
    private static void LoadFavoritedItems()
    {
        try
        {
            if (File.Exists(FavoritedItemsSaveFilePath))
            {
                string json = File.ReadAllText(FavoritedItemsSaveFilePath);
                favoritedItems = JsonConvert.DeserializeObject<HashSet<int>>(json) ?? [];
                if (favoritedItems.Count > 0)
                {
                    Debug.Log($"Quick-stack: loaded {favoritedItems.Count} favorited items");
                }
            }
            else
            {
                favoritedItems = new HashSet<int>();
            }
        }
        catch (Exception ex)
        {
            Plugin.Logger.LogError($"Quick-stack: failed to load favorited items: {ex.Message}");
            favoritedItems = new HashSet<int>();
        }
    }
}