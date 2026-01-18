using HarmonyLib;

namespace GrimshireTweaks;

public static class AutoToggleRunning
{
    // Automatically make player run (instead of walk) when loading into the game
    [HarmonyPatch(typeof(PlayerMovement), "Start")]
    [HarmonyPostfix]
    static void EnableRunning(PlayerMovement __instance)
    {
        __instance.ToggleRunning();
    }
}