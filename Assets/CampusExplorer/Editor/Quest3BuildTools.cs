using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using UnityEngine.XR.OpenXR.Features.Interactions;
using UnityEngine.XR.OpenXR.Features.MetaQuestSupport;

namespace CampusExplorer.Editor
{
    // Editor-only helpers for Android deployment and PC Quest Link testing.
    /// <summary>
    /// Keeps the headset setup reproducible. The audit is safe to run without the
    /// Android module; configuration and APK generation become available as soon
    /// as Unity Hub has installed Android Build Support and its two dependencies.
    /// </summary>
    public static class Quest3BuildTools
    {
        const string MainMenuScenePath = "Assets/CampusExplorer/Scenes/MainMenu.unity";
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string XrSettingsPath = "Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
        const string InteractionLayersPath = "Assets/XRI/Settings/Resources/InteractionLayerSettings.asset";
        const string LoaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        const string ValidationAutoFixSessionKey = "CampusExplorer.Quest3.ValidationAutoFix.2026-09-17";
        const string ValidationAutoFixRequest = "Evidence/quest3-validation-autofix.request";
        const string OpenTourSceneRequest = "Evidence/open-modern-villa-tour.request";
        const string QuestBuildRequest = "Evidence/quest3-build.request";
        const string QuestBuildSessionKey = "CampusExplorer.Quest3.Build.2026-09-18.3";
        const string QuestLinkPrepareRequest = "Evidence/quest-link-prepare.request";
        const string QuestLinkPrepareSessionKey = "CampusExplorer.QuestLink.Prepare.2026-09-22.4";
        static double questBuildNotBefore;
        static double openTourNotBefore;
        static double questLinkPrepareNotBefore;

        [InitializeOnLoadMethod]
        static void RunRequestedQuestLinkPreparation()
        {
            if (!File.Exists(QuestLinkPrepareRequest)
                || SessionState.GetBool(QuestLinkPrepareSessionKey, false))
                return;

            File.Delete(QuestLinkPrepareRequest);
            SessionState.SetBool(QuestLinkPrepareSessionKey, true);
            questLinkPrepareNotBefore = EditorApplication.timeSinceStartup + 2d;
            EditorApplication.update += PrepareQuestLinkWhenEditorIsReady;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.isPlaying = false;
        }

        static void PrepareQuestLinkWhenEditorIsReady()
        {
            if (Application.isPlaying || EditorApplication.isPlaying || EditorApplication.isCompiling
                || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.timeSinceStartup < questLinkPrepareNotBefore)
                return;

            EditorApplication.update -= PrepareQuestLinkWhenEditorIsReady;
            try
            {
                ConfigureQuestLinkTest();
                EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);
                Debug.Log("[Campus Explorer] QUEST LINK READY - Standalone Windows, D3D11 and MainMenu loaded.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [InitializeOnLoadMethod]
        static void RunRequestedQuestBuildInOpenEditor()
        {
            if (!File.Exists(QuestBuildRequest)
                || SessionState.GetBool(QuestBuildSessionKey, false))
                return;
            SessionState.SetBool(QuestBuildSessionKey, true);
            File.Delete(QuestBuildRequest);
            questBuildNotBefore = EditorApplication.timeSinceStartup + 2d;
            EditorApplication.update += BuildWhenEditorIsReady;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.isPlaying = false;
        }

        static void BuildWhenEditorIsReady()
        {
            if (Application.isPlaying || EditorApplication.isPlaying || EditorApplication.isCompiling
                || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.timeSinceStartup < questBuildNotBefore)
                return;

            EditorApplication.update -= BuildWhenEditorIsReady;
            try
            {
                BuildApk();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [InitializeOnLoadMethod]
        static void RunRequestedValidationFixInOpenEditor()
        {
            if (!File.Exists(ValidationAutoFixRequest)
                || SessionState.GetBool(ValidationAutoFixSessionKey, false))
                return;
            SessionState.SetBool(ValidationAutoFixSessionKey, true);
            File.Delete(ValidationAutoFixRequest);
            EditorApplication.delayCall += () =>
            {
                try
                {
                    FixProjectValidation();
                }
                catch (Exception exception)
                {
                    Debug.LogException(exception);
                }
            };
        }

        [InitializeOnLoadMethod]
        static void OpenRequestedTourSceneInEditor()
        {
            if (!File.Exists(OpenTourSceneRequest))
                return;
            File.Delete(OpenTourSceneRequest);
            openTourNotBefore = EditorApplication.timeSinceStartup + 3d;
            EditorApplication.update += OpenTourSceneWhenEditorIsReady;
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.isPlaying = false;
        }

        static void OpenTourSceneWhenEditorIsReady()
        {
            if (Application.isPlaying || EditorApplication.isPlaying || EditorApplication.isCompiling
                || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode
                || EditorApplication.timeSinceStartup < openTourNotBefore)
                return;

            EditorApplication.update -= OpenTourSceneWhenEditorIsReady;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Debug.Log($"[Campus Explorer] OPENED TOUR SCENE - {scene.path}");
        }

        [MenuItem("Campus Explorer/Quest 3/Audit prerequisites")]
        public static void AuditPrerequisites()
        {
            var androidRoot = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer");
            var module = Directory.Exists(androidRoot);
            var sdk = Directory.Exists(Path.Combine(androidRoot, "SDK"));
            var ndk = Directory.Exists(Path.Combine(androidRoot, "NDK"));
            var jdk = Directory.Exists(Path.Combine(androidRoot, "OpenJDK"));
            var supported = BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android);

            Directory.CreateDirectory("Evidence");
            File.WriteAllLines("Evidence/quest3-prerequisites.txt", new[]
            {
                $"Unity={Application.unityVersion}",
                $"Editor={EditorApplication.applicationPath}",
                $"AndroidBuildSupport={module}",
                $"AndroidSDK={sdk}",
                $"AndroidNDK={ndk}",
                $"OpenJDK={jdk}",
                $"AndroidBuildTargetSupported={supported}",
                "Headset=Meta Quest 3",
                "Runtime=OpenXR",
                "RequiredArchitecture=ARM64",
                "RecommendedMinSdk=32",
                "RecommendedTargetSdk=34"
            });

            var state = module && sdk && ndk && jdk && supported ? "READY" : "MISSING ANDROID MODULES";
            Debug.Log($"[Campus Explorer] QUEST 3 PREREQUISITES {state}. See Evidence/quest3-prerequisites.txt.");
        }

        [MenuItem("Campus Explorer/Quest 3/Configure project")]
        public static void ConfigureProject()
        {
            AuditPrerequisites();
            RequireAndroidSupport();

            var perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XrSettingsPath);
            if (perTarget == null)
                throw new FileNotFoundException($"XR settings asset not found: {XrSettingsPath}");

            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Android))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Android);

            var manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Android);
            if (!XRPackageMetadataStore.AssignLoader(manager, LoaderType, BuildTargetGroup.Android))
                throw new InvalidOperationException("Unable to assign the OpenXR loader to Android.");

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Android);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings == null)
                throw new InvalidOperationException("Android OpenXR settings could not be created.");

            Enable<MetaQuestFeature>(settings);
            Enable<OculusTouchControllerProfile>(settings);
            Enable<MetaQuestTouchPlusControllerProfile>(settings);

            PlayerSettings.productName = "Campus Explorer XR";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.alexis.campusexplorerxr");
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevel34;
            PlayerSettings.Android.preferredInstallLocation = AndroidPreferredInstallLocation.Auto;
            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            settings.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
            SetTeleportInteractionLayer();

            EditorUtility.SetDirty(perTarget);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();
            Debug.Log("[Campus Explorer] QUEST 3 CONFIGURATION PASS - Android OpenXR, Meta Quest Support, Touch Plus, ARM64, SDK 32/34 and IL2CPP configured.");
        }

        [MenuItem("Campus Explorer/Quest 3/Prepare Quest Link test")]
        public static void ConfigureQuestLinkTest()
        {
            var perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XrSettingsPath);
            if (perTarget == null)
                throw new FileNotFoundException($"XR settings asset not found: {XrSettingsPath}");

            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);

            var manager = perTarget.ManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if (!XRPackageMetadataStore.AssignLoader(manager, LoaderType, BuildTargetGroup.Standalone))
                throw new InvalidOperationException("Unable to assign the OpenXR loader to Standalone.");

            var generalSettings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            generalSettings.InitManagerOnStart = true;

            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            if (settings == null)
                throw new InvalidOperationException("Standalone OpenXR settings could not be created.");

            Enable<OculusTouchControllerProfile>(settings);
            Enable<MetaQuestTouchPlusControllerProfile>(settings);
            EditorUtility.SetDirty(perTarget);
            EditorUtility.SetDirty(generalSettings);
            EditorUtility.SetDirty(manager);
            EditorUtility.SetDirty(settings);
            AssetDatabase.SaveAssets();

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone,
                    BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Unable to switch the editor to Standalone Windows 64-bit.");

            // Meta Link is materially more stable with D3D11 in the editor on this
            // workstation. This setting is Standalone-only; the Quest APK keeps Vulkan.
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D11 });
            AssetDatabase.SaveAssets();

            Directory.CreateDirectory("Evidence");
            File.WriteAllLines("Evidence/quest-link-preflight.txt", new[]
            {
                $"Unity={Application.unityVersion}",
                $"BuildTarget={EditorUserBuildSettings.activeBuildTarget}",
                "XRLoader=OpenXR",
                "OculusTouchControllerProfile=True",
                "MetaQuestTouchPlusControllerProfile=True",
                $"StandaloneGraphicsAPIs={string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.StandaloneWindows64))}",
                $"AndroidGraphicsAPIs={string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))}",
                "TestMode=Meta Quest Link (PC VR)",
                "StandaloneAndroidPerformanceValidated=False"
            });
            Debug.Log("[Campus Explorer] QUEST LINK PREFLIGHT PASS - Standalone OpenXR and Quest controller profiles configured.");
        }

        [MenuItem("Campus Explorer/Quest 3/Prepare Desktop Simulator test")]
        public static void ConfigureDesktopSimulatorTest()
        {
            var perTarget = AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(XrSettingsPath);
            if (perTarget == null)
                throw new FileNotFoundException($"XR settings asset not found: {XrSettingsPath}");

            if (!perTarget.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone))
                perTarget.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);

            var generalSettings = perTarget.SettingsForBuildTarget(BuildTargetGroup.Standalone);
            generalSettings.InitManagerOnStart = false;
            EditorUtility.SetDirty(generalSettings);
            EditorUtility.SetDirty(perTarget);

            if (!EditorUserBuildSettings.SwitchActiveBuildTarget(
                    BuildTargetGroup.Standalone,
                    BuildTarget.StandaloneWindows64))
                throw new InvalidOperationException("Unable to switch the editor to Standalone Windows 64-bit.");

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
            PlayerSettings.SetGraphicsAPIs(
                BuildTarget.StandaloneWindows64,
                new[] { GraphicsDeviceType.Direct3D11 });
            AssetDatabase.SaveAssets();
            EditorSceneManager.OpenScene(MainMenuScenePath, OpenSceneMode.Single);

            Directory.CreateDirectory("Evidence");
            File.WriteAllLines("Evidence/desktop-simulator-preflight.txt", new[]
            {
                $"Unity={Application.unityVersion}",
                $"BuildTarget={EditorUserBuildSettings.activeBuildTarget}",
                "StandaloneInitializeXROnStartup=False",
                "XRInteractionSimulator=True",
                "TestMode=Desktop XR Interaction Simulator"
            });
            Debug.Log("[Campus Explorer] DESKTOP SIMULATOR READY - Meta OpenXR startup disabled for PC Play Mode.");
        }

        [MenuItem("Campus Explorer/Quest 3/Fix project validation")]
        public static void FixProjectValidation()
        {
            // Regenerate the package settings first. This is Unity OpenXR's own fix
            // for duplicated embedded feature objects after package upgrades.
            var issues = new List<OpenXRFeature.ValidationRule>();
            OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);
            var duplicate = issues.FirstOrDefault(issue =>
                issue.message != null && issue.message.Contains("duplicate settings", StringComparison.OrdinalIgnoreCase));
            duplicate?.fixIt?.Invoke();
            AssetDatabase.Refresh();

            PlayerSettings.runInBackground = true;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            // Gamma is supported on Quest when OpenGLES is not in the API list. Vulkan
            // also matches the project's Quest 3 rendering target.
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });

            var settings = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (settings != null)
            {
                settings.latencyOptimization = OpenXRSettings.LatencyOptimization.PrioritizeInputPolling;
                EditorUtility.SetDirty(settings);
            }

            SetTeleportInteractionLayer();
            ImportTmpEssentialsIfMissing();

            // Apply the package-owned automatic fixes, including current input-control
            // migrations and the URP upscaling compatibility setting.
            issues.Clear();
            OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);
            foreach (var issue in issues.Where(issue => issue.fixItAutomatic && issue.fixIt != null).ToArray())
                issue.fixIt();

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            WriteValidationReport();
            Debug.Log("[Campus Explorer] QUEST 3 PROJECT VALIDATION FIX PASS. See Evidence/quest3-project-validation.txt.");
        }

        [MenuItem("Campus Explorer/Quest 3/Build APK")]
        public static void BuildApk()
        {
            ConfigureProject();
            Directory.CreateDirectory("Builds/Quest3");
            var report = BuildPipeline.BuildPlayer(
                new[] { MainMenuScenePath, ScenePath },
                "Builds/Quest3/CampusExplorerXR-Quest3.apk",
                BuildTarget.Android,
                BuildOptions.Development);

            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Quest 3 APK build failed: {report.summary.result}");

            Debug.Log($"[Campus Explorer] QUEST 3 APK BUILD PASS - {report.summary.totalSize} bytes, {report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings.");
            ConfigureQuestLinkTest();
            Debug.Log("[Campus Explorer] Restored Standalone Windows/D3D11 after the Android build for Meta Quest Link Play Mode.");
        }

        static void Enable<T>(OpenXRSettings settings) where T : UnityEngine.XR.OpenXR.Features.OpenXRFeature
        {
            var feature = settings.GetFeature<T>();
            if (feature == null)
                throw new InvalidOperationException($"OpenXR feature not found for Android: {typeof(T).Name}");
            feature.enabled = true;
            EditorUtility.SetDirty(feature);
        }

        static void SetTeleportInteractionLayer()
        {
            var layerSettings = AssetDatabase.LoadMainAssetAtPath(InteractionLayersPath);
            if (layerSettings == null)
                throw new FileNotFoundException($"XR Interaction Layer settings not found: {InteractionLayersPath}");

            var serialized = new SerializedObject(layerSettings);
            var layers = serialized.FindProperty("m_LayerNames");
            if (layers == null)
                throw new InvalidOperationException("XR Interaction Layer settings do not expose m_LayerNames.");
            if (layers.arraySize < 32)
                layers.arraySize = 32;
            layers.GetArrayElementAtIndex(31).stringValue = "Teleport";
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(layerSettings);
        }

        static void ImportTmpEssentialsIfMissing()
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/TextMesh Pro/Resources/TMP Settings.asset") != null)
                return;
            TMPro.TMP_PackageResourceImporter.ImportResources(true, false, false);
        }

        static void WriteValidationReport()
        {
            var issues = new List<OpenXRFeature.ValidationRule>();
            OpenXRProjectValidation.GetCurrentValidationIssues(issues, BuildTargetGroup.Android);
            Directory.CreateDirectory("Evidence");
            File.WriteAllLines("Evidence/quest3-project-validation.txt", new[]
            {
                $"Unity={Application.unityVersion}",
                $"RunInBackground={PlayerSettings.runInBackground}",
                $"ColorSpace={PlayerSettings.colorSpace}",
                $"AndroidGraphicsAPIs={string.Join(",", PlayerSettings.GetGraphicsAPIs(BuildTarget.Android))}",
                $"TeleportLayer31={ReadTeleportLayer()}",
                $"TMPEssentials={(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>("Assets/TextMesh Pro/Resources/TMP Settings.asset") != null)}",
                $"RemainingOpenXRIssues={issues.Count}",
            }.Concat(issues.Select(issue => $"- {issue.message}")));
        }

        static string ReadTeleportLayer()
        {
            var layerSettings = AssetDatabase.LoadMainAssetAtPath(InteractionLayersPath);
            if (layerSettings == null)
                return "MISSING";
            var serialized = new SerializedObject(layerSettings);
            var layers = serialized.FindProperty("m_LayerNames");
            return layers != null && layers.arraySize > 31
                ? layers.GetArrayElementAtIndex(31).stringValue
                : "MISSING";
        }

        static void RequireAndroidSupport()
        {
            var androidRoot = Path.Combine(EditorApplication.applicationContentsPath, "PlaybackEngines", "AndroidPlayer");
            if (Directory.Exists(androidRoot)
                && BuildPipeline.IsBuildTargetSupported(BuildTargetGroup.Android, BuildTarget.Android))
                return;

            throw new InvalidOperationException(
                $"Android Build Support is missing from Unity {Application.unityVersion}. " +
                "In Unity Hub, open Installs, use the gear next to this Unity version, choose Add modules, " +
                "then install Android Build Support, Android SDK & NDK Tools, and OpenJDK.");
        }
    }
}
