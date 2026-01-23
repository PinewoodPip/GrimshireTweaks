using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using Yarn.Unity;
using static GrimshireTweaks.Utils;

namespace GrimshireTweaks;

public static class DialogHotkeys
{
    static List<string> DISMISS_OPTION_KEYWORDS =
    [
        "never mind", // Used by dialogues.
        "nevermind", // Used by beds.
        "no.",
        "nope.", // Logan grooming dialogue (character recustomization)

        // I don't think the following are used, but just in case :P
        "cancel",
        "exit"
    ];

    // Allow progressing dialogues with the enter key
    [HarmonyPatch(typeof(PlayerController), "ProcessInput")]
    [HarmonyPostfix]
    static void ProgressDialog(PlayerController __instance)
    {
        if (Input.GetKeyDown(KeyCode.Return) || Input.GetKeyDown(KeyCode.KeypadEnter))
        {
            var dialogManager = GameManager.Instance.DialogManager;
            if (dialogManager.IsDialogRunning())
            {
                // There's no need to check whether there are options shown; emulating the click to the LineView will be a no-op in that case.
                dialogManager.DialogContinue();
            }
        }
    }

    // Allow closing dialogs with options with the escape key
    // ie. escape will select "Nevermind" options
    [HarmonyPatch(typeof(PlayerController), "ProcessInput")]
    [HarmonyPostfix]
    static void DismissDialog(PlayerController __instance)
    {
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            var dialogManager = GameManager.Instance.DialogManager;
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