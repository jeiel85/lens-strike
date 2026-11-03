#nullable enable
// 코드로 만드는 그림 재료: 고리·원·그라데이션 스프라이트, AR 속 투사체·상대 표시·훈련 표적.
using LensStrike.Core;
using UnityEngine;

namespace LensStrike.App
{
    public static class Visuals
    {
        private static Sprite? ring, disc, vignette;

        public static Sprite Ring => ring ??= MakeSprite(256, (x, y) =>
        {
            var d = Mathf.Sqrt(x * x + y * y);
            return Mathf.Clamp01(1 - Mathf.Abs(d - 0.9f) / 0.06f);
        });

        public static Sprite Disc => disc ??= MakeSprite(128, (x, y) => Mathf.Clamp01((1 - Mathf.Sqrt(x * x + y * y)) * 8));

        /// <summary>가운데는 투명, 가장자리로 갈수록 진해지는 테두리 효과</summary>
        public static Sprite Vignette => vignette ??= MakeSprite(256, (x, y) =>
        {
            var d = Mathf.Sqrt(x * x + y * y) / 1.2f;
            return Mathf.Clamp01((d - 0.45f) / 0.55f);
        });

        private static Sprite MakeSprite(int size, System.Func<float, float, float> alpha)
        {
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[size * size];
            for (var j = 0; j < size; j++)
            for (var i = 0; i < size; i++)
            {
                var x = (i + 0.5f) / size * 2 - 1;
                var y = (j + 0.5f) / size * 2 - 1;
                px[j * size + i] = new Color32(255, 255, 255, (byte)(alpha(x, y) * 255));
            }
            tex.SetPixels32(px);
            tex.Apply();
            return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100);
        }

        private static Material? glow, ghost;

        /// <summary>빌드에 포함된 머티리얼(에디터 빌더가 Resources에 만든 것)을 쓴다</summary>
        public static Material Glow => glow ??= Resources.Load<Material>("LensStrike/Glow");
        public static Material Ghost => ghost ??= Resources.Load<Material>("LensStrike/Ghost");

        public static GameObject Sphere(string name, Color color, float diameter, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.localScale = Vector3.one * diameter;
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            r.SetPropertyBlock(block);
            return go;
        }

        public static GameObject Capsule(string name, Color color, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            var r = go.GetComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", color);
            r.SetPropertyBlock(block);
            return go;
        }

        /// <summary>캡슐 메시(높이 2, 지름 1)를 선분 A–B와 반지름에 맞춘다</summary>
        public static void FitCapsule(Transform t, Vector3 a, Vector3 b, float radius)
        {
            var mid = (a + b) * 0.5f;
            var dir = b - a;
            var len = dir.magnitude;
            t.position = mid;
            t.rotation = len > 1e-4f ? Quaternion.FromToRotation(Vector3.up, dir / len) : Quaternion.identity;
            t.localScale = new Vector3(radius * 2, (len + radius * 2) * 0.5f, radius * 2);
        }

        public static Color WeaponColor(WeaponId w) => Ui.Hex(Defs.Weapons[w].ColorHex);
    }

    /// <summary>날아가는 마법탄. 내 탄은 판정을 하고, 상대 탄은 연출만 한다.</summary>
    public sealed class Projectile
    {
        public WeaponId Weapon;
        public System.Numerics.Vector3 Origin;
        public System.Numerics.Vector3 Direction;
        public System.Numerics.Vector3 Position;
        public float Traveled;
        public bool Mine;
        public GameObject View = null!;
    }
}
