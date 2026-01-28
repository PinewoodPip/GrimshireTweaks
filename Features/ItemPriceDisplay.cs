
using System;
using HarmonyLib;
using TMPro;
using static GrimshireTweaks.Utils;

namespace GrimshireTweaks;

public static class ItemPriceDisplay
{
    // Show item prices in the details panel in the inventory UI,
    // even when outside shops.
    [HarmonyPatch(typeof(ItemDetailsPanel), "UpdateDetailsDisplay", new Type[] { typeof(InventoryItem), typeof(float) })]
    [HarmonyPostfix]
    public static void ShowFavoriteIndicator(ItemDetailsPanel __instance, InventoryItem itemRef, float slotSpoilageAmount)
    {
        if (itemRef == null) return;

        int itemPrice = itemRef.SellPrice();
        if (itemPrice > 0)
        {
            var textField = GetField<TextMeshProUGUI>(__instance, "descTMP");
            textField.text += $"\nSell price: {itemPrice}g";
        }
    }
}