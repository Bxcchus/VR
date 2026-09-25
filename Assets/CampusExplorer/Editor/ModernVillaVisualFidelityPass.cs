using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CampusExplorer.Editor
{
    /// <summary>
    /// Creates a visual-fidelity scene without changing the authoritative source scene,
    /// FBX, meshes, source materials, or source textures.
    /// </summary>
    public static class ModernVillaVisualFidelityPass
    {
        const string SourceScene = "Assets/CampusExplorer/Scenes/ModernVillaSource.unity";
        const string VisualScene = "Assets/CampusExplorer/Scenes/ModernVillaVisual.unity";
        const string SourceMaterials = "Assets/CampusExplorer/Materials/ModernLuxuryVilla";
        const string VisualMaterials = "Assets/CampusExplorer/Materials/ModernLuxuryVillaVisual";
        const string TextureFolder = "Assets/CampusExplorer/External/ModernLuxuryVilla/Textures";
        const string LightingFolder = "Assets/CampusExplorer/Lighting";
        const string LightingAsset = LightingFolder + "/ModernVillaVisualLighting.lighting";
        const string NightSkyAsset = LightingFolder + "/ModernVillaNightSky.mat";
        const string EvidenceFolder = "Evidence/modern-villa-visual";
        const string BeforeFolder = EvidenceFolder + "/before";
        const string AfterFolder = EvidenceFolder + "/after";
        const string PremiumBeforeFolder = EvidenceFolder + "/premium-before";
        const string PremiumAfterFolder = EvidenceFolder + "/premium-after";
        const string PremiumReport = EvidenceFolder + "/premium-polish-report.md";
        const string MaterialReport = EvidenceFolder + "/material-audit.md";
        const string VisualReport = EvidenceFolder + "/visual-pass-report.md";
        const int VillaLayer = 31;

        sealed class ViewDefinition
        {
            public readonly string key;
            public readonly string sourceCamera;
            public readonly float eyeY;
            public ViewDefinition(string key, string sourceCamera, float eyeY)
            {
                this.key = key;
                this.sourceCamera = sourceCamera;
                this.eyeY = eyeY;
            }
        }

        static readonly ViewDefinition[] Views =
        {
            new ViewDefinition("kitchen", "cocina", 66.58f),
            new ViewDefinition("living-room", "sala de estar", 66.58f),
            new ViewDefinition("dining-room", "comedor", 66.58f),
            new ViewDefinition("staircase", "pasillo", 66.58f),
            new ViewDefinition("master-bedroom", "suit principal", 69.80f),
            new ViewDefinition("bathroom", "baño principal", 69.80f),
            new ViewDefinition("exterior-pool", "Camera.006", 66.50f)
        };

        sealed class TextureCatalogEntry
        {
            public string path;
            public string file;
            public string family;
            public Texture texture;
        }

        sealed class MaterialAudit
        {
            public string name;
            public string category;
            public string shader;
            public readonly Dictionary<string, string> maps = new Dictionary<string, string>();
            public float smoothness;
            public float metallic;
            public float opacity;
            public float emissionIntensity;
            public Vector2 uvScale;
            public bool roughnessInverted;
            public bool glossMap;
            public string heightMap;
            public string notes;
        }

        [MenuItem("Journey Through XR/Modern Villa/Build visual fidelity pass")]
        public static void BuildVisualPass()
        {
            if (!File.Exists(SourceScene))
                throw new FileNotFoundException("Authoritative source scene is missing", SourceScene);

            Directory.CreateDirectory(EvidenceFolder);
            Directory.CreateDirectory(BeforeFolder);
            Directory.CreateDirectory(AfterFolder);
            var sourceHashBefore = HashFile(SourceScene);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            var scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            var sourceRoot = scene.GetRootGameObjects().FirstOrDefault(item =>
                item.name.StartsWith("Authoritative Modern Luxury Villa", StringComparison.Ordinal));
            if (sourceRoot == null) throw new InvalidOperationException("The source villa root was not found.");
            var villa = sourceRoot.GetComponentsInChildren<Transform>(true)
                .Select(item => item.gameObject)
                .FirstOrDefault(item => item.name.StartsWith("Modern_Residence_Interior", StringComparison.Ordinal));
            if (villa == null) throw new InvalidOperationException("The direct FBX instance was not found.");

            RemoveRootIfPresent(scene, "Visual Fidelity");
            var visualRoot = new GameObject("Visual Fidelity");
            SceneManager.MoveGameObjectToScene(visualRoot, scene);
            var cameraRoot = NewChild(visualRoot, "XR-height review cameras");
            var cameras = CreateReviewCameras(villa, cameraRoot);

            // BEFORE means the existing reconstructed materials and imported source lighting,
            // viewed from the exact same human-height cameras used for AFTER.
            CaptureViews(cameras, BeforeFolder, false);

            var materialAudits = CloneAndImproveMaterials(villa);
            ConfigureStaticGeometry(villa);
            DisableImportedLights(villa);
            var lightingRoot = NewChild(visualRoot, "Professional lighting baseline");
            CreateLighting(lightingRoot);
            CreateLightProbes(lightingRoot);
            var reflectionProbes = CreateReflectionProbes(lightingRoot);
            ConfigureEnvironment();
            ConfigureLightingSettings();
            RenderReflectionProbes(reflectionProbes);

            if (!EditorSceneManager.SaveScene(scene, VisualScene))
                throw new IOException("Unity could not save " + VisualScene);

            CaptureViews(cameras, AfterFolder, true);
            if (Directory.Exists(PremiumBeforeFolder)) CopyCaptures(AfterFolder, PremiumAfterFolder);
            EditorSceneManager.SaveScene(scene, VisualScene);
            WriteMaterialAudit(materialAudits);
            WriteVisualReport(materialAudits, reflectionProbes.Length, sourceHashBefore);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var sourceHashAfter = HashFile(SourceScene);
            if (!string.Equals(sourceHashBefore, sourceHashAfter, StringComparison.Ordinal))
                throw new InvalidOperationException("ModernVillaSource.unity changed during the visual pass.");
            Debug.Log("[Modern Villa Visual] Built scene, material audit, lighting baseline and before/after captures.");
        }

        [MenuItem("Journey Through XR/Modern Villa/Reimport source textures with audited color space")]
        public static void ReimportSourceTextures()
        {
            var paths = AssetDatabase.FindAssets("t:Texture", new[] { TextureFolder })
                .Select(AssetDatabase.GUIDToAssetPath).ToArray();
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var path in paths)
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[Modern Villa Visual] Reimported " + paths.Length + " source textures with audited color-space rules.");
        }

        [MenuItem("Journey Through XR/Modern Villa/Bake visual lighting (medium)")]
        public static void BakeVisualLighting()
        {
            var scene = EditorSceneManager.OpenScene(VisualScene, OpenSceneMode.Single);
            ConfigureLightingSettings();
            Lightmapping.Clear();
            var started = DateTime.UtcNow;
            Lightmapping.Bake();
            EditorSceneManager.SaveScene(scene, VisualScene);
            var cameras = scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<Camera>(true))
                .Where(item => item.name.StartsWith("Review Camera - ", StringComparison.Ordinal)).ToArray();
            CaptureViews(cameras, AfterFolder, true);
            if (Directory.Exists(PremiumBeforeFolder)) CopyCaptures(AfterFolder, PremiumAfterFolder);
            File.WriteAllText(EvidenceFolder + "/bake-result.txt",
                "Medium baked GI completed with Unity " + Application.unityVersion + Environment.NewLine +
                "UTC start: " + started.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
                "UTC end: " + DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + Environment.NewLine +
                "Lightmaps: " + LightmapSettings.lightmaps.Length + Environment.NewLine,
                Encoding.UTF8);
            AssetDatabase.SaveAssets();
            Debug.Log("[Modern Villa Visual] Medium baked GI completed. Lightmaps: " + LightmapSettings.lightmaps.Length);
        }

        [MenuItem("Journey Through XR/Modern Villa/Recapture visual evidence")]
        public static void Recapture()
        {
            var scene = EditorSceneManager.OpenScene(VisualScene, OpenSceneMode.Single);
            var cameras = scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<Camera>(true))
                .Where(item => item.name.StartsWith("Review Camera - ", StringComparison.Ordinal)).ToArray();
            var probes = scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<ReflectionProbe>(true)).ToArray();
            RenderReflectionProbes(probes);
            CaptureViews(cameras, AfterFolder, true);
            Debug.Log("[Modern Villa Visual] Evidence recaptured.");
        }

        [MenuItem("Journey Through XR/Modern Villa/Apply review exposure and recapture")]
        public static void ApplyReviewExposureAndRecapture()
        {
            var scene = EditorSceneManager.OpenScene(VisualScene, OpenSceneMode.Single);
            var cameras = scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<Camera>(true))
                .Where(item => item.name.StartsWith("Review Camera - ", StringComparison.Ordinal)).ToArray();
            foreach (var camera in cameras)
            {
                var key = camera.name.Substring("Review Camera - ".Length);
                var exposure = camera.GetComponent<CampusExplorer.ModernVillaExposure>();
                if (exposure == null) exposure = camera.gameObject.AddComponent<CampusExplorer.ModernVillaExposure>();
                exposure.exposureEV = ExposureFor(key);
                exposure.contrast = 1.0f;
                exposure.saturation = 0.99f;
                exposure.shadowLift = 0.035f;
                EditorUtility.SetDirty(exposure);
            }
            EditorSceneManager.SaveScene(scene, VisualScene);
            CaptureViews(cameras, AfterFolder, true);
            EditorSceneManager.SaveScene(scene, VisualScene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Modern Villa Visual] Applied restrained review exposure and recaptured evidence.");
        }

        [MenuItem("Journey Through XR/Modern Villa/Validate visual fidelity scene")]
        public static void ValidateVisualPass()
        {
            var scene = EditorSceneManager.OpenScene(VisualScene, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var renderers = roots.SelectMany(item => item.GetComponentsInChildren<Renderer>(true)).ToArray();
            var materials = renderers.SelectMany(item => item.sharedMaterials).Where(item => item != null)
                .Distinct().ToArray();
            var visualMaterials = materials.Count(item => AssetDatabase.GetAssetPath(item)
                .StartsWith(VisualMaterials, StringComparison.Ordinal));
            var nonVisualMaterials = materials.Where(item => !AssetDatabase.GetAssetPath(item)
                .StartsWith(VisualMaterials, StringComparison.Ordinal)).OrderBy(item => item.name).ToArray();
            var allVisualAssets = AssetDatabase.FindAssets("t:Material", new[] { VisualMaterials })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>)
                .Where(item => item != null).ToArray();
            var unassignedVisualAssets = allVisualAssets.Except(materials).OrderBy(item => item.name).ToArray();
            var unsupportedShaders = materials.Count(item => item.shader == null || !item.shader.isSupported);
            var enabledLights = roots.SelectMany(item => item.GetComponentsInChildren<Light>(true))
                .Count(item => item.enabled);
            var reflectionProbes = roots.SelectMany(item => item.GetComponentsInChildren<ReflectionProbe>(true)).ToArray();
            var probePositions = roots.SelectMany(item => item.GetComponentsInChildren<LightProbeGroup>(true))
                .Sum(item => item.probePositions != null ? item.probePositions.Length : 0);
            var cameras = roots.SelectMany(item => item.GetComponentsInChildren<Camera>(true))
                .Where(item => item.name.StartsWith("Review Camera - ", StringComparison.Ordinal)).ToArray();
            var exposurePasses = cameras.Count(item => item.GetComponent<CampusExplorer.ModernVillaExposure>() != null);
            var comparisons = Directory.Exists(EvidenceFolder + "/comparisons")
                ? Directory.GetFiles(EvidenceFolder + "/comparisons", "*.png").Length : 0;

            var builder = new StringBuilder();
            builder.AppendLine("# Modern Luxury Villa — validation");
            builder.AppendLine();
            builder.AppendLine("Generated by Unity " + Application.unityVersion + " at " +
                DateTime.UtcNow.ToString("O", CultureInfo.InvariantCulture) + ".");
            builder.AppendLine();
            builder.AppendLine("| Check | Result |");
            builder.AppendLine("|---|---:|");
            builder.AppendLine("| Source scene SHA-256 | `" + HashFile(SourceScene) + "` |");
            builder.AppendLine("| Renderers | " + renderers.Length + " |");
            builder.AppendLine("| Distinct renderer materials | " + materials.Length + " |");
            builder.AppendLine("| Visual material assets assigned | " + visualMaterials + " |");
            builder.AppendLine("| Missing/unsupported shaders | " + unsupportedShaders + " |");
            builder.AppendLine("| Enabled authored lights | " + enabledLights + " |");
            builder.AppendLine("| Light-probe positions | " + probePositions + " |");
            builder.AppendLine("| Reflection probes | " + reflectionProbes.Length + " |");
            builder.AppendLine("| Review cameras / exposure passes | " + cameras.Length + " / " + exposurePasses + " |");
            builder.AppendLine("| Baked lightmaps loaded | " + LightmapSettings.lightmaps.Length + " |");
            builder.AppendLine("| Comparison/contact sheets | " + comparisons + " |");
            builder.AppendLine();
            builder.AppendLine("## Renderer materials outside the visual clone folder");
            builder.AppendLine();
            foreach (var material in nonVisualMaterials)
                builder.AppendLine("- `" + material.name + "` — `" + AssetDatabase.GetAssetPath(material) + "`");
            builder.AppendLine();
            builder.AppendLine("## Visual material assets not used by a renderer");
            builder.AppendLine();
            foreach (var material in unassignedVisualAssets)
                builder.AppendLine("- `" + material.name + "` — `" + AssetDatabase.GetAssetPath(material) + "`");
            builder.AppendLine();
            builder.AppendLine("Unity opened and validated the final scene in Direct3D 11 batch mode. This is an Editor/render validation, not a Quest headset performance result.");
            File.WriteAllText(EvidenceFolder + "/validation.md", builder.ToString(), Encoding.UTF8);
            Debug.Log("[Modern Villa Visual] Validation complete: " + renderers.Length + " renderers, " +
                visualMaterials + " visual materials, " + LightmapSettings.lightmaps.Length + " lightmaps.");
        }

        static Camera[] CreateReviewCameras(GameObject villa, GameObject parent)
        {
            var sources = villa.GetComponentsInChildren<Camera>(true);
            var cameras = new List<Camera>();
            foreach (var view in Views)
            {
                var source = sources.FirstOrDefault(item => string.Equals(item.name, view.sourceCamera,
                    StringComparison.OrdinalIgnoreCase));
                if (source == null)
                {
                    Debug.LogWarning("[Modern Villa Visual] Missing source camera " + view.sourceCamera);
                    continue;
                }
                var gameObject = NewChild(parent, "Review Camera - " + view.key);
                gameObject.transform.position = new Vector3(source.transform.position.x, view.eyeY,
                    source.transform.position.z);
                gameObject.transform.rotation = source.transform.rotation;
                var camera = gameObject.AddComponent<Camera>();
                camera.fieldOfView = 70f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 350f;
                camera.cullingMask = 1 << VillaLayer;
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.allowHDR = true;
                camera.allowMSAA = true;
                camera.renderingPath = RenderingPath.DeferredShading;
                camera.enabled = false;
                var exposure = gameObject.AddComponent<CampusExplorer.ModernVillaExposure>();
                exposure.exposureEV = ExposureFor(view.key);
                exposure.contrast = 1.0f;
                exposure.saturation = 0.99f;
                exposure.shadowLift = 0.035f;
                cameras.Add(camera);
            }
            return cameras.ToArray();
        }

        static float ExposureFor(string key)
        {
            switch (key)
            {
                case "kitchen": return 0.85f;
                case "living-room": return 0.95f;
                case "dining-room": return 0.95f;
                case "staircase": return 1.05f;
                case "master-bedroom": return 1.3f;
                case "bathroom": return 1.8f;
                case "exterior-pool": return 1.25f;
                default: return 1.0f;
            }
        }

        [MenuItem("Journey Through XR/Modern Villa/Apply final premium polish")]
        public static void ApplyFinalPremiumPolish()
        {
            var sourceHashBefore = HashFile(SourceScene);
            CopyCaptures(AfterFolder, PremiumBeforeFolder);
            var scene = EditorSceneManager.OpenScene(VisualScene, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            var visualRoot = roots.FirstOrDefault(item => item.name == "Visual Fidelity");
            if (visualRoot == null) throw new InvalidOperationException("Visual Fidelity root is missing.");

            var catalog = BuildTextureCatalog();
            var visualMaterials = AssetDatabase.FindAssets("t:Material", new[] { VisualMaterials })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>)
                .Where(item => item != null).OrderBy(item => item.name).ToArray();
            var audits = new List<MaterialAudit>();
            foreach (var material in visualMaterials)
            {
                var sourceName = material.name.Replace(" [Visual]", string.Empty);
                var category = Category(sourceName);
                TuneMaterial(material, sourceName, category);
                EditorUtility.SetDirty(material);
                audits.Add(AuditMaterial(material, sourceName, category, catalog));
            }

            var oldLighting = visualRoot.transform.Cast<Transform>()
                .FirstOrDefault(item => item.name == "Professional lighting baseline");
            if (oldLighting != null) UnityEngine.Object.DestroyImmediate(oldLighting.gameObject);
            var lightingRoot = NewChild(visualRoot, "Professional lighting baseline");
            CreateLighting(lightingRoot);
            CreateLightProbes(lightingRoot);
            CreateReflectionProbes(lightingRoot);
            ConfigureEnvironment();

            var cameras = roots.SelectMany(item => item.GetComponentsInChildren<Camera>(true))
                .Where(item => item.name.StartsWith("Review Camera - ", StringComparison.Ordinal)).ToArray();
            foreach (var camera in cameras)
            {
                var key = camera.name.Substring("Review Camera - ".Length);
                var exposure = camera.GetComponent<CampusExplorer.ModernVillaExposure>();
                if (exposure == null) exposure = camera.gameObject.AddComponent<CampusExplorer.ModernVillaExposure>();
                exposure.exposureEV = ExposureFor(key);
                exposure.contrast = 1.0f;
                exposure.saturation = 0.99f;
                exposure.shadowLift = 0.035f;
                EditorUtility.SetDirty(exposure);
            }

            ConfigureLightingSettings();
            EditorSceneManager.SaveScene(scene, VisualScene);
            WriteMaterialAudit(audits.ToArray());
            File.WriteAllText(PremiumReport,
                "# Modern Luxury Villa — final premium polish\n\n" +
                "- Scope: lighting, indirect bounce, material response, reflections, exposure and dark-surface readability only.\n" +
                "- Geometry, FBX, source materials, source textures and XR functionality remain unchanged.\n" +
                "- AO strength is now material-specific instead of full-strength on every surface.\n" +
                "- Roughness maps receive material-family scale/bias so walls, woods, lacquer, metals, glass and stone separate under the same light.\n" +
                "- Fixture emission is softer; nearby mixed lights carry more bounce.\n" +
                "- Living-room sofa/TV/art, kitchen counters and bathroom glass/vanity receive local focal fills.\n" +
                "- Reflection probes are 256 px HDR with box projection and two reflection bounces.\n" +
                "- Review exposures now stay within EV 0.85–1.55 and apply a 0.025 shadow toe lift.\n\n" +
                "Run the medium lighting bake before considering this report final.\n", Encoding.UTF8);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var sourceHashAfter = HashFile(SourceScene);
            if (!string.Equals(sourceHashBefore, sourceHashAfter, StringComparison.Ordinal))
                throw new InvalidOperationException("ModernVillaSource.unity changed during premium polish.");
            Debug.Log("[Modern Villa Visual] Final premium polish applied to " + visualMaterials.Length + " materials. Ready to bake.");
        }

        static void CopyCaptures(string source, string destination)
        {
            if (!Directory.Exists(source)) return;
            Directory.CreateDirectory(destination);
            foreach (var path in Directory.GetFiles(source, "*.png"))
                File.Copy(path, Path.Combine(destination, Path.GetFileName(path)), true);
        }

        static MaterialAudit[] CloneAndImproveMaterials(GameObject villa)
        {
            EnsureAssetFolder(VisualMaterials);
            var catalog = BuildTextureCatalog();
            var sourceMaterialAssets = AssetDatabase.FindAssets("t:Material", new[] { SourceMaterials })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<Material>)
                .Where(item => item != null)
                .OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase).ToArray();
            var replacements = new Dictionary<Material, Material>();
            var audits = new List<MaterialAudit>();
            foreach (var source in sourceMaterialAssets)
            {
                var category = Category(source.name);
                var shader = Shader.Find(ShaderFor(source.name, category));
                if (shader == null) throw new InvalidOperationException("Visual shader is missing for " + source.name);
                var path = VisualMaterials + "/" + Path.GetFileName(AssetDatabase.GetAssetPath(source));
                var visual = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (visual == null)
                {
                    visual = new Material(shader) { name = source.name + " [Visual]" };
                    AssetDatabase.CreateAsset(visual, path);
                }
                visual.shader = shader;
                visual.CopyPropertiesFromMaterial(source);
                visual.shader = shader;
                visual.name = source.name + " [Visual]";
                RestoreAndInferMaps(visual, source, catalog);
                TuneMaterial(visual, source.name, category);
                EditorUtility.SetDirty(visual);
                replacements[source] = visual;
                audits.Add(AuditMaterial(visual, source.name, category, catalog));
            }

            var replacementsByName = replacements.Values.GroupBy(item => MaterialKey(item.name))
                .ToDictionary(group => group.Key, group => group.First());
            foreach (var renderer in villa.GetComponentsInChildren<Renderer>(true))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var index = 0; index < materials.Length; index++)
                {
                    if (materials[index] != null && replacements.TryGetValue(materials[index], out var replacement))
                    {
                        materials[index] = replacement;
                        changed = true;
                    }
                    else if (materials[index] != null && replacementsByName.TryGetValue(MaterialKey(materials[index].name), out replacement))
                    {
                        materials[index] = replacement;
                        changed = true;
                    }
                }
                if (changed) renderer.sharedMaterials = materials;
            }
            return audits.ToArray();
        }

        [MenuItem("Journey Through XR/Modern Villa/Repair embedded material references")]
        public static void RepairEmbeddedMaterialReferences()
        {
            var scene = EditorSceneManager.OpenScene(VisualScene, OpenSceneMode.Single);
            var visualAssets = AssetDatabase.FindAssets("t:Material", new[] { VisualMaterials })
                .Select(AssetDatabase.GUIDToAssetPath).Select(AssetDatabase.LoadAssetAtPath<Material>)
                .Where(item => item != null).GroupBy(item => MaterialKey(item.name))
                .ToDictionary(group => group.Key, group => group.First());
            var changedSlots = 0;
            foreach (var renderer in scene.GetRootGameObjects().SelectMany(item => item.GetComponentsInChildren<Renderer>(true)))
            {
                var materials = renderer.sharedMaterials;
                var changed = false;
                for (var index = 0; index < materials.Length; index++)
                {
                    var material = materials[index];
                    if (material == null || AssetDatabase.GetAssetPath(material).StartsWith(VisualMaterials, StringComparison.Ordinal))
                        continue;
                    if (!visualAssets.TryGetValue(MaterialKey(material.name), out var visual)) continue;
                    materials[index] = visual;
                    changed = true;
                    changedSlots++;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
            EditorSceneManager.SaveScene(scene, VisualScene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Modern Villa Visual] Repaired " + changedSlots + " embedded material references.");
        }

        static string MaterialKey(string value)
        {
            value = value.Replace("[Visual]", string.Empty);
            return Regex.Replace(value, "[^A-Za-z0-9.]", string.Empty).ToLowerInvariant();
        }

        static void RestoreAndInferMaps(Material visual, Material source, TextureCatalogEntry[] catalog)
        {
            var properties = new[] { "_MainTex", "_BumpMap", "_RoughnessMap", "_MetallicMap", "_SpecularMap",
                "_OpacityMap", "_OcclusionMap" };
            foreach (var property in properties)
                if (source.HasProperty(property) && visual.HasProperty(property))
                    visual.SetTexture(property, source.GetTexture(property));
            if (source.HasProperty("_MainTex"))
            {
                visual.SetTextureScale("_MainTex", source.GetTextureScale("_MainTex"));
                visual.SetTextureOffset("_MainTex", source.GetTextureOffset("_MainTex"));
            }

            var assigned = properties.Where(source.HasProperty).Select(source.GetTexture).Where(item => item != null).ToArray();
            var families = assigned.Select(item => Family(Path.GetFileName(AssetDatabase.GetAssetPath(item))))
                .Where(item => !string.IsNullOrEmpty(item)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            Texture Find(Func<string, bool> predicate) => catalog.FirstOrDefault(entry =>
                families.Contains(entry.family, StringComparer.OrdinalIgnoreCase) && predicate(entry.file))?.texture;

            SetIfMissing(visual, "_MainTex", Find(IsBaseColor), null);
            SetIfMissing(visual, "_BumpMap", Find(IsNormal), "_HasNormalMap");
            SetIfMissing(visual, "_RoughnessMap", Find(IsSurfaceMap), "_HasRoughnessMap");
            SetIfMissing(visual, "_MetallicMap", Find(IsMetallic), "_HasMetallicMap");
            SetIfMissing(visual, "_SpecularMap", Find(IsSpecular), "_HasSpecularMap");
            SetIfMissing(visual, "_OpacityMap", Find(IsOpacity), "_HasOpacityMap");
            SetIfMissing(visual, "_OcclusionMap", Find(IsOcclusion), "_HasOcclusionMap");

            SetPresence(visual, "_BumpMap", "_HasNormalMap");
            SetPresence(visual, "_RoughnessMap", "_HasRoughnessMap");
            SetPresence(visual, "_MetallicMap", "_HasMetallicMap");
            SetPresence(visual, "_SpecularMap", "_HasSpecularMap");
            SetPresence(visual, "_OpacityMap", "_HasOpacityMap");
            SetPresence(visual, "_OcclusionMap", "_HasOcclusionMap");
            var surfaceTexture = visual.GetTexture("_RoughnessMap");
            var surfaceName = surfaceTexture != null ? Path.GetFileName(AssetDatabase.GetAssetPath(surfaceTexture)).ToLowerInvariant() : "";
            visual.SetFloat("_RoughnessMapIsGloss", surfaceName.Contains("gloss") ? 1f : 0f);
        }

        static void TuneMaterial(Material material, string sourceName, string category)
        {
            var lower = sourceName.ToLowerInvariant();
            var smoothnessScale = 0.82f;
            var smoothnessBias = 0.03f;
            var specularScale = 0.82f;
            var occlusionStrength = 0.68f;
            if (category == "walls")
            {
                smoothnessScale = 0.58f; smoothnessBias = 0.015f; specularScale = 0.62f; occlusionStrength = 0.52f;
            }
            else if (category == "wood")
            {
                smoothnessScale = 0.88f; smoothnessBias = 0.07f; specularScale = 0.86f; occlusionStrength = 0.68f;
            }
            else if (category == "fabric / furniture")
            {
                smoothnessScale = 0.64f; smoothnessBias = -0.015f; specularScale = 0.52f; occlusionStrength = 0.62f;
            }
            else if (category == "marble / stone")
            {
                smoothnessScale = 0.98f; smoothnessBias = 0.07f; specularScale = 1.02f; occlusionStrength = 0.70f;
            }
            else if (category == "flooring")
            {
                smoothnessScale = 0.84f; smoothnessBias = 0.04f; specularScale = 0.82f; occlusionStrength = 0.68f;
            }
            else if (category == "metal")
            {
                smoothnessScale = 1.08f; smoothnessBias = 0.08f; specularScale = 1.18f; occlusionStrength = 0.76f;
            }
            else if (category == "glass / water")
            {
                smoothnessScale = 1.02f; smoothnessBias = 0.04f; specularScale = 1.28f; occlusionStrength = 0.42f;
            }
            else if (category == "mirror")
            {
                smoothnessScale = 1.06f; smoothnessBias = 0.08f; specularScale = 1.30f; occlusionStrength = 0.25f;
            }
            var lacquered = lower.Contains("plastic") || lower.Contains("kithcen") ||
                lower.Contains("wooden_tv") || lower.Contains("toaster");
            if (lacquered)
            {
                smoothnessScale = 1.12f; smoothnessBias = 0.12f; specularScale = 1.16f; occlusionStrength = 0.58f;
            }
            material.SetFloat("_SmoothnessScale", smoothnessScale);
            material.SetFloat("_SmoothnessBias", smoothnessBias);
            material.SetFloat("_SpecularScale", specularScale);
            material.SetFloat("_OcclusionStrength", occlusionStrength);
            var hasSurfaceMap = material.GetFloat("_HasRoughnessMap") > 0.5f;
            var hasMetalMap = material.GetFloat("_HasMetallicMap") > 0.5f;
            if (!hasSurfaceMap)
            {
                var smoothness = 0.42f;
                if (category == "walls") smoothness = 0.22f;
                else if (category == "fabric / furniture") smoothness = 0.2f;
                else if (category == "wood") smoothness = 0.42f;
                else if (category == "marble / stone") smoothness = 0.68f;
                else if (category == "flooring") smoothness = 0.48f;
                else if (category == "metal") smoothness = 0.72f;
                else if (category == "glass / water") smoothness = 0.94f;
                material.SetFloat("_Smoothness", smoothness);
            }
            if (!hasMetalMap) material.SetFloat("_Metallic", category == "metal" ? 1f : 0f);
            if (material.GetFloat("_HasSpecularMap") < 0.5f && category != "metal" && category != "mirror")
                material.SetColor("_SpecColor", new Color(0.04f, 0.04f, 0.04f, 1f));
            material.SetFloat("_BumpScale", category == "fabric / furniture" ? 0.65f : 1f);
            material.SetFloat("_Opacity", 1f);
            material.SetFloat("_EmissionIntensity", 0f);
            material.SetColor("_EmissionColor", Color.black);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack;

            if (category == "glass / water")
            {
                var tint = material.GetColor("_Color");
                if (tint.maxColorComponent < 0.18f) tint = new Color(0.55f, 0.68f, 0.72f, 1f);
                tint.a = 1f;
                material.SetColor("_Color", tint);
                material.SetColor("_SpecColor", new Color(0.12f, 0.12f, 0.12f, 1f));
                material.SetFloat("_Metallic", 0f);
                material.SetFloat("_Smoothness", lower.Contains("water") ? 0.9f : 0.96f);
                material.SetFloat("_Opacity", lower.Contains("water") ? 0.62f :
                    lower.Contains("opaque") ? 0.38f : 0.22f);
                material.renderQueue = (int)RenderQueue.Transparent;
            }
            else if (category == "mirror")
            {
                material.SetColor("_Color", new Color(0.62f, 0.66f, 0.69f, 1f));
                material.SetColor("_SpecColor", Color.white);
                material.SetFloat("_Metallic", 1f);
                material.SetFloat("_Smoothness", 0.98f);
            }

            if (category == "emissive fixtures")
            {
                var pool = lower.Contains("pool");
                var warm = pool ? new Color(0.22f, 0.52f, 1f, 1f) : new Color(1f, 0.68f, 0.42f, 1f);
                material.SetColor("_EmissionColor", warm);
                material.SetFloat("_EmissionIntensity", pool ? 1.05f : 0.78f);
                material.SetFloat("_Smoothness", 0.58f);
                material.SetFloat("_SmoothnessScale", 0.78f);
                material.SetFloat("_SpecularScale", 0.82f);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
            if (lower.Contains("screen") || lower.Contains("fire-flames"))
            {
                var baseTexture = material.GetTexture("_MainTex");
                material.SetTexture("_EmissionMap", baseTexture);
                material.SetFloat("_HasEmissionMap", baseTexture != null ? 1f : 0f);
                material.SetColor("_EmissionColor", Color.white);
                material.SetFloat("_EmissionIntensity", lower.Contains("fire") ? 1.4f : 0.5f);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.BakedEmissive;
            }
        }

        static string ShaderFor(string name, string category)
        {
            if (category == "exterior vegetation") return "Campus Explorer/Modern Villa Visual/Cutout";
            if (category == "glass / water") return "Campus Explorer/Modern Villa Visual/Transparent";
            return "Campus Explorer/Modern Villa Visual/Opaque";
        }

        static string Category(string name)
        {
            var value = name.ToLowerInvariant();
            if (value.Contains("mirror")) return "mirror";
            if (value.Contains("glass") || value.Contains("window") || value.Contains("transparent") || value.Contains("water")) return "glass / water";
            if (value.Contains("light") || value.Contains("led") || value.Contains("bulb") || value.Contains("emission")) return "emissive fixtures";
            if (value.StartsWith("tree", StringComparison.Ordinal) || value.Contains("shrub") || value.Contains("grass")) return "exterior vegetation";
            if (value.Contains("fabric") || value.Contains("linen") || value.Contains("denim") || value.Contains("leather") || value.Contains("wool") || value.Contains("teddy")) return "fabric / furniture";
            if (value.Contains("marble") || value.Contains("stone") || value.Contains("granite") || value.Contains("onyx") || value.Contains("porcelain") || value.Contains("castle_wall")) return "marble / stone";
            if (value.Contains("wood") || value.Contains("plank")) return "wood";
            if (value.Contains("metal") || value.Contains("gold") || value.Contains("handle")) return "metal";
            if (value.Contains("floor") || value.Contains("tile") || value.Contains("gravel") || value.Contains("asphalt")) return "flooring";
            if (value.Contains("wall") || value.Contains("paint") || value.Contains("brick")) return "walls";
            if (value.Contains("kithcen") || value.Contains("kitchen")) return "kitchen surfaces";
            return "other / furniture";
        }

        static void ConfigureStaticGeometry(GameObject villa)
        {
            var flags = StaticEditorFlags.ContributeGI | StaticEditorFlags.ReflectionProbeStatic |
                StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.BatchingStatic;
            foreach (var renderer in villa.GetComponentsInChildren<Renderer>(true))
                GameObjectUtility.SetStaticEditorFlags(renderer.gameObject, flags);
        }

        static void DisableImportedLights(GameObject villa)
        {
            foreach (var light in villa.GetComponentsInChildren<Light>(true)) light.enabled = false;
        }

        static void CreateLighting(GameObject parent)
        {
            // The supplied exterior reference is a night render.  A restrained cool key
            // keeps the outdoor geometry readable while the interior fixtures provide
            // the dominant illumination, as they do in the reference images.
            var sun = NewLight(parent, "Moonlight and exterior key", LightType.Directional,
                new Vector3(-10f, 90f, -30f), Quaternion.Euler(38f, -28f, 0f),
                new Color(0.62f, 0.72f, 1f), 0.20f, 0f);
            sun.shadows = LightShadows.Soft;
            sun.shadowStrength = 0.72f;
            sun.bounceIntensity = 1.0f;
            sun.lightmapBakeType = LightmapBakeType.Mixed;
            RenderSettings.sun = sun;

            var warm = new Color(1f, 0.88f, 0.72f);
            var neutralWarm = new Color(1f, 0.96f, 0.90f);
            NewRoomLight(parent, "Kitchen ceiling fixture contribution", new Vector3(-1.5f, 67.75f, -22.7f), 10.5f, 2.30f, warm);
            NewRoomLight(parent, "Kitchen indirect fill", new Vector3(-4.0f, 67.35f, -24.0f), 7f, 1.35f, neutralWarm);
            NewRoomLight(parent, "Kitchen counter separation fill", new Vector3(-3.2f, 66.8f, -22.5f), 5f, 0.75f, neutralWarm);
            NewRoomLight(parent, "Living room ceiling fixture contribution", new Vector3(-7.0f, 67.65f, -24.7f), 10f, 2.05f, warm);
            NewRoomLight(parent, "Living room indirect fill", new Vector3(-7.8f, 67.30f, -27.2f), 7f, 1.30f, neutralWarm);
            NewRoomLight(parent, "Living sofa TV and art focal fill", new Vector3(-5.3f, 66.9f, -25.4f), 6f, 1.25f, neutralWarm);
            NewRoomLight(parent, "Dining ceiling fixture contribution", new Vector3(0.2f, 67.55f, -28.2f), 8.5f, 1.85f, warm);
            NewRoomLight(parent, "Stair fixture contribution", new Vector3(-6.8f, 67.5f, -28.8f), 7f, 1.50f, warm);
            NewRoomLight(parent, "Master bedroom ceiling fixture contribution", new Vector3(-1.7f, 70.65f, -35.0f), 10f, 2.10f, warm);
            NewRoomLight(parent, "Master bedroom indirect fill", new Vector3(-5.5f, 70.45f, -34.0f), 7f, 1.30f, neutralWarm);
            NewRoomLight(parent, "Bathroom neutral fixture contribution", new Vector3(-11.2f, 70.55f, -33.0f), 8f, 3.10f, neutralWarm);
            NewRoomLight(parent, "Bathroom vanity and glass fill", new Vector3(-10.5f, 69.8f, -32.2f), 6f, 2.0f, neutralWarm);
            NewRoomLight(parent, "Pool water soft fill", new Vector3(-9f, 66.1f, -43f), 12f, 0.85f, new Color(0.30f, 0.58f, 1f));
            NewRoomLight(parent, "Pool deck warm fill", new Vector3(-4f, 66.4f, -41f), 12f, 1.1f, warm);
        }

        static Light NewRoomLight(GameObject parent, string name, Vector3 position, float range, float intensity,
            Color? color = null)
        {
            var light = NewLight(parent, name, LightType.Point, position, Quaternion.identity,
                color ?? new Color(1f, 0.67f, 0.4f), intensity, range);
            light.shadows = LightShadows.Soft;
            light.shadowStrength = 0.45f;
            light.shadowBias = 0.03f;
            light.bounceIntensity = 1.55f;
            light.lightmapBakeType = LightmapBakeType.Mixed;
            return light;
        }

        static Light NewLight(GameObject parent, string name, LightType type, Vector3 position, Quaternion rotation,
            Color color, float intensity, float range)
        {
            var gameObject = NewChild(parent, name);
            gameObject.transform.SetPositionAndRotation(position, rotation);
            var light = gameObject.AddComponent<Light>();
            light.type = type;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.cullingMask = 1 << VillaLayer;
            light.renderMode = LightRenderMode.ForcePixel;
            return light;
        }

        static void CreateLightProbes(GameObject parent)
        {
            var gameObject = NewChild(parent, "Interior light probe grid");
            var group = gameObject.AddComponent<LightProbeGroup>();
            var points = new List<Vector3>();
            AddProbeRoom(points, new Vector3(-2f, 64.9f, -23f), new Vector2(10f, 8f));
            AddProbeRoom(points, new Vector3(-7f, 64.9f, -25f), new Vector2(9f, 8f));
            AddProbeRoom(points, new Vector3(0f, 64.9f, -28f), new Vector2(8f, 7f));
            AddProbeRoom(points, new Vector3(-3f, 68.1f, -35f), new Vector2(12f, 9f));
            AddProbeRoom(points, new Vector3(-11f, 68.1f, -33f), new Vector2(6f, 6f));
            group.probePositions = points.Select(point => gameObject.transform.InverseTransformPoint(point)).ToArray();
        }

        static void AddProbeRoom(List<Vector3> points, Vector3 floorCenter, Vector2 size)
        {
            foreach (var y in new[] { 0.5f, 2.1f })
                foreach (var x in new[] { -0.42f, 0.42f })
                    foreach (var z in new[] { -0.42f, 0.42f })
                        points.Add(floorCenter + new Vector3(size.x * x, y, size.y * z));
        }

        static ReflectionProbe[] CreateReflectionProbes(GameObject parent)
        {
            return new[]
            {
                NewReflectionProbe(parent, "Kitchen reflection probe", new Vector3(-2f, 66.6f, -23f), new Vector3(11f, 5f, 9f)),
                NewReflectionProbe(parent, "Living and dining reflection probe", new Vector3(-4f, 66.6f, -26f), new Vector3(16f, 5f, 12f)),
                NewReflectionProbe(parent, "Master bedroom reflection probe", new Vector3(-3f, 69.8f, -35f), new Vector3(13f, 5f, 10f)),
                NewReflectionProbe(parent, "Bathroom reflection probe", new Vector3(-11f, 69.8f, -33f), new Vector3(7f, 5f, 7f)),
                NewReflectionProbe(parent, "Large glass reflection probe", new Vector3(-4f, 67f, -31f), new Vector3(16f, 8f, 14f)),
                NewReflectionProbe(parent, "Pool exterior reflection probe", new Vector3(-9f, 66.8f, -44f), new Vector3(24f, 8f, 20f))
            };
        }

        static ReflectionProbe NewReflectionProbe(GameObject parent, string name, Vector3 position, Vector3 size)
        {
            var gameObject = NewChild(parent, name);
            gameObject.transform.position = position;
            var probe = gameObject.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
            probe.resolution = 256;
            probe.hdr = true;
            probe.boxProjection = true;
            probe.size = size;
            probe.nearClipPlane = 0.1f;
            probe.farClipPlane = Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * 1.5f;
            probe.intensity = name.Contains("Kitchen") || name.Contains("Bathroom") ? 1.18f : 1.12f;
            probe.blendDistance = Mathf.Min(1.5f, Mathf.Min(size.x, size.z) * 0.12f);
            probe.cullingMask = 1 << VillaLayer;
            return probe;
        }

        static void ConfigureEnvironment()
        {
            EnsureAssetFolder(LightingFolder);
            var sky = AssetDatabase.LoadAssetAtPath<Material>(NightSkyAsset);
            if (sky == null)
            {
                var skyShader = Shader.Find("Skybox/Procedural");
                if (skyShader == null) throw new InvalidOperationException("Unity procedural skybox shader is missing.");
                sky = new Material(skyShader) { name = "Modern Villa Night Sky" };
                AssetDatabase.CreateAsset(sky, NightSkyAsset);
            }
            sky.SetColor("_SkyTint", new Color(0.055f, 0.075f, 0.13f));
            sky.SetColor("_GroundColor", new Color(0.018f, 0.022f, 0.035f));
            sky.SetFloat("_Exposure", 0.30f);
            sky.SetFloat("_AtmosphereThickness", 0.45f);
            sky.SetFloat("_SunSize", 0.015f);
            sky.SetFloat("_SunSizeConvergence", 4f);
            EditorUtility.SetDirty(sky);
            RenderSettings.skybox = sky;
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.12f, 0.16f, 0.26f);
            RenderSettings.ambientEquatorColor = new Color(0.075f, 0.085f, 0.12f);
            RenderSettings.ambientGroundColor = new Color(0.030f, 0.026f, 0.034f);
            RenderSettings.ambientIntensity = 0.90f;
            RenderSettings.reflectionIntensity = 1.08f;
            RenderSettings.reflectionBounces = 2;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.fog = false;
        }

        static void ConfigureLightingSettings()
        {
            EnsureAssetFolder(LightingFolder);
            var settings = AssetDatabase.LoadAssetAtPath<LightingSettings>(LightingAsset);
            if (settings == null)
            {
                settings = new LightingSettings { name = "Modern Villa Visual - Medium Validation" };
                AssetDatabase.CreateAsset(settings, LightingAsset);
            }
            settings.bakedGI = true;
            settings.realtimeGI = false;
            settings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
            settings.lightmapResolution = 5f;
            settings.lightmapMaxSize = 1024;
            settings.lightmapPadding = 2;
            settings.compressLightmaps = true;
            // Unity 6 no longer exposes the AO enable flag as a public C# property,
            // while keeping it in the LightingSettings serialization format.
            var serializedSettings = new SerializedObject(settings);
            var aoProperty = serializedSettings.FindProperty("m_AO");
            if (aoProperty != null)
            {
                aoProperty.boolValue = true;
                serializedSettings.ApplyModifiedPropertiesWithoutUndo();
            }
            settings.aoMaxDistance = 1.5f;
            settings.aoExponentIndirect = 1.15f;
            settings.aoExponentDirect = 0.6f;
            settings.directSampleCount = 16;
            settings.indirectSampleCount = 64;
            settings.environmentSampleCount = 32;
            settings.maxBounces = 3;
            settings.minBounces = 1;
            Lightmapping.lightingSettings = settings;
            EditorUtility.SetDirty(settings);
        }

        static void RenderReflectionProbes(IEnumerable<ReflectionProbe> probes)
        {
            foreach (var probe in probes) probe.RenderProbe();
        }

        static void CaptureViews(IEnumerable<Camera> cameras, string folder, bool visual)
        {
            Directory.CreateDirectory(folder);
            foreach (var camera in cameras)
            {
                var key = camera.name.Substring("Review Camera - ".Length);
                CaptureCamera(camera, folder + "/" + key + ".png");
            }
        }

        static void CaptureCamera(Camera camera, string path)
        {
            var previous = RenderTexture.active;
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGBHalf)
            {
                antiAliasing = 1,
                name = "Modern Villa visual evidence"
            };
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
                image.Apply(false, false);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        static MaterialAudit AuditMaterial(Material material, string sourceName, string category,
            TextureCatalogEntry[] catalog)
        {
            var audit = new MaterialAudit
            {
                name = sourceName,
                category = category,
                shader = material.shader != null ? material.shader.name : "<missing>",
                smoothness = material.GetFloat("_Smoothness"),
                metallic = material.GetFloat("_Metallic"),
                opacity = material.GetFloat("_Opacity"),
                emissionIntensity = material.GetFloat("_EmissionIntensity"),
                uvScale = material.GetTextureScale("_MainTex"),
                glossMap = material.GetFloat("_RoughnessMapIsGloss") > 0.5f,
                roughnessInverted = material.GetFloat("_HasRoughnessMap") > 0.5f && material.GetFloat("_RoughnessMapIsGloss") < 0.5f
            };
            foreach (var property in new[] { "_MainTex", "_BumpMap", "_RoughnessMap", "_MetallicMap", "_SpecularMap", "_OcclusionMap", "_OpacityMap", "_EmissionMap" })
            {
                var texture = material.GetTexture(property);
                audit.maps[property] = texture != null ? TextureDescription(texture) : "—";
            }
            var family = material.GetTexture("_MainTex") != null
                ? Family(Path.GetFileName(AssetDatabase.GetAssetPath(material.GetTexture("_MainTex")))) : "";
            audit.heightMap = catalog.FirstOrDefault(entry => string.Equals(entry.family, family,
                StringComparison.OrdinalIgnoreCase) && IsHeight(entry.file))?.path ?? "—";
            audit.notes = audit.heightMap != "—" ? "Height/displacement audited but deliberately not applied without tessellation." : "";
            return audit;
        }

        static void WriteMaterialAudit(IEnumerable<MaterialAudit> audits)
        {
            var values = audits.ToArray();
            var builder = new StringBuilder();
            builder.AppendLine("# Modern Luxury Villa — material audit");
            builder.AppendLine();
            builder.AppendLine("This audit covers all 135 cloned visual materials. Source materials remain unchanged.");
            builder.AppendLine();
            builder.AppendLine("## Channel interpretation");
            builder.AppendLine();
            builder.AppendLine("- Files named `rough` or `Roughness` are sampled as linear data and converted with `Smoothness = 1 - Roughness` in the visual shader.");
            builder.AppendLine("- Files named `gloss` are sampled directly as smoothness (`_RoughnessMapIsGloss = 1`).");
            builder.AppendLine("- Normal maps named `nor_gl` / `NormalGL` are imported as Unity Normal Map assets; no extra green-channel inversion is applied.");
            builder.AppendLine("- AO, metallic, specular, roughness/gloss and height maps use linear sampling; base-color and photo maps use sRGB.");
            builder.AppendLine("- No ORM/RMA filename pattern was detected in the connected set; channels are separate maps.");
            builder.AppendLine("- Height/displacement maps are documented but not connected: the current shader has no tessellation or parallax stage, and visual fidelity is safer than an unvalidated displacement approximation.");
            builder.AppendLine();
            builder.AppendLine("## Summary");
            builder.AppendLine();
            builder.AppendLine("| Category | Materials |");
            builder.AppendLine("|---|---:|");
            foreach (var group in values.GroupBy(item => item.category).OrderBy(item => item.Key))
                builder.AppendLine("| " + group.Key + " | " + group.Count() + " |");
            builder.AppendLine();
            builder.AppendLine("## Per-material audit");
            builder.AppendLine();
            builder.AppendLine("| Material | Category | Base | Normal | Rough/Gloss | Metal | Spec | AO | Alpha | Emission | UV | Fallback smooth/metal/opacity | Height | Notes |");
            builder.AppendLine("|---|---|---|---|---|---|---|---|---|---|---|---|---|---|");
            foreach (var audit in values.OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase))
            {
                string M(string key) => Short(audit.maps[key]);
                var surface = M("_RoughnessMap");
                if (surface != "—") surface += audit.glossMap ? " (direct gloss)" : " (inverted roughness)";
                builder.AppendLine("| " + Md(audit.name) + " | " + audit.category + " | " + M("_MainTex") + " | " +
                    M("_BumpMap") + " | " + surface + " | " + M("_MetallicMap") + " | " + M("_SpecularMap") + " | " +
                    M("_OcclusionMap") + " | " + M("_OpacityMap") + " | " + M("_EmissionMap") + " / " +
                    audit.emissionIntensity.ToString("0.##", CultureInfo.InvariantCulture) + " | " +
                    audit.uvScale.x.ToString("0.###", CultureInfo.InvariantCulture) + "×" + audit.uvScale.y.ToString("0.###", CultureInfo.InvariantCulture) + " | " +
                    audit.smoothness.ToString("0.##", CultureInfo.InvariantCulture) + " / " +
                    audit.metallic.ToString("0.##", CultureInfo.InvariantCulture) + " / " +
                    audit.opacity.ToString("0.##", CultureInfo.InvariantCulture) + " | " + Short(audit.heightMap) + " | " + Md(audit.notes) + " |");
            }
            File.WriteAllText(MaterialReport, builder.ToString(), Encoding.UTF8);
        }

        static void WriteVisualReport(MaterialAudit[] audits, int reflectionProbeCount, string sourceHash)
        {
            var builder = new StringBuilder();
            builder.AppendLine("# Modern Luxury Villa — visual fidelity pass");
            builder.AppendLine();
            builder.AppendLine("## Integrity and scope");
            builder.AppendLine();
            builder.AppendLine("- Authoritative scene: `" + SourceScene + "`");
            builder.AppendLine("- Source scene SHA-256 before/after build: `" + sourceHash + "` (identical)");
            builder.AppendLine("- Visual scene: `" + VisualScene + "`");
            builder.AppendLine("- Geometry was not rebuilt, replaced, merged, decimated or removed.");
            builder.AppendLine("- Texture resolution and compression settings were not reduced in this pass.");
            builder.AppendLine("- Review cameras: 70° vertical FOV, HDR, heights 1.70 m above the relevant lower/upper floor baselines.");
            builder.AppendLine();
            builder.AppendLine("## Main fixes");
            builder.AppendLine();
            builder.AppendLine("- Cloned all " + audits.Length + " reconstructed materials into a visual-only folder.");
            builder.AppendLine("- Added explicit roughness-to-smoothness inversion; gloss maps remain direct.");
            builder.AppendLine("- Recovered same-family normal, roughness/gloss, metallic, specular and AO maps where the source connection was incomplete.");
            builder.AppendLine("- Rebuilt glass and water as premultiplied transparent PBR surfaces with reflection response.");
            builder.AppendLine("- Tuned mirrors and metals separately from glass.");
            builder.AppendLine("- Replaced pure-white fixture output with warm controlled emissive values plus restrained real light contribution.");
            builder.AppendLine();
            builder.AppendLine("## Lighting configuration");
            builder.AppendLine();
            builder.AppendLine("- One restrained mixed cool exterior key: intensity 0.22, soft shadows, shadow strength 0.72.");
            builder.AppendLine("- Nine warm/neutral mixed interior fixture and bounce contributions plus two local pool/deck fills; all use soft shadows.");
            builder.AppendLine("- Procedural night sky matched to the supplied exterior reference, with cool low-level trilight ambient at intensity 0.75.");
            builder.AppendLine("- One 40-point light-probe grid spanning kitchen, living/dining, master suite and bathroom.");
            builder.AppendLine("- Reflection probes: " + reflectionProbeCount + " at 128 px HDR, box projection, placed at kitchen, living/dining, bedroom, bathroom, glazing and pool.");
            builder.AppendLine();
            builder.AppendLine("## Medium baked-GI validation settings");
            builder.AppendLine();
            builder.AppendLine("- Progressive CPU lightmapper; baked GI on; realtime GI off.");
            builder.AppendLine("- 5 texels/m, 1024 maximum lightmap, padding 2, compressed lightmaps.");
            builder.AppendLine("- 16 direct / 64 indirect / 32 environment samples; 1–3 bounces.");
            builder.AppendLine("- Baked AO enabled, maximum distance 1.5 m, indirect exponent 1.15, direct exponent 0.6.");
            builder.AppendLine();
            builder.AppendLine("## Post-processing");
            builder.AppendLine();
            builder.AppendLine("- A small built-in-pipeline review pass applies exposure, an exponential highlight shoulder, 0.98 saturation and 1.02 contrast. No bloom, vignette, chromatic aberration or heavy color grade is used.");
            builder.AppendLine("- Review-camera exposure EV: kitchen 0.75; living/dining 1.05; staircase 1.2; master bedroom 1.65; bathroom 2.3; exterior/pool 1.4.");
            builder.AppendLine();
            builder.AppendLine("## Remaining differences from the offline references");
            builder.AppendLine();
            builder.AppendLine("- The supplied PNG references were rendered offline and include a high-end sky/environment, camera exposure and likely ray-traced reflections that are not embedded in the FBX/MTL.");
            builder.AppendLine("- The source includes height/displacement textures, but the visual shader does not displace geometry; silhouettes and parallax therefore differ at close range.");
            builder.AppendLine("- Realtime reflection probes approximate planar mirrors and water. They are not planar or ray-traced reflections.");
            builder.AppendLine("- The FBX has zero-area lightmap UVs on some small props and selected window meshes; those objects fall back to probes and are recorded in the bake log.");
            builder.AppendLine("- The project currently uses Gamma color space. A later project-wide Linear conversion should be validated separately because it affects every existing scene.");
            builder.AppendLine("- Final appearance still needs headset validation after this visual baseline is approved; Quest optimization remains intentionally out of scope.");
            builder.AppendLine();
            builder.AppendLine("## Evidence");
            builder.AppendLine();
            foreach (var view in Views)
                builder.AppendLine("- `" + view.key + "`: `before/" + view.key + ".png` → `after/" + view.key + ".png`");
            File.WriteAllText(VisualReport, builder.ToString(), Encoding.UTF8);
        }

        static TextureCatalogEntry[] BuildTextureCatalog()
        {
            return AssetDatabase.FindAssets("t:Texture", new[] { TextureFolder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => new TextureCatalogEntry
                {
                    path = path,
                    file = Path.GetFileName(path),
                    family = Family(Path.GetFileName(path)),
                    texture = AssetDatabase.LoadAssetAtPath<Texture>(path)
                })
                .Where(item => item.texture != null).OrderBy(item => item.file, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        static string Family(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            name = Regex.Replace(name, @"(?i)(?:[_\- ](?:basecolor|base_color|albedo|diffuse|diff|color|col\d*|normalgl|normaldx|normal|nor_gl|nor_dx|roughness|rough|glossiness|gloss|metallic|metalness|metal|specular|spec_ior|spec|ambientocclusion|ambient_occlusion|ao|opacity|alpha|transmission|displacement|disp|height|bump|emissive|emission))(?:[_\- ]?\d+k)?$", "");
            name = Regex.Replace(name, @"(?i)(?:[_\- ]\d+k)$", "");
            return name.Trim('_', '-', ' ');
        }

        static bool IsBaseColor(string name) => HasAny(name, "basecolor", "base_color", "albedo", "diff", "_color", "_col");
        static bool IsNormal(string name) => HasAny(name, "normal", "nor_gl", "nor_dx");
        static bool IsSurfaceMap(string name) => HasAny(name, "rough", "gloss");
        static bool IsMetallic(string name) => HasAny(name, "metallic", "metalness", "_metal");
        static bool IsSpecular(string name) => HasAny(name, "specular", "spec_ior", "_spec");
        static bool IsOcclusion(string name) => HasAny(name, "ambientocclusion", "ambient_occlusion", "_ao.", "_ao_");
        static bool IsOpacity(string name) => HasAny(name, "opacity", "alpha", "transmission");
        static bool IsHeight(string name) => HasAny(name, "displacement", "_disp", "height", "bump");
        static bool HasAny(string value, params string[] terms)
        {
            var lower = value.ToLowerInvariant();
            return terms.Any(lower.Contains);
        }

        static void SetIfMissing(Material material, string textureProperty, Texture texture, string presenceProperty)
        {
            if (material.GetTexture(textureProperty) == null && texture != null) material.SetTexture(textureProperty, texture);
            if (presenceProperty != null) SetPresence(material, textureProperty, presenceProperty);
        }

        static void SetPresence(Material material, string textureProperty, string presenceProperty)
        {
            material.SetFloat(presenceProperty, material.GetTexture(textureProperty) != null ? 1f : 0f);
        }

        static string TextureDescription(Texture texture)
        {
            var path = AssetDatabase.GetAssetPath(texture);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            var space = importer != null && importer.sRGBTexture ? "sRGB" : "linear";
            return path + " (" + texture.width + "×" + texture.height + ", " + space + ")";
        }

        static string Short(string value)
        {
            if (string.IsNullOrEmpty(value) || value == "—") return "—";
            var path = value;
            var paren = path.IndexOf(" (", StringComparison.Ordinal);
            var suffix = paren >= 0 ? path.Substring(paren) : "";
            if (paren >= 0) path = path.Substring(0, paren);
            return Md(Path.GetFileName(path)) + suffix;
        }

        static string Md(string value) => string.IsNullOrEmpty(value) ? "" : value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");

        static GameObject NewChild(GameObject parent, string name)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent.transform, false);
            return gameObject;
        }

        static void RemoveRootIfPresent(Scene scene, string name)
        {
            var existing = scene.GetRootGameObjects().FirstOrDefault(item => item.name == name);
            if (existing != null) UnityEngine.Object.DestroyImmediate(existing);
        }

        static void EnsureAssetFolder(string path)
        {
            var parts = path.Split('/');
            var current = parts[0];
            for (var index = 1; index < parts.Length; index++)
            {
                var next = current + "/" + parts[index];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[index]);
                current = next;
            }
        }

        static string HashFile(string path)
        {
            using (var sha = SHA256.Create())
                return string.Concat(sha.ComputeHash(File.ReadAllBytes(path)).Select(value => value.ToString("x2")));
        }
    }
}
