
using HarmonyLib;
using TMPro;
using UnityEngine;
using static GrimshireTweaks.Utils;

public static class CollectionLogTweaks
{
    static Color AVAILABLE_COLLECTION_ITEM_COLOR = new Color(0f, 0f, 0f, 0.8f);

    // Highlight fish, seeds & critters in collections menu that are unobtained but currently available (ie. in season)
    [HarmonyPatch(typeof(CollectionSlotPanel), "SetupFish")]
    [HarmonyPostfix]
    static void HighlightAvailableFish(CollectionSlotPanel __instance, InventoryItem item, CollectionsMenu parent)
    {
        if (!__instance.isDiscovered && item is Fish fish)
        {
            if (fish.IsFishAvailable(TimeControl.Instance.CurrentDate.Season, TimeControl.Instance.CurrentDate.Day))
            {
                __instance.itemImage.color = AVAILABLE_COLLECTION_ITEM_COLOR;
            }
        }
    }
    [HarmonyPatch(typeof(CollectionSlotPanel), "SetupPlant")]
    [HarmonyPostfix]
    static void HighlightAvailableSeed(CollectionSlotPanel __instance, Seeds seed, CollectionsMenu parent)
    {
        if (!__instance.isDiscovered)
        {
            if (seed.DateRange().IsDateInRange(TimeControl.Instance.CurrentDate.Season, TimeControl.Instance.CurrentDate.Day))
            {
                __instance.itemImage.color = AVAILABLE_COLLECTION_ITEM_COLOR;
            }
        }
    }
    [HarmonyPatch(typeof(CollectionSlotPanel), "SetupCritter")]
    [HarmonyPostfix]
    static void HighlightAvailableCritter(CollectionSlotPanel __instance, CritterData critter, CollectionsMenu parent)
    {
        if (!__instance.isDiscovered)
        {
            if (critter.IfInSeason())
            {
                __instance.itemImage.color = AVAILABLE_COLLECTION_ITEM_COLOR;
            }
        }
    }

    // Show habitat hint for undiscovered fish in collections menu
    // Note: Fish class has weather & time conditions as well,
    // however these appear to be unused currently;
    // they're set to "any weather" and "all day" for all fish
    [HarmonyPatch(typeof(CollectionsMenu), "UpdateDetailsPanel")]
    [HarmonyPostfix]
    static void ShowFishHabitatHint(CollectionsMenu __instance, UIOption option)
    {
        CollectionSlotPanel panel = option.GetComponent<CollectionSlotPanel>();
        InventoryItem item = panel.itemRef;
        if (item is not Fish fish) return;
        TextMeshProUGUI fishTTHabitat = GetField<TextMeshProUGUI>(__instance, "fishTTHabitat");

        // Show habitat for undiscovered fish
        if (!panel.isDiscovered)
        {
			fishTTHabitat.text = "Habitat: " + fish.Habitat.ToString();
        }
    }
}