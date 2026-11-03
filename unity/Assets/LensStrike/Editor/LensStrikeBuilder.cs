// 씬·머티리얼·플레이어·XR 설정을 코드로 만들고 안드로이드 APK를 빌드한다.
// 씬 YAML을 손으로 고치지 않도록, 프로젝트 구성은 모두 이 스크립트가 책임진다.
//   에디터 메뉴: LensStrike/Setup Project, LensStrike/Build Android
//   배치 모드:  -executeMethod LensStrike.EditorTools.LensStrikeBuilder.SetupAndBuildAndroid
using System;
using System.IO;
using System.Linq;
using LensStrike.App;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Management;

namespace LensStrike.EditorTools
{
    public static class LensStrikeBuilder
    {
        public const string ScenePath = "Assets/LensStrike/Scenes/Main.unity";
        private const string MaterialDir = "Assets/LensStrike/Resources/LensStrike";
        public const string AndroidId = "com.jeiel85.lensstrike";
        public const string ApkPath = "Builds/Android/LensStrikeAR.apk";

        [MenuItem("LensStrike/Setup Project")]
        public static void Setup()
        {
            ConfigurePlayer();
            ConfigureXr();
            CreateMaterials();
            CreateScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[LensStrike] 프로젝트 구성 완료");
        }

        /// <summary>
        /// 릴리스 서명 정보. 저장소 밖(git 제외)의 ../.keystore/release-signing.properties 에서 읽는다.
        /// 없으면 디버그 키로 서명한다(개발용).
        /// </summary>
        private const string SigningProperties = "../.keystore/release-signing.properties";

        [MenuItem("LensStrike/Build Android")]
        public static void BuildAndroid()
        {
            EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
            Directory.CreateDirectory(Path.GetDirectoryName(ApkPath)!);
            var signed = ApplyReleaseSigning();
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { ScenePath },
                    locationPathName = ApkPath,
                    target = BuildTarget.Android,
                    targetGroup = BuildTargetGroup.Android,
                    options = BuildOptions.None,
                });
                var s = report.summary;
                Debug.Log($"[LensStrike] 빌드 결과: {s.result}, 서명 {(signed ? "릴리스 키" : "디버그 키")}, 오류 {s.totalErrors}");
                if (s.result != BuildResult.Succeeded) throw new Exception($"안드로이드 빌드 실패: {s.result}");
            }
            finally
            {
                // 키스토어 경로·비밀번호를 프로젝트 설정에 남기지 않는다
                PlayerSettings.Android.useCustomKeystore = false;
                PlayerSettings.Android.keystoreName = "";
                PlayerSettings.Android.keystorePass = "";
                PlayerSettings.Android.keyaliasName = "";
                PlayerSettings.Android.keyaliasPass = "";
                AssetDatabase.SaveAssets();
            }
        }

        private static bool ApplyReleaseSigning()
        {
            var path = Path.GetFullPath(SigningProperties);
            if (!File.Exists(path))
            {
                Debug.LogWarning("[LensStrike] 릴리스 서명 정보가 없어 디버그 키로 서명합니다.");
                return false;
            }
            var props = File.ReadAllLines(path)
                .Where(l => l.Contains('=') && !l.TrimStart().StartsWith("#"))
                .Select(l => l.Split(new[] { '=' }, 2))
                .ToDictionary(p => p[0].Trim(), p => p[1].Trim());
            string Need(string key) =>
                props.TryGetValue(key, out var v) && v.Length > 0 ? v : throw new Exception($"서명 정보에 {key}가 없습니다.");
            var store = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, Need("storeFile")));
            if (!File.Exists(store)) throw new Exception("키스토어 파일을 찾을 수 없습니다.");
            PlayerSettings.Android.useCustomKeystore = true;
            PlayerSettings.Android.keystoreName = store;
            PlayerSettings.Android.keystorePass = Need("storePassword");
            PlayerSettings.Android.keyaliasName = Need("keyAlias");
            PlayerSettings.Android.keyaliasPass = Need("keyPassword");
            return true;
        }

        /// <summary>배치 모드용: 구성 후 빌드. 실패하면 0이 아닌 종료 코드로 끝난다.</summary>
        public static void SetupAndBuildAndroid()
        {
            try
            {
                Setup();
                BuildAndroid();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        public static void SetupOnly()
        {
            try
            {
                Setup();
                EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(1);
            }
        }

        private static void ConfigurePlayer()
        {
            PlayerSettings.companyName = "jeiel85";
            PlayerSettings.productName = "Lens Strike AR";
            PlayerSettings.bundleVersion = "0.2.0";
            PlayerSettings.Android.bundleVersionCode = 1;
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.Android, AndroidId);
            PlayerSettings.SetApplicationIdentifier(UnityEditor.Build.NamedBuildTarget.iOS, AndroidId);
            PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // ARCore는 안드로이드 7.0(API 24) 이상. 여유를 두고 8.0(API 26)부터 지원한다.
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel26;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
            // 세로·가로 모두 지원
            PlayerSettings.defaultInterfaceOrientation = UIOrientation.AutoRotation;
            PlayerSettings.allowedAutorotateToPortrait = true;
            PlayerSettings.allowedAutorotateToLandscapeLeft = true;
            PlayerSettings.allowedAutorotateToLandscapeRight = true;
            PlayerSettings.allowedAutorotateToPortraitUpsideDown = false;
            PlayerSettings.iOS.cameraUsageDescription = "AR로 상대 위치를 추적하는 데 카메라를 씁니다. 영상은 기기 밖으로 보내지 않습니다.";
            PlayerSettings.SplashScreen.showUnityLogo = false;

            // 새 입력 시스템(InputSystemUIInputModule)을 쓰므로 입력 처리 방식을 '둘 다'로 둔다
            var player = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (player != null)
            {
                var so = new SerializedObject(player);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue != 2)
                {
                    prop.intValue = 2;
                    so.ApplyModifiedProperties();
                }
            }
        }

        private static void ConfigureXr()
        {
            if (!EditorBuildSettings.TryGetConfigObject(XRGeneralSettings.k_SettingsKey, out XRGeneralSettingsPerBuildTarget perTarget) || perTarget == null)
            {
                Directory.CreateDirectory("Assets/XR");
                perTarget = ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                AssetDatabase.CreateAsset(perTarget, "Assets/XR/XRGeneralSettingsPerBuildTarget.asset");
                EditorBuildSettings.AddConfigObject(XRGeneralSettings.k_SettingsKey, perTarget, true);
            }
            foreach (var group in new[] { BuildTargetGroup.Android, BuildTargetGroup.iOS })
            {
                if (!perTarget.HasSettingsForBuildTarget(group)) perTarget.CreateDefaultSettingsForBuildTarget(group);
                if (!perTarget.HasManagerSettingsForBuildTarget(group)) perTarget.CreateDefaultManagerSettingsForBuildTarget(group);
                var settings = perTarget.SettingsForBuildTarget(group);
                settings.InitManagerOnStart = true;
                var loader = group == BuildTargetGroup.Android ? "UnityEngine.XR.ARCore.ARCoreLoader" : "UnityEngine.XR.ARKit.ARKitLoader";
                XRPackageMetadataStore.AssignLoader(settings.AssignedSettings, loader, group);
                EditorUtility.SetDirty(settings);
            }
            EditorUtility.SetDirty(perTarget);
        }

        private static void CreateMaterials()
        {
            Directory.CreateDirectory(MaterialDir);
            // 조명 없이 색·투명도만 쓰는 셰이더. 머티리얼 에셋이 참조하므로 빌드에 포함된다.
            var shader = Shader.Find("Sprites/Default");
            foreach (var name in new[] { "Glow", "Ghost" })
            {
                var path = $"{MaterialDir}/{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader) { name = name };
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = shader;
                mat.color = Color.white;
                EditorUtility.SetDirty(mat);
            }
        }

        private static void CreateScene()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath)!);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var session = new GameObject("AR Session", typeof(ARSession), typeof(ARInputManager));
            session.GetComponent<ARSession>().attemptUpdate = true;

            var originGo = new GameObject("XR Origin", typeof(XROrigin));
            var offset = new GameObject("Camera Offset");
            offset.transform.SetParent(originGo.transform, false);
            var camGo = new GameObject("Main Camera", typeof(Camera), typeof(ARCameraManager), typeof(ARCameraBackground), typeof(TrackedPoseDriver), typeof(AudioListener));
            camGo.tag = "MainCamera";
            camGo.transform.SetParent(offset.transform, false);
            var cam = camGo.GetComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.black;
            cam.nearClipPlane = 0.1f;
            cam.farClipPlane = 200f;
            var tpd = camGo.GetComponent<TrackedPoseDriver>();
            tpd.positionInput = new InputActionProperty(new InputAction("Position", binding: "<HandheldARInputDevice>/devicePosition"));
            tpd.rotationInput = new InputActionProperty(new InputAction("Rotation", binding: "<HandheldARInputDevice>/deviceRotation"));
            var origin = originGo.GetComponent<XROrigin>();
            origin.Camera = cam;
            origin.CameraFloorOffsetObject = offset;
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;

            var nmGo = new GameObject("NetworkManager", typeof(NetworkManager), typeof(UnityTransport));
            var nm = nmGo.GetComponent<NetworkManager>();
            nm.NetworkConfig = new NetworkConfig { NetworkTransport = nmGo.GetComponent<UnityTransport>(), ConnectionApproval = false };

            var boot = new GameObject("Bootstrap", typeof(Bootstrap));
            boot.GetComponent<Bootstrap>().ArCamera = cam;

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        }
    }
}
