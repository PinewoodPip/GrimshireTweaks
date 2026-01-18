using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Yarn.Unity;
using static GrimshireTweaks.Utils;

namespace GrimshireTweaks;

public static class DialogDismissHotkey
{
    static List<string> DISMISS_OPTION_KEYWORDS =
    [
        "never mind", // Used by dialogues.
        "nevermind", // Used by beds.

        // I don't think the following are used, but just in case :P
        "cancel",
        "exit"
    ];

    // Allow closing dialogs with options with the escape key
    // ie. escape will select "Nevermind" options
    [HarmonyPatch(typeof(PlayerController), "ProcessInput")]
    [HarmonyPostfix]
    static void DismissDialog(PlayerController __instance)
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            var dialogManager = Object.FindObjectOfType<DialogManager>();
            if (dialogManager.IsDialogRunning())
            {
                // Check if any dialog options are "cancel" ones
                OptionsListView optionsListView = dialogManager.GetComponentInChildren<OptionsListView>();
                if (optionsListView != null && optionsListView.gameObject.activeInHierarchy)
                {
                    var options = GetField<List<OptionView>>(optionsListView, "optionViews");
                    foreach (var optionView in options)
                    {
                        string optionText = optionView.Option.Line.Text.Text.ToLower();
                        if (DISMISS_OPTION_KEYWORDS.Any(optionText.Contains))
                        {
                            // Execute the option
                            CallMethod(optionView, "InvokeOptionSelected");
                            break;
                        }
                    }
                }
            }
        }
    }
}