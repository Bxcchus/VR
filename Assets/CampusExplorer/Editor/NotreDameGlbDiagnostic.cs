using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace CampusExplorer.Editor
{
    /// <summary>
    /// Read-only audit and neutral render of the source GLB. This tool never
    /// recalculates normals, welds vertices, optimises meshes, or changes source geometry.
    /// </summary>
    public static class NotreDameGlbDiagnostic
    {
        const string ModelPath = "Assets/CampusExplorer/External/NotreDame/notre_dame_quest_4k.glb";
        const string ScenePath = "Assets/CampusExplorer/Scenes/NotreDameDiagnostic.unity";
        const string TourScenePath = "Assets/CampusExplorer/Scenes/NotreDameTour.unity";
        const string EvidenceFolder = "Evidence/notre-dame-diagnostic";

        public static void ApplyImportFixesToTourScene()
        {
            var scene = EditorSceneManager.OpenScene(TourScenePath, OpenSceneMode.Single);
            var transforms = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .ToArray();
            var environment = transforms.FirstOrDefault(item => item.name == "ImportedEnvironment");
            if (environment == null) throw new InvalidOperationException("ImportedEnvironment was not found in the tour scene");
            environment.localPosition = Vector3.zero;
            environment.localRotation = Quaternion.identity;
            environment.localScale = Vector3.one;

            var model = transforms.FirstOrDefault(item =>
                item.parent == environment && item.GetComponentsInChildren<MeshRenderer>(true).Length == 4);
            if (model == null) throw new InvalidOperationException("The Notre-Dame prefab instance was not found in the tour scene");
            var nativeMaterials = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
                .OfType<Material>()
                .ToDictionary(material => material.name, StringComparer.Ordinal);
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>(true))
            {
                var serializedRenderer = new SerializedObject(renderer);
                var materialsProperty = serializedRenderer.FindProperty("m_Materials");
                if (materialsProperty != null && materialsProperty.prefabOverride)
                    PrefabUtility.RevertPropertyOverride(materialsProperty, InteractionMode.AutomatedAction);
                serializedRenderer.Update();
                var restored = renderer.sharedMaterials.Select(material =>
                {
                    if (material == null || !nativeMaterials.TryGetValue(material.name, out var native))
                        throw new InvalidOperationException("No native glTFast material matches " + (material != null ? material.name : "<null>"));
                    return native;
                }).ToArray();
                renderer.sharedMaterials = restored;
                PrefabUtility.RecordPrefabInstancePropertyModifications(renderer);
                EditorUtility.SetDirty(renderer);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, TourScenePath);
            AssetDatabase.SaveAssets();
            var reloaded = EditorSceneManager.OpenScene(TourScenePath, OpenSceneMode.Single);
            var reloadedEnvironment = reloaded.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .First(item => item.name == "ImportedEnvironment");
            if (reloadedEnvironment.localPosition != Vector3.zero ||
                reloadedEnvironment.localRotation != Quaternion.identity ||
                reloadedEnvironment.localScale != Vector3.one)
                throw new InvalidOperationException("The tour ImportedEnvironment identity transform did not persist");
            var materialPaths = reloadedEnvironment.GetComponentsInChildren<MeshRenderer>(true)
                .SelectMany(renderer => renderer.sharedMaterials)
                .Where(material => material != null)
                .Select(AssetDatabase.GetAssetPath)
                .Distinct()
                .ToArray();
            if (materialPaths.Length != 1 || materialPaths[0] != ModelPath)
                throw new InvalidOperationException("The tour scene still uses non-native material paths: " + string.Join(", ", materialPaths));
            Debug.Log("[Notre-Dame diagnostic] TOUR IMPORT FIXES PASS - identity parent and native glTFast materials restored.");
        }

        public static void RunAudit()
        {
            Directory.CreateDirectory(EvidenceFolder);
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null) throw new FileNotFoundException("glTFast did not create a GameObject asset", ModelPath);

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            scene.name = "NotreDameDiagnostic";
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = Color.black;
            RenderSettings.fog = false;

            var parent = new GameObject("ImportedEnvironment - Diagnostic Identity");
            parent.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            parent.transform.localScale = Vector3.one;
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, parent.transform);
            instance.name = "Notre-Dame source GLB - native glTFast import";
            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;

            var renderers = instance.GetComponentsInChildren<MeshRenderer>(true);
            var filters = instance.GetComponentsInChildren<MeshFilter>(true);
            var report = BuildReport(model, instance, renderers, filters);
            File.WriteAllText(Path.Combine(EvidenceFolder, "unity-gltfast-audit.txt"), report, Encoding.UTF8);

            var cameraObject = new GameObject("Diagnostic Camera");
            var camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.025f, 0.03f);
            camera.nearClipPlane = 0.03f;
            camera.farClipPlane = 500f;
            camera.fieldOfView = 65f;
            camera.allowHDR = false;
            camera.allowMSAA = true;
            camera.depthTextureMode = DepthTextureMode.None;
            cameraObject.tag = "MainCamera";

            var interior = renderers.FirstOrDefault(r => r.sharedMaterials.Any(m => m != null && m.name.IndexOf("Interior", StringComparison.OrdinalIgnoreCase) >= 0))
                ?? renderers.OrderBy(r => r.bounds.size.sqrMagnitude).First();
            PositionInteriorCamera(camera, interior.bounds);

            foreach (var renderer in renderers) renderer.enabled = true;
            Capture(camera, Path.Combine(EvidenceFolder, "unity-native-all-meshes.png"), 1600, 900);

            foreach (var renderer in renderers)
                renderer.enabled = ReferenceEquals(renderer, interior);
            Capture(camera, Path.Combine(EvidenceFolder, "unity-native-interior-only.png"), 1600, 900);

            foreach (var renderer in renderers)
                renderer.enabled = !ReferenceEquals(renderer, interior);
            Capture(camera, Path.Combine(EvidenceFolder, "unity-native-exterior-only.png"), 1600, 900);

            foreach (var renderer in renderers) renderer.enabled = true;
            var originalMaterials = renderers.ToDictionary(r => r, r => r.sharedMaterials);
            var diagnosticMaterials = new List<Material>();
            foreach (var renderer in renderers)
            {
                var replacements = renderer.sharedMaterials.Select(source =>
                {
                    if (source == null) return null;
                    var copy = new Material(source) { name = source.name + " [TEMP DOUBLE SIDED]" };
                    if (copy.HasProperty("_Cull")) copy.SetFloat("_Cull", (float)CullMode.Off);
                    if (copy.HasProperty("_CullMode")) copy.SetFloat("_CullMode", (float)CullMode.Off);
                    if (copy.HasProperty("_DoubleSidedEnable")) copy.SetFloat("_DoubleSidedEnable", 1f);
                    diagnosticMaterials.Add(copy);
                    return copy;
                }).ToArray();
                renderer.sharedMaterials = replacements;
            }
            Capture(camera, Path.Combine(EvidenceFolder, "unity-temporary-double-sided.png"), 1600, 900);
            foreach (var entry in originalMaterials) entry.Key.sharedMaterials = entry.Value;
            foreach (var material in diagnosticMaterials) UnityEngine.Object.DestroyImmediate(material);

            foreach (var renderer in renderers) renderer.enabled = true;
            EditorSceneManager.SaveScene(scene, ScenePath);
            AssetDatabase.SaveAssets();
            Debug.Log("[Notre-Dame diagnostic] PASS. Evidence written to " + EvidenceFolder);
        }

        static string BuildReport(GameObject model, GameObject instance, MeshRenderer[] renderers, MeshFilter[] filters)
        {
            var culture = CultureInfo.InvariantCulture;
            var builder = new StringBuilder();
            builder.AppendLine("Notre-Dame Unity/glTFast diagnostic");
            builder.AppendLine("Source: " + ModelPath);
            builder.AppendLine("Unity: " + Application.unityVersion);
            builder.AppendLine("ImportedEnvironment parent: position=(0,0,0), rotation=(0,0,0), scale=(1,1,1)");
            builder.AppendLine("Additional lights: 0");
            builder.AppendLine();

            builder.AppendLine("ASSET SUBOBJECTS");
            foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath).Where(a => a != null))
            {
                builder.Append("- ").Append(asset.GetType().FullName).Append(" | ").Append(asset.name);
                if (asset is Texture assetTexture)
                    builder.Append(" | ").Append(assetTexture.width).Append('x').Append(assetTexture.height)
                        .Append(" | graphicsFormat=").Append(assetTexture.graphicsFormat);
                if (asset is Material material)
                {
                    builder.Append(" | shader=").Append(material.shader != null ? material.shader.name : "<none>");
                    var baseTexture = GetBaseTexture(material);
                    if (baseTexture != null) builder.Append(" | baseTexture=").Append(baseTexture.name).Append(' ')
                        .Append(baseTexture.width).Append('x').Append(baseTexture.height);
                }
                if (asset is Mesh mesh)
                    builder.Append(" | vertices=").Append(mesh.vertexCount).Append(" | triangles=").Append(mesh.triangles.Length / 3);
                builder.AppendLine();
            }

            builder.AppendLine();
            builder.AppendLine("HIERARCHY TRANSFORMS");
            foreach (var transform in instance.GetComponentsInChildren<Transform>(true))
            {
                builder.Append("- ").Append(GetPath(transform, instance.transform))
                    .Append(" | localPosition=").Append(Format(transform.localPosition, culture))
                    .Append(" | localRotation=").Append(Format(transform.localRotation.eulerAngles, culture))
                    .Append(" | localScale=").Append(Format(transform.localScale, culture))
                    .Append(" | lossyScale=").Append(Format(transform.lossyScale, culture))
                    .Append(" | determinant=").Append(transform.localToWorldMatrix.determinant.ToString("R", culture))
                    .AppendLine();
            }

            var bounds = Encapsulate(renderers.Select(r => r.bounds));
            builder.AppendLine();
            builder.AppendLine("WORLD BOUNDS");
            builder.AppendLine("- min=" + Format(bounds.min, culture));
            builder.AppendLine("- max=" + Format(bounds.max, culture));
            builder.AppendLine("- center=" + Format(bounds.center, culture));
            builder.AppendLine("- size=" + Format(bounds.size, culture));

            builder.AppendLine();
            builder.AppendLine("RENDERERS AND MESHES");
            foreach (var filter in filters)
            {
                var renderer = filter.GetComponent<MeshRenderer>();
                var mesh = filter.sharedMesh;
                if (mesh == null) continue;
                var triangleCount = 0;
                for (var i = 0; i < mesh.subMeshCount; i++)
                    triangleCount += (int)mesh.GetIndexCount(i) / 3;
                builder.AppendLine("- object=" + GetPath(filter.transform, instance.transform));
                builder.AppendLine("  mesh=" + mesh.name);
                builder.AppendLine("  vertices=" + mesh.vertexCount);
                builder.AppendLine("  triangles=" + triangleCount);
                builder.AppendLine("  normals=" + mesh.normals.Length);
                builder.AppendLine("  uv0=" + mesh.uv.Length);
                builder.AppendLine("  subMeshes=" + mesh.subMeshCount);
                var integrity = InspectMesh(mesh);
                builder.AppendLine("  indicesInRange=" + integrity.indicesInRange);
                builder.AppendLine("  degenerateTriangles=" + integrity.degenerateTriangles);
                builder.AppendLine("  windingConsistentWithNormals=" + integrity.consistentWinding);
                builder.AppendLine("  windingOpposedToNormals=" + integrity.opposedWinding);
                builder.AppendLine("  windingAmbiguous=" + integrity.ambiguousWinding);
                builder.AppendLine("  topologySha256Canonical=" + integrity.canonicalHash);
                builder.AppendLine("  uv0Min=" + Format(integrity.uvMin, culture));
                builder.AppendLine("  uv0Max=" + Format(integrity.uvMax, culture));
                builder.AppendLine("  localBounds.center=" + Format(mesh.bounds.center, culture));
                builder.AppendLine("  localBounds.size=" + Format(mesh.bounds.size, culture));
                builder.AppendLine("  worldBounds.center=" + Format(renderer.bounds.center, culture));
                builder.AppendLine("  worldBounds.size=" + Format(renderer.bounds.size, culture));
                builder.AppendLine("  determinant=" + filter.transform.localToWorldMatrix.determinant.ToString("R", culture));
                foreach (var material in renderer.sharedMaterials)
                {
                    if (material == null)
                    {
                        builder.AppendLine("  material=<null>");
                        continue;
                    }
                    var texture = GetBaseTexture(material);
                    builder.Append("  material=").Append(material.name)
                        .Append(" | shader=").Append(material.shader != null ? material.shader.name : "<none>")
                        .Append(" | renderQueue=").Append(material.renderQueue)
                        .Append(" | cull=").Append(ReadCull(material));
                    if (texture != null)
                        builder.Append(" | baseTexture=").Append(texture.name).Append(' ')
                            .Append(texture.width).Append('x').Append(texture.height)
                            .Append(" | graphicsFormat=").Append(texture.graphicsFormat);
                    builder.AppendLine();
                }
            }
            return builder.ToString();
        }

        static (bool indicesInRange, int degenerateTriangles, int consistentWinding, int opposedWinding,
            int ambiguousWinding, string canonicalHash, Vector3 uvMin, Vector3 uvMax) InspectMesh(Mesh mesh)
        {
            var vertices = mesh.vertices;
            var normals = mesh.normals;
            var uv = mesh.uv;
            var indices = mesh.triangles;
            var indicesInRange = indices.All(index => index >= 0 && index < vertices.Length);
            var threshold = Math.Max(1e-16, mesh.bounds.size.sqrMagnitude * 1e-12);
            var degenerate = 0;
            var consistent = 0;
            var opposed = 0;
            var ambiguous = 0;
            using var stream = new MemoryStream(indices.Length * sizeof(int));
            using var writer = new BinaryWriter(stream);
            for (var i = 0; i + 2 < indices.Length; i += 3)
            {
                var aIndex = indices[i];
                var bIndex = indices[i + 1];
                var cIndex = indices[i + 2];
                var sorted = new[] { aIndex, bIndex, cIndex };
                Array.Sort(sorted);
                writer.Write((uint)sorted[0]);
                writer.Write((uint)sorted[1]);
                writer.Write((uint)sorted[2]);
                if (!indicesInRange) continue;
                var cross = Vector3.Cross(vertices[bIndex] - vertices[aIndex], vertices[cIndex] - vertices[aIndex]);
                if (cross.magnitude <= threshold)
                {
                    degenerate++;
                    continue;
                }
                var averageNormal = normals.Length == vertices.Length
                    ? normals[aIndex] + normals[bIndex] + normals[cIndex]
                    : Vector3.zero;
                var dot = averageNormal.sqrMagnitude > 1e-20f ? Vector3.Dot(cross.normalized, averageNormal.normalized) : 0f;
                if (dot > 1e-4f) consistent++;
                else if (dot < -1e-4f) opposed++;
                else ambiguous++;
            }
            writer.Flush();
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(stream.ToArray())).Replace("-", string.Empty);
            var uvMin = uv.Length > 0 ? new Vector3(uv[0].x, uv[0].y, 0) : Vector3.zero;
            var uvMax = uvMin;
            foreach (var value in uv)
            {
                uvMin.x = Mathf.Min(uvMin.x, value.x);
                uvMin.y = Mathf.Min(uvMin.y, value.y);
                uvMax.x = Mathf.Max(uvMax.x, value.x);
                uvMax.y = Mathf.Max(uvMax.y, value.y);
            }
            return (indicesInRange, degenerate, consistent, opposed, ambiguous, hash, uvMin, uvMax);
        }

        static Texture GetBaseTexture(Material material)
        {
            foreach (var property in new[] { "_BaseMap", "_BaseColorTexture", "_MainTex" })
                if (material.HasProperty(property) && material.GetTexture(property) != null)
                    return material.GetTexture(property);
            return material.mainTexture;
        }

        static string ReadCull(Material material)
        {
            if (material.HasProperty("_Cull")) return material.GetFloat("_Cull").ToString(CultureInfo.InvariantCulture);
            if (material.HasProperty("_CullMode")) return material.GetFloat("_CullMode").ToString(CultureInfo.InvariantCulture);
            return "shader-default/unknown";
        }

        static void PositionInteriorCamera(Camera camera, Bounds interiorBounds)
        {
            var longest = interiorBounds.size.z >= interiorBounds.size.x ? Vector3.forward : Vector3.right;
            var up = Vector3.up;
            var position = interiorBounds.center - longest * (Mathf.Max(interiorBounds.size.z, interiorBounds.size.x) * 0.23f);
            position.y = interiorBounds.min.y + Mathf.Max(1.65f, interiorBounds.size.y * 0.12f);
            var target = position + longest * Mathf.Max(8f, Mathf.Max(interiorBounds.size.z, interiorBounds.size.x) * 0.35f);
            target.y = position.y + interiorBounds.size.y * 0.04f;
            camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation((target - position).normalized, up));
        }

        static Bounds Encapsulate(IEnumerable<Bounds> values)
        {
            using var iterator = values.GetEnumerator();
            if (!iterator.MoveNext()) throw new InvalidOperationException("No renderer bounds found");
            var result = iterator.Current;
            while (iterator.MoveNext()) result.Encapsulate(iterator.Current);
            return result;
        }

        static string GetPath(Transform transform, Transform root)
        {
            var names = new List<string>();
            for (var current = transform; current != null; current = current.parent)
            {
                names.Add(current.name);
                if (current == root) break;
            }
            names.Reverse();
            return string.Join("/", names);
        }

        static string Format(Vector3 vector, CultureInfo culture) =>
            $"({vector.x.ToString("R", culture)}, {vector.y.ToString("R", culture)}, {vector.z.ToString("R", culture)})";

        static void Capture(Camera camera, string path, int width, int height)
        {
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 4
            };
            var image = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            camera.targetTexture = target;
            RenderTexture.active = target;
            camera.Render();
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply(false, false);
            File.WriteAllBytes(path, image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(target);
            UnityEngine.Object.DestroyImmediate(image);
        }
    }
}
