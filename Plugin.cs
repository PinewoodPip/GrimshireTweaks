
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace GrimshireTweaks;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    public static readonly string INTENDED_GAME_VERSION = "0.28.1";

    internal static new ManualLogSource Logger;
    internal static ConfigEntry<float> DialogSpeedSetting;
    internal static ConfigEntry<float> QuickStackRange;
    internal static ConfigEntry<bool> QuickStackSpoilableItems;
    internal static ConfigEntry<bool> QuickStackToolbarItems;
    internal static ConfigEntry<KeyboardShortcut> QuickStackKeybind;
    internal static ConfigEntry<string> AutoLoadSaveFileName;
    internal static ConfigEntry<bool> RemindersHUDWeatherReminder;
    internal static ConfigEntry<bool> RemindersHUDTroughReminder;
    internal static ConfigEntry<bool> RemindersHUDBirthdayReminder;
    internal static ConfigEntry<bool> RemindersHUDCritterReminder;
    internal static ConfigEntry<bool> RemindersHUDToolReminder;

    // Utility class for associating a feature with a bool config setting.
    class ToggleableFeature
    {
        internal Type patchClass;
        internal ConfigEntry<bool> enabledSetting;

        public bool enabled => enabledSetting.Value;

        public ToggleableFeature(Type patchClass, ConfigEntry<bool> enabledSetting)
        {
            this.patchClass = patchClass;
            this.enabledSetting = enabledSetting;
        }
    }

    private List<ToggleableFeature> toggleableFeatures; // Initialized in Awake()

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} is loaded!");

        // Warn about game version mismatch
        if (Application.version != INTENDED_GAME_VERSION)
        {
            Logger.LogWarning($"This version of Grimshire Tweaks was made for Grimshire v{INTENDED_GAME_VERSION} but you're running v{Application.version} - some features may not work. Check for a mod update!");
        }

        // Define settings
        toggleableFeatures = new List<ToggleableFeature>()
        {
            // ------------
            // QoL features
            // ------------
            new(typeof(GiftItemTooltips), Config.Bind(
                "QualityOfLife",
                "GiftItemTooltips",
                true,
                "Makes giftable items show who likes the item in their tooltip, if you've discovered who likes the item"
            )),
            new(typeof(DialogHotkeys), Config.Bind(
                "QualityOfLife",
                "DialogHotkeys",
                true,
                "Allows you to progress dialogues with the \"Enter\" key, and select dialogue choices like \"Nevermind\" or \"No\" with the \"Esc\" key"
            )),
            new(typeof(CraftFromContainers), Config.Bind(
                "QualityOfLife",
                "CraftFromContainers",
                true,
                "Allows the crafting workbench to use ingredients from chests on your farm"
            )),
            new(typeof(DailyDialogueIndicator), Config.Bind(
                "QualityOfLife",
                "DailyDialogueIndicator",
                false,
                "Shows a speech bubble for villagers you haven't spoken to today yet"
            )),
            new(typeof(ComposterValueDisplay), Config.Bind(
                "QualityOfLife",
                "ComposterValueDisplay",
                true,
                "Shows compost quality value in tooltips for compostable items"
            )),
            new(typeof(EfficientAutoCook), Config.Bind(
                "QualityOfLife",
                "EfficientAutoCook",
                true,
                "Makes the cooking UI prioritize pulling ingredients with lowest stamina value"
            )),
            new(typeof(CollectionLogTweaks), Config.Bind(
                "QualityOfLife",
                "CollectionLogTweaks",
                true,
                "Highlights unobtained fish/critters/seeds that are currently in season in the collection log"
            )),
            new(typeof(ItemPriceDisplay), Config.Bind(
                "QualityOfLife",
                "ItemPriceDisplay",
                false,
                "Shows item sell prices in the details panel in the inventory UI, even when outside shop UIs"
            )),
            new(typeof(LateNotification), Config.Bind(
                "QualityOfLife",
                "LateNotification",
                false,
                "Shows an \"It's getting late...\" notification when the clock hits 10PM"
            )),
            new(typeof(HighlightHarvestableCrops), Config.Bind(
                "QualityOfLife",
                "HighlightHarvestableCrops",
                false,
                "Highlights harvestable crops when holding the scythe"
            )),
            new(typeof(ToolNotifications), Config.Bind(
                "QualityOfLife",
                "ToolNotifications",
                true,
                "Controls whether notifications for equipping tools are shown"
            )),
            new(typeof(AutoOpenGates), Config.Bind(
                "QualityOfLife",
                "AutoOpenGates",
                false,
                "Automatically opens/closes gates when you get close to them or walk away"
            )),

            // ------------
            // Reminder settings
            // ------------
            new(typeof(RemindersHUD), Config.Bind(
                "QualityOfLife.Reminders",
                "RemindersHUD",
                false,
                "Shows reminders in the pinned quest display. See other settings in this category to configure which reminders are shown"
            )),

            // ------------
            // Customization & misc tweaks
            // ------------
            new(typeof(InvertScrollDirections), Config.Bind(
                "MiscCustomization",
                "InvertScrollDirections",
                false,
                "Inverts the toolbar scroll direction, such that scrolling down moves to the slot on the right"
            )),
            new(typeof(ShortenInputCooldowns), Config.Bind(
                "MiscCustomization",
                "ShortenInputCooldowns",
                false,
                "Reduces the cooldown for left-clicking in UIs"
            )),
            new(typeof(AutoToggleRunning), Config.Bind(
                "MiscCustomization",
                "AutoToggleRunning",
                false,
                "Makes the player default to running instead of walking"
            )),
            new(typeof(SkipSplashScreen), Config.Bind(
                "MiscCustomization",
                "SkipSplashScreen",
                false,
                "Skips the splash screen on game launch"
            )),

            // ------------
            // Fixes
            // ------------
            new(typeof(OptimizeObjectPlacement), Config.Bind(
                "Fixes",
                "OptimizeObjectPlacement",
                true,
                "Optimizes the code for placing objects, reducing lag that occurs when placing objects like tree seeds"
            )),
            new(typeof(OptimizeInteriorChecks), Config.Bind(
                "Fixes",
                "OptimizeIsInteriorChecks",
                true,
                "Optimizes the WeatherSystem.IsInteriorScene() checks (used commonly throughout the game) to reduce stutter; most noticeable when placing objects"
            )),
            new(typeof(FixToolbarWorldInteraction), Config.Bind(
                "Fixes",
                "FixToolbarWorldInteraction",
                true,
                "Prevents left-click from interacting with the world while the cursor is over toolbar slots"
            )),

            // ------------
            // Dev features
            // ------------
            new(typeof(AutoLoadSave), Config.Bind(
                "Dev",
                "AutoLoadSave",
                false,
                "Automatically loads a save when the game starts; set \"AutoLoadSaveFileName\" setting to configure which save is loaded"
            )),
        };
        // ------------
        // Numeric settings
        // ------------
        DialogSpeedSetting = Config.Bind(
            "QualityOfLife",
            "DialogSpeed",
            1f,
            "Adjusts the speed of dialogue text appearing; default is 1.0. Animal speech noises will try to play at the original frequency."
        );
        // ------------
        // Quick stack settings
        // ------------
        QuickStackKeybind = Config.Bind(
            "QualityOfLife.QuickStack",
            "QuickStackKeybind",
            new KeyboardShortcut(KeyCode.G),
            "Keybind for quick-stacking to nearby chests"
        );
        QuickStackRange = Config.Bind(
            "QualityOfLife.QuickStack",
            "QuickStackRange",
            3f,
            "The maximum range (in tiles) for quick-stacking to nearby chests, using Chebyshev distance (ie. diagonals count as 1 tile)."
        );
        QuickStackSpoilableItems = Config.Bind(
            "QualityOfLife.QuickStack",
            "QuickStackSpoilableItems",
            false,
            "Determines whether edible items can be quick-stacked"
        );
        QuickStackToolbarItems = Config.Bind(
            "QualityOfLife.QuickStack",
            "QuickStackToolbarItems",
            true,
            "Determines whether items in the toolbar (first inventory row) can be quick-stacked"
        );
        // ------------
        // Reminders HUD settings
        // ------------
        RemindersHUDWeatherReminder = Config.Bind(
            "QualityOfLife.Reminders",
            "ShowWeatherReminder",
            true,
            "Determines whether to show tomorrow's weather in the reminders widget"
        );
        RemindersHUDTroughReminder = Config.Bind(
            "QualityOfLife.Reminders",
            "ShowTroughReminder",
            true,
            "Determines whether to show empty trough warning in the reminders widget"
        RemindersHUDBirthdayReminder = Config.Bind(
            "QualityOfLife.Reminders",
            "ShowBirthdayReminder",
            true,
            "Determines whether to show birthday reminders in the reminders widget"
        );
        RemindersHUDCritterReminder = Config.Bind(
            "QualityOfLife.Reminders",
            "ShowCritterReminder",
            true,
            "Determines whether to show a reminder for picking up Tano's tamed critter in the reminders widget"
        );
        RemindersHUDToolReminder = Config.Bind(
            "QualityOfLife.Reminders",
            "ShowToolReminder",
            true,
            "Determines whether to show a reminder for tool upgrades in the reminders widget"
        );
        // ------------
        // Dev settings
        // ------------
        AutoLoadSaveFileName = Config.Bind(
            "Dev",
            "AutoLoadSaveFileName",
            "",
            "The filename (with \".grimshire\" extension) of the specific save to load on game start. Leave empty to load the most recent save. Requires \"AutoLoadSave\" to be enabled."
        );

        // Load features & patches
        TryLoadFeature(typeof(PluginVersionDisplay));
        TryLoadFeature(typeof(DialogSpeed));
        TryLoadFeature(typeof(QuickStackFromWorld));
        foreach (var feature in toggleableFeatures)
        {
            if (feature.enabled)
            {
                TryLoadFeature(feature.patchClass);
            }
        }
    }

    // Attempts to apply the Harmony patches of a feature.
    public void TryLoadFeature(Type featureClass)
    {
        try
        {
            Logger.LogInfo($"Enabling feature {featureClass.Name}");
            Harmony.CreateAndPatchAll(featureClass);
        }
        catch (Exception e)
        {
            Logger.LogError($"Error enabling feature {featureClass.Name} (check for a newer version of the mod or report this): {e}");
        }
    }
}
