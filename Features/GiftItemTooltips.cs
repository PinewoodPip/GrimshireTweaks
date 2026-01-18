using System;
using HarmonyLib;
using TMPro;
using System.Collections.Generic;
using static GrimshireTweaks.Utils;

namespace GrimshireTweaks;

public static class GiftItemTooltips
{
    // Show known gift preferences in item tooltips
    [HarmonyPatch(typeof(ItemDetailsPanel), "UpdateDetailsDisplay", new Type[] { typeof(InventoryItem), typeof(float) })] // Inventory tab
    [HarmonyPostfix]
    static void ShowGiftRecipients(ItemDetailsPanel __instance, InventoryItem itemRef, float slotSpoilageAmount)
    {
        if (itemRef == null) return;
        List<string> likedByCharNames = [];
        CharacterManager charManager = GameManager.Instance.CharacterManager;

        // Find characters that like this item,
        // and that the player knows about
        foreach (var character in ResourceManager.Instance.GetListOfAllCharacters())
        {
            foreach (var gift in character.LikesGiftsList)
            {
                if (gift == itemRef && charManager.HasFavGiftBeenDiscovered(character.ID, gift.ID))
                {
                    likedByCharNames.Add(character.FirstName);
                    break;
                }
            }
        }

        // Add liked hint label
        if (likedByCharNames.Count > 0)
        {
            var textField = GetField<TextMeshProUGUI>(__instance, "descTMP");
            textField.text += $"\n\nLiked by {string.Join(", ", likedByCharNames)}";
        }
    }
}