
using System;
using HarmonyLib;
using TMPro;
using UnityEngine;
using static GrimshireTweaks.Utils;

public static class ComposterValueDisplay
{
    // Show composter value in item tooltips
    [HarmonyPatch(typeof(ItemInfoDisplay), "Display")] // Tooltips (ex. in hotbar)
    [HarmonyPostfix]
    static void ShowComposterValue(ItemInfoDisplay __instance, bool enabled, InventoryItem itemRef, float itemSpoilage, RectTransform parent)
    {
        if (itemRef == null) return;
        var compostValue = itemRef.compostValue;
        if (compostValue > 0f)
        {
            var textField = GetField<TextMeshProUGUI>(__instance, "infoTMP");
            textField.text += $"\nCompost quality: {compostValue}";
        }
    }
    [HarmonyPatch(typeof(ItemDetailsPanel), "UpdateDetailsDisplay", new Type[] { typeof(InventoryItem), typeof(float) })] // Inventory tab
    [HarmonyPostfix]
    static void ShowComposterValueInInventory(ItemDetailsPanel __instance, InventoryItem itemRef, float slotSpoilageAmount)
    {
        if (itemRef == null) return;
        var compostValue = itemRef.compostValue;
        if (compostValue > 0f)
        {
            var textField = GetField<TextMeshProUGUI>(__instance, "descTMP");
            textField.text += $"\n\nCompost quality: {compostValue}";
        }
    }
}