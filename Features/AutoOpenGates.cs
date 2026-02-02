
using System.Linq;
using HarmonyLib;
using UnityEngine;
using static GrimshireTweaks.Utils;

public static class AutoOpenGates
{
    public static float DISTANCE_THRESHOLD = 1.0f;

    // Auto-open gates when the player comes close to them.
    [HarmonyPatch(typeof(Gate), "Start")]
    [HarmonyPostfix]
    static void OnGateCreated(Gate __instance)
    {
        __instance.gameObject.AddComponent<GateAutoOpener>();
    }

    // Auxiliary component to handle gate auto-opening.
    public class GateAutoOpener : MonoBehaviour
    {
        Gate gate;
        bool wasPlayerInside = false;

        void Start()
        {
            gate = GetComponent<Gate>();
        }

        void Update()
        {
            // Check if the player moved in/out of range
            Collider2D[] hitColliders = Physics2D.OverlapCircleAll(transform.position, DISTANCE_THRESHOLD);
            bool isInside = hitColliders.Any(collider => collider.CompareTag("Player"));
            if (isInside == wasPlayerInside) return;

            // Toggle gate state
            if ((isInside && gate.isClosed) || (!isInside && !gate.isClosed))
            {
                CallMethod(gate, "ToggleOpenStatus");
            }

            wasPlayerInside = isInside;
        }
    }
}