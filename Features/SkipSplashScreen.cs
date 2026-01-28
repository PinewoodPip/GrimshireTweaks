
using HarmonyLib;
using UnityEngine;

namespace GrimshireTweaks;

public static class SkipSplashScreen
{
    // Skip splash screen (somewhat)
    [HarmonyPatch(typeof(SplashScreenLoader), "Start")]
    [HarmonyPrefix]
    static bool SkipToMainMenu(SplashScreenLoader __instance)
    {
        __instance.Invoke("LoadMainMenu", 0.25f); // Needs a delay to avoid a race condition with settings loading
        return false;
    }
}