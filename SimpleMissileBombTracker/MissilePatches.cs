using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace NuclearOption.SimpleMissileBombTrackerTacMapTrails;

internal static class MissilePatches
{
    internal static void Install(Harmony harmony)
    {
        PatchPostfix(
            harmony,
            AccessTools.Method(typeof(Missile), "StartMissile"),
            nameof(OnMissileStarted),
            "missile and bomb launch tracking");

        PatchPrefix(
            harmony,
            AccessTools.Method(
                typeof(Missile),
                nameof(Missile.TakeDamage),
                new[]
                {
                    typeof(float),
                    typeof(float),
                    typeof(float),
                    typeof(float),
                    typeof(float),
                    typeof(PersistentID)
                }),
            nameof(OnMissileDamaged),
            "interception detection");

        PatchPostfix(
            harmony,
            FindDetonationReceiver(),
            nameof(OnMissileDetonated),
            "impact detection");

        PatchPostfix(
            harmony,
            AccessTools.Method(typeof(GameManager), nameof(GameManager.ResetGame)),
            nameof(OnGameReset),
            "mission reset cleanup");
    }

    private static void OnMissileStarted(Missile __instance)
    {
        try
        {
            if (!SimpleMissileBombTrackerPlugin.Enabled.Value ||
                __instance == null ||
                !GameManager.GetLocalAircraft(out Aircraft aircraft) ||
                aircraft == null)
            {
                return;
            }

            bool ownedByPlayer = PlayerWeaponOwnership.IsFromLocalAircraft(
                __instance,
                aircraft,
                out int parentMissileCount);
            if (SimpleMissileBombTrackerPlugin.VerboseLogging.Value)
            {
                SimpleMissileBombTrackerPlugin.Log.LogInfo(
                    $"Launch '{__instance.unitName}': ownedByPlayer={ownedByPlayer}, " +
                    $"parentMissiles={parentMissileCount}.");
            }

            if (ownedByPlayer)
            {
                string reason = parentMissileCount > 0 ? "submunition launch" : "launch";
                TrackFactory.StartTracking(__instance, reason);
            }
        }
        catch (Exception exception)
        {
            SimpleMissileBombTrackerPlugin.Log.LogError(
                $"Launch tracking hook failed: {exception}");
        }
    }

    private static void OnMissileDamaged(
        Missile __instance,
        float pierceDamage,
        float blastDamage,
        float amountAffected,
        float fireDamage,
        PersistentID dealerID)
    {
        try
        {
            if (__instance == null ||
                !MarkerStore.ByMissile.TryGetValue(__instance, out Track track) ||
                !dealerID.IsValid ||
                dealerID == __instance.persistentID ||
                dealerID == __instance.ownerID)
            {
                return;
            }

            ArmorProperties armor = __instance.GetArmorProperties();
            bool effectiveDamage = armor != null &&
                (pierceDamage > armor.pierceArmor ||
                 (amountAffected > 0f && blastDamage > armor.blastArmor) ||
                 fireDamage > armor.fireArmor);
            if (!effectiveDamage)
            {
                return;
            }

            if (!track.Damaged && SimpleMissileBombTrackerPlugin.VerboseLogging.Value)
            {
                SimpleMissileBombTrackerPlugin.Log.LogInfo(
                    $"'{__instance.unitName}' received external damage and may be intercepted.");
            }

            track.Damaged = true;
        }
        catch (Exception exception)
        {
            SimpleMissileBombTrackerPlugin.Log.LogError(
                $"Interception tracking hook failed: {exception}");
        }
    }

    private static void OnMissileDetonated(
        Missile __instance,
        bool useUnit,
        bool armed,
        bool hitArmor)
    {
        try
        {
            if (__instance == null ||
                !MarkerStore.ByMissile.TryGetValue(__instance, out Track track))
            {
                return;
            }

            GlobalPosition impact = GlobalPositionExtensions.ToGlobalPosition(__instance.transform.position);
            track.Impacted = true;
            track.Impact = impact;
            track.ImpactTime = Time.time;
            track.AddFinalPoint(impact);
            track.Outcome = ClassifyOutcome(track, armed, hitArmor, useUnit);
            track.EndedByDetonation = true;
            track.ShowMarker = true;
            MarkerStore.RemoveMissileEntry(__instance, track);
            track.Missile = null;

            if (SimpleMissileBombTrackerPlugin.VerboseLogging.Value)
            {
                SimpleMissileBombTrackerPlugin.Log.LogInfo(
                    $"'{__instance.unitName}' ended as {track.Outcome} at {impact.AsVector3()} " +
                    $"(armed={armed}, hitArmor={hitArmor}, useUnit={useUnit}, damaged={track.Damaged}).");
            }
        }
        catch (Exception exception)
        {
            SimpleMissileBombTrackerPlugin.Log.LogError(
                $"Impact tracking hook failed: {exception}");
        }
    }

    private static void OnGameReset()
    {
        MarkerStore.ClearAll();
    }

    private static ShotOutcome ClassifyOutcome(
        Track track,
        bool armed,
        bool hitArmor,
        bool useUnit)
    {
        if (!armed)
        {
            return ShotOutcome.Failed;
        }

        if (hitArmor || useUnit)
        {
            return ShotOutcome.Hit;
        }

        return track.Damaged ? ShotOutcome.Intercepted : ShotOutcome.Failed;
    }

    private static MethodInfo FindDetonationReceiver()
    {
        Type[] expectedParameters =
        {
            typeof(Unit),
            typeof(bool),
            typeof(Vector3),
            typeof(bool),
            typeof(bool),
            typeof(bool),
            typeof(Vector3)
        };

        return typeof(Missile)
            .GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .FirstOrDefault(method =>
                method.Name.StartsWith("UserCode_RpcDetonate_", StringComparison.Ordinal) &&
                method.GetParameters().Select(parameter => parameter.ParameterType)
                    .SequenceEqual(expectedParameters));
    }

    private static void PatchPrefix(
        Harmony harmony,
        MethodBase original,
        string patchName,
        string featureName)
    {
        Patch(harmony, original, patchName, featureName, prefix: true);
    }

    private static void PatchPostfix(
        Harmony harmony,
        MethodBase original,
        string patchName,
        string featureName)
    {
        Patch(harmony, original, patchName, featureName, prefix: false);
    }

    private static void Patch(
        Harmony harmony,
        MethodBase original,
        string patchName,
        string featureName,
        bool prefix)
    {
        if (original == null)
        {
            SimpleMissileBombTrackerPlugin.Log.LogWarning(
                $"Could not find the game method for {featureName}. That part of the mod is disabled.");
            return;
        }

        MethodInfo patch = AccessTools.Method(typeof(MissilePatches), patchName);
        HarmonyMethod harmonyPatch = new(patch);
        try
        {
            if (prefix)
            {
                harmony.Patch(original, prefix: harmonyPatch);
            }
            else
            {
                harmony.Patch(original, postfix: harmonyPatch);
            }
        }
        catch (Exception exception)
        {
            SimpleMissileBombTrackerPlugin.Log.LogError(
                $"Could not install the hook for {featureName}: {exception}");
        }
    }
}
