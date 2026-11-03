#nullable enable
// AR 공유 좌표로 명중을 판정한다. 상대 폰의 위치·방향(포즈)으로 상대 몸(머리 구, 몸통·다리 캡슐)을 세우고,
// 내 카메라 정면 광선(조준선)이나 날아가는 투사체가 그 몸에 닿는지 본다.
// 좌표계: Y가 위(중력 반대), 카메라 정면이 +Z. 단위는 미터.
using System;
using System.Numerics;

namespace LensStrike.Core
{
    public struct Capsule
    {
        public Vector3 A;
        public Vector3 B;
        public float Radius;
    }

    public struct BodyShape
    {
        public Vector3 HeadCenter;
        public float HeadRadius;
        public Capsule Torso;
        public Capsule Legs;
    }

    public struct RayHit
    {
        public int Head;
        public int Body;
        public float DistanceM;
    }

    public static class ArHit
    {
        // 폰을 들고 조준할 때의 평균 체형 가정(m). 폰은 얼굴 앞 약 30cm, 눈높이에 있다고 본다.
        public const float PhoneToHead = 0.30f;
        public const float HeadRadius = 0.12f;
        public const float TorsoTop = 0.25f;
        public const float TorsoBottom = 0.85f;
        public const float TorsoRadius = 0.20f;
        public const float LegsBottom = 1.55f;
        public const float LegsRadius = 0.14f;

        /// <summary>상대 폰의 위치와 정면 방향으로 상대 몸 모양을 만든다.</summary>
        public static BodyShape BodyFromPhone(Vector3 phonePos, Vector3 phoneForward)
        {
            var flat = new Vector3(phoneForward.X, 0, phoneForward.Z);
            // 폰이 거의 위나 아래를 향하면 앞뒤를 알 수 없으니 폰 위치를 머리로 본다
            var back = flat.LengthSquared() > 1e-4f ? -Vector3.Normalize(flat) * PhoneToHead : Vector3.Zero;
            var head = phonePos + back;
            return new BodyShape
            {
                HeadCenter = head,
                HeadRadius = HeadRadius,
                Torso = new Capsule { A = head - Vector3.UnitY * TorsoTop, B = head - Vector3.UnitY * TorsoBottom, Radius = TorsoRadius },
                Legs = new Capsule { A = head - Vector3.UnitY * TorsoBottom, B = head - Vector3.UnitY * LegsBottom, Radius = LegsRadius },
            };
        }

        /// <summary>광선과 점 사이의 거리와, 광선 위 가장 가까운 지점까지의 길이 t</summary>
        public static float RayPointDistance(Vector3 origin, Vector3 dir, Vector3 p, out float t)
        {
            t = Vector3.Dot(p - origin, dir);
            if (t < 0) return float.PositiveInfinity;
            return Vector3.Distance(origin + dir * t, p);
        }

        /// <summary>광선(origin에서 dir 방향, 길이 maxT)과 선분 AB 사이의 최단 거리. t는 광선 위 위치.</summary>
        public static float RaySegmentDistance(Vector3 origin, Vector3 dir, float maxT, Vector3 a, Vector3 b, out float t)
        {
            // 두 선분(광선 [0,maxT], AB [0,1]) 사이 최단 거리 — 표준 클램프 풀이
            var d1 = dir * maxT;
            var d2 = b - a;
            var r = origin - a;
            float aa = Vector3.Dot(d1, d1), ee = Vector3.Dot(d2, d2), ff = Vector3.Dot(d2, r);
            float s, u;
            var c = Vector3.Dot(d1, r);
            if (ee <= 1e-9f)
            {
                // 대상이 점(머리 중심처럼 길이 0인 선분)이면 그 점을 광선 위로 투영한다
                s = aa > 1e-9f ? Math.Clamp(-c / aa, 0f, 1f) : 0f;
                t = s * maxT;
                return Vector3.Distance(origin + d1 * s, a);
            }
            var bb = Vector3.Dot(d1, d2);
            var denom = aa * ee - bb * bb;
            s = denom > 1e-9f ? Math.Clamp((bb * ff - c * ee) / denom, 0f, 1f) : 0f;
            u = ee > 1e-9f ? (bb * s + ff) / ee : 0f;
            if (u < 0f)
            {
                u = 0f;
                s = aa > 1e-9f ? Math.Clamp(-c / aa, 0f, 1f) : 0f;
            }
            else if (u > 1f)
            {
                u = 1f;
                s = aa > 1e-9f ? Math.Clamp((bb - c) / aa, 0f, 1f) : 0f;
            }
            t = s * maxT;
            return Vector3.Distance(origin + d1 * s, a + d2 * u);
        }

        /// <summary>선분 PQ(투사체가 한 프레임 동안 지난 길)와 선분 AB 사이의 최단 거리</summary>
        public static float SegmentSegmentDistance(Vector3 p, Vector3 q, Vector3 a, Vector3 b)
        {
            var len = Vector3.Distance(p, q);
            if (len < 1e-6f) return PointSegmentDistance(p, a, b);
            return RaySegmentDistance(p, (q - p) / len, len, a, b, out _);
        }

        public static float PointSegmentDistance(Vector3 p, Vector3 a, Vector3 b)
        {
            var ab = b - a;
            var len2 = ab.LengthSquared();
            var t = len2 < 1e-9f ? 0f : Math.Clamp(Vector3.Dot(p - a, ab) / len2, 0f, 1f);
            return Vector3.Distance(p, a + ab * t);
        }

        /// <summary>
        /// 한 광선이 몸의 어디에 닿는지. tanSpread는 조준 허용 원뿔(멀수록 허용 반경이 커짐). 머리가 우선한다.
        /// </summary>
        public static HitZone? ClassifyRay(BodyShape body, Vector3 origin, Vector3 dir, float maxRange, float tanSpread, out float distance)
        {
            distance = Vector3.Distance(origin, body.HeadCenter);
            var dh = RayPointDistance(origin, dir, body.HeadCenter, out var th);
            if (th <= maxRange && dh <= body.HeadRadius + th * tanSpread) return HitZone.Head;
            foreach (var cap in new[] { body.Torso, body.Legs })
            {
                var d = RaySegmentDistance(origin, dir, maxRange, cap.A, cap.B, out var t);
                if (t > 0 && d <= cap.Radius + t * tanSpread) return HitZone.Body;
            }
            return null;
        }

        /// <summary>
        /// 즉시 명중 무기(소총·샷건·플라즈마)의 한 발. 산탄은 원뿔 안에 알을 흩뿌려 알마다 판정한다.
        /// 사거리 밖이면 null.
        /// </summary>
        public static RayHit? EvaluateShot(BodyShape body, Vector3 camPos, Vector3 camForward, Vector3 camUp, WeaponDef weapon, Random rng)
        {
            var dir = Vector3.Normalize(camForward);
            if (Vector3.Distance(camPos, body.HeadCenter) > weapon.MaxRangeM) return null;
            var hit = new RayHit { DistanceM = Vector3.Distance(camPos, body.HeadCenter) };
            if (weapon.Pellets > 0)
            {
                var right = Vector3.Normalize(Vector3.Cross(camUp, dir));
                var up = Vector3.Cross(dir, right);
                var maxAngle = weapon.SpreadDeg * MathF.PI / 180f;
                for (var i = 0; i < weapon.Pellets; i++)
                {
                    // 원뿔 안에 고르게(면적 기준) 흩어진 방향
                    var ang = maxAngle * MathF.Sqrt((float)rng.NextDouble());
                    var rot = (float)rng.NextDouble() * MathF.PI * 2f;
                    var pelletDir = Vector3.Normalize(dir + (right * MathF.Cos(rot) + up * MathF.Sin(rot)) * MathF.Tan(ang));
                    var zone = ClassifyRay(body, camPos, pelletDir, weapon.MaxRangeM, 0f, out _);
                    if (zone == HitZone.Head) hit.Head++;
                    else if (zone == HitZone.Body) hit.Body++;
                }
            }
            else
            {
                var tan = MathF.Tan(weapon.SpreadDeg * MathF.PI / 180f);
                var zone = ClassifyRay(body, camPos, dir, weapon.MaxRangeM, tan, out _);
                if (zone == HitZone.Head) hit.Head = 1;
                else if (zone == HitZone.Body) hit.Body = 1;
            }
            return hit.Head + hit.Body > 0 ? hit : (RayHit?)null;
        }

        /// <summary>투사체가 from→to 로 움직이는 동안 몸에 닿았는지. 머리가 우선한다.</summary>
        public static HitZone? SweepProjectile(BodyShape body, Vector3 from, Vector3 to, float radius)
        {
            if (SegmentSegmentDistance(from, to, body.HeadCenter, body.HeadCenter) <= body.HeadRadius + radius) return HitZone.Head;
            if (SegmentSegmentDistance(from, to, body.Torso.A, body.Torso.B) <= body.Torso.Radius + radius) return HitZone.Body;
            if (SegmentSegmentDistance(from, to, body.Legs.A, body.Legs.B) <= body.Legs.Radius + radius) return HitZone.Body;
            return null;
        }
    }
}
