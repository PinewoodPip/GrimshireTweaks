
using System.Collections.Generic;
using HarmonyLib;

namespace GrimshireTweaks;

public static class AutoLoadSave
{
    // Auto-load the latest save (or a specific configured one) when the game starts
    // Thankfully there seems to be no need to load the main menu scene first,
    // so we can do it right from splash screen
    [HarmonyPatch(typeof(SplashScreenLoader), "LoadMainMenu")]
    [HarmonyPrefix]
    static bool OnMainMenuLoaded(SplashScreenLoader __instance)
    {
        List<SaveObject> saves = GameData.GetAllSaveObjects();
        if (saves.Count == 0) return true;

        SaveObject save = null;

        // Get specific save
        string targetFileName = Plugin.AutoLoadSaveFileName.Value;
        if (targetFileName != "")
        {
            save = saves.Find(s => s.fileName == targetFileName);
            if (save == null)
            {
                Plugin.Logger.LogWarning($"Could not find save with filename \"{targetFileName}\", falling back to most recent save");
            }
        }
        // Get most recent save
        if (save == null)
        {
            saves.Sort((a, b) => b.fileCreationDate.CompareTo(a.fileCreationDate));
            save = saves[0];
        }

        bool canLoad = save != null;
        if (canLoad)
        {
            GameData.SetActiveSaveFileAndStart(save);
        }
        return !canLoad;
    }
}