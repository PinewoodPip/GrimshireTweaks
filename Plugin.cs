
using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace GrimshireTweaks;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    internal static ConfigEntry<float> DialogSpeedSetting;

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

        // Load features & patches
        Harmony.CreateAndPatchAll(typeof(PluginVersionDisplay));
        Harmony.CreateAndPatchAll(typeof(DialogSpeed));
        foreach (var feature in toggleableFeatures)
        {
            if (feature.enabled)
            {
                Logger.LogInfo($"Enabling feature {feature.patchClass.Name}");
                Harmony.CreateAndPatchAll(feature.patchClass);
            }
        }
    }
}
