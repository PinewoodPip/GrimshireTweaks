
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.Mono;
using HarmonyLib;

namespace GrimshireTweaks;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class Plugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    private void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo($"{MyPluginInfo.PLUGIN_GUID} is loaded!");

        // Load features & patches
        Harmony.CreateAndPatchAll(typeof(GiftItemTooltips));
        Harmony.CreateAndPatchAll(typeof(AutoToggleRunning));
        Harmony.CreateAndPatchAll(typeof(DialogDismissHotkey));
        Harmony.CreateAndPatchAll(typeof(ShortenInputCooldowns));
        Harmony.CreateAndPatchAll(typeof(CraftFromContainers));
        Harmony.CreateAndPatchAll(typeof(PluginVersionDisplay));
        Harmony.CreateAndPatchAll(typeof(DailyDialogueIndicator));
        Harmony.CreateAndPatchAll(typeof(ComposterValueDisplay));
        Harmony.CreateAndPatchAll(typeof(OptimizeObjectPlacement));
        Harmony.CreateAndPatchAll(typeof(EfficientAutoCook));
        Harmony.CreateAndPatchAll(typeof(CollectionLogTweaks));
        Harmony.CreateAndPatchAll(typeof(InvertScrollDirections));
        Harmony.CreateAndPatchAll(typeof(LateNotification));
        Harmony.CreateAndPatchAll(typeof(MiscTweaks));
        Harmony.CreateAndPatchAll(typeof(FixToolbarWorldInteraction));
        Harmony.CreateAndPatchAll(typeof(SkipSplashScreen));
    }
}
