using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Yarn.Unity;
using static GrimshireTweaks.Utils;

namespace GrimshireTweaks;

public static class ShortenInputCooldowns
{
    static float INPUT_COOLDOWN = 0.05f; // Not 0 just in case there's some edgecase in the game that actually warrants a cooldown (possibly the reason why there is one in the first place)

    // Decrease input cooldown
    // Main use case is to be able to swap items around in the inventory faster;
    // the default 0.2s cooldown gets in the way often.
    [HarmonyPatch(typeof(UIController), "Start")]
    [HarmonyPostfix]
    static void ReduceInputCooldown(UIController __instance)
    {
        // Game never rewrites this field, so it's safe to do just in Start()
        // movementDelayInterval also exists, but it seems to be for UI sliders only(?), so not very relevant.
        SetField(__instance, "inputDelayInterval", INPUT_COOLDOWN);
    }
}