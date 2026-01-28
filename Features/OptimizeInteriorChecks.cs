
using HarmonyLib;
using UnityEngine;

namespace GrimshireTweaks;

public static class OptimizeInteriorChecks
{
    private static LightSystem cachedLightSystem = null;

    // Replacement for WeatherSystem.IsInteriorScene() to cache the component reference
    // The original function did up to 2 FindObjectOfType() calls per check,
    // used many times throughout the game, contributing to small stutters
    [HarmonyPatch(typeof(WeatherSystem), "IsInteriorScene")]
    [HarmonyPrefix]
	public static bool IsInteriorScene(WeatherSystem __instance, ref bool __result)
    {
        // Note: Unity overloads this operator to return null for stale refs, so it won't leak/stick in case the LightSystem is destroyed & recreated
        if (cachedLightSystem == null)
        {
            cachedLightSystem = GameObject.FindObjectOfType<LightSystem>();
        }
        __result = cachedLightSystem && cachedLightSystem.isInteriorScene;
        return false;
    }
}