using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace CelestialAerus.Effects
{
    /// <summary>
    /// Records a projectile's recent center positions for its shader ribbon.
    /// One instance per projectile — the list is never shared.
    ///
    /// Safety rules (2026-11-14 redesign):
    /// - NaN positions are never recorded; a single NaN point corrupts the
    ///   whole ribbon.
    /// - If the new point is impossibly far from the last one, the trail is
    ///   cleared and restarted instead of drawing a line across the screen.
    ///   The default threshold is 150px: the fastest ordinary projectile
    ///   moves ~44px per AI call (ReturnSpeed 44 with extraUpdates 1), so
    ///   150px is a wide margin above any legitimate flight — anything
    ///   beyond it is a teleport, a stale point, or a position snap, and
    ///   the ribbon must not connect across it.
    /// - Projectiles that legitimately move faster, or record less often,
    ///   pass their own threshold. The Thread of the Empyrean yoyo moves up
    ///   to 80px per tick in flight and 120px per tick on its return dash
    ///   (vanilla yoyo AI: return speed is 1.5x top speed) and records
    ///   every 2 ticks, so its real gaps reach 240px — the 150px default
    ///   kept clearing its trail mid-flight and made the rainbow ribbon
    ///   flicker in and out of existence (fixed 2026-11-15).
    ///
    /// Normal behavior is identical to the old copy-pasted Add/trim code:
    /// same point counts, same trimming, same visuals.
    /// </summary>
    public sealed class TrailRecorder
    {
        // See the class doc: legitimate movement is at most ~44px per AI
        // call for ordinary projectiles, so a 150px jump between
        // consecutive points is never real flight for them.
        private const float DefaultMaxJumpDistance = 150f;

        private readonly List<Vector2> _positions;
        private readonly int _maxPoints;
        private readonly float _maxJumpDistance;

        public TrailRecorder(int maxPoints, float maxJumpDistance = DefaultMaxJumpDistance)
        {
            _maxPoints = maxPoints;
            _maxJumpDistance = maxJumpDistance;
            _positions = new List<Vector2>(maxPoints);
        }

        public IReadOnlyList<Vector2> Positions => _positions;
        public int Count => _positions.Count;

        public void Record(Vector2 center)
        {
            if (float.IsNaN(center.X) || float.IsNaN(center.Y))
                return;

            if (_positions.Count > 0)
            {
                Vector2 last = _positions[_positions.Count - 1];
                if (Vector2.DistanceSquared(center, last) > _maxJumpDistance * _maxJumpDistance)
                    _positions.Clear();
            }

            _positions.Add(center);
            if (_positions.Count > _maxPoints)
                _positions.RemoveAt(0);
        }

        public void Clear() => _positions.Clear();
    }
}
