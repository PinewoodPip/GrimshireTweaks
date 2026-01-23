
using Febucci.UI;
using Febucci.UI.Core;
using HarmonyLib;
using UnityEngine;
using Yarn.Unity;
using static GrimshireTweaks.Utils;

namespace GrimshireTweaks;

// Allows dialogue speed to be customized.
public static class DialogSpeed
{
    public static float DEFAULT_TYPEWRITER_SPEED = 1f;

    // Patch typewriter to re-set to the configured speed at the start of each line.
    // This method would normally reset the speed to 1.0.
    [HarmonyPatch(typeof(TypewriterCore), "StartShowingText")]
    [HarmonyPostfix]
    static void AdjustAnimalSoundFrequency(TypewriterCore __instance, bool restart)
    {
        float newSpeed = Plugin.DialogSpeedSetting.Value;
        __instance.SetTypewriterSpeed(newSpeed);
    }
    
    // Auto-adjust character interval of animal noises to (try to) keep the same sound effect frequency.
    [HarmonyPatch(typeof(DialogSpeaker), "SetupSpeaker")]
    [HarmonyPostfix]
    static void CustomizeDialogSpeed(DialogSpeaker __instance, Character speaker)
    {
        int speakerFrequency = speaker ? speaker.SpeakerFreqLetterCount : 4; // Default value taken from original method

        float newSpeed = Plugin.DialogSpeedSetting.Value;
        float speedMultiplier = newSpeed / DEFAULT_TYPEWRITER_SPEED;
        __instance.SetSpeakerFreq(Mathf.CeilToInt(speakerFrequency * speedMultiplier)); // This method uses an int, so with some speeds it may sound slightly faster/slower than normal.
    }
}