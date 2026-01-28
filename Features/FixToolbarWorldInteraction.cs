
using HarmonyLib;
using UnityEngine;

namespace GrimshireTweaks;

public static class FixToolbarWorldInteraction
{
    // Prevent interacting with world while the cursor is over the toolbar
    // Ex. if you're near a crafting station and you want to change your
    // toolbar item by clicking on it, the game would interact with the crafting station as well
    [HarmonyPatch(typeof(PlayerController), "OnInteract")]
    [HarmonyPrefix]
    static bool PreventInteractOverToolbar(PlayerController __instance)
    {
		ToolBarUI toolBarUI = Object.FindObjectOfType<ToolBarUI>();
        if (toolBarUI == null) return true; // Happens during cutscenes
        RectTransform rectTransform = toolBarUI.transform.Find("Horizontal Laoyut").GetComponent<RectTransform>(); // This gameobject has a larger rect than the parent toolbar, as it's where the BG image is. "Laoyut" typo is from the game
        bool isPointerOver = RectTransformUtility.RectangleContainsScreenPoint(rectTransform, Input.mousePosition, null);
        return toolBarUI.gameObject.activeInHierarchy && !isPointerOver;
    }
    
}