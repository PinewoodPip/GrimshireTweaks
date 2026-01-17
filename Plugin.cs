using System;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;
using TMPro;
using UnityEngine;

namespace GrimshireTweaks;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    static int LATE_WARNING_HOUR = 20;
    static int LATE_WARNING_MINUTE = 0;

    private void Awake()
    {
        // Plugin startup logic
        Logger = base.Logger;
        Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} is loaded!");

        Harmony.CreateAndPatchAll(typeof(Plugin));
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

    // Show composter value in item tooltips
    [HarmonyPatch(typeof(ItemInfoDisplay), "Display")] // Tooltips (ex. in hotbar)
    [HarmonyPostfix]
    static void ShowComposterValue(ItemInfoDisplay __instance, bool enabled, InventoryItem itemRef, float itemSpoilage, RectTransform parent)
    {
        if (itemRef == null) return;
        var compostValue = itemRef.compostValue;
        if (compostValue > 0f)
        {
            var textField = Traverse.Create(__instance).Field("infoTMP").GetValue<TextMeshProUGUI>();
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
            var textField = Traverse.Create(__instance).Field("descTMP").GetValue<TextMeshProUGUI>();
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
}
