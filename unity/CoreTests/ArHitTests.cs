using System;
using System.Numerics;
using LensStrike.Core;
using Xunit;

public class ArHitTests
{
    // 상대: 원점에서 +Z로 10m 앞, 폰을 눈높이(1.6m)에 들고 나(−Z 방향)를 바라본다
    private static readonly Vector3 OppPhone = new Vector3(0, 1.6f, 10);
    private static readonly BodyShape Opp = ArHit.BodyFromPhone(OppPhone, -Vector3.UnitZ);
    private static readonly Vector3 Me = new Vector3(0, 1.6f, 0);

    private static Vector3 AimAt(Vector3 target) => Vector3.Normalize(target - Me);

    [Fact]
    public void 상대_폰_뒤쪽에_머리가_있다()
    {
        Assert.Equal(10.3f, Opp.HeadCenter.Z, 3);
        Assert.Equal(1.6f, Opp.HeadCenter.Y, 3);
        Assert.Equal(1.6f - ArHit.TorsoTop, Opp.Torso.A.Y, 3);
    }

    [Fact]
    public void 머리를_겨누면_헤드샷_몸통을_겨누면_몸통()
    {
        var rng = new Random(1);
        var head = ArHit.EvaluateShot(Opp, Me, AimAt(Opp.HeadCenter), Vector3.UnitY, Defs.Weapons[WeaponId.M16], rng);
        Assert.Equal(1, head!.Value.Head);
        var chest = ArHit.EvaluateShot(Opp, Me, AimAt(Opp.HeadCenter - Vector3.UnitY * 0.5f), Vector3.UnitY, Defs.Weapons[WeaponId.M16], rng);
        Assert.Equal(1, chest!.Value.Body);
        Assert.Equal(0, chest.Value.Head);
        Assert.Equal(10.3f, chest.Value.DistanceM, 2);
    }

    [Fact]
    public void 옆으로_1m_벗어나면_빗나간다()
    {
        var miss = ArHit.EvaluateShot(Opp, Me, AimAt(Opp.HeadCenter + Vector3.UnitX * 1f), Vector3.UnitY, Defs.Weapons[WeaponId.M16], new Random(1));
        Assert.Null(miss);
    }

    [Fact]
    public void 조준_허용_원뿔은_멀수록_넓다()
    {
        // 머리 반지름(0.12)보다 조금 바깥(0.2m)을 겨냥: 소총(0.8°)은 10m에서 허용 0.14m → 0.26m까지 머리로 인정
        var nearHead = AimAt(Opp.HeadCenter + Vector3.UnitX * 0.2f);
        var zone = ArHit.ClassifyRay(Opp, Me, nearHead, 60, MathF.Tan(0.8f * MathF.PI / 180f), out _);
        Assert.Equal(HitZone.Head, zone);
        Assert.Null(ArHit.ClassifyRay(Opp, Me, nearHead, 60, 0f, out _));
    }

    [Fact]
    public void 뒤를_향해_쏘면_맞지_않는다()
    {
        Assert.Null(ArHit.ClassifyRay(Opp, Me, -Vector3.UnitZ, 60, 0.1f, out _));
    }

    [Fact]
    public void 샷건은_사거리_밖이면_빗나가고_가까우면_여러_알이_맞는다()
    {
        var farOpp = ArHit.BodyFromPhone(new Vector3(0, 1.6f, 15), -Vector3.UnitZ);
        Assert.Null(ArHit.EvaluateShot(farOpp, Me, AimAt(farOpp.Torso.A), Vector3.UnitY, Defs.Weapons[WeaponId.Shotgun], new Random(3)));

        var nearOpp = ArHit.BodyFromPhone(new Vector3(0, 1.6f, 3), -Vector3.UnitZ);
        var near = ArHit.EvaluateShot(nearOpp, Me, Vector3.Normalize(nearOpp.Torso.A * 0.5f + nearOpp.Torso.B * 0.5f - Me), Vector3.UnitY,
            Defs.Weapons[WeaponId.Shotgun], new Random(3));
        Assert.NotNull(near);
        Assert.True(near!.Value.Head + near.Value.Body >= 4);
    }

    [Fact]
    public void 투사체가_몸을_지나가면_맞고_비켜가면_빗나간다()
    {
        var from = Me;
        var to = Me + AimAt(Opp.HeadCenter - Vector3.UnitY * 0.5f) * 11f;
        Assert.Equal(HitZone.Body, ArHit.SweepProjectile(Opp, from, to, 0.22f));
        var dodged = ArHit.BodyFromPhone(OppPhone + Vector3.UnitX * 1.0f, -Vector3.UnitZ);
        Assert.Null(ArHit.SweepProjectile(dodged, from, to, 0.22f));
    }

    [Fact]
    public void 폰이_수직이어도_예외_없이_폰_위치를_머리로_본다()
    {
        var b = ArHit.BodyFromPhone(new Vector3(1, 1.5f, 2), Vector3.UnitY);
        Assert.Equal(new Vector3(1, 1.5f, 2), b.HeadCenter);
    }
}
