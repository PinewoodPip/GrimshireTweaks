using HarmonyLib;
using TMPro;

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
    }
}