using System;
using System.Numerics;
using LensStrike.Core;
using Xunit;

public class CalibrationTests
{
    private static void Near(Vector3 expected, Vector3 actual, float tol = 1e-3f) =>
        Assert.True(Vector3.Distance(expected, actual) < tol, $"expected {expected}, actual {actual}");

    [Fact]
    public void 요_각도는_정면_기준()
    {
        Assert.Equal(0f, new AnchorSample { Forward = Vector3.UnitZ }.Yaw, 4);
        Assert.Equal(MathF.PI / 2, new AnchorSample { Forward = Vector3.UnitX }.Yaw, 4);
        Near(Vector3.UnitX, SharedFrame.RotateY(Vector3.UnitZ, MathF.PI / 2));
    }

    [Fact]
    public void 같은_자리_기준점으로_참가자_좌표를_방장_좌표로_바꾼다()
    {
        // 실제 세계: 기준점 W=(2,0,3), 정면은 세계 +X.
        // 방장 세션은 세계와 같고, 참가자 세션은 세계를 90° 돌리고 (5,0,-1) 옮긴 좌표를 쓴다고 하자.
        var world = new Vector3(2, 0.8f, 3);
        Func<Vector3, Vector3> guestFromWorldPoint = w => SharedFrame.RotateY(w, -MathF.PI / 2) + new Vector3(5, 0, -1);
        Func<Vector3, Vector3> guestFromWorldDir = d => SharedFrame.RotateY(d, -MathF.PI / 2);

        var hostAnchor = new AnchorSample { Position = world, Forward = Vector3.UnitX };
        var guestAnchor = new AnchorSample { Position = guestFromWorldPoint(world), Forward = guestFromWorldDir(Vector3.UnitX) };
        var frame = SharedFrame.FromAnchors(hostAnchor, guestAnchor);

        // 참가자가 세계 (−4, 1.5, 7)에 서서 세계 −Z를 본다면, 공유(방장) 좌표로도 그대로여야 한다
        var p = new Vector3(-4, 1.5f, 7);
        Near(p, frame.ToSharedPoint(guestFromWorldPoint(p)));
        Near(-Vector3.UnitZ, frame.ToSharedDirection(guestFromWorldDir(-Vector3.UnitZ)));
        Near(guestFromWorldPoint(p), frame.ToLocalPoint(p));
    }

    [Fact]
    public void 방장은_항등_변환()
    {
        var a = new AnchorSample { Position = new Vector3(1, 0, 1), Forward = Vector3.UnitZ };
        var f = SharedFrame.FromAnchors(a, a);
        Near(new Vector3(3, 2, 1), f.ToSharedPoint(new Vector3(3, 2, 1)));
    }
}
