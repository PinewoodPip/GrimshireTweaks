
using HarmonyLib;

public static class LateNotification
{
    static int LATE_WARNING_HOUR = 22;
    static int LATE_WARNING_MINUTE = 0;

    // Show notification when it's getting late in the day (so you don't forget to sleep lmao happened to me once)
    [HarmonyPatch(typeof(TimeCompassUI), "SetTimeText")]
    [HarmonyPrefix]
    static bool ShowLateNotification()
    {
        int hours = (int)TimeControl.Instance.Minutes / 60;
        int minutes = (int)TimeControl.Instance.Minutes % 60;
        if (hours == LATE_WARNING_HOUR && minutes == LATE_WARNING_MINUTE)
        {
            GameManager.Instance.PopUpDialogBox.DisplayMsg("It's getting late...", 2f);
        }
        return true;
    }
}