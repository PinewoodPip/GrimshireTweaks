
using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

public static class QuickStackFromWorld
{
    // Allow quick-stacking from world containers through a hotkey.
    [HarmonyPatch(typeof(PlayerController), "ProcessInput")]
    [HarmonyPostfix]
    static void QuickStackToNearbyContainers(PlayerController __instance)
    {
        if (!GrimshireTweaks.Plugin.QuickStackKeybind.Value.IsDown()) return;

        // Check whether quick-stack is usable
        if (!GameManager.Instance.IsCurrentlyAtFarm())
        {
            GameManager.Instance.PopUpDialogBox.DisplayMsg("I can't quick-stack outside my farm!", 1f);
            return;
        }
        PauseMenu pauseMenu = GameManager.Instance.PauseMenu;
        if (pauseMenu?.IsOpen() ?? false)
        {
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
        Debug.Log($"Quick-stacking to {nearbyChests.Count} nearby chests.");
        
        // Try to stack items from player inventory to chest inventories
        int transferedStacks = 0;
        foreach (var chest in nearbyChests)
        {
            Inventory chestInventory = chest.GetInventory();
            foreach (var slot in playerInventory.Items)
            {
                InventoryItem item = slot.itemReference;
                if (item == null) continue;
                int stackAmount = slot.GetStackAmount();
                int stacksToMove = Math.Min(stackAmount, chestInventory.SpaceAvailable(item, stackAmount)); // 2nd param is actually unused
                if (chestInventory.ContainsItemAmount(item, 1) && chestInventory.CanAddStack(item, stacksToMove))
                {
                    // Transfer the stack
                    chestInventory.AddItem(item, slot.spoilageAverageAmount, stacksToMove);
                    playerInventory.RemoveItemAmount(item, stacksToMove);
                    Debug.Log($"Quick-stacked {stacksToMove} of {item.Label} to chest at {chest.transform.position}.");

                    // Track how many stacks were fully moved
                    // This is somewhat flawed, in the sense that stacks that are only partially moved won't be reported - not sure what the best feedback for this would be
                    if (stackAmount == stacksToMove)
                    {
                        transferedStacks += 1;
                    }
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
            GameManager.Instance.PopUpDialogBox.DisplayMsg($"Quick-stacked {transferedStacks} item stacks", 1.5f);
        }
    }
}