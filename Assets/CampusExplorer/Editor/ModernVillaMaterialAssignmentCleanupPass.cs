using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using CampusExplorer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CampusExplorer.Editor
{
    /// <summary>
    /// Audits and repairs renderer material-slot assignments only. This pass does not
    /// edit material assets, meshes, lighting, XR systems, collisions or navigation.
    /// </summary>
    public static class ModernVillaMaterialAssignmentCleanupPass
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string EvidenceRoot = "Evidence/material-assignment-cleanup";
        const string BeforeFolder = EvidenceRoot + "/before";
        const string AfterFolder = EvidenceRoot + "/after";
        const string OverrideFolder = "Assets/CampusExplorer/Materials/ModernLuxuryVillaOverrides";
        const string GrassSourcePath = "Assets/CampusExplorer/Materials/ModernLuxuryVilla/Uncut_Grass_oilpt20_01.mat";
        const string GrassFieldPath = OverrideFolder + "/ExteriorGrass_Field.mat";
        const string GrassLandscapePath = OverrideFolder + "/ExteriorGrass_LandscapeMasked.mat";
        const string GrassNormalPath = "Assets/CampusExplorer/External/ModernLuxuryVilla/Textures/Uncut_Grass_oilpt20_2K_Normal.jpg";
        const string PlasticRemotePath = "Assets/CampusExplorer/Materials/ModernLuxuryVilla/Plastic006_black_01.mat";
        const string BathroomTilePath = "Assets/CampusExplorer/Materials/ModernLuxuryVilla/Seychelles_Beige_Marble_Tiles_02.mat";
        const int VillaLayer = 31;

        [MenuItem("Journey Through XR/Modern Villa/Material cleanup/Audit and capture BEFORE")]
        public static void AuditAndCaptureBefore()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(BeforeFolder);
            WriteAssignmentAudit("before");
            CaptureBathroomOverview(scene, Path.Combine(BeforeFolder, "bathroom-shower.png"));
            CaptureGrassUsers(scene, BeforeFolder);
            AssetDatabase.Refresh();
            Debug.Log("[Material Cleanup] BEFORE audit and captures completed.");
        }

        [MenuItem("Journey Through XR/Modern Villa/Material cleanup/Capture correction details BEFORE")]
        public static void CaptureCorrectionDetailsBefore()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(BeforeFolder);
            CaptureCorrectionViews(scene, BeforeFolder);
            AssetDatabase.Refresh();
            Debug.Log("[Material Cleanup] Detailed BEFORE captures completed.");
        }

        [MenuItem("Journey Through XR/Modern Villa/Material cleanup/Apply verified corrections")]
        public static void ApplyVerifiedCorrections()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(AfterFolder);
            EnsureAssetFolder(OverrideFolder);

            var sourceGrass = AssetDatabase.LoadAssetAtPath<Material>(GrassSourcePath);
            var grassNormal = AssetDatabase.LoadAssetAtPath<Texture>(GrassNormalPath);
            var opaqueShader = Shader.Find("Campus Explorer/Modern Villa PBR/Opaque");
            var clippedShader = Shader.Find("Campus Explorer/Modern Villa PBR/Opaque Regional Material");
            if (sourceGrass == null || grassNormal == null || opaqueShader == null || clippedShader == null)
                throw new InvalidOperationException("Grass source, normal map or cleanup shader is missing.");

            var fieldMaterial = CreateOrUpdateMaterial(GrassFieldPath, sourceGrass, clippedShader,
                "ExteriorGrass_Field");
            ConfigureExteriorGrass(fieldMaterial, grassNormal);
            fieldMaterial.SetFloat("_ClipMinX", 10000f);
            fieldMaterial.SetFloat("_ClipMaxX", 10001f);
            fieldMaterial.SetFloat("_ClipMinZ", 10000f);
            fieldMaterial.SetFloat("_ClipMaxZ", 10001f);
            var landscapeMaterial = CreateOrUpdateMaterial(GrassLandscapePath, sourceGrass, clippedShader,
                "ExteriorGrass_LandscapeMasked");
            ConfigureExteriorGrass(landscapeMaterial, grassNormal);
            var bathroomTile = AssetDatabase.LoadAssetAtPath<Material>(BathroomTilePath);
            if (bathroomTile == null) throw new InvalidOperationException("Source-authored bathroom tile material is missing.");
            ConfigureBathroomRegion(landscapeMaterial, bathroomTile);
            landscapeMaterial.SetFloat("_ClipMinX", -12.65f);
            landscapeMaterial.SetFloat("_ClipMaxX", -6.00f);
            landscapeMaterial.SetFloat("_ClipMinZ", -36.20f);
            landscapeMaterial.SetFloat("_ClipMaxZ", -32.20f);
            EditorUtility.SetDirty(fieldMaterial);
            EditorUtility.SetDirty(landscapeMaterial);

            AssignOnlySlot("Grass_Field", 0, fieldMaterial);
            AssignOnlySlot("Landscape_Ground", 0, landscapeMaterial);

            var remotePlastic = AssetDatabase.LoadAssetAtPath<Material>(PlasticRemotePath);
            if (remotePlastic == null) throw new InvalidOperationException("Source-authored remote plastic material is missing.");
            AssignOnlySlot("TV_Remote_Control", 0, remotePlastic);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            CaptureCorrectionViews(scene, AfterFolder);
            WriteAssignmentAudit("after");
            WriteReport();
            AssetDatabase.Refresh();
            Debug.Log("[Material Cleanup] Verified material-slot corrections applied and captured.");
        }

        [MenuItem("Journey Through XR/Modern Villa/Material cleanup/Validate final state")]
        public static void ValidateFinalState()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var sourceGrass = AssetDatabase.LoadAssetAtPath<Material>(GrassSourcePath);
            var fieldMaterial = AssetDatabase.LoadAssetAtPath<Material>(GrassFieldPath);
            var landscapeMaterial = AssetDatabase.LoadAssetAtPath<Material>(GrassLandscapePath);
            var tileMaterial = AssetDatabase.LoadAssetAtPath<Material>(BathroomTilePath);
            var remotePlastic = AssetDatabase.LoadAssetAtPath<Material>(PlasticRemotePath);
            Require(sourceGrass != null, "the shared source grass material still exists");
            Require(fieldMaterial != null && landscapeMaterial != null, "the two scene-specific grass overrides exist");
            Require(MaterialAt("Grass_Field", 0) == fieldMaterial, "Grass_Field keeps an exterior grass material");
            Require(MaterialAt("Landscape_Ground", 0) == landscapeMaterial, "Landscape_Ground uses the bathroom-safe grass override");
            Require(landscapeMaterial.shader != null && landscapeMaterial.shader.name.EndsWith("Regional Material", StringComparison.Ordinal),
                "only Landscape_Ground replaces grass inside the bathroom footprint");
            Require(MaterialAt("Floor_Bathroom_Tiles", 0) == tileMaterial,
                "the bathroom floor retains the source-authored Seychelles tile");
            Require(MaterialAt("TV_Remote_Control", 0) == remotePlastic,
                "the living-room remote uses the source-authored black plastic");
            Require(UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include)
                    .Where(IsVillaRenderer).Count(UsesGrassOrOverride) == 2,
                "exactly the two exterior ground renderers use grass-family materials");
            Require(GameObject.Find("XR Player") != null && GameObject.Find("Villa POIs") != null &&
                    GameObject.Find("InteractiveLights") != null,
                "XR player, POIs and interactive lights remain present");
            Debug.Log("[Material Cleanup] Final state validated: bathroom tile revealed, exterior grass preserved, remote plastic restored.");
        }

        [MenuItem("Journey Through XR/Modern Villa/Material cleanup/Capture bathroom candidates")]
        public static void CaptureBathroomCandidates()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(BeforeFolder);
            var definitions = new[]
            {
                (name: "baño", eyeY: 66.55f, file: "bathroom-ground.png"),
                (name: "baño comun", eyeY: 69.75f, file: "bathroom-common.png"),
                (name: "baño principal", eyeY: 69.80f, file: "bathroom-master.png")
            };
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            foreach (var definition in definitions)
            {
                var source = cameras.FirstOrDefault(camera => string.Equals(camera.name, definition.name,
                    StringComparison.OrdinalIgnoreCase));
                if (source == null) continue;
                CaptureTemporaryCamera(scene,
                    new Vector3(source.transform.position.x, definition.eyeY, source.transform.position.z),
                    source.transform.rotation, Path.Combine(BeforeFolder, definition.file));
            }
            AssetDatabase.Refresh();
            Debug.Log("[Material Cleanup] Bathroom candidate captures completed.");
        }

        [MenuItem("Journey Through XR/Modern Villa/Material cleanup/Capture ground bathroom isolation")]
        public static void CaptureGroundBathroomIsolation()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Directory.CreateDirectory(BeforeFolder);
            var source = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include)
                .First(camera => string.Equals(camera.name, "baño", StringComparison.OrdinalIgnoreCase));
            var position = new Vector3(source.transform.position.x, 66.55f, source.transform.position.z);
            var grassUsers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include)
                .Where(renderer => IsVillaRenderer(renderer) && UsesGrass(renderer)).ToArray();
            CaptureTemporaryCamera(scene, position, source.transform.rotation,
                Path.Combine(BeforeFolder, "bathroom-ground-isolation-all.png"));
            foreach (var renderer in grassUsers)
            {
                var wasEnabled = renderer.enabled;
                renderer.enabled = false;
                try
                {
                    CaptureTemporaryCamera(scene, position, source.transform.rotation,
                        Path.Combine(BeforeFolder, "bathroom-ground-isolation-without-" + Safe(renderer.name) + ".png"));
                }
                finally { renderer.enabled = wasEnabled; }
            }
            Debug.Log("[Material Cleanup] Ground bathroom isolation captures completed.");
        }

        static void WriteAssignmentAudit(string phase)
        {
            Directory.CreateDirectory(EvidenceRoot);
            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include)
                .Where(IsVillaRenderer)
                .OrderBy(renderer => HierarchyPath(renderer.transform), StringComparer.OrdinalIgnoreCase)
                .ToArray();

            var builder = new StringBuilder();
            builder.AppendLine("# Unity material-slot audit — " + phase);
            builder.AppendLine();
            builder.AppendLine("- Scene: `" + ScenePath + "`");
            builder.AppendLine("- Villa renderers: `" + renderers.Length + "`");
            builder.AppendLine("- Renderers using a grass material: `" + renderers.Count(UsesGrass) + "`");
            builder.AppendLine();
            builder.AppendLine("## Grass material users");
            builder.AppendLine();
            builder.AppendLine("| Renderer | World center | Bounds | Mesh/submeshes | Material slots |");
            builder.AppendLine("|---|---|---|---|---|");
            foreach (var renderer in renderers.Where(UsesGrass)) AppendRendererRow(builder, renderer);

            builder.AppendLine();
            builder.AppendLine("## Bathroom and shower renderers");
            builder.AppendLine();
            builder.AppendLine("| Renderer | World center | Bounds | Mesh/submeshes | Material slots |");
            builder.AppendLine("|---|---|---|---|---|");
            foreach (var renderer in renderers.Where(IsBathroomNamed)) AppendRendererRow(builder, renderer);

            builder.AppendLine();
            builder.AppendLine("## Indoor renderers using outdoor-only material families");
            builder.AppendLine();
            builder.AppendLine("Candidates are reported for review; this audit does not change them automatically.");
            builder.AppendLine();
            builder.AppendLine("| Renderer | World center | Material slots | Reason for review |");
            builder.AppendLine("|---|---|---|---|");
            foreach (var renderer in renderers.Where(IsIndoorOutdoorCandidate))
            {
                builder.AppendLine("| `" + Md(HierarchyPath(renderer.transform)) + "` | `" + Vec(renderer.bounds.center) +
                    "` | " + Md(MaterialList(renderer)) + " | Outdoor material family within main villa tour bounds |");
            }

            File.WriteAllText(Path.Combine(EvidenceRoot, "unity-material-assignment-audit-" + phase + ".md"),
                builder.ToString(), Encoding.UTF8);

            var rows = new StringBuilder("renderer\tslot\tmaterial\tasset\tcenter\n");
            foreach (var renderer in renderers)
            {
                var materials = renderer.sharedMaterials;
                for (var index = 0; index < materials.Length; index++)
                {
                    var material = materials[index];
                    rows.Append(renderer.name).Append('\t').Append(index).Append('\t')
                        .Append(material != null ? material.name : "<null>").Append('\t')
                        .Append(material != null ? AssetDatabase.GetAssetPath(material) : "—").Append('\t')
                        .Append(Vec(renderer.bounds.center)).AppendLine();
                }
            }
            File.WriteAllText(Path.Combine(EvidenceRoot, "unity-renderer-material-slots-" + phase + ".tsv"),
                rows.ToString(), Encoding.UTF8);
        }

        static bool IsVillaRenderer(Renderer renderer)
        {
            var current = renderer.transform;
            while (current.parent != null) current = current.parent;
            return current.name.StartsWith("Authoritative Modern Luxury Villa", StringComparison.Ordinal);
        }

        static bool UsesGrass(Renderer renderer)
        {
            return renderer.sharedMaterials.Any(material => material != null &&
                material.name.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0);
        }

        static bool UsesGrassOrOverride(Renderer renderer)
        {
            return renderer.sharedMaterials.Any(material => material != null &&
                (material.name.IndexOf("grass", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 AssetDatabase.GetAssetPath(material).StartsWith(OverrideFolder, StringComparison.Ordinal)));
        }

        static bool IsBathroomNamed(Renderer renderer)
        {
            var value = renderer.name.ToLowerInvariant();
            return value.Contains("bath") || value.Contains("shower") || value.Contains("baño");
        }

        static bool IsIndoorOutdoorCandidate(Renderer renderer)
        {
            var center = renderer.bounds.center;
            var withinTourBuilding = center.x > -15f && center.x < 6f && center.z < -18f && center.z > -40f;
            if (!withinTourBuilding) return false;
            return renderer.sharedMaterials.Any(material =>
            {
                if (material == null) return false;
                var value = material.name.ToLowerInvariant();
                return value.Contains("grass") || value.Contains("gravel") || value.Contains("pool") ||
                       value.Contains("exterior") || value.Contains("asphalt");
            });
        }

        static void AppendRendererRow(StringBuilder builder, Renderer renderer)
        {
            var filter = renderer.GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            var meshDescription = mesh != null ? mesh.name + "/" + mesh.subMeshCount : renderer.GetType().Name;
            builder.AppendLine("| `" + Md(HierarchyPath(renderer.transform)) + "` | `" + Vec(renderer.bounds.center) +
                "` | `" + Vec(renderer.bounds.size) + "` | `" + Md(meshDescription) + "` | " +
                Md(MaterialList(renderer)) + " |");
        }

        static string MaterialList(Renderer renderer)
        {
            return string.Join("; ", renderer.sharedMaterials.Select((material, index) =>
                "slot " + index + " = `" + (material != null ? material.name : "<null>") + "` (" +
                (material != null ? AssetDatabase.GetAssetPath(material) : "—") + ")"));
        }

        static void CaptureBathroomOverview(Scene scene, string path)
        {
            var source = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include)
                .FirstOrDefault(camera => string.Equals(camera.name, "baño principal", StringComparison.OrdinalIgnoreCase));
            if (source == null) throw new InvalidOperationException("The authored 'baño principal' camera was not found.");
            var position = new Vector3(source.transform.position.x, 69.80f, source.transform.position.z);
            CaptureTemporaryCamera(scene, position, source.transform.rotation, path);
        }

        static void CaptureCorrectionViews(Scene scene, string folder)
        {
            Directory.CreateDirectory(folder);
            var cameras = UnityEngine.Object.FindObjectsByType<Camera>(FindObjectsInactive.Include);
            var bathroom = cameras.First(camera => string.Equals(camera.name, "baño", StringComparison.OrdinalIgnoreCase));
            CaptureTemporaryCamera(scene, new Vector3(bathroom.transform.position.x, 66.55f, bathroom.transform.position.z),
                bathroom.transform.rotation, Path.Combine(folder, "bathroom-shower.png"));

            var exterior = cameras.First(camera => string.Equals(camera.name, "Camera.004", StringComparison.OrdinalIgnoreCase));
            CaptureTemporaryCamera(scene, new Vector3(exterior.transform.position.x, 67.00f, exterior.transform.position.z),
                exterior.transform.rotation, Path.Combine(folder, "exterior-grass.png"));
            var grassTarget = new Vector3(-10.5f, 64.72f, -15.5f);
            var grassCamera = new Vector3(-10.5f, 65.30f, -12.8f);
            CaptureTemporaryCamera(scene, grassCamera, Quaternion.LookRotation(grassTarget - grassCamera, Vector3.up),
                Path.Combine(folder, "exterior-grass-close.png"));

            var remote = FindVillaRenderer("TV_Remote_Control");
            var target = remote.bounds.center;
            var distance = Mathf.Max(0.32f, remote.bounds.extents.magnitude * 2.8f);
            var direction = new Vector3(1f, 0.75f, -1f).normalized;
            var position = target + direction * distance;
            CaptureTemporaryCamera(scene, position, Quaternion.LookRotation(target - position, Vector3.up),
                Path.Combine(folder, "living-room-tv-remote.png"));
        }

        static Material CreateOrUpdateMaterial(string path, Material source, Shader shader, string assetName)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(source) { name = assetName };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.CopyPropertiesFromMaterial(source);
                material.name = assetName;
            }
            material.shader = shader;
            return material;
        }

        static void ConfigureExteriorGrass(Material material, Texture normal)
        {
            material.SetTexture("_BumpMap", normal);
            material.SetFloat("_HasNormalMap", 1f);
            material.SetFloat("_BumpScale", 0.32f);
            material.SetFloat("_Smoothness", 0.04f);
            material.SetFloat("_GrassDetailStrength", 0.24f);
            material.SetFloat("_HasSpecularMap", 0f);
            material.SetFloat("_HasOcclusionMap", 0.18f);
            // The source grass is olive. The former bright-green tint overwhelmed
            // its luminance detail and made the lawn diverge from the reference.
            material.SetColor("_Color", new Color(0.59f, 0.605f, 0.20f, 1f));
            material.SetColor("_SpecColor", new Color(0.025f, 0.025f, 0.025f, 1f));
            var scale = new Vector2(9f, 9f);
            foreach (var property in new[] { "_MainTex", "_BumpMap", "_SpecularMap", "_OcclusionMap" })
                if (material.HasProperty(property)) material.SetTextureScale(property, scale);
        }

        static void ConfigureBathroomRegion(Material target, Material tile)
        {
            target.SetTexture("_IndoorTex", tile.GetTexture("_MainTex"));
            target.SetTexture("_IndoorBumpMap", tile.GetTexture("_BumpMap"));
            target.SetTexture("_IndoorSpecularMap", tile.GetTexture("_SpecularMap"));
            target.SetTexture("_IndoorOcclusionMap", tile.GetTexture("_OcclusionMap"));
            target.SetColor("_IndoorColor", tile.GetColor("_Color"));
            target.SetFloat("_IndoorSmoothness", tile.GetFloat("_Smoothness"));
            target.SetFloat("_IndoorBumpScale", tile.GetTexture("_BumpMap") != null ? tile.GetFloat("_BumpScale") : 0f);
            var scale = new Vector2(2.5f, 2.5f);
            foreach (var property in new[] { "_IndoorTex", "_IndoorBumpMap", "_IndoorSpecularMap", "_IndoorOcclusionMap" })
                if (target.HasProperty(property)) target.SetTextureScale(property, scale);
        }

        static void AssignOnlySlot(string rendererName, int slot, Material material)
        {
            var renderer = FindVillaRenderer(rendererName);
            var materials = renderer.sharedMaterials;
            if (slot < 0 || slot >= materials.Length)
                throw new InvalidOperationException(rendererName + " does not contain material slot " + slot + ".");
            materials[slot] = material;
            renderer.sharedMaterials = materials;
            EditorUtility.SetDirty(renderer);
            PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
        }

        static Material MaterialAt(string rendererName, int slot)
        {
            var materials = FindVillaRenderer(rendererName).sharedMaterials;
            return slot >= 0 && slot < materials.Length ? materials[slot] : null;
        }

        static Renderer FindVillaRenderer(string rendererName)
        {
            var renderer = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include)
                .FirstOrDefault(item => IsVillaRenderer(item) && string.Equals(item.name, rendererName, StringComparison.Ordinal));
            return renderer != null ? renderer : throw new InvalidOperationException("Villa renderer not found: " + rendererName);
        }

        static void EnsureAssetFolder(string path)
        {
            var current = "Assets";
            foreach (var segment in path.Substring("Assets/".Length).Split('/'))
            {
                var next = current + "/" + segment;
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, segment);
                current = next;
            }
        }

        static void WriteReport()
        {
            var text = new StringBuilder();
            text.AppendLine("# Modern Luxury Villa — material-assignment cleanup");
            text.AppendLine();
            text.AppendLine("## Corrections");
            text.AppendLine();
            text.AppendLine("| Mesh / slot | Before | After | Source evidence |");
            text.AppendLine("|---|---|---|---|");
            text.AppendLine("| `Landscape_Ground`, slot 0 | Shared `Uncut_Grass_oilpt20_01`; the terrain overlaps the ground-floor bathroom and displays grass indoors | Scene-only `ExteriorGrass_LandscapeMasked`; same authored grass outside, source-authored Seychelles tile family only within the bathroom footprint | OBJ assigns grass to `Landscape_Ground`; OBJ assigns `Seychelles_Beige_Marble_Tiles_02` to `Floor_Bathroom_Tiles`; package video shows tile in the bathroom |");
            text.AppendLine("| `Grass_Field`, slot 0 | Shared uncut-grass material without its supplied normal map, 1× tiling | Scene-only `ExteriorGrass_Field`; authored base map retained as subtle detail, normal restored, matte lawn response, two-scale breakup and 9× tiling | Package grass texture set plus exterior reference video showing a short, uniform lawn |");
            text.AppendLine("| `TV_Remote_Control`, slot 0 | `Asphalt_Street` | `Plastic006_black_01` | Explicit `usemtl Plastic006_black_01` immediately after `o TV_Remote_Control` in the supplied OBJ |");
            text.AppendLine();
            text.AppendLine("## Deliberately unchanged");
            text.AppendLine();
            text.AppendLine("- Shared `Uncut_Grass_oilpt20_01.mat` was not edited.");
            text.AppendLine("- `Floor_Bathroom_Tiles` remains assigned to `Seychelles_Beige_Marble_Tiles_02`.");
            text.AppendLine("- Default-material entries with no reliable explicit OBJ assignment were audited but not guessed.");
            text.AppendLine("- Geometry, UVs, hierarchy, lighting, XR, collisions and interaction systems were not edited.");
            text.AppendLine();
            text.AppendLine("## Evidence");
            text.AppendLine();
            text.AppendLine("- Bathroom: `before/bathroom-shower.png` → `after/bathroom-shower.png`");
            text.AppendLine("- Living-room remote: `before/living-room-tv-remote.png` → `after/living-room-tv-remote.png`");
            text.AppendLine("- Exterior grass: `before/exterior-grass.png` → `after/exterior-grass.png`");
            text.AppendLine("- Exterior grass close-up: `before/exterior-grass-close.png` → `after/exterior-grass-close.png`");
            File.WriteAllText(Path.Combine(EvidenceRoot, "material-assignment-cleanup-report.md"),
                text.ToString(), Encoding.UTF8);
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Material cleanup validation failed: " + message);
        }

        static void CaptureGrassUsers(Scene scene, string folder)
        {
            var users = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include)
                .Where(renderer => IsVillaRenderer(renderer) && UsesGrass(renderer)).ToArray();
            var directions = new[] { Vector3.forward, Vector3.back, Vector3.left, Vector3.right };
            foreach (var renderer in users)
            {
                var distance = Mathf.Max(2.5f, renderer.bounds.extents.magnitude * 0.75f);
                for (var index = 0; index < directions.Length; index++)
                {
                    var target = renderer.bounds.center;
                    var position = target - directions[index] * distance + Vector3.up * Mathf.Min(1.2f, renderer.bounds.extents.y);
                    var rotation = Quaternion.LookRotation((target - position).normalized, Vector3.up);
                    CaptureTemporaryCamera(scene, position, rotation,
                        Path.Combine(folder, "grass-user-" + Safe(renderer.name) + "-" + index + ".png"));
                }
            }
        }

        static void CaptureTemporaryCamera(Scene scene, Vector3 position, Quaternion rotation, string path)
        {
            var holder = new GameObject("Material Cleanup Evidence Camera");
            SceneManager.MoveGameObjectToScene(holder, scene);
            holder.transform.SetPositionAndRotation(position, rotation);
            var camera = holder.AddComponent<Camera>();
            camera.enabled = false;
            camera.fieldOfView = 70f;
            camera.nearClipPlane = 0.05f;
            camera.farClipPlane = 350f;
            camera.cullingMask = 1 << VillaLayer;
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            camera.renderingPath = RenderingPath.Forward;
            var exposure = holder.AddComponent<ModernVillaExposure>();
            exposure.exposureEV = 1.35f;
            exposure.contrast = 0.98f;
            exposure.saturation = 0.90f;
            exposure.shadowLift = 0.028f;
            exposure.whiteBalance = new Color(1.03f, 0.96f, 1.02f, 1f);
            try { Capture(camera, path); }
            finally { UnityEngine.Object.DestroyImmediate(holder); }
        }

        static void Capture(Camera camera, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? EvidenceRoot);
            var previous = RenderTexture.active;
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGBHalf);
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
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
            }
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

        static string Vec(Vector3 value) => string.Format(CultureInfo.InvariantCulture,
            "{0:0.###}, {1:0.###}, {2:0.###}", value.x, value.y, value.z);
        static string Md(string value) => value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
        static string Safe(string value) => string.Concat(value.Select(character =>
            char.IsLetterOrDigit(character) ? character : '_'));
    }
}
