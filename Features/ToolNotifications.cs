
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace GrimshireTweaks;

public static class ToolNotifications
{
    static bool _RequestedToolMessage = false;

    // Prevent notifications for equipping tools from showing.
    [HarmonyPatch(typeof(PopUpDialogBox), "DisplayFancy")]
    [HarmonyPrefix]
    static bool OnFancyMessage(PopUpDialogBox __instance, string msg, Sprite spriteImage, float displayTimeSecs, AudioClip audioClip)
    {
        return !_RequestedToolMessage;;
    }

    // Track when the game attempts to show the "Equipped X" messages.
    [HarmonyPatch(typeof(LocalizationManager), "LocalizeString")]
    [HarmonyPostfix]
    static void After(string table, string key, params object[] variables)
    {
        _RequestedToolMessage = table == "Miscellaneous" && key == "popup.EquippedTool";
    }
}