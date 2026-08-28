using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace NuclearOption.SimpleMissileBombTrackerTacMapTrails;

internal sealed class MapTrailGraphic : MaskableGraphic
{
    private readonly Vector2[] _points = new Vector2[Track.MaximumPoints];
    private readonly Vector2[] _offsets = new Vector2[Track.MaximumPoints];
    private int _pointCount;
    private int _trackVersion = -1;
    private float _mapFactor = float.NaN;
    private float _width = 1f;

    internal void SetTrail(Track track, float mapFactor, float width, Color trailColor)
    {
        int pointCount = Mathf.Min(track.Points.Count, Track.MaximumPoints);
        bool geometryChanged =
            _trackVersion != track.PointsVersion ||
            _pointCount != pointCount ||
            !Mathf.Approximately(_mapFactor, mapFactor) ||
            !Mathf.Approximately(_width, width);

        if (geometryChanged)
        {
            _pointCount = pointCount;
            _trackVersion = track.PointsVersion;
            _mapFactor = mapFactor;
            _width = Mathf.Max(0.01f, width);

            for (int index = 0; index < pointCount; index++)
            {
                Vector3 world = track.Points[index].AsVector3() * mapFactor;
                _points[index] = new Vector2(world.x, world.z);
            }

            SetVerticesDirty();
        }

        if (color != trailColor)
        {
            color = trailColor;
        }
    }

    protected override void OnPopulateMesh(VertexHelper vertexHelper)
    {
        vertexHelper.Clear();
        if (_pointCount < 2)
        {
            return;
        }

        float halfWidth = _width * 0.5f;
        for (int index = 0; index < _pointCount; index++)
        {
            _offsets[index] = CalculateJoinOffset(index, halfWidth);
        }

        Color32 vertexColor = color;
        for (int index = 0; index < _pointCount; index++)
        {
            AddVertex(vertexHelper, _points[index] - _offsets[index], vertexColor);
            AddVertex(vertexHelper, _points[index] + _offsets[index], vertexColor);
        }

        for (int index = 0; index < _pointCount - 1; index++)
        {
            int start = index * 2;
            int end = start + 2;
            vertexHelper.AddTriangle(start, start + 1, end + 1);
            vertexHelper.AddTriangle(start, end + 1, end);
        }
    }

    private Vector2 CalculateJoinOffset(int index, float halfWidth)
    {
        Vector2 incoming = index > 0
            ? NormalizedDirection(_points[index - 1], _points[index])
            : NormalizedDirection(_points[index], _points[index + 1]);
        Vector2 outgoing = index < _pointCount - 1
            ? NormalizedDirection(_points[index], _points[index + 1])
            : incoming;

        if (incoming == Vector2.zero)
        {
            incoming = outgoing;
        }

        if (outgoing == Vector2.zero)
        {
            outgoing = incoming;
        }

        Vector2 incomingNormal = new(-incoming.y, incoming.x);
        Vector2 tangent = incoming + outgoing;
        if (tangent.sqrMagnitude < 0.0001f)
        {
            return incomingNormal * halfWidth;
        }

        tangent.Normalize();
        Vector2 miter = new(-tangent.y, tangent.x);
        float denominator = Mathf.Abs(Vector2.Dot(miter, incomingNormal));
        float miterLength = halfWidth / Mathf.Max(0.35f, denominator);
        return miter * Mathf.Min(miterLength, halfWidth * 2f);
    }

    private static Vector2 NormalizedDirection(Vector2 start, Vector2 end)
    {
        Vector2 direction = end - start;
        return direction.sqrMagnitude < 0.0001f
            ? Vector2.zero
            : direction.normalized;
    }

    private static void AddVertex(VertexHelper vertexHelper, Vector2 position, Color32 vertexColor)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.color = vertexColor;
        vertex.position = position;
        vertexHelper.AddVert(vertex);
    }
}

internal sealed class TacMapTrailRenderer : MonoBehaviour
{
    private GameObject _root;
    private RectTransform _rootTransform;
    private Transform _rootParent;
    private static Sprite _markerSprite;

    private void LateUpdate()
    {
        try
        {
            Tick();
        }
        catch (Exception exception)
        {
            SimpleMissileBombTrackerPlugin.Log.LogError($"Tac-map trail rendering failed: {exception}");
        }
    }

    private void OnDestroy()
    {
        if (_root != null)
        {
            Destroy(_root);
        }

        if (_markerSprite != null)
        {
            Texture texture = _markerSprite.texture;
            Destroy(_markerSprite);
            if (texture != null)
            {
                Destroy(texture);
            }

            _markerSprite = null;
        }
    }

    private void Tick()
    {
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        if (map == null || map.iconLayer == null || map.mapImage == null)
        {
            SetRootActive(false);
            return;
        }

        EnsureRoot(map);
        if (_rootTransform == null)
        {
            return;
        }

        bool visible =
            SimpleMissileBombTrackerPlugin.Enabled.Value &&
            map.gameObject.activeInHierarchy &&
            map.iconLayer.activeInHierarchy &&
            map.mapDisplayFactor > 0.000001f;
        SetRootActive(visible);
        if (!visible)
        {
            return;
        }

        float mapScale = Mathf.Abs(map.mapImage.transform.localScale.x);
        if (mapScale < 0.0001f)
        {
            mapScale = 1f;
        }

        float inverseScale = 1f / mapScale;
        float mapFactor = map.mapDisplayFactor;
        float trailWidth = SimpleMissileBombTrackerPlugin.TrailThickness.Value * inverseScale;
        float markerSize = SimpleMissileBombTrackerPlugin.MarkerSize.Value * inverseScale;
        float now = Time.time;
        float trailLifetime = SimpleMissileBombTrackerPlugin.MapTrailLifetime.Value;
        float markerLifetime = SimpleMissileBombTrackerPlugin.MarkerLifetimeSeconds.Value;
        Color trailColor = SimpleMissileBombTrackerPlugin.TrailColor.Value;

        foreach (Track track in MarkerStore.Tracks)
        {
            float timeSinceImpact = track.Impacted ? now - track.ImpactTime : 0f;
            bool showTrail =
                track.Points.Count >= 2 &&
                (!track.Impacted || timeSinceImpact <= trailLifetime);
            if (showTrail)
            {
                EnsureTrail(track);
                track.TrailObject.SetActive(true);
                track.TrailGraphic.SetTrail(track, mapFactor, trailWidth, trailColor);
            }
            else if (track.TrailObject != null)
            {
                track.TrailObject.SetActive(false);
            }

            bool showMarker =
                track.ShowMarker &&
                track.Impacted &&
                timeSinceImpact <= markerLifetime;
            if (showMarker)
            {
                EnsureMarker(track);
                GlobalPosition end = track.CurrentEnd();
                Vector3 world = end.AsVector3() * mapFactor;
                track.MarkerObject.SetActive(true);
                track.MarkerImage.color = MarkerColor(track.Outcome);
                track.MarkerTransform.localPosition = new Vector3(world.x, world.z, 0f);
                track.MarkerTransform.localEulerAngles = Vector3.zero;
                track.MarkerTransform.localScale = Vector3.one * markerSize;
            }
            else if (track.MarkerObject != null)
            {
                track.MarkerObject.SetActive(false);
            }
        }
    }

    private void EnsureRoot(DynamicMap map)
    {
        Transform desiredParent = map.iconLayer.transform;
        if (_root != null &&
            _rootTransform != null &&
            _rootParent == desiredParent)
        {
            return;
        }

        if (_root != null)
        {
            Destroy(_root);
        }

        _root = new GameObject("SimpleMissileBombTracker_TacMapRoot");
        _rootTransform = _root.AddComponent<RectTransform>();
        _rootTransform.SetParent(desiredParent, false);
        _rootTransform.anchorMin = Vector2.zero;
        _rootTransform.anchorMax = Vector2.one;
        _rootTransform.pivot = new Vector2(0.5f, 0.5f);
        _rootTransform.offsetMin = Vector2.zero;
        _rootTransform.offsetMax = Vector2.zero;
        _rootTransform.localPosition = Vector3.zero;
        _rootTransform.localRotation = Quaternion.identity;
        _rootTransform.localScale = Vector3.one;
        _rootTransform.SetSiblingIndex(0);
        _rootParent = desiredParent;

        foreach (Track track in MarkerStore.Tracks)
        {
            track.MarkerObject = null;
            track.MarkerTransform = null;
            track.MarkerImage = null;
            track.TrailObject = null;
            track.TrailGraphic = null;
        }
    }

    private void EnsureTrail(Track track)
    {
        if (track.TrailObject != null && track.TrailGraphic != null)
        {
            return;
        }

        track.TrailObject = new GameObject("SimpleMissileBombTracker_Trail");
        RectTransform trailTransform = track.TrailObject.AddComponent<RectTransform>();
        trailTransform.SetParent(_rootTransform, false);
        trailTransform.anchorMin = Vector2.zero;
        trailTransform.anchorMax = Vector2.one;
        trailTransform.pivot = new Vector2(0.5f, 0.5f);
        trailTransform.offsetMin = Vector2.zero;
        trailTransform.offsetMax = Vector2.zero;
        trailTransform.localPosition = Vector3.zero;
        trailTransform.localRotation = Quaternion.identity;
        trailTransform.localScale = Vector3.one;
        trailTransform.SetSiblingIndex(0);
        track.TrailGraphic = track.TrailObject.AddComponent<MapTrailGraphic>();
        track.TrailGraphic.raycastTarget = false;
    }

    private void EnsureMarker(Track track)
    {
        if (track.MarkerObject != null && track.MarkerImage != null)
        {
            return;
        }

        track.MarkerObject = new GameObject("SimpleMissileBombTracker_ImpactMarker");
        track.MarkerTransform = track.MarkerObject.AddComponent<RectTransform>();
        track.MarkerTransform.SetParent(_rootTransform, false);
        track.MarkerTransform.anchorMin = new Vector2(0.5f, 0.5f);
        track.MarkerTransform.anchorMax = new Vector2(0.5f, 0.5f);
        track.MarkerTransform.pivot = new Vector2(0.5f, 0.5f);
        track.MarkerTransform.sizeDelta = Vector2.one;
        track.MarkerImage = track.MarkerObject.AddComponent<Image>();
        track.MarkerImage.sprite = GetMarkerSprite();
        track.MarkerImage.type = Image.Type.Simple;
        track.MarkerImage.raycastTarget = false;
    }

    private static Color MarkerColor(ShotOutcome outcome)
    {
        return outcome switch
        {
            ShotOutcome.Hit => SimpleMissileBombTrackerPlugin.MapHitColor.Value,
            ShotOutcome.Intercepted => SimpleMissileBombTrackerPlugin.MapInterceptedColor.Value,
            _ => SimpleMissileBombTrackerPlugin.MapFailedColor.Value
        };
    }

    private static Sprite GetMarkerSprite()
    {
        if (_markerSprite != null)
        {
            return _markerSprite;
        }

        const int textureSize = 64;
        Texture2D texture = new(textureSize, textureSize, TextureFormat.RGBA32, false)
        {
            wrapMode = TextureWrapMode.Clamp,
            filterMode = FilterMode.Bilinear,
            name = "SimpleMissileBombTracker_MarkerTexture"
        };

        Color32 transparent = new(255, 255, 255, 0);
        Color32 solid = new(255, 255, 255, 255);
        Color32[] pixels = new Color32[textureSize * textureSize];
        for (int y = 0; y < textureSize; y++)
        {
            for (int x = 0; x < textureSize; x++)
            {
                bool onMarker =
                    Mathf.Abs(x - y) <= 5 ||
                    Mathf.Abs(x - (textureSize - 1 - y)) <= 5;
                pixels[y * textureSize + x] = onMarker ? solid : transparent;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        _markerSprite = Sprite.Create(
            texture,
            new Rect(0f, 0f, textureSize, textureSize),
            new Vector2(0.5f, 0.5f),
            100f);
        _markerSprite.name = "SimpleMissileBombTracker_MarkerSprite";
        return _markerSprite;
    }

    private void SetRootActive(bool active)
    {
        if (_root != null && _root.activeSelf != active)
        {
            _root.SetActive(active);
        }
    }
}
