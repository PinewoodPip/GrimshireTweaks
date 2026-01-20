
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
        var compostValue = GetCompostValue(itemRef);
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
        var compostValue = GetCompostValue(itemRef);
        if (compostValue > 0f)
        {
            var textField = GetField<TextMeshProUGUI>(__instance, "descTMP");
            textField.text += $"\n\nCompost quality: {compostValue}";
        }
    }

    // Returns the compost value for an item (ignoring stack amount)
    // Logic from `CompostBinProcessor.GetCompostBasedOnStoredValue()`
    public static float GetCompostValue(InventoryItem item)
    {
        float value = 0f;
        if (item.compostValue > 0f)
        {
            value += item.compostValue;
        }
        else if (item.IsEdible())
        {
            value += item.CaloricValue * 0.3f;
        }
        else if (item.BurnValue > 0)
        {
            value += item.BurnValue * 0.3f;
        }
        return value;
    }
}