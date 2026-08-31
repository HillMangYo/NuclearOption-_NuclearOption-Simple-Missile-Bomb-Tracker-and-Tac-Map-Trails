using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace NuclearOption.SimpleMissileBombTrackerTacMapTrails;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class SimpleMissileBombTrackerPlugin : BaseUnityPlugin
{
    public const string PluginGuid = "nuclearoption.simplemissilebombtrackerandtacmaptrails";
    public const string PluginName = "Simple Missile & Bomb Tracker + Tac Map Trails";
    public const string PluginVersion = "1.0.1";

    internal static ManualLogSource Log;

    internal static ConfigEntry<bool> Enabled;
    internal static ConfigEntry<float> MarkerLifetimeSeconds;
    internal static ConfigEntry<float> MarkerSize;
    internal static ConfigEntry<Color> TrailColor;
    internal static ConfigEntry<float> TrailThickness;
    internal static ConfigEntry<float> PathSmoothingWindow;
    internal static ConfigEntry<float> MapTrailLifetime;
    internal static ConfigEntry<Color> MapHitColor;
    internal static ConfigEntry<Color> MapInterceptedColor;
    internal static ConfigEntry<Color> MapFailedColor;
    internal static ConfigEntry<bool> WorldPathsEnabled;
    internal static ConfigEntry<int> MaximumWorldPaths;
    internal static ConfigEntry<float> WorldLineWidth;
    internal static ConfigEntry<float> WorldPulseWidth;
    internal static ConfigEntry<float> WorldLineOpacity;
    internal static ConfigEntry<float> WorldPulseOpacity;
    internal static ConfigEntry<float> WorldHitLifetime;
    internal static ConfigEntry<float> WorldMissLifetime;
    internal static ConfigEntry<Color> WorldInFlightColor;
    internal static ConfigEntry<Color> WorldPulseColor;
    internal static ConfigEntry<Color> WorldHitColor;
    internal static ConfigEntry<Color> WorldInterceptedColor;
    internal static ConfigEntry<Color> WorldFailedColor;
    internal static ConfigEntry<bool> VerboseLogging;

    private Harmony _harmony;

    private void Awake()
    {
        Log = Logger;

        Enabled = Config.Bind(
            "General",
            "Mod Enabled",
            true,
            "Turn the entire mod on or off.");
        MarkerLifetimeSeconds = Config.Bind(
            "Tactical Map",
            "Impact Marker Duration (seconds)",
            135f,
            new ConfigDescription(
                "How long hit, intercepted, and failed markers remain after the shot ends.",
                new AcceptableValueRange<float>(5f, 1200f)));
        MapTrailLifetime = Config.Bind(
            "Tactical Map",
            "Trail Duration After Impact (seconds)",
            35f,
            new ConfigDescription(
                "How long the flight trail remains after impact or interception. Trails remain visible while a shot is in flight.",
                new AcceptableValueRange<float>(1f, 1200f)));
        MarkerSize = Config.Bind(
            "Tactical Map",
            "Impact Marker Size (pixels)",
            6.4f,
            new ConfigDescription(
                "On-screen size of the impact X.",
                new AcceptableValueRange<float>(2f, 48f)));
        TrailColor = Config.Bind(
            "Tactical Map",
            "Trail Color",
            new Color(0.6f, 0.6f, 0.6f, 0.425f),
            "Color of missile and bomb trails on the tactical map and cockpit radar.");
        TrailThickness = Config.Bind(
            "Tactical Map",
            "Trail Thickness (pixels)",
            0.75f,
            new ConfigDescription(
                "On-screen thickness of tactical-map and radar trails.",
                new AcceptableValueRange<float>(0.1f, 10f)));
        PathSmoothingWindow = Config.Bind(
            "Tracking",
            "Path Smoothing Window (seconds)",
            1f,
            new ConfigDescription(
                "How much recent movement is averaged to make missile and bomb trails smooth. The exact launch and final impact positions are preserved.",
                new AcceptableValueRange<float>(0.15f, 2f)));
        MapHitColor = Config.Bind(
            "Tactical Map",
            "Hit Marker Color",
            new Color(0.15f, 0.6f, 1f, 0.7f),
            "Marker color for a confirmed hit.");
        MapInterceptedColor = Config.Bind(
            "Tactical Map",
            "Intercepted Marker Color",
            new Color(0.72f, 0.25f, 1f, 0.7f),
            "Marker color when a missile or bomb is intercepted.");
        MapFailedColor = Config.Bind(
            "Tactical Map",
            "Failed Marker Color",
            new Color(0.6f, 0.6f, 0.6f, 0.7f),
            "Marker color for a miss or failed shot.");

        WorldPathsEnabled = Config.Bind(
            "3D World Paths",
            "Show 3D Paths",
            false,
            "Draw recent flight paths in the 3D world. Disabled by default.");
        MaximumWorldPaths = Config.Bind(
            "3D World Paths",
            "Maximum 3D Paths",
            15,
            new ConfigDescription(
                "Maximum number of recent 3D paths shown at once.",
                new AcceptableValueRange<int>(1, WorldPathRenderer.MaximumLinePool)));
        WorldLineWidth = Config.Bind(
            "3D World Paths",
            "Base Line Width (meters)",
            2f,
            new ConfigDescription(
                "World-space thickness of the base 3D line.",
                new AcceptableValueRange<float>(0.05f, 20f)));
        WorldPulseWidth = Config.Bind(
            "3D World Paths",
            "Pulse Width (meters)",
            4f,
            new ConfigDescription(
                "Width of the bright pulse moving along a live path.",
                new AcceptableValueRange<float>(0.1f, 40f)));
        WorldLineOpacity = Config.Bind(
            "3D World Paths",
            "Base Line Opacity",
            0.85f,
            new ConfigDescription(
                "Opacity of the base 3D path.",
                new AcceptableValueRange<float>(0.05f, 1f)));
        WorldPulseOpacity = Config.Bind(
            "3D World Paths",
            "Pulse Opacity",
            1f,
            new ConfigDescription(
                "Opacity of the pulse moving along a live path.",
                new AcceptableValueRange<float>(0.1f, 1f)));
        WorldHitLifetime = Config.Bind(
            "3D World Paths",
            "Hit Path Duration (seconds)",
            4f,
            new ConfigDescription(
                "How long a successful 3D path remains after impact.",
                new AcceptableValueRange<float>(1f, 60f)));
        WorldMissLifetime = Config.Bind(
            "3D World Paths",
            "Miss or Intercept Path Duration (seconds)",
            60f,
            new ConfigDescription(
                "How long a missed or intercepted 3D path remains.",
                new AcceptableValueRange<float>(1f, 600f)));
        WorldInFlightColor = Config.Bind(
            "3D World Paths",
            "In-Flight Color",
            new Color(0.2f, 1f, 0.35f, 1f),
            "3D path color while a shot is in flight.");
        WorldPulseColor = Config.Bind(
            "3D World Paths",
            "Pulse Color",
            Color.white,
            "Color of the pulse moving along a live 3D path.");
        WorldHitColor = Config.Bind(
            "3D World Paths",
            "Hit Color",
            new Color(0.1f, 0.7f, 1f, 1f),
            "3D path color for a confirmed hit.");
        WorldInterceptedColor = Config.Bind(
            "3D World Paths",
            "Intercepted Color",
            new Color(1f, 0.3f, 0.7f, 1f),
            "3D path color for an intercepted shot.");
        WorldFailedColor = Config.Bind(
            "3D World Paths",
            "Failed Color",
            new Color(0.15f, 0.45f, 0.2f, 1f),
            "3D path color for a miss or failed shot.");
        VerboseLogging = Config.Bind(
            "Debug",
            "Verbose Logging",
            false,
            "Write detailed launch and outcome information to the BepInEx log.");

        _harmony = new Harmony(PluginGuid);
        MissilePatches.Install(_harmony);

        gameObject.AddComponent<ShotTracker>();
        gameObject.AddComponent<TacMapTrailRenderer>();
        gameObject.AddComponent<WorldPathRenderer>();

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded.");
    }

    private void OnDestroy()
    {
        MarkerStore.ClearAll();
        _harmony?.UnpatchSelf();
    }
}
