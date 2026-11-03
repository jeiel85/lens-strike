using LensStrike.Core;
using Xunit;

public class MatchTests
{
    private static readonly ShotReport RifleBody = new ShotReport { Weapon = WeaponId.K2, Head = 0, Body = 1, DistanceM = 5 };

    private static Match Duel(int killTarget = 2)
    {
        var m = new Match(new MatchOptions { KillTarget = killTarget, RespawnMs = 1000, ProtectMs = 500 });
        m.AddPlayer(1, "A", CharacterId.Ranger);
        m.AddPlayer(2, "B", CharacterId.Mage);
        m.Start();
        return m;
    }

    private static ShotReport With(ShotReport s, int? head = null, int? body = null, float? dist = null, WeaponId? weapon = null, float? charge = null) =>
        new ShotReport { Weapon = weapon ?? s.Weapon, Head = head ?? s.Head, Body = body ?? s.Body, DistanceM = dist ?? s.DistanceM, Charge = charge ?? s.Charge };

    [Fact]
    public void 시작_전에는_피해가_없다()
    {
        var m = new Match();
        m.AddPlayer(1, "A", CharacterId.Ranger);
        m.AddPlayer(2, "B", CharacterId.Mage);
        Assert.Null(m.ApplyShot(1, 2, RifleBody, 0));
    }

    [Fact]
    public void 몸통과_머리_피해()
    {
        var m = Duel();
        Assert.Equal(8, m.ApplyShot(1, 2, RifleBody, 0)!.Value.Damage);
        Assert.Equal(16, m.ApplyShot(1, 2, With(RifleBody, head: 1, body: 0), 1000)!.Value.Damage);
        Assert.Equal(76, m.Get(2)!.Hp);
    }

    [Fact]
    public void 자기_캐릭터_무기가_아니면_거부()
    {
        var m = Duel();
        Assert.Null(m.ApplyShot(1, 2, With(RifleBody, weapon: WeaponId.Fireball), 0));
    }

    [Fact]
    public void 사거리_밖은_거부하고_먼_거리는_감쇠()
    {
        var m = Duel();
        Assert.Null(m.ApplyShot(1, 2, With(RifleBody, dist: 51), 0));
        Assert.Equal(System.MathF.Round(8 * 0.4f), m.ApplyShot(1, 2, With(RifleBody, dist: 50), 0)!.Value.Damage);
    }

    [Fact]
    public void 산탄_알_수는_무기_한도로_자른다()
    {
        var m = Duel();
        var hit = m.ApplyShot(1, 2, new ShotReport { Weapon = WeaponId.Shotgun, Body = 50, DistanceM = 2 }, 0);
        Assert.Equal(8 * 6, hit!.Value.Damage);
    }

    [Fact]
    public void 연사_속도를_넘는_보고는_거부()
    {
        var m = Duel(99);
        Assert.NotNull(m.ApplyShot(1, 2, RifleBody, 0));
        Assert.NotNull(m.ApplyShot(1, 2, RifleBody, 0));
        Assert.Null(m.ApplyShot(1, 2, RifleBody, 0));
        Assert.NotNull(m.ApplyShot(1, 2, RifleBody, 110));
    }

    [Fact]
    public void 점사는_한_사이클에_3발()
    {
        var m = Duel(99);
        var burst = new ShotReport { Weapon = WeaponId.M16, Body = 1, DistanceM = 5 };
        var accepted = 0;
        for (var i = 0; i < 5; i++) if (m.ApplyShot(1, 2, burst, 0) != null) accepted++;
        Assert.Equal(4, accepted);
        var next = 0;
        for (var i = 0; i < 4; i++) if (m.ApplyShot(1, 2, burst, 610) != null) next++;
        Assert.Equal(3, next);
    }

    [Fact]
    public void 충전량에_비례한_피해와_감전()
    {
        var m = new Match(new MatchOptions { KillTarget = 0, RespawnMs = 1000, ProtectMs = 0 });
        m.AddPlayer(1, "T", CharacterId.Trooper);
        m.AddPlayer(2, "D", CharacterId.Ranger);
        m.Start();
        Assert.Equal(45, m.ApplyShot(1, 2, new ShotReport { Weapon = WeaponId.Plasma, Body = 1, DistanceM = 3, Charge = 1 }, 0)!.Value.Damage);
        Assert.Equal(9, m.ApplyShot(1, 2, new ShotReport { Weapon = WeaponId.Plasma, Body = 1, DistanceM = 3, Charge = 0 }, 5000)!.Value.Damage);
        Assert.Equal(600, m.Get(2)!.ShockMs);
    }

    [Fact]
    public void 목표_킬에_닿으면_끝난다()
    {
        var m = Duel(1);
        float t = 0;
        HitEvent? hit = null;
        while (m.Phase == Phase.Playing) hit = m.ApplyShot(1, 2, With(RifleBody, head: 1, body: 0), t += 500);
        Assert.True(hit!.Value.Killed);
        Assert.Equal(1, m.Get(1)!.Kills);
        Assert.Equal(Phase.Ended, m.Phase);
        Assert.Equal(1UL, m.WinnerId);
    }

    [Fact]
    public void 부활과_부활_직후_무적()
    {
        var m = Duel(5);
        float t = 0;
        while (m.Get(2)!.Alive) m.ApplyShot(1, 2, With(RifleBody, head: 1, body: 0), t += 500);
        Assert.Null(m.ApplyShot(1, 2, RifleBody, t += 500));
        m.Tick(1000);
        Assert.True(m.Get(2)!.Alive);
        Assert.Equal(100, m.Get(2)!.Hp);
        Assert.Null(m.ApplyShot(1, 2, RifleBody, t += 500));
        m.Tick(500);
        Assert.NotNull(m.ApplyShot(1, 2, RifleBody, t += 500));
    }

    [Fact]
    public void 화상은_지속_피해이고_건_사람이_킬을_가져간다()
    {
        var m = new Match(new MatchOptions { KillTarget = 1, RespawnMs = 1000, ProtectMs = 0 });
        m.AddPlayer(1, "M", CharacterId.Mage);
        m.AddPlayer(2, "R", CharacterId.Ranger);
        m.Start();
        m.ApplyShot(1, 2, new ShotReport { Weapon = WeaponId.Fireball, Body = 1, DistanceM = 3 }, 0);
        Assert.Equal(72, m.Get(2)!.Hp);
        var events = m.Tick(1000);
        Assert.Equal(HitZone.Burn, events[0].Zone);
        Assert.Equal(4, events[0].Damage, 3);
        Assert.Equal(1UL, events[0].From);
        m.Tick(5000);
        Assert.Equal(60, m.Get(2)!.Hp, 3);
        m.Get(2)!.Hp = 2;
        m.Get(2)!.BurnMs = 1000;
        m.Get(2)!.BurnDps = 4;
        m.Get(2)!.BurnFrom = 1;
        m.Tick(1000);
        Assert.Equal(Phase.Ended, m.Phase);
        Assert.Equal(1UL, m.WinnerId);
    }

    [Fact]
    public void 젖음은_배율을_걸고_끝나면_풀린다()
    {
        var m = Duel(5);
        m.ApplyShot(2, 1, new ShotReport { Weapon = WeaponId.Waterball, Body = 1, DistanceM = 3 }, 0);
        Assert.Equal(1.6f, m.Get(1)!.SoakFactor);
        m.Tick(3500);
        Assert.Equal(0, m.Get(1)!.SoakMs);
        Assert.Equal(1f, m.Get(1)!.SoakFactor);
    }
}
