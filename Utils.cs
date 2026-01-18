using HarmonyLib;

namespace GrimshireTweaks;

public static class Utils
{
    // Utility method to read private fields.
    public static T GetField<T>(object obj, string fieldName)
    {
        var field = Traverse.Create(obj).Field(fieldName);
        return field.GetValue<T>();
    }

    // Utility method to write private fields.
    public static void SetField<T>(object obj, string fieldName, T value)
    {
        var field = Traverse.Create(obj).Field(fieldName);
        field.SetValue(value);
    }

    // Utility method to call private methods.
    public static T CallMethod<T>(object obj, string methodName, params object[] parameters)
    {
        var method = Traverse.Create(obj).Method(methodName, parameters);
        return method.GetValue<T>();
    }
    public static void CallMethod(object obj, string methodName, params object[] parameters)
    {
        var method = Traverse.Create(obj).Method(methodName, parameters);
        method.GetValue();
    }
}