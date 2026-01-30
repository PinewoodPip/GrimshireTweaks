using HarmonyLib;
using TMPro;
using UnityEngine;

namespace GrimshireTweaks;

public static class PluginVersionDisplay
{
    // Automatically make player run (instead of walk) when loading into the game
    [HarmonyPatch(typeof(MainMenuUI), "Start")]
    [HarmonyPostfix]
    static void AppendPluginVersion(MainMenuUI __instance)
    {
        TextMeshProUGUI settingsButtonText = __instance.transform.Find("MainMenuGroup/SettingsOption").GetComponentInChildren<TextMeshProUGUI>();
        settingsButtonText.text += "\n<size=16>Grimshire Tweaks v" + MyPluginInfo.PLUGIN_VERSION + "</size>";

        // Add version mismatch warning
        if (Application.version != Plugin.INTENDED_GAME_VERSION)
        {
            settingsButtonText.text += $"\n<size=12><color=#ff5959>Game version mismatch; intended for Grimshire v{Plugin.INTENDED_GAME_VERSION}</color></size>";
        }
    }
}