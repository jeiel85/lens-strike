#nullable enable
// 효과음은 코드로 합성하고(음원 파일 없음), 진동은 안드로이드 VibrationEffect로 짧게 준다.
using System;
using System.Collections.Generic;
using LensStrike.Core;
using UnityEngine;

namespace LensStrike.App
{
    public sealed class Sfx : MonoBehaviour
    {
        private const int Rate = 44100;
        private readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        private AudioSource[] voices = Array.Empty<AudioSource>();
        private int next;

        private void Awake()
        {
            voices = new AudioSource[8];
            for (var i = 0; i < voices.Length; i++)
            {
                voices[i] = gameObject.AddComponent<AudioSource>();
                voices[i].playOnAwake = false;
            }
            var rng = new System.Random(7);
            float Noise() => (float)(rng.NextDouble() * 2 - 1);

            Make("rifle", 0.1f, (t, d) => (Noise() * 0.5f + Square(Sweep(t, d, 180, 60)) * 0.3f) * Env(t, d, 0.003f));
            Make("shotgun", 0.32f, (t, d) => (Noise() * 0.9f + Sine(Sweep(t, d, 120, 40)) * 0.6f) * Env(t, d, 0.003f));
            Make("plasma", 0.28f, (t, d) => (Saw(Sweep(t, d, 1500, 200)) * 0.5f + Sine(Sweep(t, d, 1800, 400)) * 0.3f) * Env(t, d, 0.005f));
            Make("fireball", 0.5f, (t, d) => (Noise() * 0.6f + Tri(Sweep(t, d, 220, 90)) * 0.4f) * Env(t, d, 0.02f));
            Make("waterball", 0.25f, (t, d) => (Sine(Sweep(t, d, 300, 900)) * 0.6f + Noise() * 0.15f) * Env(t, d, 0.01f));
            Make("charge", 0.5f, (t, d) => Sine(Sweep(t, d, 300, 1200)) * 0.25f * Env(t, d, 0.05f));
            Make("empty", 0.05f, (t, d) => Square(Sweep(t, d, 900, 700)) * 0.25f * Env(t, d, 0.002f));
            Make("reload", 0.25f, (t, d) => Noise() * 0.35f * (t < 0.05f || (t > 0.18f && t < 0.23f) ? 1f : 0f));
            Make("hit", 0.07f, (t, d) => Square(Sweep(t, d, 1200, 1000)) * 0.35f * Env(t, d, 0.002f));
            Make("headshot", 0.14f, (t, d) => Square(Sweep(t, d, 1800, 1600)) * 0.35f * Env(t, d, 0.002f) + (t > 0.05f ? Sine(t * 2400) * 0.2f : 0));
            Make("kill", 0.3f, (t, d) => Tri(t < 0.1f ? t * 600 : t * 900) * 0.45f * Env(t, d, 0.005f));
            Make("hurt", 0.2f, (t, d) => (Saw(Sweep(t, d, 160, 70)) * 0.6f + Noise() * 0.3f) * Env(t, d, 0.005f));
            Make("death", 0.8f, (t, d) => Saw(Sweep(t, d, 300, 40)) * 0.5f * Env(t, d, 0.01f));
            Make("splash", 0.3f, (t, d) => Noise() * 0.5f * Env(t, d, 0.01f));
        }

        // 위상(주파수 적분)을 근사: 선형 주파수 변화 f(t) = a + (b-a)·t/d 의 적분
        private static float Sweep(float t, float d, float a, float b) => a * t + (b - a) * t * t / (2 * d);
        private static float Sine(float phase) => Mathf.Sin(phase * Mathf.PI * 2);
        private static float Square(float phase) => Sine(phase) >= 0 ? 1 : -1;
        private static float Saw(float phase) => 2 * (phase - Mathf.Floor(phase + 0.5f));
        private static float Tri(float phase) => 2 * Mathf.Abs(Saw(phase)) - 1;
        private static float Env(float t, float d, float attack) => t < attack ? t / attack : Mathf.Pow(1 - (t - attack) / Mathf.Max(1e-3f, d - attack), 2);

        private void Make(string id, float duration, Func<float, float, float> f)
        {
            var n = Mathf.CeilToInt(duration * Rate);
            var data = new float[n];
            for (var i = 0; i < n; i++) data[i] = Mathf.Clamp(f(i / (float)Rate, duration), -1, 1) * 0.8f;
            var clip = AudioClip.Create(id, n, 1, Rate, false);
            clip.SetData(data, 0);
            clips[id] = clip;
        }

        public void Play(string id, float volume = 1f)
        {
            if (!clips.TryGetValue(id, out var clip)) return;
            var v = voices[next++ % voices.Length];
            v.pitch = 1f;
            v.PlayOneShot(clip, volume);
        }

        public void Fire(WeaponId w, float charge)
        {
            switch (w)
            {
                case WeaponId.M16:
                case WeaponId.K2: Play("rifle"); break;
                case WeaponId.Shotgun: Play("shotgun"); break;
                case WeaponId.Plasma: Play("plasma", 0.6f + 0.4f * charge); break;
                case WeaponId.Fireball: Play("fireball"); break;
                case WeaponId.Waterball: Play("waterball"); break;
            }
        }

        public static void Vibrate(long ms)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                using var vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
                using var effect = new AndroidJavaClass("android.os.VibrationEffect");
                using var e = effect.CallStatic<AndroidJavaObject>("createOneShot", ms, -1);
                vibrator.Call("vibrate", e);
            }
            catch (Exception)
            {
                // 진동 없는 기기·권한 문제: 진동만 생략한다
            }
            if (ms < 0) Handheld.Vibrate(); // 실행되지 않음. Unity가 VIBRATE 권한을 매니페스트에 넣도록 API 사용을 남긴다.
#else
            // 에디터·기타 플랫폼은 진동 없음. 아래 줄은 실행되지 않지만 Unity가 VIBRATE 권한을 넣도록 API 사용을 남긴다.
            if (ms < 0) Handheld.Vibrate();
#endif
        }
    }
}
