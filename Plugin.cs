using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using TMPro;
using UnityEngine;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace GrimshireTweaks;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    static int LATE_WARNING_HOUR = 20;
    static int LATE_WARNING_MINUTE = 0;
    static Color AVAILABLE_COLLECTION_ITEM_COLOR = new Color(0f, 0f, 0f, 0.8f);

    // Mixins and trackers for SquareBoundsChecker optimizations
    class SquareBoundsCheckerMixin
    {
        public Vector3 previousPosition;
    }
    static ConditionalWeakTable<SquareBoundsChecker, SquareBoundsCheckerMixin> squareBoundsCheckerMixinStates = new ConditionalWeakTable<SquareBoundsChecker, SquareBoundsCheckerMixin>();
    static Collider2D[] squareBoundsCheckerCollisionResults = new Collider2D[1];

    // These characters are hardcoded to always have service dialogue.
    static HashSet<string> NPCS_WITH_SERVICE_DIALOGUE = new HashSet<string>
    {
        "Rowan", "Gruff", "Wilfred", "Kai", "Percy", "Logan", "Tano"
    };

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} is loaded!");

        Harmony.CreateAndPatchAll(typeof(Plugin));
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

    // Disable highlight material on NPCs that do not have dialogue left for the day
    [HarmonyPatch(typeof(IInteractable), "Highlight")]
    [HarmonyPrefix]
    static bool PreventNPCHighlight(IInteractable __instance, Material highlightMat)
    {
        if (__instance is InteractableDialogue npc && highlightMat != null)
        {
            PlayerController player = GameManager.Instance.Player;
            InventoryItem heldItem = player.GetHeldItemRef();

            // Keep highlight if the player is holding a giftable item
            if (heldItem != null && heldItem.IsGiftable())
            {
                return true;
            }

            // Check if the NPC has any dialog left for today
            // TODO there's some extra checks missing here, which would need to be ported;
            // it's worth noting that the relevant methods that sound like getters (ex. GetCharacterDialog())
            // actually have side effects, thus we cannot call them to check which dialog would play
            Character character = npc.CharRef;
            bool canTalk = NPCS_WITH_SERVICE_DIALOGUE.Contains(character.name) || character.NonVillager || GameManager.Instance.FestivalManager.IsThereAFestivalActive() || !GameManager.Instance.CharacterManager.IsCharacterDoneTalkingToday(character.ID);
            return canTalk;
        }
        return true;
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
            textField.text += $"\nComposter Value: {compostValue}";
        }
    }
    [HarmonyPatch(typeof(ItemDetailsPanel), "UpdateDetailsDisplay", new Type[] {typeof(InventoryItem), typeof(float)})] // Inventory tab
    [HarmonyPostfix]
    static void ShowComposterValueInInventory(ItemDetailsPanel __instance, InventoryItem itemRef, float slotSpoilageAmount)
    {
        if (itemRef == null) return;
        var compostValue = itemRef.compostValue;
        if (compostValue > 0f)
        {
            var textField = GetField<TextMeshProUGUI>(__instance, "descTMP");
            textField.text += $"\n\nComposter Value: {compostValue}";
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

    // Utility method to read private fields.
    static T GetField<T>(object obj, string fieldName)
    {
        var field = Traverse.Create(obj).Field(fieldName);
        return field.GetValue<T>();
    }

    // Utility method to write private fields.
    static void SetField<T>(object obj, string fieldName, T value)
    {
        var field = Traverse.Create(obj).Field(fieldName);
        field.SetValue(value);
    }

    static T CallMethod<T>(object obj, string methodName, params object[] parameters)
    {
        var method = Traverse.Create(obj).Method(methodName, parameters);
        return method.GetValue<T>();
    }
    static void CallMethod(object obj, string methodName, params object[] parameters)
    {
        var method = Traverse.Create(obj).Method(methodName, parameters);
        method.GetValue();
    }
}
