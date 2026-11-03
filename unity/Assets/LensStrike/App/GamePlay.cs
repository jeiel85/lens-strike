#nullable enable
// 한 판의 진행: 방아쇠 입력 → 내 조준선(공유 좌표)과 상대 몸(상대 폰 포즈) 판정 → 방장 보고,
// 방장이 돌려준 결과(HP·효과·킬)를 HUD와 효과음·진동으로 보여 준다.
using System;
using System.Collections.Generic;
using LensStrike.Core;
using UnityEngine;
using NVec = System.Numerics.Vector3;

namespace LensStrike.App
{
    public sealed class GamePlay : IDisposable
    {
        public readonly GameLink Link;
        public Snapshot? Snapshot { get; private set; }
        public Action<Snapshot>? PhaseChanged;
        public Action<Snapshot>? Updated;

        private readonly ArWorld ar;
        private readonly Hud hud;
        private readonly Sfx sfx;
        private readonly Canvas canvas;
        private readonly Gun gun;
        private readonly System.Random rng = new System.Random();
        private readonly List<Projectile> projectiles = new List<Projectile>();
        private PoseMsg opponentPose;
        private float opponentPoseAt = -10;
        private Phase? lastPhase;
        private bool showBody;
        private float poseSendAt;
        private readonly GameObject bodyHead, bodyTorso, bodyLegs;
        private float lastNow;

        private static float NowMs => Time.realtimeSinceStartup * 1000f;

        public GamePlay(GameLink link, CharacterId character, ArWorld ar, Hud hud, Sfx sfx, Canvas canvas)
        {
            Link = link;
            this.ar = ar;
            this.hud = hud;
            this.sfx = sfx;
            this.canvas = canvas;
            gun = new Gun(Defs.Characters[character].Weapons);
            showBody = PlayerPrefs.GetInt("ls.showBody", 1) == 1;

            var ghost = new Color(0.47f, 1f, 0.7f, 0.25f);
            bodyHead = Visuals.Sphere("OppHead", new Color(1f, 0.86f, 0.47f, 0.35f), ArHit.HeadRadius * 2, Visuals.Ghost);
            bodyTorso = Visuals.Capsule("OppTorso", ghost, Visuals.Ghost);
            bodyLegs = Visuals.Capsule("OppLegs", ghost, Visuals.Ghost);

            link.SnapshotReceived += OnSnapshot;
            link.HitReceived += OnHit;
            link.OpponentPose += p =>
            {
                opponentPose = p;
                opponentPoseAt = Time.unscaledTime;
            };
            link.OpponentProjectile += OnOpponentProjectile;

            hud.SetWeapons(gun.Weapons, () => gun.Current);
            hud.SyncWeapons(gun.Current);
            hud.WeaponSelected = w =>
            {
                gun.Select(w);
                hud.SyncWeapons(gun.Current);
            };
            hud.ReloadPressed = () => HandleGun(gun.Reload(NowMs));
            hud.DebugToggled = () =>
            {
                // 연습 모드에서는 표적을 지금 보는 방향 3m 앞으로 다시 세운다
                if (Link.IsPractice)
                {
                    PlaceDummy();
                    return;
                }
                showBody = !showBody;
                PlayerPrefs.SetInt("ls.showBody", showBody ? 1 : 0);
            };
            hud.FireArea.Down = () =>
            {
                if (gun.Def.Mode == FireMode.Charge && !Conditions().Blocked) sfx.Play("charge", 0.6f);
                HandleGun(gun.Press(NowMs, Conditions()));
            };
            hud.FireArea.Up = () => HandleGun(gun.Release(NowMs, Conditions()));
            hud.Show(true);

            if (link is PracticeLink)
            {
                ar.SetAsHostFrame();
                PlaceDummy();
            }
        }

        /// <summary>연습: 지금 카메라 앞 3m에 나를 바라보는 훈련 표적을 세운다(공유 좌표 = 내 좌표)</summary>
        private void PlaceDummy()
        {
            var f = ar.CamForwardShared;
            var flat = new NVec(f.X, 0, f.Z);
            flat = flat.LengthSquared() < 1e-4f ? NVec.UnitZ : NVec.Normalize(flat);
            opponentPose = new PoseMsg { Position = ar.CamPosShared + flat * 3f, Forward = -flat, Valid = true };
            opponentPoseAt = float.MaxValue;
        }

        private PlayerState? Me => Snapshot?.Find(Link.MyId);
        private PlayerState? Opp => Snapshot?.Other(Link.MyId);

        private GunConditions Conditions()
        {
            var me = Me;
            return new GunConditions
            {
                SlowFactor = me?.SoakFactor ?? 1f,
                Blocked = me == null || !me.Alive || me.ShockMs > 0 || Snapshot?.Phase != Phase.Playing || !ar.Calibrated,
            };
        }

        /// <summary>상대 몸. 상대 포즈가 오래됐거나(1.5초) 추적이 끊겼으면 판정하지 않는다.</summary>
        private BodyShape? OpponentBody()
        {
            if (!opponentPose.Valid || Time.unscaledTime - opponentPoseAt > 1.5f) return null;
            return ArHit.BodyFromPhone(opponentPose.Position, opponentPose.Forward);
        }

        private void HandleGun(List<GunEvent> events)
        {
            foreach (var e in events)
            {
                switch (e.Kind)
                {
                    case GunEventKind.Fire:
                        var w = Defs.Weapons[e.Weapon];
                        sfx.Fire(e.Weapon, e.Charge);
                        Sfx.Vibrate(w.Mode == FireMode.Auto ? 10 : 20);
                        if (w.ProjectileSpeed > 0) LaunchProjectile(w);
                        else ResolveHitscan(w, e.Charge);
                        break;
                    case GunEventKind.Empty:
                        sfx.Play("empty");
                        break;
                    case GunEventKind.ReloadStart:
                    case GunEventKind.ReloadDone:
                        sfx.Play("reload");
                        break;
                }
            }
        }

        private void ResolveHitscan(WeaponDef w, float charge)
        {
            var body = OpponentBody();
            if (!body.HasValue) return;
            var hit = ArHit.EvaluateShot(body.Value, ar.CamPosShared, ar.CamForwardShared, ar.CamUpShared, w, rng);
            if (hit.HasValue)
            {
                Link.Shoot(new ShotReport { Weapon = w.Id, Head = hit.Value.Head, Body = hit.Value.Body, DistanceM = hit.Value.DistanceM, Charge = charge });
                return;
            }
            var d = NVec.Distance(ar.CamPosShared, body.Value.HeadCenter);
            if (d > w.MaxRangeM && ArHit.ClassifyRay(body.Value, ar.CamPosShared, NVec.Normalize(ar.CamForwardShared), d + 5, 0.03f, out _) != null)
                hud.Float("사거리 밖", Ui.Muted);
        }

        private void LaunchProjectile(WeaponDef w)
        {
            var origin = ar.CamPosShared - ar.CamUpShared * 0.1f;
            var dir = NVec.Normalize(ar.CamForwardShared);
            projectiles.Add(Spawn(w.Id, origin, dir, mine: true));
            Link.SendProjectile(new ProjectileMsg { Weapon = w.Id, Origin = origin, Direction = dir });
        }

        private Projectile Spawn(WeaponId weapon, NVec origin, NVec dir, bool mine)
        {
            var w = Defs.Weapons[weapon];
            var view = Visuals.Sphere(w.Name, Visuals.WeaponColor(weapon), w.ProjectileRadius * 2, Visuals.Glow);
            var trail = view.AddComponent<TrailRenderer>();
            trail.time = 0.25f;
            trail.startWidth = w.ProjectileRadius * 1.6f;
            trail.endWidth = 0;
            trail.sharedMaterial = Visuals.Glow;
            trail.startColor = Visuals.WeaponColor(weapon);
            trail.endColor = new Color(1, 1, 1, 0);
            view.transform.position = ar.ToLocal(origin);
            return new Projectile { Weapon = weapon, Origin = origin, Direction = dir, Position = origin, Mine = mine, View = view };
        }

        private void OnOpponentProjectile(ProjectileMsg p)
        {
            if (Defs.Weapons[p.Weapon].ProjectileSpeed <= 0) return;
            projectiles.Add(Spawn(p.Weapon, p.Origin, NVec.Normalize(p.Direction), mine: false));
        }

        private void StepProjectiles(float dt)
        {
            var body = OpponentBody();
            for (var i = projectiles.Count - 1; i >= 0; i--)
            {
                var p = projectiles[i];
                var w = Defs.Weapons[p.Weapon];
                var step = w.ProjectileSpeed * dt;
                var next = p.Position + p.Direction * step;
                var done = false;
                if (p.Mine && body.HasValue && Me?.Alive == true && Snapshot?.Phase == Phase.Playing)
                {
                    var zone = ArHit.SweepProjectile(body.Value, p.Position, next, w.ProjectileRadius);
                    if (zone.HasValue)
                    {
                        Link.Shoot(new ShotReport
                        {
                            Weapon = p.Weapon, Head = zone == HitZone.Head ? 1 : 0, Body = zone == HitZone.Body ? 1 : 0,
                            DistanceM = p.Traveled + step, Charge = 1,
                        });
                        done = true;
                    }
                }
                p.Position = next;
                p.Traveled += step;
                if (p.Traveled > w.MaxRangeM) done = true;
                if (done)
                {
                    UnityEngine.Object.Destroy(p.View);
                    projectiles.RemoveAt(i);
                }
                else
                {
                    p.View.transform.position = ar.ToLocal(p.Position);
                }
            }
        }

        private void OnHit(HitEvent e)
        {
            var mine = Link.MyId;
            if (e.From == mine && e.Zone != HitZone.Burn)
            {
                var head = e.Zone == HitZone.Head;
                hud.HitMarker(head, e.Killed);
                hud.Float($"{(head ? "헤드샷 " : "")}-{Mathf.RoundToInt(e.Damage)}", head ? Ui.Accent : Color.white);
                sfx.Play(head ? "headshot" : "hit");
                if (e.Killed)
                {
                    sfx.Play("kill");
                    hud.Banner("처치!");
                }
            }
            if (e.To == mine)
            {
                if (e.Zone != HitZone.Burn)
                {
                    hud.Hurt();
                    sfx.Play("hurt");
                    Sfx.Vibrate(70);
                    if (e.Effect == EffectKind.Soak) sfx.Play("splash");
                }
                if (e.Killed)
                {
                    sfx.Play("death");
                    Sfx.Vibrate(300);
                }
            }
        }

        private void OnSnapshot(Snapshot s)
        {
            Snapshot = s;
            if (s.Phase != lastPhase)
            {
                lastPhase = s.Phase;
                PhaseChanged?.Invoke(s);
            }
            hud.SetPlayers(Me, Opp, s.KillTarget);
            Updated?.Invoke(s);
        }

        public void Update()
        {
            var now = NowMs;
            var dt = lastNow > 0 ? Mathf.Clamp((now - lastNow) / 1000f, 0, 0.1f) : 0;
            lastNow = now;

            Link.Tick(dt * 1000f);
            if (!Link.IsPractice && ar.Calibrated && Time.unscaledTime >= poseSendAt)
            {
                poseSendAt = Time.unscaledTime + 1f / 15f;
                Link.SendPose(ar.MyPose());
            }

            var cond = Conditions();
            HandleGun(gun.Update(now, cond));
            StepProjectiles(dt);

            hud.Layout(Screen.width > Screen.height);
            var body = OpponentBody();
            var aimOn = false;
            if (body.HasValue)
            {
                var b = body.Value;
                var tan = Mathf.Tan(Mathf.Max(gun.Def.SpreadDeg, 1f) * Mathf.Deg2Rad);
                aimOn = ArHit.ClassifyRay(b, ar.CamPosShared, NVec.Normalize(ar.CamForwardShared), gun.Def.MaxRangeM, tan, out _) != null;
                var headLocal = ar.ToLocal(b.HeadCenter);
                var screen = ar.Cam.WorldToScreenPoint(headLocal + Vector3.up * 0.3f);
                hud.OpponentIndicator(true, screen, canvas);
                var dist = NVec.Distance(ar.CamPosShared, b.HeadCenter);
                hud.SetSeen(Link.IsPractice ? $"훈련 표적 {dist:0.0}m" : $"상대 {dist:0.0}m");
                PlaceBody(b);
            }
            else
            {
                hud.OpponentIndicator(false, Vector3.zero, canvas);
                hud.SetSeen(!ar.Tracking ? "카메라로 주변을 천천히 비춰 주세요(위치 추적 중)" : !ar.Calibrated ? "좌표를 맞춰야 쏠 수 있어요" : "상대 위치를 받는 중…");
                bodyHead.SetActive(false);
                bodyTorso.SetActive(false);
                bodyLegs.SetActive(false);
            }

            hud.SetAmmo(gun.IsReloading(now) ? "재장전 중…" : $"{gun.Ammo(gun.Current)} / {gun.Def.Magazine}");
            hud.Tick(gun.ChargeLevel(now), gun.ReloadProgress(now), !cond.Blocked, aimOn, gun.Def, Me);
        }

        private void PlaceBody(BodyShape b)
        {
            // 연습 표적은 항상 보이고, 상대 몸 모양은 ◎ 버튼으로 켜고 끈다
            var visible = showBody || Link.IsPractice;
            bodyHead.SetActive(visible);
            bodyTorso.SetActive(visible);
            bodyLegs.SetActive(visible);
            if (!visible) return;
            bodyHead.transform.position = ar.ToLocal(b.HeadCenter);
            Visuals.FitCapsule(bodyTorso.transform, ar.ToLocal(b.Torso.A), ar.ToLocal(b.Torso.B), b.Torso.Radius);
            Visuals.FitCapsule(bodyLegs.transform, ar.ToLocal(b.Legs.A), ar.ToLocal(b.Legs.B), b.Legs.Radius);
        }

        public void Dispose()
        {
            Link.SnapshotReceived -= OnSnapshot;
            Link.HitReceived -= OnHit;
            Link.OpponentProjectile -= OnOpponentProjectile;
            Link.Close();
            foreach (var p in projectiles) UnityEngine.Object.Destroy(p.View);
            projectiles.Clear();
            UnityEngine.Object.Destroy(bodyHead);
            UnityEngine.Object.Destroy(bodyTorso);
            UnityEngine.Object.Destroy(bodyLegs);
            hud.FireArea.Down = hud.FireArea.Up = null;
            hud.Show(false);
        }
    }
}
