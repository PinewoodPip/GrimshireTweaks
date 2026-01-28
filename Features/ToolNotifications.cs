
using System.Text.RegularExpressions;
using HarmonyLib;
using UnityEngine;

namespace GrimshireTweaks;

public static class ToolNotifications
{
    // Prevent notifications for equipping tools from showing.
    [HarmonyPatch(typeof(PopUpDialogBox), "DisplayFancy")]
    [HarmonyPrefix]
    static bool OnFancyMessage(PopUpDialogBox __instance, string msg, Sprite spriteImage, float displayTimeSecs, AudioClip audioClip)
    {
        // TODO support localizations when the game adds them
        // An IL edit to patch out the calls would be the most robust way of doing this,
        // but it felt overkill.
        bool isToolEquipMessage = Regex.IsMatch(msg, @"^Equipped ");
        return !isToolEquipMessage;
    }
}