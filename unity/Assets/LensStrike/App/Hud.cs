#nullable enable
// 경기 화면(HUD): 체력바·점수·조준선·무기 버튼·탄약·상태·피격 효과·상대 방향 표시.
// 세로 화면은 조작부를 아래 가로줄로, 가로 화면은 오른쪽 세로줄로 배치한다.
using System;
using System.Collections.Generic;
using LensStrike.Core;
using UnityEngine;
using UnityEngine.UI;

namespace LensStrike.App
{
    public sealed class Hud
    {
        public readonly RectTransform Root;
        public readonly Ui.PressArea FireArea;
        public Action<WeaponId>? WeaponSelected;
        public Action? ReloadPressed;
        public Action? ExitPressed;
        public Action? DebugToggled;

        private readonly Text meName, meHp, oppName, oppHp, score, scoreSub, ammo, status, seen, banner, respawn;
        private readonly Image meFill, oppFill, crosshairRing, chargeRing, hurt, burn, soak, shock, protect, deadDim, arrow;
        private readonly RectTransform controls, weaponsBox, ammoBox, markers, floats, oppMarker;
        private readonly Text oppMarkerText;
        private readonly Image oppMarkerHp;
        private readonly List<Button> weaponButtons = new List<Button>();
        private readonly List<(Text text, float at, Vector2 from)> floaters = new List<(Text, float, Vector2)>();
        private float markerAt = -10, hurtAt = -10, bannerAt = -10;
        private bool? landscape;

        public Hud(Transform canvas)
        {
            Root = Ui.Stretch(Ui.Rect("Hud", canvas));

            var fire = Ui.Image("FireArea", Root, new Color(0, 0, 0, 0), raycast: true);
            Ui.Stretch(fire.rectTransform);
            FireArea = fire.gameObject.AddComponent<Ui.PressArea>();

            // 피격·상태 효과(사격 영역 위, 버튼 아래)
            hurt = Overlay("Hurt", new Color(1, 0, 0, 0));
            burn = Overlay("Burn", new Color(1, 0.43f, 0, 0));
            soak = Overlay("Soak", new Color(0.16f, 0.43f, 1, 0));
            shock = Overlay("Shock", new Color(0.7f, 0.9f, 1, 0));
            protect = Overlay("Protect", new Color(1, 1, 1, 0));
            deadDim = Ui.Image("Dead", Root, new Color(0, 0, 0, 0.5f));
            Ui.Stretch(deadDim.rectTransform);
            var deadTitle = Ui.Text("Title", deadDim.transform, "쓰러졌습니다", 72, Ui.Danger, style: FontStyle.Bold);
            Ui.Place(deadTitle.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 50), new Vector2(900, 100));
            respawn = Ui.Text("Respawn", deadDim.transform, "", 44, Color.white);
            Ui.Place(respawn.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -40), new Vector2(900, 80));
            deadDim.gameObject.SetActive(false);

            // 상대 표시(머리 위 이름·HP)와 화면 밖 방향 화살표
            oppMarker = Ui.Rect("OppMarker", Root);
            Ui.Place(oppMarker, new Vector2(0, 0), Vector2.zero, new Vector2(300, 90), new Vector2(0.5f, 0));
            oppMarkerText = Ui.Text("Name", oppMarker, "", 34, Color.white, style: FontStyle.Bold);
            Ui.Place(oppMarkerText.rectTransform, new Vector2(0.5f, 1), Vector2.zero, new Vector2(300, 50), new Vector2(0.5f, 1));
            var hpBg = Ui.Image("HpBg", oppMarker, Ui.Dim);
            Ui.Place(hpBg.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 12), new Vector2(200, 16), new Vector2(0.5f, 0));
            oppMarkerHp = Ui.Image("Hp", hpBg.transform, Ui.Danger);
            oppMarkerHp.rectTransform.anchorMin = Vector2.zero;
            oppMarkerHp.rectTransform.anchorMax = new Vector2(1, 1);
            oppMarkerHp.rectTransform.pivot = new Vector2(0, 0.5f);
            oppMarkerHp.rectTransform.offsetMin = oppMarkerHp.rectTransform.offsetMax = Vector2.zero;
            arrow = Ui.Image("Arrow", Root, Ui.Danger);
            arrow.sprite = Visuals.Disc;
            arrow.rectTransform.sizeDelta = new Vector2(44, 44);
            var arrowText = Ui.Text("Label", arrow.transform, "▲", 34, Color.white);
            Ui.Stretch(arrowText.rectTransform);

            // 조준선
            var cross = Ui.Rect("Crosshair", Root);
            Ui.Place(cross, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(90, 90));
            crosshairRing = Ui.Image("Ring", cross, Color.white);
            crosshairRing.sprite = Visuals.Ring;
            Ui.Stretch(crosshairRing.rectTransform);
            var dot = Ui.Image("Dot", cross, Color.white);
            dot.sprite = Visuals.Disc;
            Ui.Place(dot.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
            foreach (var (dx, dy) in new[] { (1, 0), (-1, 0), (0, 1), (0, -1) })
            {
                var tick = Ui.Image("Tick", cross, Color.white);
                Ui.Place(tick.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(dx * 70, dy * 70), dx != 0 ? new Vector2(28, 4) : new Vector2(4, 28));
            }
            chargeRing = Ui.Image("Charge", cross, Ui.Accent);
            chargeRing.sprite = Visuals.Ring;
            chargeRing.type = Image.Type.Filled;
            chargeRing.fillMethod = Image.FillMethod.Radial360;
            chargeRing.fillOrigin = (int)Image.Origin360.Top;
            chargeRing.fillClockwise = true;
            Ui.Place(chargeRing.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(200, 200));

            markers = Ui.Rect("HitMarker", Root);
            Ui.Place(markers, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(10, 10));
            foreach (var (dx, dy) in new[] { (1, 1), (-1, 1), (1, -1), (-1, -1) })
            {
                var m = Ui.Image("M", markers, Color.white);
                Ui.Place(m.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(dx * 30, dy * 30), new Vector2(26, 6));
                m.rectTransform.localRotation = Quaternion.Euler(0, 0, dx * dy * 45);
            }
            floats = Ui.Rect("Floats", Root);
            Ui.Stretch(floats);

            // 위쪽: 나가기 · 내 HP · 점수 · 상대 HP · 표시 켜기
            var top = Ui.Rect("Top", Root);
            top.anchorMin = new Vector2(0, 1);
            top.anchorMax = new Vector2(1, 1);
            top.pivot = new Vector2(0.5f, 1);
            top.offsetMin = new Vector2(24, -150);
            top.offsetMax = new Vector2(-24, -24);
            var exit = Ui.Button("Exit", top, "✕", Ui.Dim, Color.white, () => ExitPressed?.Invoke(), 44);
            Ui.Place((RectTransform)exit.transform, new Vector2(0, 1), Vector2.zero, new Vector2(100, 100), new Vector2(0, 1));
            var dbg = Ui.Button("Debug", top, "◎", Ui.Dim, Color.white, () => DebugToggled?.Invoke(), 44);
            Ui.Place((RectTransform)dbg.transform, new Vector2(1, 1), Vector2.zero, new Vector2(100, 100), new Vector2(1, 1));
            (meName, meHp, meFill) = Bar(top, "Me", new Vector2(0, 1), new Vector2(120, 0), Ui.Ok);
            (oppName, oppHp, oppFill) = Bar(top, "Opp", new Vector2(1, 1), new Vector2(-120, 0), Ui.Danger);
            var scoreBg = Ui.Image("Score", top, Ui.Dim);
            Ui.Place(scoreBg.rectTransform, new Vector2(0.5f, 1), Vector2.zero, new Vector2(190, 110), new Vector2(0.5f, 1));
            score = Ui.Text("Value", scoreBg.transform, "", 52, Color.white, style: FontStyle.Bold);
            Ui.Place(score.rectTransform, new Vector2(0.5f, 1), new Vector2(0, -4), new Vector2(190, 66), new Vector2(0.5f, 1));
            scoreSub = Ui.Text("Sub", scoreBg.transform, "", 24, Ui.Muted);
            Ui.Place(scoreSub.rectTransform, new Vector2(0.5f, 0), new Vector2(0, 6), new Vector2(190, 32), new Vector2(0.5f, 0));

            seen = Ui.Text("Seen", Root, "", 30, new Color(1, 1, 1, 0.85f), TextAnchor.UpperLeft);
            Ui.Place(seen.rectTransform, new Vector2(0, 1), new Vector2(30, -170), new Vector2(700, 50), new Vector2(0, 1));
            status = Ui.Text("Status", Root, "", 36, Color.white, style: FontStyle.Bold);
            Ui.Place(status.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, -170), new Vector2(1000, 60));
            banner = Ui.Text("Banner", Root, "", 84, Ui.Danger, style: FontStyle.Bold);
            Ui.Place(banner.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0, 250), new Vector2(1000, 120));
            banner.gameObject.SetActive(false);

            // 조작부
            controls = Ui.Rect("Controls", Root);
            weaponsBox = Ui.Rect("Weapons", controls);
            ammoBox = Ui.Rect("AmmoBox", controls);
            ammo = Ui.Text("Ammo", ammoBox, "", 46, Color.white, style: FontStyle.Bold);
            var reload = Ui.Button("Reload", ammoBox, "재장전", Ui.Dim, Color.white, () => ReloadPressed?.Invoke(), 32);
            Ui.Place(ammo.rectTransform, new Vector2(0, 0.5f), Vector2.zero, new Vector2(240, 80), new Vector2(0, 0.5f));
            Ui.Place((RectTransform)reload.transform, new Vector2(1, 0.5f), Vector2.zero, new Vector2(180, 80), new Vector2(1, 0.5f));
        }

        private Image Overlay(string name, Color c)
        {
            var img = Ui.Image(name, Root, c);
            img.sprite = Visuals.Vignette;
            Ui.Stretch(img.rectTransform, -40, -40, -40, -40);
            return img;
        }

        private static (Text name, Text hp, Image fill) Bar(RectTransform parent, string id, Vector2 anchor, Vector2 pos, Color color)
        {
            var box = Ui.Rect(id, parent);
            box.anchorMin = anchor.x < 0.5f ? new Vector2(0, 1) : new Vector2(0.5f, 1);
            box.anchorMax = anchor.x < 0.5f ? new Vector2(0.5f, 1) : new Vector2(1, 1);
            box.pivot = new Vector2(anchor.x, 1);
            box.offsetMin = anchor.x < 0.5f ? new Vector2(pos.x, -100) : new Vector2(105, -100);
            box.offsetMax = anchor.x < 0.5f ? new Vector2(-105, 0) : new Vector2(pos.x, 0);
            var align = anchor.x < 0.5f ? TextAnchor.UpperLeft : TextAnchor.UpperRight;
            var name = Ui.Text("Name", box, "", 28, Color.white, align);
            name.horizontalOverflow = HorizontalWrapMode.Overflow;
            Ui.Stretch(name.rectTransform, 0, 0, 70, 50);
            if (anchor.x > 0.5f) Ui.Stretch(name.rectTransform, 70, 0, 0, 50);
            var hp = Ui.Text("Hp", box, "", 30, Color.white, anchor.x < 0.5f ? TextAnchor.UpperRight : TextAnchor.UpperLeft, FontStyle.Bold);
            Ui.Stretch(hp.rectTransform, 0, 0, 0, 50);
            var track = Ui.Image("Track", box, Ui.Dim);
            track.rectTransform.anchorMin = new Vector2(0, 0);
            track.rectTransform.anchorMax = new Vector2(1, 0);
            track.rectTransform.pivot = new Vector2(0.5f, 0);
            track.rectTransform.offsetMin = new Vector2(0, 26);
            track.rectTransform.offsetMax = new Vector2(0, 46);
            var fill = Ui.Image("Fill", track.transform, color);
            fill.type = Image.Type.Filled;
            fill.fillMethod = Image.FillMethod.Horizontal;
            fill.fillOrigin = anchor.x < 0.5f ? 0 : 1;
            fill.sprite = Visuals.Disc; // Filled 타입은 스프라이트가 있어야 동작
            fill.sprite = null;
            Ui.Stretch(fill.rectTransform);
            return (name, hp, fill);
        }

        public void SetWeapons(IReadOnlyList<WeaponId> weapons, Func<WeaponId> current)
        {
            foreach (var b in weaponButtons) UnityEngine.Object.Destroy(b.gameObject);
            weaponButtons.Clear();
            foreach (var w in weapons)
            {
                var def = Defs.Weapons[w];
                var id = w;
                var b = Ui.Button(def.Name, weaponsBox, def.Name, Ui.Dim, Color.white, () => WeaponSelected?.Invoke(id), 32);
                weaponButtons.Add(b);
            }
            weaponsBox.gameObject.SetActive(weapons.Count > 1);
            landscape = null;
        }

        public void SyncWeapons(WeaponId current)
        {
            for (var i = 0; i < weaponButtons.Count; i++)
            {
                var name = weaponButtons[i].name;
                var active = Defs.Weapons[current].Name == name;
                weaponButtons[i].image.color = active ? Visuals.WeaponColor(current) * new Color(1, 1, 1, 0.55f) : Ui.Dim;
            }
        }

        /// <summary>화면 방향이 바뀌면 조작부를 다시 배치한다</summary>
        public void Layout(bool isLandscape)
        {
            if (landscape == isLandscape) return;
            landscape = isLandscape;
            var n = weaponButtons.Count;
            if (isLandscape)
            {
                Ui.Place(controls, new Vector2(1, 0), new Vector2(-24, 24), new Vector2(330, 760), new Vector2(1, 0));
                Ui.Place(weaponsBox, new Vector2(0.5f, 1), Vector2.zero, new Vector2(330, 560), new Vector2(0.5f, 1));
                for (var i = 0; i < n; i++)
                    Ui.Place((RectTransform)weaponButtons[i].transform, new Vector2(0.5f, 1), new Vector2(0, -i * 120), new Vector2(330, 104), new Vector2(0.5f, 1));
                Ui.Place(ammoBox, new Vector2(0.5f, 0), Vector2.zero, new Vector2(330, 180), new Vector2(0.5f, 0));
                Ui.Place(ammo.rectTransform, new Vector2(0.5f, 1), Vector2.zero, new Vector2(330, 80), new Vector2(0.5f, 1));
                Ui.Place((RectTransform)ammoBox.GetChild(1), new Vector2(0.5f, 0), Vector2.zero, new Vector2(220, 84), new Vector2(0.5f, 0));
            }
            else
            {
                Ui.Place(controls, new Vector2(0.5f, 0), new Vector2(0, 30), new Vector2(1040, 230), new Vector2(0.5f, 0));
                Ui.Place(weaponsBox, new Vector2(0.5f, 1), Vector2.zero, new Vector2(1040, 104), new Vector2(0.5f, 1));
                var w = n > 0 ? Mathf.Min(320, (1040 - (n - 1) * 16) / n) : 0;
                for (var i = 0; i < n; i++)
                {
                    var x = (i - (n - 1) / 2f) * (w + 16);
                    Ui.Place((RectTransform)weaponButtons[i].transform, new Vector2(0.5f, 1), new Vector2(x, 0), new Vector2(w, 104), new Vector2(0.5f, 1));
                }
                Ui.Place(ammoBox, new Vector2(0.5f, 0), Vector2.zero, new Vector2(520, 100), new Vector2(0.5f, 0));
                Ui.Place(ammo.rectTransform, new Vector2(0, 0.5f), Vector2.zero, new Vector2(300, 90), new Vector2(0, 0.5f));
                Ui.Place((RectTransform)ammoBox.GetChild(1), new Vector2(1, 0.5f), Vector2.zero, new Vector2(200, 90), new Vector2(1, 0.5f));
            }
        }

        public void SetPlayers(PlayerState? me, PlayerState? opp, int killTarget)
        {
            meName.text = me != null ? $"{me.Name} · {Defs.Characters[me.Character].Name}" : "";
            meHp.text = me != null ? Mathf.CeilToInt(me.Hp).ToString() : "";
            meFill.fillAmount = me != null ? Mathf.Clamp01(me.Hp / me.MaxHp) : 0;
            meFill.color = me != null && me.Hp / me.MaxHp < 0.3f ? Ui.Hex("#ff9f1c") : Ui.Ok;
            oppName.text = opp != null ? $"{opp.Name} · {Defs.Characters[opp.Character].Name}" : "상대 기다리는 중";
            oppHp.text = opp != null ? Mathf.CeilToInt(opp.Hp).ToString() : "";
            oppFill.fillAmount = opp != null ? Mathf.Clamp01(opp.Hp / opp.MaxHp) : 0;
            score.text = killTarget > 0 ? $"{me?.Kills ?? 0} : {opp?.Kills ?? 0}" : $"처치 {me?.Kills ?? 0}";
            scoreSub.text = killTarget > 0 ? $"{killTarget}킬 승리" : "연습 모드";
            oppMarkerHp.rectTransform.anchorMax = new Vector2(opp != null ? Mathf.Clamp01(opp.Hp / opp.MaxHp) : 0, 1);
            oppMarkerText.text = opp?.Name ?? "";

            var dead = me != null && !me.Alive;
            deadDim.gameObject.SetActive(dead);
            if (dead) respawn.text = $"{Mathf.Max(1, Mathf.CeilToInt(me!.RespawnMs / 1000f))}초 뒤 부활";

            var s = new List<string>();
            if (me != null && me.ShockMs > 0) s.Add("⚡ 감전 — 사격 불가");
            if (me != null && me.SoakMs > 0) s.Add("💧 젖음 — 연사 느려짐");
            if (me != null && me.BurnMs > 0) s.Add("🔥 화상");
            status.text = string.Join("   ", s);
        }

        public void SetSeen(string text) => seen.text = text;
        public void SetAmmo(string text) => ammo.text = text;

        public void Banner(string text)
        {
            banner.text = text;
            bannerAt = Time.unscaledTime;
        }

        public void HitMarker(bool head, bool kill)
        {
            markerAt = Time.unscaledTime;
            var c = kill ? Ui.Danger : head ? Ui.Accent : Color.white;
            foreach (var img in markers.GetComponentsInChildren<Image>()) img.color = c;
        }

        public void Hurt() => hurtAt = Time.unscaledTime;

        public void Float(string text, Color color)
        {
            var t = Ui.Text("Float", floats, text, 52, color, style: FontStyle.Bold);
            var from = new Vector2(UnityEngine.Random.Range(-60f, 60f), 60);
            Ui.Place(t.rectTransform, new Vector2(0.5f, 0.5f), from, new Vector2(600, 80));
            var outline = t.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0, 0, 0, 0.8f);
            outline.effectDistance = new Vector2(3, -3);
            floaters.Add((t, Time.unscaledTime, from));
        }

        /// <summary>
        /// 상대 위치 표시. 화면 안이면 머리 위에 이름·HP, 밖이면 가장자리에 방향 화살표.
        /// screenPos는 화면 픽셀 좌표(z&lt;0이면 등 뒤).
        /// </summary>
        public void OpponentIndicator(bool visible, Vector3 screenPos, Canvas canvas)
        {
            if (!visible)
            {
                oppMarker.gameObject.SetActive(false);
                arrow.gameObject.SetActive(false);
                return;
            }
            var scale = canvas.scaleFactor;
            var size = new Vector2(Screen.width, Screen.height) / scale;
            var p = new Vector2(screenPos.x, screenPos.y) / scale;
            var behind = screenPos.z < 0;
            if (behind) p = size - p; // 등 뒤면 화면 반대편 방향
            var inside = !behind && p.x > 0 && p.x < size.x && p.y > 0 && p.y < size.y;
            oppMarker.gameObject.SetActive(inside);
            arrow.gameObject.SetActive(!inside);
            if (inside)
            {
                oppMarker.anchoredPosition = p;
                return;
            }
            var center = size / 2;
            var dir = (p - center).normalized;
            if (dir.sqrMagnitude < 1e-4f) dir = Vector2.down;
            var margin = 70f;
            var tx = dir.x != 0 ? (center.x - margin) / Mathf.Abs(dir.x) : float.MaxValue;
            var ty = dir.y != 0 ? (center.y - margin) / Mathf.Abs(dir.y) : float.MaxValue;
            var edge = center + dir * Mathf.Min(tx, ty);
            arrow.rectTransform.anchorMin = arrow.rectTransform.anchorMax = Vector2.zero;
            arrow.rectTransform.anchoredPosition = edge;
            arrow.rectTransform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg - 90);
        }

        public void Tick(float charge, float reload, bool canShoot, bool aimOnTarget, WeaponDef weapon, PlayerState? me)
        {
            var now = Time.unscaledTime;
            var c = !canShoot ? new Color(1, 1, 1, 0.35f) : aimOnTarget ? Visuals.WeaponColor(weapon.Id) : Color.white;
            crosshairRing.color = c;
            // 조준 원 크기: 산탄은 넓게
            var ringSize = weapon.Pellets > 0 ? 170 : weapon.ProjectileSpeed > 0 ? 130 : 90;
            crosshairRing.rectTransform.parent.GetComponent<RectTransform>().sizeDelta = new Vector2(ringSize, ringSize);
            chargeRing.fillAmount = charge > 0 ? charge : reload < 1 ? reload : 0;
            chargeRing.color = charge > 0 ? (charge >= 1 ? Color.white : Visuals.WeaponColor(weapon.Id)) : new Color(1, 1, 1, 0.8f);

            markers.gameObject.SetActive(now - markerAt < 0.35f);
            var hurtT = (now - hurtAt) / 0.4f;
            hurt.color = new Color(1, 0, 0, hurtT < 1 ? 0.6f * (1 - hurtT) : 0);
            burn.color = new Color(1, 0.43f, 0, me != null && me.BurnMs > 0 ? 0.4f + 0.15f * Mathf.Sin(now * 16) : 0);
            soak.color = new Color(0.16f, 0.43f, 1, me != null && me.SoakMs > 0 ? 0.45f : 0);
            shock.color = new Color(0.7f, 0.9f, 1, me != null && me.ShockMs > 0 ? UnityEngine.Random.Range(0.2f, 0.6f) : 0);
            protect.color = new Color(1, 1, 1, me != null && me.ProtectMs > 0 ? 0.3f + 0.2f * Mathf.Sin(now * 12) : 0);
            banner.gameObject.SetActive(now - bannerAt < 1.1f);

            for (var i = floaters.Count - 1; i >= 0; i--)
            {
                var (t, at, from) = floaters[i];
                var k = (now - at) / 0.9f;
                if (k >= 1)
                {
                    UnityEngine.Object.Destroy(t.gameObject);
                    floaters.RemoveAt(i);
                    continue;
                }
                t.rectTransform.anchoredPosition = from + new Vector2(0, k * 120);
                var col = t.color;
                col.a = 1 - k * k;
                t.color = col;
            }
        }

        public void Show(bool on) => Root.gameObject.SetActive(on);
    }
}
