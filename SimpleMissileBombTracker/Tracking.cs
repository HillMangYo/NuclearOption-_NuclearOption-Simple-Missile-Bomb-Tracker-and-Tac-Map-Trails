using System;
using System.Collections.Generic;
using UnityEngine;

namespace NuclearOption.SimpleMissileBombTrackerTacMapTrails;

internal enum ShotOutcome
{
    InFlight,
    Failed,
    Hit,
    Intercepted
}

internal sealed class Track
{
    internal const int MaximumPoints = 256;

    private const int MaximumSmoothingSamples = 24;
    private const int ProtectedRecentPoints = 8;
    private const float MinimumPointDistance = 5f;
    private const float MinimumForcedPointDistance = 0.25f;

    private readonly struct TimedPosition
    {
        internal TimedPosition(GlobalPosition position, float time)
        {
            Position = position;
            Time = time;
        }

        internal GlobalPosition Position { get; }
        internal float Time { get; }
    }

    internal Missile Missile;
    internal PersistentID WeaponId;
    internal readonly List<GlobalPosition> Points = new(MaximumPoints);
    private readonly List<TimedPosition> _smoothingSamples = new(MaximumSmoothingSamples);
    internal GlobalPosition LastObservedPosition;
    internal int PointsVersion;
    internal float StartTime;
    internal float LastSampleTime;
    internal bool Impacted;
    internal GlobalPosition Impact;
    internal float ImpactTime;
    internal ShotOutcome Outcome;
    internal bool Damaged;
    internal bool ProducedSubmunitions;
    internal bool EndedByDetonation;
    internal bool ShowMarker = true;

    internal GameObject MarkerObject;
    internal RectTransform MarkerTransform;
    internal UnityEngine.UI.Image MarkerImage;
    internal GameObject TrailObject;
    internal MapTrailGraphic TrailGraphic;

    internal bool Alive => !Impacted && Missile != null;

    internal GlobalPosition CurrentEnd()
    {
        if (Impacted)
        {
            return Impact;
        }

        return Points.Count > 0 ? Points[^1] : default;
    }

    internal void InitializePath(
        GlobalPosition launch,
        GlobalPosition current,
        float now)
    {
        Points.Clear();
        _smoothingSamples.Clear();
        Points.Add(launch);

        Vector3 difference = current.AsVector3() - launch.AsVector3();
        if (difference.sqrMagnitude >=
            MinimumForcedPointDistance * MinimumForcedPointDistance)
        {
            Points.Add(current);
        }

        // A recovered missile may be far from its launch point. Preserve that
        // launch connector, but begin live smoothing only at the observed position.
        _smoothingSamples.Add(new TimedPosition(current, now));
        LastObservedPosition = current;
        PointsVersion++;
    }

    internal void AddPoint(GlobalPosition point)
    {
        float now = Time.time;
        LastObservedPosition = point;
        if (_smoothingSamples.Count > 0)
        {
            Vector3 previous = _smoothingSamples[^1].Position.AsVector3();
            Vector3 current = point.AsVector3();
            if ((current - previous).sqrMagnitude <
                MinimumPointDistance * MinimumPointDistance)
            {
                return;
            }
        }

        AddSmoothingSample(point, now);
        GlobalPosition smoothed = CalculateMovingAverage();
        if (Points.Count == 0 ||
            (smoothed.AsVector3() - Points[^1].AsVector3()).sqrMagnitude >=
            MinimumForcedPointDistance * MinimumForcedPointDistance)
        {
            // Every averaged point is immutable once appended. New samples affect
            // only the next point, never any earlier section of the trail.
            Points.Add(smoothed);
        }

        while (Points.Count > MaximumPoints)
        {
            RemoveLeastImportantInteriorPoint();
        }

        PointsVersion++;
    }

    internal void AddFinalPoint(GlobalPosition point)
    {
        float now = Time.time;
        if (Points.Count == 0)
        {
            Points.Add(point);
        }
        else if (Points[^1] == point)
        {
            // The exact endpoint is already present.
        }
        else
        {
            Vector3 difference = point.AsVector3() - Points[^1].AsVector3();
            if (difference.sqrMagnitude <
                MinimumForcedPointDistance * MinimumForcedPointDistance)
            {
                Points[^1] = point;
            }
            else
            {
                Points.Add(point);
            }

            while (Points.Count > MaximumPoints)
            {
                RemoveLeastImportantInteriorPoint();
            }
        }

        _smoothingSamples.Clear();
        _smoothingSamples.Add(new TimedPosition(point, now));
        LastObservedPosition = point;
        PointsVersion++;
    }

    internal void DestroyMapVisuals()
    {
        if (MarkerObject != null)
        {
            UnityEngine.Object.Destroy(MarkerObject);
        }

        if (TrailObject != null)
        {
            UnityEngine.Object.Destroy(TrailObject);
        }

        MarkerObject = null;
        MarkerTransform = null;
        MarkerImage = null;
        TrailObject = null;
        TrailGraphic = null;
    }

    private void AddSmoothingSample(GlobalPosition point, float now)
    {
        _smoothingSamples.Add(new TimedPosition(point, now));

        float smoothingWindow = Mathf.Clamp(
            SimpleMissileBombTrackerPlugin.PathSmoothingWindow.Value,
            0.15f,
            2f);
        float oldestTime = now - smoothingWindow;
        while (_smoothingSamples.Count > 1 &&
               (_smoothingSamples[0].Time < oldestTime ||
                _smoothingSamples.Count > MaximumSmoothingSamples))
        {
            _smoothingSamples.RemoveAt(0);
        }
    }

    private GlobalPosition CalculateMovingAverage()
    {
        GlobalPosition origin = _smoothingSamples[0].Position;
        Vector3 originVector = origin.AsVector3();
        Vector3 relativeSum = Vector3.zero;
        foreach (TimedPosition sample in _smoothingSamples)
        {
            relativeSum += sample.Position.AsVector3() - originVector;
        }

        Vector3 average = originVector + relativeSum / _smoothingSamples.Count;
        return new GlobalPosition(average);
    }

    private void RemoveLeastImportantInteriorPoint()
    {
        // Keep the launch, first smoothed anchor, and newest few anchors. Remove
        // only one locally redundant point at a time on unusually long flights.
        int firstCandidate = Points.Count > 3 ? 2 : 1;
        int lastCandidate = Points.Count - ProtectedRecentPoints - 1;
        if (lastCandidate < firstCandidate)
        {
            lastCandidate = Points.Count - 2;
        }

        int removeIndex = firstCandidate;
        float smallestError = float.MaxValue;
        for (int index = firstCandidate; index <= lastCandidate; index++)
        {
            float error = PointToSegmentDistanceSquared(
                Points[index].AsVector3(),
                Points[index - 1].AsVector3(),
                Points[index + 1].AsVector3());
            if (error < smallestError)
            {
                smallestError = error;
                removeIndex = index;
            }
        }

        Points.RemoveAt(removeIndex);
    }

    private static float PointToSegmentDistanceSquared(
        Vector3 point,
        Vector3 segmentStart,
        Vector3 segmentEnd)
    {
        Vector3 segment = segmentEnd - segmentStart;
        float lengthSquared = segment.sqrMagnitude;
        if (lengthSquared < 0.0001f)
        {
            return (point - segmentStart).sqrMagnitude;
        }

        float amount = Mathf.Clamp01(
            Vector3.Dot(point - segmentStart, segment) / lengthSquared);
        Vector3 closest = segmentStart + segment * amount;
        return (point - closest).sqrMagnitude;
    }
}

internal static class PlayerWeaponOwnership
{
    private const int MaximumOwnerChainDepth = 8;
    private static readonly HashSet<PersistentID> KnownPlayerWeapons = new();

    internal static bool IsFromLocalAircraft(
        Missile missile,
        Aircraft localAircraft,
        out int parentMissileCount)
    {
        parentMissileCount = 0;
        if (missile == null || localAircraft == null)
        {
            return false;
        }

        PersistentID localAircraftId = localAircraft.persistentID;
        PersistentID ownerId = missile.ownerID;
        Span<PersistentID> verifiedParents = stackalloc PersistentID[MaximumOwnerChainDepth];
        int verifiedParentCount = 0;
        for (int depth = 0; depth < MaximumOwnerChainDepth; depth++)
        {
            if (!ownerId.IsValid || Contains(verifiedParents, verifiedParentCount, ownerId))
            {
                return false;
            }

            if (ownerId == localAircraftId)
            {
                RememberVerifiedParents(verifiedParents, verifiedParentCount);
                return true;
            }

            // A networked parent may disappear before its child is observed. Remembering
            // confirmed player weapons preserves that ownership without accepting unrelated shots.
            if (KnownPlayerWeapons.Contains(ownerId))
            {
                parentMissileCount++;
                RememberVerifiedParents(verifiedParents, verifiedParentCount);
                return true;
            }

            if (!ownerId.TryGetUnit(out Unit owner) || owner == null)
            {
                return false;
            }

            if (owner is not Missile parentMissile)
            {
                return false;
            }

            parentMissileCount++;
            verifiedParents[verifiedParentCount++] = parentMissile.persistentID;
            ownerId = parentMissile.ownerID;
        }

        return false;
    }

    internal static void Remember(Missile missile)
    {
        if (missile != null && missile.persistentID.IsValid)
        {
            KnownPlayerWeapons.Add(missile.persistentID);
        }
    }

    internal static void MarkTrackedParentAsDispenser(Missile child)
    {
        if (child == null || !child.ownerID.IsValid)
        {
            return;
        }

        Track parentTrack = null;
        if (child.ownerID.TryGetUnit(out Unit owner) &&
            owner is Missile parentMissile)
        {
            MarkerStore.ByMissile.TryGetValue(parentMissile, out parentTrack);
        }

        if (parentTrack == null)
        {
            parentTrack = MarkerStore.Tracks.Find(
                candidate => candidate.WeaponId == child.ownerID);
        }

        if (parentTrack == null)
        {
            return;
        }

        parentTrack.ProducedSubmunitions = true;
        if (parentTrack.Impacted &&
            !parentTrack.EndedByDetonation &&
            !parentTrack.Damaged)
        {
            parentTrack.ShowMarker = false;
        }
    }

    internal static void Clear()
    {
        KnownPlayerWeapons.Clear();
    }

    private static bool Contains(
        ReadOnlySpan<PersistentID> ids,
        int count,
        PersistentID candidate)
    {
        for (int index = 0; index < count; index++)
        {
            if (ids[index] == candidate)
            {
                return true;
            }
        }

        return false;
    }

    private static void RememberVerifiedParents(
        ReadOnlySpan<PersistentID> verifiedParents,
        int count)
    {
        for (int index = 0; index < count; index++)
        {
            PersistentID parentId = verifiedParents[index];
            if (parentId.IsValid)
            {
                KnownPlayerWeapons.Add(parentId);
            }
        }
    }
}

internal static class MarkerStore
{
    private const int MaximumStoredTracks = 120;

    internal static readonly List<Track> Tracks = new();
    internal static readonly Dictionary<Missile, Track> ByMissile = new();

    internal static void Add(Missile missile, Track track)
    {
        Tracks.Add(track);
        ByMissile[missile] = track;

        if (Tracks.Count <= MaximumStoredTracks)
        {
            return;
        }

        int removeIndex = Tracks.FindIndex(candidate => candidate.Impacted);
        if (removeIndex < 0)
        {
            removeIndex = 0;
        }

        Remove(Tracks[removeIndex]);
    }

    internal static void Remove(Track track)
    {
        if (track == null)
        {
            return;
        }

        track.DestroyMapVisuals();
        Tracks.Remove(track);
        RemoveMissileEntry(track.Missile, track);
    }

    internal static void RemoveMissileEntry(Missile missile, Track expectedTrack)
    {
        if (ReferenceEquals(missile, null))
        {
            return;
        }

        if (ByMissile.TryGetValue(missile, out Track current) && ReferenceEquals(current, expectedTrack))
        {
            ByMissile.Remove(missile);
        }
    }

    internal static void ClearAll()
    {
        foreach (Track track in Tracks)
        {
            track.DestroyMapVisuals();
        }

        Tracks.Clear();
        ByMissile.Clear();
        PlayerWeaponOwnership.Clear();
    }
}

internal static class TrackFactory
{
    internal static void StartTracking(Missile missile, string reason)
    {
        if (missile == null || MarkerStore.ByMissile.ContainsKey(missile))
        {
            return;
        }

        float now = Time.time;
        Track track = new()
        {
            Missile = missile,
            WeaponId = missile.persistentID,
            StartTime = now,
            LastSampleTime = now,
            Outcome = ShotOutcome.InFlight
        };

        GlobalPosition launch = missile.startPosition;
        Vector3 launchVector = launch.AsVector3();
        if (launchVector.sqrMagnitude < 0.0001f)
        {
            launch = GlobalPositionExtensions.ToGlobalPosition(missile.transform.position);
        }

        GlobalPosition current = GlobalPositionExtensions.ToGlobalPosition(missile.transform.position);
        track.InitializePath(launch, current, now);
        PlayerWeaponOwnership.MarkTrackedParentAsDispenser(missile);
        MarkerStore.Add(missile, track);
        PlayerWeaponOwnership.Remember(missile);

        if (SimpleMissileBombTrackerPlugin.VerboseLogging.Value)
        {
            SimpleMissileBombTrackerPlugin.Log.LogInfo(
                $"Tracking '{missile.unitName}' ({reason}) from {launch.AsVector3()}.");
        }
    }
}

internal sealed class ShotTracker : MonoBehaviour
{
    private const float SampleInterval = 0.15f;
    private const float ReconcileInterval = 0.5f;

    private DynamicMap _lastMap;
    private bool _observedMapState;
    private float _lastReconcileTime;
    private readonly List<Track> _tracksToRemove = new();
    private readonly List<Missile> _staleMissileKeys = new();

    private void Update()
    {
        try
        {
            Tick();
        }
        catch (Exception exception)
        {
            SimpleMissileBombTrackerPlugin.Log.LogError($"Shot tracking failed: {exception}");
        }
    }

    private void Tick()
    {
        DynamicMap map = SceneSingleton<DynamicMap>.i;
        if (!_observedMapState || map != _lastMap)
        {
            MarkerStore.ClearAll();
            _lastMap = map;
            _observedMapState = true;
        }

        float now = Time.time;
        if (SimpleMissileBombTrackerPlugin.Enabled.Value &&
            now - _lastReconcileTime >= ReconcileInterval)
        {
            _lastReconcileTime = now;
            ReconcileMissedLaunches();
        }

        float retention = Mathf.Max(
            SimpleMissileBombTrackerPlugin.MarkerLifetimeSeconds.Value,
            SimpleMissileBombTrackerPlugin.MapTrailLifetime.Value);
        if (SimpleMissileBombTrackerPlugin.WorldPathsEnabled.Value)
        {
            retention = Mathf.Max(
                retention,
                Mathf.Max(
                    SimpleMissileBombTrackerPlugin.WorldHitLifetime.Value,
                    SimpleMissileBombTrackerPlugin.WorldMissLifetime.Value));
        }
        retention = Mathf.Max(1f, retention);

        _tracksToRemove.Clear();
        foreach (Track track in MarkerStore.Tracks)
        {
            if (!track.Impacted)
            {
                if (track.Missile == null)
                {
                    FinalizeMissingMissile(track, now);
                }
                else if (track.ProducedSubmunitions && track.Missile.disabled)
                {
                    FinalizeSuccessfulDispenser(track, now);
                }
                else if (now - track.LastSampleTime >= SampleInterval)
                {
                    track.AddPoint(
                        GlobalPositionExtensions.ToGlobalPosition(track.Missile.transform.position));
                    track.LastSampleTime = now;
                }
            }

            if (track.Impacted && now - track.ImpactTime > retention)
            {
                _tracksToRemove.Add(track);
            }
        }

        foreach (Track track in _tracksToRemove)
        {
            MarkerStore.Remove(track);
        }

        PruneDestroyedMissileKeys();
    }

    private static void FinalizeMissingMissile(Track track, float now)
    {
        Missile missile = track.Missile;
        GlobalPosition finalPosition = track.LastObservedPosition;
        track.AddFinalPoint(finalPosition);
        track.Impacted = true;
        track.ImpactTime = now;
        track.Impact = finalPosition;
        track.Outcome = track.Damaged ? ShotOutcome.Intercepted : ShotOutcome.Failed;
        track.ShowMarker = !track.ProducedSubmunitions || track.Damaged;
        MarkerStore.RemoveMissileEntry(missile, track);
        track.Missile = null;
    }

    private static void FinalizeSuccessfulDispenser(Track track, float now)
    {
        Missile missile = track.Missile;
        GlobalPosition finalPosition = GlobalPositionExtensions.ToGlobalPosition(
            missile.transform.position);
        track.AddFinalPoint(finalPosition);
        track.Impacted = true;
        track.ImpactTime = now;
        track.Impact = finalPosition;
        track.Outcome = ShotOutcome.Failed;
        track.ShowMarker = false;
        MarkerStore.RemoveMissileEntry(missile, track);
        track.Missile = null;
    }

    private static void ReconcileMissedLaunches()
    {
        if (!GameManager.GetLocalAircraft(out Aircraft aircraft) || aircraft == null)
        {
            return;
        }

        Missile[] missiles = UnityEngine.Object.FindObjectsOfType<Missile>();
        foreach (Missile missile in missiles)
        {
            if (missile == null ||
                missile.disabled ||
                MarkerStore.ByMissile.ContainsKey(missile) ||
                !PlayerWeaponOwnership.IsFromLocalAircraft(
                    missile,
                    aircraft,
                    out int parentMissileCount))
            {
                continue;
            }

            string reason = parentMissileCount > 0
                ? "late submunition detection"
                : "late detection";
            TrackFactory.StartTracking(missile, reason);
            if (SimpleMissileBombTrackerPlugin.VerboseLogging.Value)
            {
                SimpleMissileBombTrackerPlugin.Log.LogWarning(
                    $"Recovered a missed launch for '{missile.unitName}' " +
                    $"(parentMissiles={parentMissileCount}).");
            }
        }
    }

    private void PruneDestroyedMissileKeys()
    {
        if (MarkerStore.ByMissile.Count == 0)
        {
            return;
        }

        _staleMissileKeys.Clear();
        foreach (KeyValuePair<Missile, Track> pair in MarkerStore.ByMissile)
        {
            if (pair.Key == null)
            {
                _staleMissileKeys.Add(pair.Key);
            }
        }

        foreach (Missile missile in _staleMissileKeys)
        {
            MarkerStore.ByMissile.Remove(missile);
        }
    }
}
