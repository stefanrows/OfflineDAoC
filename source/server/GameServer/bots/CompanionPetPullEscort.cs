using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS
{
    /// <summary>
    /// /petpull with /stay: while the owner walks out to pull, the Mentalist
    /// trails him so its heal-over-time stays in range of the pet, and walks
    /// only where no monster would aggro it. Back at camp it holds its spot
    /// again; it opens with damage only near the camp (the Animist grove).
    /// </summary>
    public static class CompanionPetPullEscort
    {
        /// <summary>The owner is "out pulling" beyond this distance from the stayed camp.</summary>
        public const float OwnerOutDistance = 400;
        /// <summary>The Mentalist trails this far behind the owner, on his side of the camp.</summary>
        public const float TrailDistance = 250;
        /// <summary>Kept clear of every monster's aggro range on top.</summary>
        public const float AggroMargin = 150;
        /// <summary>Damage only this close to the camp center.</summary>
        public const float DamageRadius = 600;
        private const float Step = 100;

        public readonly record struct Threat(Vector2 Position, float AggroRange);

        public static bool IsOwnerOut(Vector3 owner, Vector3 camp) =>
            Flat(owner, camp) > OwnerOutDistance;

        public static bool MayDamage(Vector3 self, Vector3 camp) => Flat(self, camp) <= DamageRadius;

        public static bool IsSafe(Vector3 point, IEnumerable<Threat> threats)
        {
            Vector2 p = new(point.X, point.Y);
            foreach (Threat threat in threats)
                if (Vector2.Distance(p, threat.Position) < threat.AggroRange + AggroMargin)
                    return false;
            return true;
        }

        /// <summary>
        /// The trailing spot: <see cref="TrailDistance"/> behind the owner on the
        /// line back to camp, moved toward camp until it is clear of every
        /// threat. Null when no spot on that line is safe (hold where it is).
        /// </summary>
        public static Vector3? EscortPoint(Vector3 owner, Vector3 camp, IReadOnlyCollection<Threat> threats)
        {
            Vector3 back = camp - owner;
            back.Z = 0;
            float length = back.Length();
            if (length < 1)
                return camp;
            Vector3 direction = back / length;
            for (float along = Math.Min(TrailDistance, length); along <= length; along += Step)
            {
                Vector3 candidate = owner + direction * along;
                candidate.Z = owner.Z + (camp.Z - owner.Z) * (along / length);
                if (IsSafe(candidate, threats))
                    return candidate;
            }
            return IsSafe(camp, threats) ? camp : null;
        }

        private static float Flat(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y));
    }
}
