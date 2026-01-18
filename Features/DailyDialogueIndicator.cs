
using HarmonyLib;
using UnityEngine;
using static GrimshireTweaks.Utils;

public class DailyDialogueIndicator
{
    // Show speech bubble when hovering over NPCs that have not been spoken to today
    [HarmonyPatch(typeof(IInteractable), "Highlight")]
    [HarmonyPrefix]
    static bool ToggleDialogIndicator(IInteractable __instance, Material highlightMat)
    {
        if (__instance is not InteractableDialogue npc) return true;

        Character character = npc.CharRef;
        if (character.NonVillager) return true;

        // Toggle dialogue indicator
        CharacterController charController = GameManager.Instance.CharacterManager.GetCharacterObjInScene(character.ID).GetComponent<CharacterController>();
        EmoticonsController emoticonsController = charController.GetComponentInChildren<EmoticonsController>();
        if (highlightMat != null) // Hovering over NPC
        {
            PlayerController player = GameManager.Instance.Player;
            InventoryItem heldItem = player.GetHeldItemRef();

            // Don't show indicator if player is holding a giftable item
            // TODO if the player stops holding a gift while still hovering, make the icon appear?
            if (heldItem != null && heldItem.IsGiftable())
            {
                return true;
            }

            // Show indicator if the NPC has not been talked to today
            bool wasTalkedToToday = GameManager.Instance.CharacterManager.IsCharacterDoneTalkingToday(character.ID);
            if (!wasTalkedToToday)
            {
                emoticonsController.ShowIcon("pause", 3f);
            }
        }
        else // No longer hovering over NPC
        {
            CallMethod(emoticonsController, "HideAllIcons");
        }
        return true;
    }
}