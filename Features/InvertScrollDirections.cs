
using HarmonyLib;
using static GrimshireTweaks.Utils;

public static class InvertScrollDirections
{
    // Invert toolbar and pause menu tab bar scroll directions
    [HarmonyPatch(typeof(ToolBarUI), "ChangeSelection")]
    [HarmonyPrefix]
    static bool InvertToolbarScrollDirection(ToolBarUI __instance, ref int direction)
    {
        direction = -direction;
        return true;
    }
    [HarmonyPatch(typeof(UIController), "ScrollForward")]
    [HarmonyPrefix]
    static bool UIScrollForward(UIController __instance)
    {
        UIMenuBase uimenuBase = GetField<UIMenuBase>(__instance, "currentMenu");
        if (uimenuBase == null || uimenuBase.ParentMenu == null || uimenuBase.ParentMenu is not PauseMenu)
        {
            return true;
        }
        uimenuBase.ParentMenu.PrevPage();
        return false;
    }
    [HarmonyPatch(typeof(UIController), "ScrollBackward")]
    [HarmonyPrefix]
    static bool UIScrollBackward(UIController __instance)
    {
        UIMenuBase uimenuBase = GetField<UIMenuBase>(__instance, "currentMenu");
        if (uimenuBase == null || uimenuBase.ParentMenu == null || uimenuBase.ParentMenu is not PauseMenu)
        {
            return true;
        }
        uimenuBase.ParentMenu.NextPage();
        return false;
    }
}