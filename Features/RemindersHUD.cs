
using System.Collections.Generic;
using HarmonyLib;
using TMPro;
using UnityEngine;
using static GrimshireTweaks.Utils;

namespace GrimshireTweaks;

public static class RemindersHUD
{
    static bool hadEmptyTroughs
    {
        get;
        set { if (value != field) isDirty = true; field = value; }
    }
    static bool knowsTomorrowsWeather
    {
        get;
        set { if (value != field) isDirty = true; field = value; }
    }
    static bool isDirty = true;
    static TextMeshProUGUI remindersText = null;

    [HarmonyPatch(typeof(PinnedQuestDisplay), "UpdateDisplay")]
    [HarmonyPostfix]
    static void AfterPinnedQuestDisplayUpdateDisplay(PinnedQuestDisplay __instance, Quest pinnedQuest)
    {
        bool hasQuest = pinnedQuest != null;
        var descTMP = GetField<TextMeshProUGUI>(__instance, "descTMP");

        // Initialize the widget
        // The quest panel background does not have autosize,
        // so we can't really reuse its description text
        if (remindersText == null)
        {
            remindersText = GameObject.Instantiate(descTMP);
            remindersText.transform.SetParent(descTMP.transform.parent);
            remindersText.autoSizeTextContainer = false;
            remindersText.color = Color.white;
            remindersText.fontSize = 12;
            remindersText.fontSizeMax = 12;
            remindersText.outlineColor = Color.black;
            remindersText.outlineWidth = 0.2f;
        }
        Vector2 questPanelPos = descTMP.rectTransform.anchoredPosition;
        remindersText.rectTransform.anchoredPosition = hasQuest ? questPanelPos - new Vector2(0, 75) : questPanelPos;

        // Append reminders to description
        var reminders = GetReminders();
        remindersText.text = "";
        foreach (string reminder in reminders)
        {
            remindersText.text += $"• {reminder}\n";
        }
    }

    // Update the widget if it was marked as dirty.
    [HarmonyPatch(typeof(PlayerController), "Update")]
    [HarmonyPostfix]
    static void TryUpdateRemindersHUD(PlayerController __instance)
    {
        if (isDirty)
        {
            QuestManager questManager = GameManager.Instance.QuestManager;
            Quest pinnedQuest = questManager.GetPinnedQuest();
            var pinnedQuestDisplay = GetField<PinnedQuestDisplay>(questManager, "pinnedQuestDisplay");
            pinnedQuestDisplay?.UpdateDisplay(pinnedQuest);
            isDirty = false;
        }
    }

    // Returns the reminders that should be shown.
    // Hook this to implement additional reminders in your mod.
    public static List<string> GetReminders()
    {
        List<string> reminders = [];
        if (Plugin.RemindersHUDWeatherReminder.Value && knowsTomorrowsWeather)
        {
            string weatherLabel = WeatherSystem.Instance.GetTomorrowsWeather().Name; // Ex. "sunny day"
            reminders.Add($"Tomorrow is {weatherLabel}");
        }
        if (Plugin.RemindersHUDTroughReminder.Value && hadEmptyTroughs)
        {
            reminders.Add("Some troughs are empty");
        }

        // Birthday reminder
        if (Plugin.RemindersHUDBirthdayReminder.Value)
        {
            CharacterManager characterManager = GameManager.Instance.CharacterManager;
            Character birthdayChar = characterManager.CharacterBdayToday();
            CharacterData birthdayCharData = birthdayChar ? characterManager.GetCharacterDataByID(birthdayChar.ID) : null;
            if (birthdayChar != null && !birthdayCharData.giftedToday)
            {
                reminders.Add($"{birthdayChar.FirstName}'s birthday!");
            }
        }
        return reminders;
    }

    // Track whether the player has learnt tomorrow's weather (from Percy dialogue)
    [HarmonyPatch(typeof(DialogManager), "GetTomorrowsWeather")]
    [HarmonyPostfix]
    static void AfterDialogManagerGetTomorrowsWeather(DialogManager __instance)
    {
        knowsTomorrowsWeather = true;
    }
    [HarmonyPatch(typeof(EndOfDayScreen), "SetupDisplay")]
    [HarmonyPostfix]
    static void AfterEndOfDayScreenSetupDisplay(EndOfDayScreen __instance)
    {
        knowsTomorrowsWeather = false;
    }

    // Update empty trough status
    [HarmonyPatch(typeof(PersistentBarnManager), "Start")]
    [HarmonyPostfix]
    static void Start(PersistentBarnManager __instance)
    {
        hadEmptyTroughs = HasEmptyTroughs();
    }
    [HarmonyPatch(typeof(TroughMenu), "Interact")]
    [HarmonyPostfix]
    static void AfterTroughMenuInteract(TroughMenu __instance, UIOption option)
    {
        hadEmptyTroughs = HasEmptyTroughs();
    }
    [HarmonyPatch(typeof(TroughMenu), "Act")]
    [HarmonyPostfix]
    static void AfterTroughMenuAct(TroughMenu __instance, UIOption option)
    {
        hadEmptyTroughs = HasEmptyTroughs();
    }
    // Update empty trough status after daily feeding update
    [HarmonyPatch(typeof(PersistentAnimalsManager), "UpdateAllAnimalsInList")]
    [HarmonyPostfix]
    static void AfterUpdateAllAnimalsInList(PersistentAnimalsManager __instance)
    {
        hadEmptyTroughs = HasEmptyTroughs();
    }

    // Returns whether any troughs in barns/coops/etc. have no food left.
    public static bool HasEmptyTroughs()
    {
        PersistentBarnManager manager = GameObject.FindObjectOfType<PersistentBarnManager>();
        var barnData = GetField<List<PersistentBarnData>>(manager, "listOfBarnData"); // Note: this contains coops, etc. as well
        foreach (var barn in barnData)
        {
            // Note: GetRandomFoodFromTrough() has side-effects (removes the item), so it's not safe to use
            // We need to manually check if the inventory of the through is empty
            PersistentPlaceablesManager persistentPlaceablesManager = GameManager.Instance.PersistentPlaceablesManager;
            List<PersistentProcessorData> processorData = GetField<List<PersistentProcessorData>>(persistentPlaceablesManager, "processorData");
            PersistentProcessorData value = processorData.Find((PersistentProcessorData x) => x.nameOfScene == barn.barnID);
            Inventory inventory = new Inventory();
            inventory.LoadInventoryFromData(value.inputInv);
            if (inventory.IsEmpty())
            {
                return true;
            }

            // Iterate troughs in current scene as well (these aren't synched to the persistent structs yet)
            Trough[] troughs = GameObject.FindObjectsOfType<Trough>();
            foreach (var trough in troughs)
            {
                if (trough.IsEmpty())
                {
                    return true;
                }
            }
        }
        return false;
    }
    // Update UI when gifting on a birthday.
    [HarmonyPatch(typeof(CharacterManager), "CharacterRecievedGift")]
    [HarmonyPostfix]
    static void AfterCharacterRecievedGift(CharacterManager __instance, int characterID)
    {
        Character birthdayChar = __instance.CharacterBdayToday();
        if (characterID == birthdayChar?.ID)
        {
            isDirty = true;
        }
    }
}