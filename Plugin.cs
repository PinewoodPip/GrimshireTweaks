using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using TMPro;
using UnityEngine;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using static GrimshireTweaks.Utils;

namespace GrimshireTweaks;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    static int LATE_WARNING_HOUR = 22;
    static int LATE_WARNING_MINUTE = 0;
    static Color AVAILABLE_COLLECTION_ITEM_COLOR = new Color(0f, 0f, 0f, 0.8f);

    // Mixins and trackers for SquareBoundsChecker optimizations
    class SquareBoundsCheckerMixin
    {
        public Vector3 previousPosition;
    }
    static ConditionalWeakTable<SquareBoundsChecker, SquareBoundsCheckerMixin> squareBoundsCheckerMixinStates = new ConditionalWeakTable<SquareBoundsChecker, SquareBoundsCheckerMixin>();
    static Collider2D[] squareBoundsCheckerCollisionResults = new Collider2D[1];

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} is loaded!");

        Harmony.CreateAndPatchAll(typeof(Plugin));
        Harmony.CreateAndPatchAll(typeof(GiftItemTooltips));
        Harmony.CreateAndPatchAll(typeof(AutoToggleRunning));
        Harmony.CreateAndPatchAll(typeof(DialogDismissHotkey));
        Harmony.CreateAndPatchAll(typeof(ShortenInputCooldowns));
        Harmony.CreateAndPatchAll(typeof(CraftFromContainers));
        Harmony.CreateAndPatchAll(typeof(PluginVersionDisplay));
        Harmony.CreateAndPatchAll(typeof(DailyDialogueIndicator));
    }

    // Optimize object placement checks
    // These normally run every frame and lag quite a lot with larger objects (mostly tree seeds),
    // partially due to allocations from Physics2D.OverlapCircle()
    [HarmonyPatch(typeof(SquareBoundsChecker), "Validate")]
    [HarmonyPrefix]
    static bool OptimizeSquareBoundsChecker(SquareBoundsChecker __instance)
    {
        // Skip updating if the position of the checker hasn't changed
        var previousState = squareBoundsCheckerMixinStates.GetOrCreateValue(__instance);
        bool positionChanged = previousState.previousPosition != __instance.transform.position;
        previousState.previousPosition = __instance.transform.position; // Update pos
        if (!positionChanged)
        {
            return false;
        }

        // Run original logic, but with collision checks modified
        // to reduce lag from allocations
        SquareBoundsChecker _this = __instance;
        var CheckIfValidSpot = Traverse.Create(_this).Method("CheckIfValidSpot", new object[] { true });
        Vector3 position = _this.transform.position;

        SetField<float>(__instance, "updateCoolDown", 0.05f);
        SetField<Vector3>(__instance, "myPosition", position);

        // This check was modified to use the NonAlloc variant
        // to reduce lag from allocation + subsequent garbage collection
        int collisionsCount = Physics2D.OverlapCircleNonAlloc(position, 0.25f, squareBoundsCheckerCollisionResults, ~(LayerManager.Instance.PlayerLayer | _this.placeableObjRef.layersToIgnore | LayerManager.Instance.IgnoreLayer));
        CheckIfValidSpot.GetValue(collisionsCount == 0);

        if (_this.placeableObjRef.placeableOnlyInside && _this.validSpot)
        {
            CheckIfValidSpot.GetValue(WeatherSystem.Instance.IsInteriorScene());
        }
        if (_this.placeableObjRef.placeableOnlyOutside && _this.validSpot)
        {
            CheckIfValidSpot.GetValue(!WeatherSystem.Instance.IsInteriorScene());
        }
        if (_this.placeableObjRef.placeableOnlyInWater && _this.validSpot)
        {
            CheckIfValidSpot.GetValue(_this.placeableObjRef.tileMapManagerRef.IsValidFishTrapSpot(position));
        }
        if (_this.placeableObjRef.placeableOnlyInScene != null && _this.validSpot)
        {
            CheckIfValidSpot.GetValue(GameManager.Instance.GetCurrentScene() == _this.placeableObjRef.placeableOnlyInScene);
        }
        if (_this.placeableObjRef.isPatherTile && _this.validSpot)
        {
            CheckIfValidSpot.GetValue(!_this.placeableObjRef.tileMapManagerRef.IsPatherTile(position));
        }
        if (_this.placeableObjRef.isWallItem && _this.validSpot)
        {
            // Also modified to use NonAlloc variant
            collisionsCount = Physics2D.OverlapCircleNonAlloc(position, 0.25f, squareBoundsCheckerCollisionResults, LayerManager.Instance.WallLayer);
            CheckIfValidSpot.GetValue(collisionsCount > 0);
        }
        if (_this.placeableObjRef.isPipe && _this.validSpot)
        {
            CheckIfValidSpot.GetValue(!_this.placeableObjRef.tileMapManagerRef.IsTilePipe(position));
        }

        return false;
    }

    // Cooking UI: when auto-selecting ingredients,
    // prioritize the ones with less stamina value (average of player & villager values)
    [HarmonyPatch(typeof(CookingUI), "PullIngredients")]
    [HarmonyPrefix]
    static bool PrioritizeLowStaminaIngredients(CookingUI __instance)
    {
        var ui = __instance;
        IngredientOption[] ingredOptions = GetField<IngredientOption[]>(ui, "ingredOptions");
        Inventory playerInventory = GetField<Inventory>(ui, "playerInventory");

        // For each ingredient the recipe requires
        foreach (IngredientOption option in ingredOptions)
        {
            if (!option.gameObject.activeSelf) continue; // Skip unused ingredient slots

            Logger.LogInfo($"Finding best ingredient for category {option.requiredFoodCategoryType}, item {option.requiredFoodItem?.Label}");

            // Find the valid ingredient in the player's inventory that restores the least stamina
            float lowestStamina = float.MaxValue;
            int bestIndex = -1;
            for (int i = 0; i < playerInventory.Items.Length; i++)
            {
                InventoryItem item = playerInventory[i].itemReference;
                if (item != null && ((option.requiredFoodCategoryType != FoodCategoryType.None && option.requiredFoodCategoryType == item.FoodCategoryType) || option.requiredFoodItem == item))
                {
                    float staminaValue = (item.PlayerDietCaloricValue + item.CaloricValue) / 2f; // Use average of player & villager stamina values
                    if (staminaValue < lowestStamina)
                    {
                        Logger.LogInfo($"Found better ingredient for recipe slot: {item.Label} (stamina {staminaValue})");
                        lowestStamina = staminaValue;
                        bestIndex = i;
                    }
                }
            }
            // Select the item
            if (bestIndex != -1)
            {
                option.SelectItemFromPlayerInv(playerInventory[bestIndex].itemReference, bestIndex);
            }
        }
        ui.CheckIfAllIngredSelected();
        return false; // Skip original method
    }

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

    // Invert toolbar and pause menu tab bar scroll directions
    [HarmonyPatch(typeof(ToolBarUI), "ChangeSelection")]
    [HarmonyPrefix]
    static bool InvertToolbarScrollDirection(ToolBarUI __instance, ref int direction)
    {
        direction = -direction;
        return true;
    }
    [HarmonyPatch(typeof(UIController), "ScrollForward")]
    [HarmonyPrefix]
    static bool UIScrollForward(UIController __instance)
    {
        UIMenuBase uimenuBase = GetField<UIMenuBase>(__instance, "currentMenu");
        if (uimenuBase == null || uimenuBase.ParentMenu == null || uimenuBase.ParentMenu is not PauseMenu)
        {
            return true;
        }
        uimenuBase.ParentMenu.PrevPage();
        return false;
    }
    [HarmonyPatch(typeof(UIController), "ScrollBackward")]
    [HarmonyPrefix]
    static bool UIScrollBackward(UIController __instance)
    {
        UIMenuBase uimenuBase = GetField<UIMenuBase>(__instance, "currentMenu");
        if (uimenuBase == null || uimenuBase.ParentMenu == null || uimenuBase.ParentMenu is not PauseMenu)
        {
            return true;
        }
        uimenuBase.ParentMenu.NextPage();
        return false;
    }

    // Show notification when it's getting late in the day (so you don't forget to sleep lmao happened to me once)
    [HarmonyPatch(typeof(TimeCompassUI), "SetTimeText")]
    [HarmonyPrefix]
    static bool ShowLateNotification()
    {
        int hours = (int)TimeControl.Instance.Minutes / 60;
        int minutes = (int)TimeControl.Instance.Minutes % 60;
        if (hours == LATE_WARNING_HOUR && minutes == LATE_WARNING_MINUTE)
        {
            GameManager.Instance.PopUpDialogBox.DisplayMsg("It's getting late...", 2f);
        }
        return true;
    }

    // Prevent interacting with world while the cursor is over the toolbar
    // Ex. if you're near a crafting station and you want to change your
    // toolbar item by clicking on it, the game would interact with the crafting station as well
    [HarmonyPatch(typeof(PlayerController), "OnInteract")]
    [HarmonyPrefix]
    static bool PreventInteractOverToolbar(PlayerController __instance)
    {
		ToolBarUI toolBarUI = FindObjectOfType<ToolBarUI>();
        if (toolBarUI == null) return true; // Happens during cutscenes
        RectTransform rectTransform = toolBarUI.transform.Find("Horizontal Laoyut").GetComponent<RectTransform>(); // This gameobject has a larger rect than the parent toolbar, as it's where the BG image is. "Laoyut" typo is from the game
        bool isPointerOver = RectTransformUtility.RectangleContainsScreenPoint(rectTransform, Input.mousePosition, null);
        return toolBarUI.gameObject.activeInHierarchy && !isPointerOver;
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

    // Skip splash screen (somewhat)
    [HarmonyPatch(typeof(SplashScreenLoader), "Start")]
    [HarmonyPrefix]
    static bool SkipSplashScreen(SplashScreenLoader __instance)
    {
        __instance.Invoke("LoadMainMenu", 0.5f); // Needs a delay to avoid a race condition with settings loading
        return false;
    }
}
