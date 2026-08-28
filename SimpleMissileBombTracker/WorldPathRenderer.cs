using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace NuclearOption.SimpleMissileBombTrackerTacMapTrails;

internal sealed class WorldPathRenderer : MonoBehaviour
{
    internal const int MaximumLinePool = 15;

    private const int GradientKeys = 8;
    private const float PulseHalfWidth = 0.045f;
    private const float PulsePlateauHalfWidth = 0.026f;
    private const float PulseSpeed = 0.375f;

    private readonly Vector3[] _pointBuffer = new Vector3[Track.MaximumPoints];
    private readonly GradientColorKey[] _colorKeys = new GradientColorKey[GradientKeys];
    private readonly GradientAlphaKey[] _alphaKeys = new GradientAlphaKey[GradientKeys];
    private readonly Keyframe[] _widthKeys = new Keyframe[GradientKeys];

    private LineRenderer[] _lines;
    private Gradient[] _gradients;
    private AnimationCurve[] _widthCurves;
    private float _pulsePosition;
    private float _firstPulse;
    private float _secondPulse;

    private static Material _material;
    private static bool _materialResolved;

    private void LateUpdate()
    {
        try
        {
            Tick();
        }
        catch (Exception exception)
        {
            SimpleMissileBombTrackerPlugin.Log.LogError($"3D path rendering failed: {exception}");
        }
    }

    private void OnDestroy()
    {
        if (_material != null)
        {
            Destroy(_material);
            _material = null;
        }

        _materialResolved = false;
    }

    private void Tick()
    {
        if (!SimpleMissileBombTrackerPlugin.Enabled.Value ||
            !SimpleMissileBombTrackerPlugin.WorldPathsEnabled.Value)
        {
            HideAll();
            return;
        }

        Material material = ResolveMaterial();
        if (material == null)
        {
            HideAll();
            return;
        }

        EnsurePool(material);

        float now = Time.time;
        float baseWidth = Mathf.Max(0.02f, SimpleMissileBombTrackerPlugin.WorldLineWidth.Value);
        float pulseWidth = Mathf.Max(baseWidth, SimpleMissileBombTrackerPlugin.WorldPulseWidth.Value);
        int maximumVisible = Mathf.Clamp(
            SimpleMissileBombTrackerPlugin.MaximumWorldPaths.Value,
            1,
            MaximumLinePool);

        _pulsePosition += Time.deltaTime * PulseSpeed;
        if (_pulsePosition >= 1f)
        {
            _pulsePosition -= 1f;
        }

        float secondPosition = _pulsePosition + 0.5f;
        if (secondPosition >= 1f)
        {
            secondPosition -= 1f;
        }

        _firstPulse = Mathf.Clamp(Mathf.Min(_pulsePosition, secondPosition), 0.055f, 0.945f);
        _secondPulse = Mathf.Clamp(Mathf.Max(_pulsePosition, secondPosition), 0.055f, 0.945f);

        int usedLines = 0;
        List<Track> tracks = MarkerStore.Tracks;
        for (int trackIndex = tracks.Count - 1;
             trackIndex >= 0 && usedLines < maximumVisible;
             trackIndex--)
        {
            Track track = tracks[trackIndex];
            if (track.Impacted)
            {
                float lifetime = track.Outcome == ShotOutcome.Hit
                    ? SimpleMissileBombTrackerPlugin.WorldHitLifetime.Value
                    : SimpleMissileBombTrackerPlugin.WorldMissLifetime.Value;
                if (now - track.ImpactTime > lifetime)
                {
                    continue;
                }
            }

            int pointCount = BuildPath(track);
            if (pointCount < 2)
            {
                continue;
            }

            LineRenderer line = _lines[usedLines];
            Color pathColor;
            float renderedWidth;
            bool pulsing;
            bool widthPulse;

            if (track.Alive)
            {
                pathColor = SimpleMissileBombTrackerPlugin.WorldInFlightColor.Value;
                renderedWidth = baseWidth;
                pulsing = true;
                widthPulse = true;
            }
            else if (track.ProducedSubmunitions &&
                     !track.EndedByDetonation &&
                     !track.ShowMarker)
            {
                pathColor = SimpleMissileBombTrackerPlugin.WorldInFlightColor.Value;
                renderedWidth = baseWidth;
                pulsing = false;
                widthPulse = false;
            }
            else if (track.Outcome == ShotOutcome.Hit)
            {
                pathColor = SimpleMissileBombTrackerPlugin.WorldHitColor.Value;
                renderedWidth = pulseWidth;
                pulsing = true;
                widthPulse = false;
            }
            else if (track.Outcome == ShotOutcome.Intercepted)
            {
                pathColor = SimpleMissileBombTrackerPlugin.WorldInterceptedColor.Value;
                renderedWidth = baseWidth;
                pulsing = false;
                widthPulse = false;
            }
            else
            {
                pathColor = SimpleMissileBombTrackerPlugin.WorldFailedColor.Value;
                renderedWidth = baseWidth;
                pulsing = false;
                widthPulse = false;
            }

            line.positionCount = pointCount;
            for (int pointIndex = 0; pointIndex < pointCount; pointIndex++)
            {
                line.SetPosition(pointIndex, _pointBuffer[pointIndex]);
            }

            ApplyGradient(_gradients[usedLines], line, pathColor, pulsing);
            ApplyWidth(
                _widthCurves[usedLines],
                line,
                renderedWidth,
                pulseWidth,
                widthPulse);
            if (!line.gameObject.activeSelf)
            {
                line.gameObject.SetActive(true);
            }

            usedLines++;
        }

        for (int index = usedLines; index < MaximumLinePool; index++)
        {
            if (_lines[index].gameObject.activeSelf)
            {
                _lines[index].gameObject.SetActive(false);
            }
        }
    }

    private int BuildPath(Track track)
    {
        int pointCount = Mathf.Min(track.Points.Count, Track.MaximumPoints);
        for (int index = 0; index < pointCount; index++)
        {
            _pointBuffer[index] = GlobalPositionExtensions.ToLocalPosition(track.Points[index]);
        }

        return pointCount;
    }

    private void ApplyGradient(Gradient gradient, LineRenderer line, Color baseColor, bool pulsing)
    {
        float baseAlpha = Mathf.Clamp01(SimpleMissileBombTrackerPlugin.WorldLineOpacity.Value);
        if (pulsing)
        {
            Color pulseColor = SimpleMissileBombTrackerPlugin.WorldPulseColor.Value;
            float pulseAlpha = Mathf.Clamp01(SimpleMissileBombTrackerPlugin.WorldPulseOpacity.Value);
            SetColorKey(0, _firstPulse - PulseHalfWidth, baseColor, baseAlpha);
            SetColorKey(1, _firstPulse - PulsePlateauHalfWidth, pulseColor, pulseAlpha);
            SetColorKey(2, _firstPulse + PulsePlateauHalfWidth, pulseColor, pulseAlpha);
            SetColorKey(3, _firstPulse + PulseHalfWidth, baseColor, baseAlpha);
            SetColorKey(4, _secondPulse - PulseHalfWidth, baseColor, baseAlpha);
            SetColorKey(5, _secondPulse - PulsePlateauHalfWidth, pulseColor, pulseAlpha);
            SetColorKey(6, _secondPulse + PulsePlateauHalfWidth, pulseColor, pulseAlpha);
            SetColorKey(7, _secondPulse + PulseHalfWidth, baseColor, baseAlpha);
        }
        else
        {
            for (int index = 0; index < GradientKeys; index++)
            {
                SetColorKey(index, (float)index / (GradientKeys - 1), baseColor, baseAlpha);
            }
        }

        gradient.SetKeys(_colorKeys, _alphaKeys);
        line.colorGradient = gradient;
    }

    private void ApplyWidth(
        AnimationCurve widthCurve,
        LineRenderer line,
        float baseWidth,
        float pulseWidth,
        bool widthPulse)
    {
        if (widthPulse)
        {
            _widthKeys[0] = WidthKey(_firstPulse - PulseHalfWidth, baseWidth);
            _widthKeys[1] = WidthKey(_firstPulse - PulsePlateauHalfWidth, pulseWidth);
            _widthKeys[2] = WidthKey(_firstPulse + PulsePlateauHalfWidth, pulseWidth);
            _widthKeys[3] = WidthKey(_firstPulse + PulseHalfWidth, baseWidth);
            _widthKeys[4] = WidthKey(_secondPulse - PulseHalfWidth, baseWidth);
            _widthKeys[5] = WidthKey(_secondPulse - PulsePlateauHalfWidth, pulseWidth);
            _widthKeys[6] = WidthKey(_secondPulse + PulsePlateauHalfWidth, pulseWidth);
            _widthKeys[7] = WidthKey(_secondPulse + PulseHalfWidth, baseWidth);
        }
        else
        {
            for (int index = 0; index < GradientKeys; index++)
            {
                _widthKeys[index] = WidthKey(
                    (float)index / (GradientKeys - 1),
                    baseWidth);
            }
        }

        widthCurve.keys = _widthKeys;
        line.widthCurve = widthCurve;
        line.widthMultiplier = 1f;
    }

    private void SetColorKey(int index, float time, Color keyColor, float alpha)
    {
        _colorKeys[index].color = keyColor;
        _colorKeys[index].time = time;
        _alphaKeys[index].alpha = alpha;
        _alphaKeys[index].time = time;
    }

    private static Keyframe WidthKey(float time, float value)
    {
        return new Keyframe(time, value, 0f, 0f);
    }

    private void HideAll()
    {
        if (_lines == null)
        {
            return;
        }

        foreach (LineRenderer line in _lines)
        {
            if (line != null && line.gameObject.activeSelf)
            {
                line.gameObject.SetActive(false);
            }
        }
    }

    private void EnsurePool(Material material)
    {
        if (_lines != null)
        {
            return;
        }

        _lines = new LineRenderer[MaximumLinePool];
        _gradients = new Gradient[MaximumLinePool];
        _widthCurves = new AnimationCurve[MaximumLinePool];

        for (int index = 0; index < MaximumLinePool; index++)
        {
            GameObject lineObject = new($"SimpleMissileBombTracker_3DPath_{index}");
            lineObject.transform.SetParent(transform, false);
            LineRenderer line = lineObject.AddComponent<LineRenderer>();
            line.useWorldSpace = true;
            line.alignment = LineAlignment.View;
            line.numCapVertices = 2;
            line.numCornerVertices = 4;
            line.textureMode = LineTextureMode.Stretch;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.sharedMaterial = material;
            line.positionCount = 0;
            lineObject.SetActive(false);

            _lines[index] = line;
            _gradients[index] = new Gradient();
            _widthCurves[index] = new AnimationCurve();
        }
    }

    private static Material ResolveMaterial()
    {
        if (_materialResolved)
        {
            return _material;
        }

        _materialResolved = true;
        string[] shaderNames =
        {
            "Sprites/Default",
            "UI/Default",
            "Legacy Shaders/Particles/Alpha Blended",
            "Particles/Standard Unlit",
            "Hidden/Internal-Colored",
            "Unlit/Color"
        };

        foreach (string shaderName in shaderNames)
        {
            Shader shader = Shader.Find(shaderName);
            if (shader == null)
            {
                continue;
            }

            _material = new Material(shader)
            {
                name = "SimpleMissileBombTracker_3DPathMaterial"
            };
            SimpleMissileBombTrackerPlugin.Log.LogInfo(
                $"3D paths are using shader '{shaderName}'.");
            return _material;
        }

        SimpleMissileBombTrackerPlugin.Log.LogWarning(
            "No compatible line shader was found. Optional 3D paths are unavailable; tac-map trails still work.");
        return null;
    }
}
