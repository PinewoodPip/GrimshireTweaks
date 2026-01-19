
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using static GrimshireTweaks.Utils;

public static class OptimizeObjectPlacement
{
    // Mixins and trackers for SquareBoundsChecker optimizations
    class SquareBoundsCheckerMixin
    {
        public Vector3 previousPosition;
    }
    static ConditionalWeakTable<SquareBoundsChecker, SquareBoundsCheckerMixin> squareBoundsCheckerMixinStates = new ConditionalWeakTable<SquareBoundsChecker, SquareBoundsCheckerMixin>();
    static Collider2D[] squareBoundsCheckerCollisionResults = new Collider2D[1];

    // Optimize object placement checks
    // These normally run every frame and lag quite a lot with larger objects (mostly tree seeds),
    // partially due to allocations from Physics2D.OverlapCircle()
    [HarmonyPatch(typeof(SquareBoundsChecker), "Validate")]
    [HarmonyPrefix]
    static bool OptimizeSquareBoundsChecker(SquareBoundsChecker __instance)
    {
        // Skip updating if the position of the checker hasn't changed
        var previousState = squareBoundsCheckerMixinStates.GetOrCreateValue(__instance);
        bool positionChanged = previousState.previousPosition != __instance.transform.position;
        previousState.previousPosition = __instance.transform.position; // Update pos
        if (!positionChanged)
        {
            return false;
        }

        // Run original logic, but with collision checks modified
        // to reduce lag from allocations
        SquareBoundsChecker _this = __instance;
        var CheckIfValidSpot = Traverse.Create(_this).Method("CheckIfValidSpot", new object[] { true });
        Vector3 position = _this.transform.position;

        SetField<float>(__instance, "updateCoolDown", 0.05f);
        SetField<Vector3>(__instance, "myPosition", position);

        // This check was modified to use the NonAlloc variant
        // to reduce lag from allocation + subsequent garbage collection
        int collisionsCount = Physics2D.OverlapCircleNonAlloc(position, 0.25f, squareBoundsCheckerCollisionResults, ~(LayerManager.Instance.PlayerLayer | _this.placeableObjRef.layersToIgnore | LayerManager.Instance.IgnoreLayer));
        CheckIfValidSpot.GetValue(collisionsCount == 0);

        if (_this.placeableObjRef.placeableOnlyInside && _this.validSpot)
        {
            CheckIfValidSpot.GetValue(WeatherSystem.Instance.IsInteriorScene());
        }
        if (_this.placeableObjRef.placeableOnlyOutside && _this.validSpot)
        {
            CheckIfValidSpot.GetValue(!WeatherSystem.Instance.IsInteriorScene());
        }
        if (_this.placeableObjRef.placeableOnlyInWater && _this.validSpot)
        {
            CheckIfValidSpot.GetValue(_this.placeableObjRef.tileMapManagerRef.IsValidFishTrapSpot(position));
        }
        if (_this.placeableObjRef.placeableOnlyInScene != null && _this.validSpot)
        {
            CheckIfValidSpot.GetValue(GameManager.Instance.GetCurrentScene() == _this.placeableObjRef.placeableOnlyInScene);
        }
        if (_this.placeableObjRef.isPatherTile && _this.validSpot)
        {
            CheckIfValidSpot.GetValue(!_this.placeableObjRef.tileMapManagerRef.IsPatherTile(position));
        }
        if (_this.placeableObjRef.isWallItem && _this.validSpot)
        {
            // Also modified to use NonAlloc variant
            collisionsCount = Physics2D.OverlapCircleNonAlloc(position, 0.25f, squareBoundsCheckerCollisionResults, LayerManager.Instance.WallLayer);
            CheckIfValidSpot.GetValue(collisionsCount > 0);
        }
        if (_this.placeableObjRef.isPipe && _this.validSpot)
        {
            CheckIfValidSpot.GetValue(!_this.placeableObjRef.tileMapManagerRef.IsTilePipe(position));
        }

        return false;
    }
}