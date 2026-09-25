using System;
using System.IO;
using System.Linq;
using CampusExplorer;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace CampusExplorer.Editor
{
    /// <summary>
    /// Integrates the original Notre-Dame GLB as the sole visual environment.
    /// Only invisible collision helpers and XR interaction objects are added.
    /// </summary>
    public static class NotreDameTourSceneBuilder
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/NotreDameTour.unity";
        const string ModelPath = "Assets/CampusExplorer/External/NotreDame/notre_dame_quest_4k.glb";

        static readonly (string id, string title, string body)[] Stops =
        {
            ("entree-nef", "Entrée de la nef", "Depuis l'entrée, la nef guide le regard vers le chœur grâce à ses travées, ses grandes arcades et son élévation gothique."),
            ("arcades", "Piliers et arcades", "La répétition des piliers et des grandes arcades structure la nef. Elle conduit progressivement le visiteur vers la croisée du transept."),
            ("transept", "Transept et rosaces", "Les grandes rosaces du transept datent du XIIIe siècle. Le développement des arcs-boutants a permis d'ouvrir davantage les murs et d'augmenter la surface vitrée."),
            ("choeur", "Chœur et autel", "Le chœur organise l'espace liturgique autour de l'autel. Il se trouve à l'est de la croisée du transept, au terme du parcours dans la nef."),
        };

        [MenuItem("Campus Explorer/Build Notre-Dame XR Tour")]
        public static void BuildVerifyAndCapture()
        {
            BuildScene();
            VerifyScene();
            CapturePreview();
        }

        public static void BuildScene()
        {
            RemoveArtificialReconstructionAssets();
            AssetDatabase.ImportAsset(ModelPath, ImportAssetOptions.ForceSynchronousImport);
            var modelAsset = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (modelAsset == null)
                throw new FileNotFoundException($"Model asset not found: {ModelPath}");

            CampusTourSceneBuilder.BuildScene();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);

            var placeholder = GameObject.Find("Placeholder Visuals - primitives");
            if (placeholder != null)
                UnityEngine.Object.DestroyImmediate(placeholder);

            var slot = GameObject.Find("Full Environment Asset Slot");
            if (slot == null)
                throw new InvalidOperationException("Environment slot was not found.");
            slot.name = "Environment";

            var importedEnvironment = new GameObject("ImportedEnvironment");
            importedEnvironment.transform.SetParent(slot.transform, false);

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(modelAsset, importedEnvironment.transform);
            instance.name = "Notre-Dame - Original GLB - Raiz - CC BY 4.0";

            instance.transform.localPosition = Vector3.zero;
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
            importedEnvironment.transform.localPosition = Vector3.zero;
            importedEnvironment.transform.localRotation = Quaternion.identity;
            importedEnvironment.transform.localScale = Vector3.one;
            var importedBounds = CalculateBounds(instance);

            MarkStatic(instance);
            WriteTransformAudit(instance, importedBounds);
            ReplaceCampusCollisions();
            UpdateTourContent();

            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene("Assets/CampusExplorer/Scenes/CampusTour.unity", true),
                new EditorBuildSettingsScene("Assets/CampusExplorer/Scenes/IncrementA.unity", true)
            };
            AssetDatabase.SaveAssets();
            Debug.Log($"[Campus Explorer] ORIGINAL MODEL TOUR BUILD PASS - {ScenePath}");
        }

        static void RemoveArtificialReconstructionAssets()
        {
            AssetDatabase.DeleteAsset("Assets/CampusExplorer/Generated/NotreDame");
            AssetDatabase.DeleteAsset("Assets/CampusExplorer/Generated");
            AssetDatabase.DeleteAsset("Assets/CampusExplorer/External/NotreDame/Materials");
            AssetDatabase.Refresh();
        }

        public static void VerifyScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var model = FindSceneObject(scene, "Notre-Dame - Original GLB - Raiz - CC BY 4.0");
            Require(model != null && model.activeInHierarchy, "the original GLB visible and active");
            Require(model.transform.localPosition == Vector3.zero, "the original model local position preserved");
            Require(model.transform.localRotation == Quaternion.identity, "the original model local rotation preserved");
            Require(model.transform.localScale == Vector3.one, "the original model local scale preserved");
            Require(model.GetComponentsInChildren<Renderer>(true).Length == 4, "the four original mesh renderers");
            Require(model.GetComponentsInChildren<Renderer>(true).All(renderer => renderer.sharedMaterials.All(material => material != null)), "all original material slots assigned");

            Require(FindSceneObject(scene, "ImportedEnvironment") != null, "Environment/ImportedEnvironment hierarchy");
            Require(FindSceneObject(scene, "Notre-Dame - Reconstructed XR Nave") == null, "the artificial reconstruction removed");
            Require(FindSceneObject(scene, "Nave Walkway - clean XR floor") == null, "the visual replacement floor removed");

            var placeholder = FindSceneObject(scene, "Placeholder Visuals - primitives");
            Require(placeholder == null, "the old visual placeholders removed");
            var collisions = FindSceneObject(scene, "Notre-Dame Navigation Collision");
            Require(collisions != null && collisions.GetComponentsInChildren<Renderer>(true).Length == 0, "collision helpers are invisible");
            Require(UnityEngine.Object.FindObjectsByType<TeleportationArea>(FindObjectsInactive.Include).Length == Stops.Length, "four teleport areas");

            var points = UnityEngine.Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include);
            Require(points.Length == Stops.Length, "four points of interest");
            Require(points.Select(point => point.PointId).OrderBy(id => id).SequenceEqual(Stops.Select(stop => stop.id).OrderBy(id => id)), "the four Notre-Dame point identifiers");
            Require(scene.GetRootGameObjects().Sum(CountMissingScripts) == 0, "no missing scripts");
            Debug.Log("[Campus Explorer] ORIGINAL MODEL VALIDATION PASS - original GLB visible, original hierarchy/materials retained, no replacement architecture, XR systems present.");
        }

        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var camera = UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (camera == null)
                throw new InvalidOperationException("Preview capture requires the XR camera.");

            Directory.CreateDirectory("Evidence");
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.035f, 0.055f);
            camera.orthographic = false;

            camera.fieldOfView = 66f;
            camera.transform.position = new Vector3(0f, 1.65f, 12.2f);
            camera.transform.LookAt(new Vector3(0f, 2.2f, -8f));
            CaptureCamera(camera, "Evidence/notre-dame-original-entry.png", 1280, 720);

            camera.fieldOfView = 60f;
            camera.transform.position = new Vector3(7.2f, 4.8f, 13.5f);
            camera.transform.LookAt(new Vector3(0f, 3.1f, -2f));
            CaptureCamera(camera, "Evidence/notre-dame-original-overview.png", 1280, 720);
            Debug.Log("[Campus Explorer] Original-model previews captured.");
        }

        [MenuItem("Campus Explorer/Build Notre-Dame Windows Simulator")]
        public static void BuildWindowsSimulator()
        {
            Directory.CreateDirectory("Builds/WindowsNotreDame");
            var report = BuildPipeline.BuildPlayer(new[] { ScenePath }, "Builds/WindowsNotreDame/NotreDameXR.exe", BuildTarget.StandaloneWindows64, BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Notre-Dame Windows build failed: {report.summary.result}");
            Debug.Log($"[Campus Explorer] NOTRE-DAME WINDOWS BUILD PASS - {report.summary.totalSize} bytes, {report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings.");
        }

        static void ReplaceCampusCollisions()
        {
            var old = GameObject.Find("Gameplay Collision");
            if (old != null) UnityEngine.Object.DestroyImmediate(old);

            var root = new GameObject("Notre-Dame Navigation Collision");
            CollisionBox("Floor collision", root.transform, new Vector3(0f, -0.08f, 0f), new Vector3(8f, 0.16f, 24f));
            CollisionBox("West boundary", root.transform, new Vector3(-4f, 1.5f, 0f), new Vector3(0.12f, 3f, 24f));
            CollisionBox("East boundary", root.transform, new Vector3(4f, 1.5f, 0f), new Vector3(0.12f, 3f, 24f));
            CollisionBox("North boundary", root.transform, new Vector3(0f, 1.5f, 12f), new Vector3(8f, 3f, 0.12f));
            CollisionBox("South boundary", root.transform, new Vector3(0f, 1.5f, -12f), new Vector3(8f, 3f, 0.12f));
        }

        static void CollisionBox(string name, Transform parent, Vector3 position, Vector3 size)
        {
            var gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = position;
            gameObject.AddComponent<BoxCollider>().size = size;
        }

        static void UpdateTourContent()
        {
            var points = UnityEngine.Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include).OrderByDescending(point => point.transform.position.z).ToArray();
            var hover = AssetDatabase.LoadAssetAtPath<Material>("Assets/CampusExplorer/Materials/Epitech_Hover.mat");
            for (var i = 0; i < points.Length; i++)
            {
                var point = points[i];
                var data = Stops[i];
                var serialized = new SerializedObject(point);
                var panel = (InfoPanel)serialized.FindProperty("infoPanel").objectReferenceValue;
                var renderer = point.GetComponent<Renderer>();
                point.Configure(data.id, data.title, data.body, panel, renderer, renderer.sharedMaterial, hover);
                point.transform.parent.name = $"POI - {data.title}";
                if (panel != null) panel.gameObject.name = $"Information Panel - {data.id}";
                var label = point.transform.parent.GetComponentsInChildren<Text>(true).FirstOrDefault(text => text.name == "Label");
                if (label != null) label.text = $"SÉLECTIONNER\n{data.title}";
                EditorUtility.SetDirty(point);
            }

            var pads = UnityEngine.Object.FindObjectsByType<TeleportationArea>(FindObjectsInactive.Include).OrderByDescending(pad => pad.transform.position.z).ToArray();
            for (var i = 0; i < pads.Length && i < Stops.Length; i++)
                pads[i].gameObject.name = $"Téléportation - {Stops[i].title}";

            var instructions = GameObject.Find("Campus Tour Instructions")?.GetComponentsInChildren<Text>(true).FirstOrDefault(text => text.name == "Instructions");
            if (instructions != null)
                instructions.text = "VISITE DE NOTRE-DAME\n\nEntrée → Arcades → Transept → Chœur\n\n1. Téléportez-vous sur les dalles cyan.\n2. Sélectionnez chaque point lumineux.\n3. Consultez les quatre panneaux.\n4. Terminez le parcours dans le chœur.";

            var completion = GameObject.Find("Completion Panel")?.GetComponentsInChildren<Text>(true).FirstOrDefault(text => text.name == "Body");
            if (completion != null)
                completion.text = "Vous avez découvert quatre points majeurs de Notre-Dame de Paris.";
        }

        static void UpdateLighting()
        {
            foreach (var light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include))
                UnityEngine.Object.DestroyImmediate(light.gameObject);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.34f, 0.34f, 0.36f);
            RenderSettings.fog = false;

            var sunObject = new GameObject("Notre-Dame Directional Light");
            sunObject.transform.rotation = Quaternion.Euler(44f, -28f, 0f);
            var sun = sunObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.2f;
            sun.color = new Color(1f, 0.92f, 0.82f);
            sun.shadows = LightShadows.Soft;

            foreach (var z in new[] { 8f, 0f, -8f })
            {
                var lightObject = new GameObject($"Nave fill light {z}");
                lightObject.transform.position = new Vector3(0f, 4.2f, z);
                var point = lightObject.AddComponent<Light>();
                point.type = LightType.Point;
                point.range = 13f;
                point.intensity = 2.2f;
                point.color = new Color(1f, 0.78f, 0.56f);
                point.shadows = LightShadows.None;
            }
        }

        static Bounds CalculateBounds(GameObject root)
        {
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
                throw new InvalidOperationException("The imported model has no renderer.");
            var bounds = renderers[0].bounds;
            foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
            return bounds;
        }

        static void WriteTransformAudit(GameObject instance, Bounds importedBounds)
        {
            Directory.CreateDirectory("Evidence");
            var renderers = instance.GetComponentsInChildren<Renderer>(true);
            var materials = renderers.SelectMany(renderer => renderer.sharedMaterials).Where(material => material != null).Distinct().ToArray();
            var materialLines = materials.Select(material =>
            {
                var texture = material.mainTexture as Texture2D;
                var textureDescription = texture == null ? "no base texture" : $"{texture.name} ({texture.width}x{texture.height})";
                return $"- {material.name} | shader: {material.shader.name} | base texture: {textureDescription}";
            });

            File.WriteAllText("Evidence/notre-dame-unity-integration-audit.txt",
                $"Original asset: {ModelPath}\n" +
                "Instantiated hierarchy: Environment/ImportedEnvironment/Notre-Dame - Original GLB - Raiz - CC BY 4.0\n" +
                "Original child transform: position (0,0,0), rotation (0,0,0), scale (1,1,1)\n" +
                "ImportedEnvironment parent: position (0,0,0), rotation (0,0,0), scale (1,1,1)\n" +
                "The GLB internal root transform is preserved by glTFast (SceneObjectCreation.Always).\n" +
                $"World bounds: center {importedBounds.center}, size {importedBounds.size}\n" +
                $"Renderers: {renderers.Length}\n" +
                "Materials and embedded textures:\n" + string.Join("\n", materialLines) + "\n" +
                "Visible replacement architecture: none\n" +
                "Navigation collision helpers: invisible BoxColliders only\n");
        }

        static void MarkStatic(GameObject root)
        {
            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                GameObjectUtility.SetStaticEditorFlags(transform.gameObject, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
        }

        static int CountMissingScripts(GameObject gameObject)
        {
            var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            foreach (Transform child in gameObject.transform) count += CountMissingScripts(child.gameObject);
            return count;
        }

        static GameObject FindSceneObject(Scene scene, string objectName)
        {
            return scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true)).Select(transform => transform.gameObject).FirstOrDefault(gameObject => gameObject.name == objectName);
        }

        static void CaptureCamera(Camera camera, string path, int width, int height)
        {
            var renderTexture = new RenderTexture(width, height, 24);
            var image = new Texture2D(width, height, TextureFormat.RGB24, false);
            camera.targetTexture = renderTexture;
            RenderTexture.active = renderTexture;
            camera.Render();
            image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            image.Apply();
            File.WriteAllBytes(path, image.EncodeToPNG());
            camera.targetTexture = null;
            RenderTexture.active = null;
            UnityEngine.Object.DestroyImmediate(renderTexture);
            UnityEngine.Object.DestroyImmediate(image);
        }

        static void Require(bool condition, string expectation)
        {
            if (!condition) throw new InvalidOperationException($"Notre-Dame validation failed: expected {expectation}.");
        }
    }
}
