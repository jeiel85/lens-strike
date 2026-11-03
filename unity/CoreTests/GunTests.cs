using System.Collections.Generic;
using System.Linq;
using LensStrike.Core;
using Xunit;

public class GunTests
{
    private static readonly GunConditions Ok = GunConditions.Normal;
    private static int Fires(IEnumerable<GunEvent> events) => events.Count(e => e.Kind == GunEventKind.Fire);

    [Fact]
    public void 단발은_간격_전에는_다시_쏘지_않는다()
    {
        var g = new Gun(new[] { WeaponId.Shotgun });
        Assert.Equal(1, Fires(g.Press(0, Ok)));
        g.Release(10, Ok);
        Assert.Equal(0, Fires(g.Press(500, Ok)));
        Assert.Equal(1, Fires(g.Press(900, Ok)));
        Assert.Equal(4, g.Ammo(WeaponId.Shotgun));
    }

    [Fact]
    public void 점사는_탭_한_번에_3발()
    {
        var g = new Gun(new[] { WeaponId.M16, WeaponId.K2, WeaponId.Shotgun });
        var n = Fires(g.Press(0, Ok));
        g.Release(5, Ok);
        for (var t = 10; t <= 300; t += 10) n += Fires(g.Update(t, Ok));
        Assert.Equal(3, n);
        Assert.Equal(27, g.Ammo(WeaponId.M16));
    }

    [Fact]
    public void 연사는_누르는_동안_계속()
    {
        var g = new Gun(new[] { WeaponId.K2 });
        var n = Fires(g.Press(0, Ok));
        for (var t = 10; t < 1100; t += 10) n += Fires(g.Update(t, Ok));
        Assert.Equal(10, n);
        g.Release(1100, Ok);
        Assert.Equal(0, Fires(g.Update(1300, Ok)));
    }

    [Fact]
    public void 젖으면_사격_간격이_늘어난다()
    {
        var g = new Gun(new[] { WeaponId.K2 });
        var slow = new GunConditions { SlowFactor = 2, Blocked = false };
        var n = Fires(g.Press(0, slow));
        for (var t = 10; t < 1100; t += 10) n += Fires(g.Update(t, slow));
        Assert.Equal(5, n);
    }

    [Fact]
    public void 충전식은_뗄_때_충전량과_함께_쏜다()
    {
        var g = new Gun(new[] { WeaponId.Plasma });
        Assert.Empty(g.Press(0, Ok));
        Assert.Equal(0.5f, g.ChargeLevel(450), 3);
        var shot = g.Release(450, Ok).Single();
        Assert.Equal(GunEventKind.Fire, shot.Kind);
        Assert.Equal(0.5f, shot.Charge, 3);
        g.Press(1000, Ok);
        Assert.Equal(0.2f, g.Release(1010, Ok).Single().Charge, 3);
    }

    [Fact]
    public void 탄약이_떨어지면_자동_재장전()
    {
        var g = new Gun(new[] { WeaponId.Fireball });
        float t = 0;
        for (var i = 0; i < 5; i++)
        {
            g.Press(t, Ok);
            g.Release(t, Ok);
            t += 1100;
        }
        Assert.Equal(0, g.Ammo(WeaponId.Fireball));
        Assert.Contains(g.Update(t, Ok), e => e.Kind == GunEventKind.ReloadStart);
        Assert.True(g.IsReloading(t + 100));
        Assert.Contains(g.Update(t + 2600, Ok), e => e.Kind == GunEventKind.ReloadDone);
        Assert.Equal(5, g.Ammo(WeaponId.Fireball));
    }

    [Fact]
    public void 감전_중에는_쏠_수_없다()
    {
        var g = new Gun(new[] { WeaponId.K2 });
        Assert.Empty(g.Press(0, new GunConditions { SlowFactor = 1, Blocked = true }));
    }
}
