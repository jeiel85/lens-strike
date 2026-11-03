#nullable enable
// AR 카메라 포즈를 읽고, 내 AR 세션 좌표(로컬)와 방장 기준 공유 좌표 사이를 바꾼다.
using LensStrike.Core;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using NVec = System.Numerics.Vector3;

namespace LensStrike.App
{
    public sealed class ArWorld : MonoBehaviour
    {
        public Camera Cam = null!;
        public SharedFrame Frame { get; private set; } = SharedFrame.Identity;
        /// <summary>좌표를 맞췄는지. 방장은 기준점을 저장하면 맞춘 것으로 본다.</summary>
        public bool Calibrated { get; private set; }

        public static NVec N(Vector3 v) => new NVec(v.x, v.y, v.z);
        public static Vector3 U(NVec v) => new Vector3(v.X, v.Y, v.Z);

        public bool Tracking => ARSession.state == ARSessionState.SessionTracking;

        /// <summary>지금 카메라 위치·정면(로컬 좌표). 기준점을 저장할 때 쓴다.</summary>
        public AnchorSample LocalSample() => new AnchorSample { Position = N(Cam.transform.position), Forward = N(Cam.transform.forward) };

        /// <summary>방장: 자기 좌표가 곧 공유 좌표</summary>
        public void SetAsHostFrame()
        {
            Frame = SharedFrame.Identity;
            Calibrated = true;
        }

        /// <summary>참가자: 방장 기준점(공유 좌표)과 같은 자리에서 잰 내 기준점(로컬 좌표)으로 변환을 만든다.</summary>
        public void CalibrateTo(AnchorSample hostAnchor, AnchorSample myLocalAnchor)
        {
            Frame = SharedFrame.FromAnchors(hostAnchor, myLocalAnchor);
            Calibrated = true;
        }

        public void ResetCalibration()
        {
            Frame = SharedFrame.Identity;
            Calibrated = false;
        }

        public NVec CamPosShared => Frame.ToSharedPoint(N(Cam.transform.position));
        public NVec CamForwardShared => Frame.ToSharedDirection(N(Cam.transform.forward));
        public NVec CamUpShared => Frame.ToSharedDirection(N(Cam.transform.up));

        public PoseMsg MyPose() => new PoseMsg { Position = CamPosShared, Forward = CamForwardShared, Valid = Tracking && Calibrated };

        public Vector3 ToLocal(NVec shared) => U(Frame.ToLocalPoint(shared));
        public Vector3 ToLocalDir(NVec shared) => U(Frame.ToLocalDirection(shared));
    }
}
