#nullable enable
// 두 폰의 AR 좌표계를 하나로 맞춘다("같은 자리 맞대기").
// 방장 폰과 참가자 폰을 차례로 같은 지점·같은 방향에 놓고 각자 기준점을 저장하면,
// 참가자 좌표를 방장 좌표로 바꾸는 변환(세로축 회전 + 이동)을 구할 수 있다.
// AR 좌표계는 둘 다 중력 방향(Y)이 맞춰져 있으므로 수평 회전(요) 하나만 구하면 된다.
using System;
using System.Numerics;

namespace LensStrike.Core
{
    public struct AnchorSample
    {
        public Vector3 Position;
        public Vector3 Forward;

        public float Yaw => MathF.Atan2(Forward.X, Forward.Z);
    }

    /// <summary>로컬(내 AR 세션) 좌표 → 공유(방장) 좌표</summary>
    public readonly struct SharedFrame
    {
        public readonly float YawOffset;
        public readonly Vector3 LocalOrigin;
        public readonly Vector3 SharedOrigin;

        public SharedFrame(float yawOffset, Vector3 localOrigin, Vector3 sharedOrigin)
        {
            YawOffset = yawOffset;
            LocalOrigin = localOrigin;
            SharedOrigin = sharedOrigin;
        }

        public static SharedFrame Identity => new SharedFrame(0, Vector3.Zero, Vector3.Zero);

        /// <summary>방장 기준점(공유 좌표)과 내 기준점(로컬 좌표)이 같은 지점·방향이라는 사실로 변환을 만든다.</summary>
        public static SharedFrame FromAnchors(AnchorSample sharedAnchor, AnchorSample localAnchor) =>
            new SharedFrame(WrapAngle(sharedAnchor.Yaw - localAnchor.Yaw), localAnchor.Position, sharedAnchor.Position);

        public Vector3 ToSharedPoint(Vector3 local) => RotateY(local - LocalOrigin, YawOffset) + SharedOrigin;

        public Vector3 ToSharedDirection(Vector3 local) => RotateY(local, YawOffset);

        public Vector3 ToLocalPoint(Vector3 shared) => RotateY(shared - SharedOrigin, -YawOffset) + LocalOrigin;

        public Vector3 ToLocalDirection(Vector3 shared) => RotateY(shared, -YawOffset);

        /// <summary>Y축 회전. 정면(0,0,1)을 angle만큼 돌리면 (sin, 0, cos) 방향이 된다(Yaw와 같은 기준).</summary>
        public static Vector3 RotateY(Vector3 v, float angle)
        {
            var c = MathF.Cos(angle);
            var s = MathF.Sin(angle);
            return new Vector3(v.X * c + v.Z * s, v.Y, -v.X * s + v.Z * c);
        }

        public static float WrapAngle(float a)
        {
            while (a > MathF.PI) a -= MathF.PI * 2f;
            while (a < -MathF.PI) a += MathF.PI * 2f;
            return a;
        }
    }
}
