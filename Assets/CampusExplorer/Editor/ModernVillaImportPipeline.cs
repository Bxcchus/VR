using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CampusExplorer.Editor
{
    /// <summary>
    /// Imports the authoritative FBX without changing geometry or texture quality,
    /// then writes a separate source scene and a reproducible import report.
    /// </summary>
    public sealed class ModernVillaImportPipeline : AssetPostprocessor
    {
        const string ModelPath = "Assets/CampusExplorer/External/ModernLuxuryVilla/Modern_Residence_Interior.fbx";
        const string SourceFolder = "Assets/CampusExplorer/External/ModernLuxuryVilla";
        const string MtlPath = SourceFolder + "/Modern_Luxury_Villa_B5.blend.mtl";
        const string MaterialsFolder = "Assets/CampusExplorer/Materials/ModernLuxuryVilla";
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaSource.unity";
        const string EvidenceFolder = "Evidence/modern-villa-audit";
        const string ReportPath = EvidenceFolder + "/unity-import-audit.md";
        const int DiagnosticLayer = 31;

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(SourceFolder + "/Textures/", StringComparison.Ordinal)) return;
            ApplyTextureSettings((TextureImporter)assetImporter, assetPath);
        }

        void OnPreprocessModel()
        {
            if (!string.Equals(assetPath, ModelPath, StringComparison.Ordinal)) return;
            var importer = (ModelImporter)assetImporter;
            importer.importCameras = true;
            importer.importLights = true;
            importer.importVisibility = true;
            importer.importAnimation = true;
            importer.importBlendShapes = true;
            importer.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            importer.materialLocation = ModelImporterMaterialLocation.InPrefab;
            importer.materialName = ModelImporterMaterialName.BasedOnMaterialName;
            importer.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
            // Deliberately leave global scale, mesh compression, mesh optimisation,
            // normals, tangents and texture settings at the source/default values.
        }

        [InitializeOnLoadMethod]
        static void ScheduleWhenReady()
        {
            EditorApplication.update -= RunWhenReady;
            EditorApplication.update += RunWhenReady;
        }

        static void RunWhenReady()
        {
            // The report is local evidence and is intentionally not kept in Git.
            // The source scene is the durable marker that this one-time import ran.
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                EditorApplication.update -= RunWhenReady;
                return;
            }
            if (EditorApplication.isCompiling || EditorApplication.isUpdating ||
                EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null) return;
            EditorApplication.update -= RunWhenReady;
            EditorApplication.delayCall += () =>
            {
                try { AuditAndCreateSourceScene(); }
                catch (Exception exception)
                {
                    Directory.CreateDirectory(EvidenceFolder);
                    File.WriteAllText(EvidenceFolder + "/unity-import-error.txt", exception.ToString(), Encoding.UTF8);
                    Debug.LogException(exception);
                }
            };
        }

        [MenuItem("Journey Through XR/Modern Villa/Audit imported source")]
        public static void AuditAndCreateSourceScene()
        {
            Directory.CreateDirectory(EvidenceFolder);
            ConfigureSourceTextures();
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (prefab == null)
            {
                AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            }
            if (prefab == null) throw new FileNotFoundException("Unity has not imported the Modern Villa FBX", ModelPath);

            var activeScene = SceneManager.GetActiveScene();
            var canOpenAdditively = activeScene.IsValid() && !string.IsNullOrEmpty(activeScene.path);
            var diagnosticScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,
                canOpenAdditively ? NewSceneMode.Additive : NewSceneMode.Single);
            diagnosticScene.name = "ModernVillaSource";
            var parent = new GameObject("Authoritative Modern Luxury Villa - Identity Transform");
            SceneManager.MoveGameObjectToScene(parent, diagnosticScene);
            parent.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            parent.transform.localScale = Vector3.one;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, diagnosticScene);
            instance.name = "Modern_Residence_Interior - Direct FBX Import";
            instance.transform.SetParent(parent.transform, false);
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            var reconnectedMaterials = CreateReconnectedMaterials();
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                var connected = renderer.sharedMaterials.Select(material =>
                    material != null && reconnectedMaterials.TryGetValue(material.name, out var replacement)
                        ? replacement : material).ToArray();
                renderer.sharedMaterials = connected;
            }

            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            var filters = instance.GetComponentsInChildren<MeshFilter>(true);
            var skinned = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            var meshes = filters.Select(item => item.sharedMesh)
                .Concat(skinned.Select(item => item.sharedMesh)).Where(item => item != null).Distinct().ToArray();
            var materials = renderers.SelectMany(item => item.sharedMaterials)
                .Where(item => item != null).Distinct().ToArray();
            var bounds = Encapsulate(renderers.Where(item => item.enabled).Select(item => item.bounds));
            var triangles = meshes.Sum(CountTriangles);
            var textureGuids = AssetDatabase.FindAssets("t:Texture", new[] { SourceFolder });
            var assignedTextures = GetAssignedTextures(materials).ToArray();
            var missingMaterialSlots = renderers.Sum(renderer => renderer.sharedMaterials.Count(material => material == null));
            var missingShaders = materials.Count(material => material.shader == null ||
                material.shader.name == "Hidden/InternalErrorShader");

            foreach (var transform in instance.GetComponentsInChildren<Transform>(true))
                transform.gameObject.layer = DiagnosticLayer;
            foreach (var light in instance.GetComponentsInChildren<Light>(true))
                light.cullingMask = 1 << DiagnosticLayer;

            EditorSceneManager.SaveScene(diagnosticScene, ScenePath);
            WriteReport(prefab, instance, renderers, meshes, materials, textureGuids.Length,
                assignedTextures, missingMaterialSlots, missingShaders, triangles, bounds);
            CaptureOverview(diagnosticScene, instance, bounds);
            if (canOpenAdditively) EditorSceneManager.CloseScene(diagnosticScene, true);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Modern Villa] Direct FBX import audited. Scene: " + ScenePath + " Report: " + ReportPath);
        }

        sealed class MtlDefinition
        {
            public string name;
            public Color diffuse = Color.white;
            public Color specular = new Color(0.04f, 0.04f, 0.04f, 1f);
            public Color emission = Color.black;
            public float opacity = 1f;
            public float shininess = 250f;
            public float bumpScale = 1f;
            public readonly Dictionary<string, string> maps =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        static Dictionary<string, Material> CreateReconnectedMaterials()
        {
            EnsureAssetFolder(MaterialsFolder);
            var importedMaterials = AssetDatabase.LoadAllAssetsAtPath(ModelPath).OfType<Material>()
                .GroupBy(item => item.name, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
            var textures = AssetDatabase.FindAssets("t:Texture", new[] { SourceFolder + "/Textures" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(path => new { path, texture = AssetDatabase.LoadAssetAtPath<Texture>(path) })
                .Where(item => item.texture != null)
                .GroupBy(item => Path.GetFileName(item.path), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First().texture, StringComparer.OrdinalIgnoreCase);
            var result = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (var definition in ParseMtl())
            {
                importedMaterials.TryGetValue(definition.name, out var importedMaterial);
                var hasOpacityMap = definition.maps.ContainsKey("map_d");
                var foliage = hasOpacityMap && (definition.name.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    definition.name.IndexOf("shrub", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    definition.name.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0);
                var shaderName = foliage ? "Campus Explorer/Modern Villa PBR/Cutout" :
                    definition.opacity < 0.999f ? "Campus Explorer/Modern Villa PBR/Transparent" :
                    "Campus Explorer/Modern Villa PBR/Opaque";
                var shader = Shader.Find(shaderName);
                if (shader == null) throw new InvalidOperationException("Modern Villa shader is missing: " + shaderName);
                var path = MaterialsFolder + "/" + SanitizeFileName(definition.name) + ".mat";
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (material == null)
                {
                    material = new Material(shader) { name = definition.name };
                    AssetDatabase.CreateAsset(material, path);
                }
                else material.shader = shader;

                material.name = definition.name;
                var importedColor = ReadColor(importedMaterial, "_Color", definition.diffuse);
                // The shared PBR shader multiplies _Color.a by _Opacity. The MTL d value
                // belongs in _Opacity only; assigning it to both squares transparency.
                material.SetColor("_Color", new Color(importedColor.r, importedColor.g,
                    importedColor.b, 1f));
                material.SetColor("_SpecColor", ReadColor(importedMaterial, "_SpecColor", definition.specular));
                material.SetColor("_EmissionColor", ReadColor(importedMaterial, "_EmissionColor", definition.emission));
                material.SetFloat("_Opacity", definition.opacity);
                var sourceSmoothness = ReadFloat(importedMaterial, "_Glossiness",
                    Mathf.Clamp01(Mathf.Sqrt(Mathf.Max(0f, definition.shininess) / 1000f)));
                material.SetFloat("_Smoothness", sourceSmoothness);
                material.SetFloat("_BumpScale", definition.bumpScale);
                ConnectTexture(material, definition, textures, "map_Kd", "_MainTex", null);
                if (material.GetTexture("_MainTex") == null)
                    material.SetTexture("_MainTex", FindFamilyTexture(definition, textures, IsBaseColorTexture));
                ConnectTexture(material, definition, textures, "map_Bump", "_BumpMap", "_HasNormalMap");
                ConnectTexture(material, definition, textures, "map_Ns", "_RoughnessMap", "_HasRoughnessMap");
                ConnectTexture(material, definition, textures, "map_refl", "_MetallicMap", "_HasMetallicMap");
                ConnectTexture(material, definition, textures, "map_Ks", "_SpecularMap", "_HasSpecularMap");
                ConnectTexture(material, definition, textures, "map_d", "_OpacityMap", "_HasOpacityMap");
                var occlusion = FindFamilyTexture(definition, textures, IsOcclusionTexture);
                material.SetTexture("_OcclusionMap", occlusion);
                material.SetFloat("_HasOcclusionMap", occlusion != null ? 1f : 0f);
                EditorUtility.SetDirty(material);
                result[definition.name] = material;
            }
            AssetDatabase.SaveAssets();
            return result;
        }

        static void ConnectTexture(Material material, MtlDefinition definition,
            Dictionary<string, Texture> textures, string mapKey, string property, string presenceProperty)
        {
            Texture texture = null;
            if (definition.maps.TryGetValue(mapKey, out var fileName))
            {
                var requestedName = Path.GetFileName(fileName);
                if (!textures.TryGetValue(requestedName, out texture))
                {
                    var matchingName = textures.Keys.FirstOrDefault(candidate =>
                        fileName.EndsWith(candidate, StringComparison.OrdinalIgnoreCase));
                    if (matchingName != null) texture = textures[matchingName];
                }
            }
            material.SetTexture(property, texture);
            if (presenceProperty != null) material.SetFloat(presenceProperty, texture != null ? 1f : 0f);
        }

        static Texture FindFamilyTexture(MtlDefinition definition, Dictionary<string, Texture> textures,
            Func<string, bool> predicate)
        {
            var families = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var sourceName in definition.maps.Values)
            {
                var matchingName = textures.Keys.FirstOrDefault(candidate =>
                    sourceName.EndsWith(candidate, StringComparison.OrdinalIgnoreCase));
                if (matchingName != null) families.Add(TextureFamily(matchingName));
            }
            foreach (var pair in textures.OrderBy(item => item.Key, StringComparer.OrdinalIgnoreCase))
                if (predicate(pair.Key) && families.Contains(TextureFamily(pair.Key))) return pair.Value;
            return null;
        }

        static string TextureFamily(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            var markers = new[] { "_normalgl", "_normal", "_nor_gl", "_roughness", "_rough", "_gloss",
                "_specular", "_spec_ior", "_metalness", "_metal", "_displacement", "_disp",
                "_ambientocclusion", "_ao", "_bump", "_basecolor", "_color", "_diff", "_col1" };
            var markerIndex = markers.Select(marker => name.IndexOf(marker, StringComparison.Ordinal))
                .Where(index => index >= 0).DefaultIfEmpty(name.Length).Min();
            return name.Substring(0, markerIndex);
        }

        static bool IsBaseColorTexture(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            return name.Contains("basecolor") || name.Contains("_color") ||
                name.Contains("_diff") || name.Contains("_col1");
        }

        static bool IsOcclusionTexture(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName).ToLowerInvariant();
            return name.Contains("ambientocclusion") || name.EndsWith("_ao");
        }

        static IEnumerable<MtlDefinition> ParseMtl()
        {
            if (!File.Exists(MtlPath)) throw new FileNotFoundException("The villa MTL file is missing", MtlPath);
            var culture = CultureInfo.InvariantCulture;
            MtlDefinition current = null;
            foreach (var rawLine in File.ReadAllLines(MtlPath))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#') continue;
                var split = line.IndexOf(' ');
                var key = split < 0 ? line : line.Substring(0, split);
                var value = split < 0 ? string.Empty : line.Substring(split + 1).Trim();
                if (key == "newmtl")
                {
                    if (current != null) yield return current;
                    current = new MtlDefinition { name = value };
                    continue;
                }
                if (current == null) continue;
                if (key == "Kd") current.diffuse = ParseColor(value, culture, 1f);
                else if (key == "Ks") current.specular = ParseColor(value, culture, 1f);
                else if (key == "Ke") current.emission = ParseColor(value, culture, 1f);
                else if (key == "d") current.opacity = ParseFloat(value, culture, 1f);
                else if (key == "Ns") current.shininess = ParseFloat(value, culture, 250f);
                else if (key.StartsWith("map_", StringComparison.Ordinal))
                {
                    if (key == "map_Bump" && value.StartsWith("-bm ", StringComparison.Ordinal))
                    {
                        var bumpParts = value.Split(new[] { ' ' }, 3, StringSplitOptions.RemoveEmptyEntries);
                        if (bumpParts.Length == 3)
                        {
                            current.bumpScale = ParseFloat(bumpParts[1], culture, 1f);
                            value = bumpParts[2];
                        }
                    }
                    current.maps[key] = value;
                }
            }
            if (current != null) yield return current;
        }

        static Color ParseColor(string value, CultureInfo culture, float alpha)
        {
            var values = value.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (values.Length < 3) return Color.white;
            return new Color(ParseFloat(values[0], culture, 1f), ParseFloat(values[1], culture, 1f),
                ParseFloat(values[2], culture, 1f), alpha);
        }

        static float ParseFloat(string value, CultureInfo culture, float fallback) =>
            float.TryParse(value, NumberStyles.Float, culture, out var result) ? result : fallback;

        static Color ReadColor(Material material, string property, Color fallback) =>
            material != null && material.HasProperty(property) ? material.GetColor(property) : fallback;

        static float ReadFloat(Material material, string property, float fallback) =>
            material != null && material.HasProperty(property) ? material.GetFloat(property) : fallback;

        static string SanitizeFileName(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            return new string(value.Select(character => invalid.Contains(character) ? '_' : character).ToArray());
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

        static void ConfigureSourceTextures()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture", new[] { SourceFolder + "/Textures" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;
                var before = EditorJsonUtility.ToJson(importer);
                ApplyTextureSettings(importer, path);
                if (!string.Equals(before, EditorJsonUtility.ToJson(importer), StringComparison.Ordinal))
                    importer.SaveAndReimport();
            }
        }

        static void ApplyTextureSettings(TextureImporter importer, string path)
        {
            var name = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            var isNormal = name.Contains("normal") || name.Contains("nor_gl");
            // Detect the map role, not the material family. For example,
            // Metal050A_..._Color is a base-colour texture and must remain sRGB.
            var isMetalData = System.Text.RegularExpressions.Regex.IsMatch(name,
                @"(?:^|[_\-])(metal|metallic|metalness)(?:[_\-]|$)");
            var isLinearData = isNormal || name.Contains("rough") || name.Contains("gloss") ||
                isMetalData || name.Contains("spec") || name.Contains("disp") || name.Contains("height") ||
                name.Contains("ambientocclusion") || name.EndsWith("_ao") || name.Contains("bump");
            importer.maxTextureSize = 8192;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.compressionQuality = 100;
            importer.sRGBTexture = !isLinearData;
            if (isNormal)
            {
                importer.textureType = TextureImporterType.NormalMap;
                importer.convertToNormalmap = false;
            }
        }

        static void WriteReport(GameObject prefab, GameObject instance, Renderer[] renderers, Mesh[] meshes,
            Material[] materials, int projectTextureCount, Texture[] assignedTextures, int missingMaterialSlots,
            int missingShaders, long triangles, Bounds bounds)
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
            var culture = CultureInfo.InvariantCulture;
            var builder = new StringBuilder();
            builder.AppendLine("# Modern Luxury Villa — Unity import audit");
            builder.AppendLine();
            builder.AppendLine("- Unity: `" + Application.unityVersion + "`");
            builder.AppendLine("- Source: `" + ModelPath + "`");
            builder.AppendLine("- Import: direct FBX through `ModelImporter`; no geometry replacement or simplification");
            builder.AppendLine("- Global scale: `" + importer.globalScale.ToString("R", culture) + "`");
            builder.AppendLine("- File scale: `" + importer.fileScale.ToString("R", culture) + "`; use file scale: `" + importer.useFileScale + "`");
            builder.AppendLine("- Material mode: `" + importer.materialImportMode + "`; location: `" + importer.materialLocation + "`");
            builder.AppendLine();
            builder.AppendLine("## Imported result");
            builder.AppendLine();
            builder.AppendLine("| Metric | Unity result |");
            builder.AppendLine("|---|---:|");
            builder.AppendLine("| Mesh assets | " + meshes.Length + " |");
            builder.AppendLine("| Renderers | " + renderers.Length + " |");
            builder.AppendLine("| Unique materials assigned | " + materials.Length + " |");
            builder.AppendLine("| Texture assets under source folder | " + projectTextureCount + " |");
            builder.AppendLine("| Unique textures assigned to materials | " + assignedTextures.Length + " |");
            builder.AppendLine("| Triangles | " + triangles + " |");
            builder.AppendLine("| Missing material slots | " + missingMaterialSlots + " |");
            builder.AppendLine("| Missing/error shaders | " + missingShaders + " |");
            builder.AppendLine("| World bounds min | " + Format(bounds.min, culture) + " |");
            builder.AppendLine("| World bounds max | " + Format(bounds.max, culture) + " |");
            builder.AppendLine("| World dimensions | " + Format(bounds.size, culture) + " |");
            builder.AppendLine();
            builder.AppendLine("## Hierarchy");
            builder.AppendLine();
            foreach (var transform in instance.GetComponentsInChildren<Transform>(true))
                builder.AppendLine("- `" + GetPath(transform, instance.transform) + "` — position " +
                    Format(transform.localPosition, culture) + ", rotation " +
                    Format(transform.localRotation.eulerAngles, culture) + ", scale " +
                    Format(transform.localScale, culture));
            builder.AppendLine();
            builder.AppendLine("## Materials and connected textures");
            builder.AppendLine();
            foreach (var material in materials.OrderBy(item => item.name, StringComparer.OrdinalIgnoreCase))
            {
                builder.AppendLine("### " + material.name);
                builder.AppendLine();
                builder.AppendLine("- Shader: `" + (material.shader != null ? material.shader.name : "<missing>") + "`");
                var textures = GetMaterialTextures(material).ToArray();
                if (textures.Length == 0) builder.AppendLine("- Assigned texture slots: none");
                foreach (var pair in textures)
                    builder.AppendLine("- `" + pair.Key + "`: `" + AssetDatabase.GetAssetPath(pair.Value) + "` (" +
                        pair.Value.width + "×" + pair.Value.height + ")");
                builder.AppendLine();
            }
            File.WriteAllText(ReportPath, builder.ToString(), Encoding.UTF8);
        }

        static void CaptureOverview(Scene scene, GameObject instance, Bounds bounds)
        {
            var importedLights = instance.GetComponentsInChildren<Light>(true);
            var importedLightStates = importedLights.Select(item => item.enabled).ToArray();
            foreach (var importedLight in importedLights) importedLight.enabled = false;
            var cameraObject = new GameObject("Temporary import audit camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            var camera = cameraObject.AddComponent<Camera>();
            camera.cullingMask = 1 << DiagnosticLayer;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.055f, 0.06f, 0.07f);
            camera.fieldOfView = 55f;
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = Mathf.Max(500f, bounds.size.magnitude * 4f);
            var direction = new Vector3(1f, 0.65f, -1f).normalized;
            camera.transform.position = bounds.center + direction * Mathf.Max(5f, bounds.size.magnitude * 0.8f);
            camera.transform.LookAt(bounds.center);

            var lightObject = new GameObject("Temporary neutral audit light");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.7f;
            light.color = new Color(1f, 0.96f, 0.9f);
            light.cullingMask = 1 << DiagnosticLayer;
            light.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

            var previousAmbientMode = RenderSettings.ambientMode;
            var previousAmbient = RenderSettings.ambientLight;
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.13f, 0.14f, 0.16f);
            CaptureCamera(camera, EvidenceFolder + "/unity-import-overview.png");

            var requestedViews = new[] { "cocina", "suit principal", "baño principal", "comedor", "Camera", "Camera.002" };
            var sourceCameras = instance.GetComponentsInChildren<Camera>(true);
            foreach (var viewName in requestedViews)
            {
                var sourceCamera = sourceCameras.FirstOrDefault(item =>
                    string.Equals(item.name, viewName, StringComparison.OrdinalIgnoreCase));
                if (sourceCamera == null) continue;
                sourceCamera.cullingMask = 1 << DiagnosticLayer;
                sourceCamera.clearFlags = CameraClearFlags.SolidColor;
                sourceCamera.backgroundColor = new Color(0.055f, 0.06f, 0.07f);
                sourceCamera.nearClipPlane = 0.03f;
                sourceCamera.farClipPlane = 500f;
                sourceCamera.allowHDR = false;
                var safeName = new string(viewName.Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray());
                CaptureCamera(sourceCamera, EvidenceFolder + "/unity-source-camera-" + safeName + ".png");
            }

            UnityEngine.Object.DestroyImmediate(cameraObject);
            UnityEngine.Object.DestroyImmediate(lightObject);
            for (var index = 0; index < importedLights.Length; index++)
                importedLights[index].enabled = importedLightStates[index];
            RenderSettings.ambientMode = previousAmbientMode;
            RenderSettings.ambientLight = previousAmbient;
        }

        static void CaptureCamera(Camera camera, string path)
        {
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = target;
            camera.Render();
            RenderTexture.active = target;
            var image = new Texture2D(1920, 1080, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1920, 1080), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(image);
            UnityEngine.Object.DestroyImmediate(target);
        }

        static IEnumerable<Texture> GetAssignedTextures(IEnumerable<Material> materials) =>
            materials.SelectMany(material => GetMaterialTextures(material).Select(pair => pair.Value)).Distinct();

        static IEnumerable<KeyValuePair<string, Texture>> GetMaterialTextures(Material material)
        {
            if (material.shader == null) yield break;
            for (var index = 0; index < material.shader.GetPropertyCount(); index++)
            {
                if (material.shader.GetPropertyType(index) != ShaderPropertyType.Texture) continue;
                var propertyName = material.shader.GetPropertyName(index);
                var texture = material.GetTexture(propertyName);
                if (texture != null) yield return new KeyValuePair<string, Texture>(propertyName, texture);
            }
        }

        static long CountTriangles(Mesh mesh)
        {
            long count = 0;
            for (var subMesh = 0; subMesh < mesh.subMeshCount; subMesh++)
                if (mesh.GetTopology(subMesh) == MeshTopology.Triangles)
                    count += (long)mesh.GetIndexCount(subMesh) / 3L;
            return count;
        }

        static Bounds Encapsulate(IEnumerable<Bounds> values)
        {
            using (var iterator = values.GetEnumerator())
            {
                if (!iterator.MoveNext()) return new Bounds(Vector3.zero, Vector3.zero);
                var result = iterator.Current;
                while (iterator.MoveNext()) result.Encapsulate(iterator.Current);
                return result;
            }
        }

        static string GetPath(Transform transform, Transform root)
        {
            var names = new Stack<string>();
            for (var current = transform; current != null; current = current.parent)
            {
                names.Push(current.name);
                if (current == root) break;
            }
            return string.Join("/", names.ToArray());
        }

        static string Format(Vector3 value, CultureInfo culture) => string.Format(culture,
            "({0:R}, {1:R}, {2:R})", value.x, value.y, value.z);
    }
}
