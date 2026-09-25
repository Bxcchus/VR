using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CampusExplorer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CampusExplorer.Editor
{
    /// <summary>
    /// Reference-matching pass for the runtime tour. It changes only presentation:
    /// camera exposure, the authored HDRI environment, broken foliage materials and
    /// the small realtime contribution used by the three interactive switches.
    /// </summary>
    public static class ModernVillaVisualStabilizationPass
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string EvidenceRoot = "Evidence/visual-stabilization";
        const string BeforeFolder = EvidenceRoot + "/before";
        const string AfterFolder = EvidenceRoot + "/after";
        const string AuditPath = EvidenceRoot + "/unity-visual-audit.md";
        const string SkyMaterialPath = "Assets/CampusExplorer/Lighting/ModernVillaReferenceSky.mat";
        const string HdriPath = "Assets/CampusExplorer/External/ModernLuxuryVilla/Textures/horn-koppe_spring_8k.exr";
        const string BranchTexturePath = "Assets/CampusExplorer/External/ModernLuxuryVilla/Textures/Tree Branch.png";
        const string ShrubTexturePath = "Assets/CampusExplorer/External/ModernLuxuryVilla/Textures/Captura de pantalla_21-2-2026_14174_www.google.com.png";
        const int VillaLayer = 31;

        sealed class View
        {
            public readonly string key;
            public readonly string sourceCamera;
            public readonly float eyeY;
            public View(string key, string sourceCamera, float eyeY)
            {
                this.key = key;
                this.sourceCamera = sourceCamera;
                this.eyeY = eyeY;
            }
        }

        static readonly View[] Views =
        {
            new("exterior-front", "Camera.004", 67.00f),
            new("kitchen", "cocina", 66.58f),
            new("living-room", "sala de estar", 66.58f),
            new("dining-room", "comedor", 66.58f),
            new("master-bedroom", "suit principal", 69.80f),
            new("bathroom", "baño principal", 69.80f),
            new("pool-terrace", "Camera.006", 66.50f),
        };

        [MenuItem("Journey Through XR/Modern Villa/Visual stabilization/Audit and capture BEFORE")]
        public static void AuditAndCaptureBefore()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(BeforeFolder);
            CaptureViews(scene, BeforeFolder);
            WriteAudit(scene, "before");
            AssetDatabase.Refresh();
            Debug.Log("[Visual Stabilization] BEFORE audit and seven captures completed.");
        }

        [MenuItem("Journey Through XR/Modern Villa/Visual stabilization/Apply fixes and capture AFTER")]
        public static void ApplyFixesAndCaptureAfter()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(AfterFolder);

            FixFoliageMaterials();
            PlayerSettings.colorSpace = ColorSpace.Linear;
            ConfigureReferenceEnvironment();
            ConfigureStableLighting();
            ConfigureXrCamera();
            TuneInteractiveLights();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            CaptureViews(scene, AfterFolder);
            WriteAudit(scene, "after");
            WriteModifiedAssetsReport();
            AssetDatabase.Refresh();
            Debug.Log("[Visual Stabilization] Visual fixes applied and seven AFTER captures completed.");
        }

        [MenuItem("Journey Through XR/Modern Villa/Visual stabilization/Capture AFTER only")]
        public static void CaptureAfterOnly()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(AfterFolder);
            CaptureViews(scene, AfterFolder);
            WriteAudit(scene, "after");
            WriteModifiedAssetsReport();
            AssetDatabase.Refresh();
            Debug.Log("[Visual Stabilization] Seven AFTER captures and final audit completed.");
        }

        [MenuItem("Journey Through XR/Modern Villa/Visual stabilization/Validate final state")]
        public static void ValidateFinalState()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var main = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include)
                .FirstOrDefault(camera => camera.CompareTag("MainCamera"));
            Require(main != null, "Main Camera exists");
            Require(main.GetComponent<ModernVillaExposure>() != null, "XR camera exposure is active");
            Require(RenderSettings.skybox != null && AssetDatabase.GetAssetPath(RenderSettings.skybox) == SkyMaterialPath,
                "reference HDRI sky is active");

            var branchTexture = AssetDatabase.LoadAssetAtPath<Texture>(BranchTexturePath);
            foreach (var path in BranchMaterialPaths())
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                Require(material != null, "branch material exists: " + path);
                Require(material.GetTexture("_MainTex") == branchTexture &&
                        material.GetFloat("_HasOpacityMap") < 0.5f,
                    "branch cards use the main texture alpha without multiplying its red channel: " + path);
            }

            var treeRenderer = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include)
                .FirstOrDefault(renderer => renderer.name == "Tree_Cluster_Garden");
            Require(treeRenderer != null && treeRenderer.sharedMaterials.Any(material => material != null &&
                    material.shader != null && material.shader.name.Contains("Cutout", StringComparison.OrdinalIgnoreCase) &&
                    material.GetTexture("_MainTex") == branchTexture),
                "the exterior tree renderer uses the repaired cutout branch material");

            var importedLights = ImportedVillaLights().ToArray();
            Require(importedLights.Length >= 30 && importedLights.All(light => !light.enabled),
                "FBX lights with incompatible physical-unit intensities are disabled");
            var stableRoot = GameObject.Find("Visual Stabilization Lighting");
            Require(stableRoot != null && stableRoot.GetComponentsInChildren<Light>(true).Length == 8,
                "one directional and seven local stabilization lights are present");

            var interactiveRoot = GameObject.Find("InteractiveLights");
            Require(interactiveRoot != null, "InteractiveLights hierarchy remains present");
            var interactiveLights = interactiveRoot.GetComponentsInChildren<Light>(true);
            Require(interactiveLights.Length == 3, "three interactive realtime lights remain present");
            Require(interactiveLights.All(light => light.shadows == LightShadows.None &&
                                                   light.lightmapBakeType == LightmapBakeType.Realtime &&
                                                   light.intensity <= 0.9f),
                "interactive lights are Quest-friendly and exposure-safe");

            Require(scene.name == "ModernVillaTour" && GameObject.Find("XR Player") != null &&
                    GameObject.Find("InteractiveLights") != null,
                "tour scene and critical XR/interaction hierarchies remain intact");
            Debug.Log("[Visual Stabilization] Final state validated: HDRI, XR exposure, foliage alpha and 3 restrained realtime lights.");
        }

        static void FixFoliageMaterials()
        {
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(BranchTexturePath);
            if (texture == null) throw new FileNotFoundException("Authored branch texture missing", BranchTexturePath);

            ConfigureFoliageTextureImporter(BranchTexturePath);
            ConfigureFoliageTextureImporter(ShrubTexturePath);

            var branchVisual = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/CampusExplorer/Materials/ModernLuxuryVillaVisual/Tree_Branch.001.mat");
            if (branchVisual == null) throw new FileNotFoundException("Visual branch material missing.");
            var cutoutShader = Shader.Find("Campus Explorer/Modern Villa Visual/Cutout");
            if (cutoutShader == null) throw new InvalidOperationException("Visual cutout shader missing.");
            branchVisual.shader = cutoutShader;

            foreach (var path in BranchMaterialPaths())
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null) continue;
                material.SetTexture("_MainTex", texture);
                material.SetTexture("_OpacityMap", texture);
                // This image already has an alpha mask. Its red channel is leaf color,
                // not opacity, so using it a second time erases most foliage.
                material.SetFloat("_HasOpacityMap", 0f);
                material.SetFloat("_Cutoff", 0.42f);
                material.SetColor("_Color", Color.white);
                EditorUtility.SetDirty(material);
            }

            var modelPath = "Assets/CampusExplorer/External/ModernLuxuryVilla/Modern_Residence_Interior.fbx";
            var modelImporter = AssetImporter.GetAtPath(modelPath) as ModelImporter;
            if (modelImporter == null) throw new InvalidOperationException("Villa FBX importer is unavailable.");
            var branchIdentifier = new AssetImporter.SourceAssetIdentifier(typeof(Material), "Tree Branch.001");
            var externalMap = modelImporter.GetExternalObjectMap();
            if (!externalMap.TryGetValue(branchIdentifier, out var mapped) || mapped != branchVisual)
            {
                modelImporter.AddRemap(branchIdentifier, branchVisual);
                modelImporter.SaveAndReimport();
            }

            foreach (var renderer in UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var index = 0; index < materials.Length; index++)
                {
                    var material = materials[index];
                    if (material == null || (!material.name.Contains("Tree Branch", StringComparison.OrdinalIgnoreCase) &&
                                             !material.name.Contains("Tree_Branch", StringComparison.OrdinalIgnoreCase)))
                        continue;
                    materials[index] = branchVisual;
                    changed = true;
                }
                if (!changed) continue;
                renderer.sharedMaterials = materials;
                EditorUtility.SetDirty(renderer);
            }
        }

        static void ConfigureFoliageTextureImporter(string path)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return;
            var changed = !importer.alphaIsTransparency || !importer.mipmapEnabled ||
                          !importer.mipMapsPreserveCoverage ||
                          !Mathf.Approximately(importer.alphaTestReferenceValue, 0.42f);
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.mipMapsPreserveCoverage = true;
            importer.alphaTestReferenceValue = 0.42f;
            var android = importer.GetPlatformTextureSettings("Android");
            changed |= !android.overridden || android.maxTextureSize != 1024 ||
                       android.format != TextureImporterFormat.ASTC_6x6 ||
                       android.textureCompression != TextureImporterCompression.CompressedHQ;
            android.overridden = true;
            android.maxTextureSize = 1024;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.textureCompression = TextureImporterCompression.CompressedHQ;
            importer.SetPlatformTextureSettings(android);
            if (changed) importer.SaveAndReimport();
        }

        static IEnumerable<string> BranchMaterialPaths()
        {
            yield return "Assets/CampusExplorer/Materials/ModernLuxuryVilla/Tree_Branch.001.mat";
            yield return "Assets/CampusExplorer/Materials/ModernLuxuryVillaVisual/Tree_Branch.001.mat";
        }

        static void ConfigureReferenceEnvironment()
        {
            var hdri = AssetDatabase.LoadAssetAtPath<Texture>(HdriPath);
            if (hdri == null) throw new FileNotFoundException("Original villa HDRI missing", HdriPath);
            var shader = Shader.Find("Skybox/Panoramic");
            if (shader == null) throw new InvalidOperationException("Skybox/Panoramic shader is unavailable.");

            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyMaterialPath);
            if (sky == null)
            {
                sky = new Material(shader) { name = "ModernVillaReferenceSky" };
                AssetDatabase.CreateAsset(sky, SkyMaterialPath);
            }
            else if (sky.shader != shader)
            {
                sky.shader = shader;
            }
            sky.SetTexture("_MainTex", hdri);
            sky.SetFloat("_Exposure", 0.82f);
            sky.SetFloat("_Rotation", 12f);
            EditorUtility.SetDirty(sky);

            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Skybox;
            RenderSettings.ambientIntensity = 0.90f;
            RenderSettings.reflectionIntensity = 0.92f;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.defaultReflectionResolution = 128;
            DynamicGI.UpdateEnvironment();
        }

        static void ConfigureXrCamera()
        {
            var camera = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include)
                .FirstOrDefault(item => item.CompareTag("MainCamera"));
            if (camera == null) throw new InvalidOperationException("Main Camera was not found.");
            camera.allowHDR = true;
            camera.allowMSAA = true;
            camera.nearClipPlane = Mathf.Min(camera.nearClipPlane, 0.08f);
            camera.renderingPath = RenderingPath.Forward;
            var exposure = camera.GetComponent<ModernVillaExposure>();
            if (exposure == null) exposure = camera.gameObject.AddComponent<ModernVillaExposure>();
            ConfigureExposure(exposure);
            EditorUtility.SetDirty(camera);
            EditorUtility.SetDirty(exposure);
        }

        static void ConfigureStableLighting()
        {
            foreach (var light in ImportedVillaLights())
            {
                light.enabled = false;
                EditorUtility.SetDirty(light);
            }

            var existing = GameObject.Find("Visual Stabilization Lighting");
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
            var root = new GameObject("Visual Stabilization Lighting");

            var sunObject = new GameObject("Reference Exterior Key", typeof(Light));
            sunObject.transform.SetParent(root.transform, false);
            sunObject.transform.rotation = Quaternion.Euler(42f, -28f, 0f);
            var sun = sunObject.GetComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(0.79f, 0.86f, 1f);
            sun.intensity = 0.60f;
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.55f;
            sun.cullingMask = 1 << VillaLayer;

            CreateFill(root.transform, "Kitchen Reference Fill", new Vector3(-0.50f, 67.10f, -24.77f),
                new Color(1f, 0.86f, 0.72f), 1.45f, 5.2f);
            CreateFill(root.transform, "Living Room Reference Fill", new Vector3(-7.10f, 66.95f, -25.40f),
                new Color(1f, 0.82f, 0.68f), 1.20f, 5.5f);
            CreateFill(root.transform, "Dining Room Reference Fill", new Vector3(-3.70f, 66.95f, -30.45f),
                new Color(1f, 0.88f, 0.76f), 1.50f, 4.8f);
            CreateFill(root.transform, "Staircase Reference Fill", new Vector3(-6.50f, 67.45f, -31.40f),
                new Color(1f, 0.78f, 0.60f), 1.15f, 4.5f);
            CreateFill(root.transform, "Master Bedroom Reference Fill", new Vector3(2.20f, 70.10f, -29.25f),
                new Color(1f, 0.80f, 0.67f), 1.10f, 5.0f);
            CreateFill(root.transform, "Bathroom Reference Fill", new Vector3(-7.25f, 70.10f, -34.70f),
                new Color(0.93f, 0.96f, 1f), 1.30f, 4.5f);
            CreateFill(root.transform, "Terrace Reference Fill", new Vector3(-5.30f, 67.00f, -50.45f),
                new Color(0.75f, 0.86f, 1f), 0.70f, 6.0f);
        }

        static IEnumerable<Light> ImportedVillaLights()
        {
            return UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include).Where(light =>
            {
                var root = light.transform;
                while (root.parent != null) root = root.parent;
                return root.name.StartsWith("Authoritative Modern Luxury Villa", StringComparison.Ordinal);
            });
        }

        static void CreateFill(Transform parent, string name, Vector3 position, Color color, float intensity, float range)
        {
            var gameObject = new GameObject(name, typeof(Light));
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.position = position;
            var light = gameObject.GetComponent<Light>();
            light.type = LightType.Point;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.renderMode = LightRenderMode.Auto;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.cullingMask = 1 << VillaLayer;
        }

        static void ConfigureExposure(ModernVillaExposure exposure)
        {
            exposure.exposureEV = 1.35f;
            exposure.contrast = 0.98f;
            exposure.saturation = 0.90f;
            exposure.shadowLift = 0.028f;
            exposure.whiteBalance = new Color(1.03f, 0.96f, 1.02f, 1f);
        }

        static void TuneInteractiveLights()
        {
            var root = GameObject.Find("InteractiveLights");
            if (root == null) throw new InvalidOperationException("InteractiveLights root is missing.");
            foreach (var light in root.GetComponentsInChildren<Light>(true))
            {
                if (light.name.Contains("Kitchen", StringComparison.OrdinalIgnoreCase))
                {
                    light.intensity = 0.85f;
                    light.range = 3.8f;
                    light.color = new Color(1f, 0.84f, 0.68f);
                }
                else if (light.name.Contains("Living", StringComparison.OrdinalIgnoreCase))
                {
                    light.intensity = 0.68f;
                    light.range = 4.2f;
                    light.color = new Color(1f, 0.77f, 0.60f);
                }
                else if (light.name.Contains("Master", StringComparison.OrdinalIgnoreCase))
                {
                    light.intensity = 0.32f;
                    light.range = 3.5f;
                    light.color = new Color(1f, 0.76f, 0.62f);
                }
                light.shadows = LightShadows.None;
                light.renderMode = LightRenderMode.ForcePixel;
                light.cullingMask = 1 << VillaLayer;
                EditorUtility.SetDirty(light);
            }
        }

        static void CaptureViews(Scene scene, string folder)
        {
            Directory.CreateDirectory(folder);
            var allCameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            var temporary = new List<GameObject>();
            try
            {
                foreach (var view in Views)
                {
                    var source = allCameras.FirstOrDefault(camera =>
                        string.Equals(camera.name, view.sourceCamera, StringComparison.OrdinalIgnoreCase));
                    if (source == null)
                    {
                        Debug.LogWarning("[Visual Stabilization] Source camera missing: " + view.sourceCamera);
                        continue;
                    }
                    var holder = new GameObject("Visual Stabilization Capture - " + view.key);
                    SceneManager.MoveGameObjectToScene(holder, scene);
                    temporary.Add(holder);
                    holder.transform.SetPositionAndRotation(
                        new Vector3(source.transform.position.x, view.eyeY, source.transform.position.z),
                        source.transform.rotation);
                    var camera = holder.AddComponent<Camera>();
                    camera.enabled = false;
                    camera.fieldOfView = 70f;
                    camera.nearClipPlane = 0.05f;
                    camera.farClipPlane = 350f;
                    camera.cullingMask = 1 << VillaLayer;
                    camera.clearFlags = CameraClearFlags.Skybox;
                    camera.allowHDR = true;
                    camera.allowMSAA = true;
                    camera.renderingPath = RenderingPath.Forward;
                    var exposure = holder.AddComponent<ModernVillaExposure>();
                    ConfigureExposure(exposure);
                    Capture(camera, Path.Combine(folder, view.key + ".png"));
                }
            }
            finally
            {
                foreach (var item in temporary) UnityEngine.Object.DestroyImmediate(item);
            }
        }

        static void Capture(Camera camera, string path)
        {
            var old = RenderTexture.active;
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGBHalf)
            {
                antiAliasing = 1,
                name = "Visual stabilization evidence"
            };
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                image.Apply(false, false);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = old;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        static void WriteAudit(Scene scene, string phase)
        {
            Directory.CreateDirectory(EvidenceRoot);
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            var lights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include)
                .OrderBy(light => HierarchyPath(light.transform)).ToArray();
            var probes = UnityEngine.Object.FindObjectsByType<ReflectionProbe>(FindObjectsInactive.Include);
            var materials = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include)
                .SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null).Distinct().ToArray();
            var cutoutRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include)
                .Where(renderer => renderer.sharedMaterials.Any(material => material != null &&
                    ((material.shader != null && material.shader.name.Contains("Cutout", StringComparison.OrdinalIgnoreCase)) ||
                     material.name.Contains("Shrub", StringComparison.OrdinalIgnoreCase) ||
                     material.name.Contains("Branch", StringComparison.OrdinalIgnoreCase))))
                .OrderBy(renderer => HierarchyPath(renderer.transform)).ToArray();

            var text = new StringBuilder();
            text.AppendLine("# Unity visual audit — " + phase);
            text.AppendLine();
            text.AppendLine("- Scene: `" + scene.path + "`");
            text.AppendLine("- Color space: `" + PlayerSettings.colorSpace + "`");
            text.AppendLine("- Rendering path: built-in / `" + cameras.FirstOrDefault(camera => camera.CompareTag("MainCamera"))?.renderingPath + "`");
            text.AppendLine("- Skybox: `" + (RenderSettings.skybox != null ? AssetDatabase.GetAssetPath(RenderSettings.skybox) : "none") + "`");
            text.AppendLine("- Ambient: `" + RenderSettings.ambientMode + "`, intensity `" + RenderSettings.ambientIntensity.ToString("0.###", CultureInfo.InvariantCulture) + "`");
            text.AppendLine("- Reflection intensity: `" + RenderSettings.reflectionIntensity.ToString("0.###", CultureInfo.InvariantCulture) + "`");
            text.AppendLine("- Enabled lights: `" + lights.Count(light => light.enabled && light.gameObject.activeInHierarchy) + "` / `" + lights.Length + "`");
            text.AppendLine("- Reflection probes: `" + probes.Length + "`");
            text.AppendLine("- Distinct renderer materials: `" + materials.Length + "`");
            text.AppendLine("- Loaded baked lightmaps: `" + (LightmapSettings.lightmaps?.Length ?? 0) + "`");
            text.AppendLine("- Renderers using foliage/cutout cards: `" + cutoutRenderers.Length + "`");
            text.AppendLine();
            text.AppendLine("## Lights");
            text.AppendLine();
            text.AppendLine("| Hierarchy | Type | Bake | Enabled | Intensity | Range | Shadows | Mask |");
            text.AppendLine("|---|---|---|---:|---:|---:|---|---:|");
            foreach (var light in lights)
                text.AppendLine("| `" + HierarchyPath(light.transform) + "` | " + light.type + " | " + light.lightmapBakeType +
                    " | " + (light.enabled && light.gameObject.activeInHierarchy) + " | " +
                    light.intensity.ToString("0.###", CultureInfo.InvariantCulture) + " | " +
                    light.range.ToString("0.###", CultureInfo.InvariantCulture) + " | " + light.shadows + " | `0x" +
                    light.cullingMask.ToString("X8") + "` |");
            text.AppendLine();
            text.AppendLine("## Foliage and cutout cards");
            text.AppendLine();
            foreach (var renderer in cutoutRenderers)
            {
                var descriptions = renderer.sharedMaterials.Where(material => material != null).Select(material =>
                    material.name + " / " + (material.shader != null ? material.shader.name : "missing shader") +
                    " / main=" + (material.HasProperty("_MainTex") && material.GetTexture("_MainTex") != null ? material.GetTexture("_MainTex").name : "none") +
                    " / opacity=" + (material.HasProperty("_OpacityMap") && material.GetTexture("_OpacityMap") != null ? material.GetTexture("_OpacityMap").name : "none"));
                text.AppendLine("- `" + HierarchyPath(renderer.transform) + "` — bounds `" + renderer.bounds.size.ToString("F2") + "` — " + string.Join("; ", descriptions));
            }
            File.WriteAllText(AuditPath.Replace(".md", "-" + phase + ".md"), text.ToString(), Encoding.UTF8);
        }

        static string HierarchyPath(Transform transform)
        {
            var value = transform.name;
            while (transform.parent != null)
            {
                transform = transform.parent;
                value = transform.name + "/" + value;
            }
            return value;
        }

        static void WriteModifiedAssetsReport()
        {
            File.WriteAllText(EvidenceRoot + "/modified-assets.md",
                "# Modified assets — visual stabilization\n\n" +
                "- `ModernVillaTour.unity`: authored HDRI environment, XR-camera exposure and reduced interactive-light contribution.\n" +
                "- `ModernVillaReferenceSky.mat`: panoramic sky using the package's original `horn-koppe_spring_8k.exr`.\n" +
                "- `Tree_Branch.001.mat` (source and visual clone): restored the package's `Tree Branch.png` color/alpha map.\n" +
                "- `Tree Branch.png.meta`: alpha coverage and Android ASTC 6x6 import settings.\n" +
                "- `ModernVillaExposure.cs` / shader: shared, neutral white-balance control for review and XR cameras.\n" +
                "- `ModernVillaXRSpawnGuard.cs`: editor debug overlay is hidden by default and toggled with F8.\n\n" +
                "No mesh, collision, locomotion, POI, menu, teleport, tour, door, player-height or light-switch logic was changed.\n",
                Encoding.UTF8);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Visual stabilization validation failed: " + message);
        }
    }
}
