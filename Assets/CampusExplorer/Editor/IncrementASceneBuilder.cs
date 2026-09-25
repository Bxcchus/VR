using System.IO;
using System.Linq;
using CampusExplorer;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace CampusExplorer.Editor
{
    public static class IncrementASceneBuilder
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/IncrementA.unity";
        const string MaterialFolder = "Assets/CampusExplorer/Materials";
        const string RigPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.6.0/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
        const string SimulatorPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.6.0/XR Interaction Simulator/XR Interaction Simulator.prefab";

        public static void BuildVerifyAndCapture()
        {
            BuildScene();
            VerifyScene();
            CapturePreview();
        }

        [MenuItem("Campus Explorer/Build Increment A Scene")]
        public static void BuildScene()
        {
            EnsureFolder(MaterialFolder);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var room = new GameObject("Room - primitives only");
            var floorMaterial = CreateMaterial("Floor", new Color(0.16f, 0.21f, 0.26f));
            var wallMaterial = CreateMaterial("Wall", new Color(0.73f, 0.77f, 0.80f));
            var accentMaterial = CreateMaterial("Accent", new Color(0.05f, 0.55f, 0.72f));
            var hoverMaterial = CreateMaterial("Accent Hover", new Color(0.25f, 0.95f, 1f));
            var darkMaterial = CreateMaterial("Panel", new Color(0.025f, 0.04f, 0.07f));

            CreatePrimitive("Floor", PrimitiveType.Cube, room.transform, new Vector3(0f, -0.05f, 0f), new Vector3(8f, 0.1f, 8f), floorMaterial);
            CreatePrimitive("North Wall", PrimitiveType.Cube, room.transform, new Vector3(0f, 1.5f, -4f), new Vector3(8f, 3f, 0.12f), wallMaterial);
            CreatePrimitive("South Wall", PrimitiveType.Cube, room.transform, new Vector3(0f, 1.5f, 4f), new Vector3(8f, 3f, 0.12f), wallMaterial);
            CreatePrimitive("West Wall", PrimitiveType.Cube, room.transform, new Vector3(-4f, 1.5f, 0f), new Vector3(0.12f, 3f, 8f), wallMaterial);
            CreatePrimitive("East Wall", PrimitiveType.Cube, room.transform, new Vector3(4f, 1.5f, 0f), new Vector3(0.12f, 3f, 8f), wallMaterial);
            var ceiling = CreatePrimitive("Ceiling", PrimitiveType.Cube, room.transform, new Vector3(0f, 3.05f, 0f), new Vector3(8f, 0.1f, 8f), wallMaterial);
            ceiling.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            var teleportPad = CreatePrimitive("Teleport Pad", PrimitiveType.Cube, room.transform,
                new Vector3(0f, 0.025f, -2.35f), new Vector3(2.4f, 0.05f, 1.8f), accentMaterial);
            teleportPad.AddComponent<TeleportationArea>();

            CreateLighting();
            CreateXRSetup();
            new GameObject("Standalone Smoke Probe").AddComponent<StandaloneSmokeProbe>();
            var infoPanel = CreateInfoPanel(darkMaterial, accentMaterial);
            CreatePointOfInterest(infoPanel, accentMaterial, hoverMaterial);
            CreateInstructionPanel(darkMaterial);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Campus Explorer] Increment A generated successfully: {ScenePath}");
        }

        [MenuItem("Campus Explorer/Verify Increment A Scene")]
        public static void VerifyScene()
        {
            if (!File.Exists(ScenePath))
                throw new FileNotFoundException($"Scene not found: {ScenePath}");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            Require(roots.Count(root => root.name == "XR Player") == 1, "exactly one XR Player root");
            Require(roots.Count(root => root.name == "XR Interaction Simulator (Editor)") == 1, "exactly one simulator root");
            Require(Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Length == 1, "exactly one camera");
            Require(Object.FindObjectsByType<XRInteractionManager>(FindObjectsInactive.Include).Length == 1, "exactly one XR Interaction Manager");
            Require(Object.FindObjectsByType<XRInteractionSimulator>(FindObjectsInactive.Include).Length == 1, "exactly one XR Interaction Simulator");
            Require(Object.FindObjectsByType<TeleportationArea>(FindObjectsInactive.Include).Length == 1, "exactly one teleportation area");
            Require(Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include).Length == 1, "exactly one point of interest");
            Require(Object.FindObjectsByType<InfoPanel>(FindObjectsInactive.Include).Length == 1, "exactly one information panel controller");
            Require(Object.FindObjectsByType<StandaloneSmokeProbe>(FindObjectsInactive.Include).Length == 1, "exactly one standalone smoke probe");

            var missingScriptCount = roots.Sum(root => CountMissingScripts(root));
            Require(missingScriptCount == 0, "no missing MonoBehaviour scripts");
            var sampleDependencies = AssetDatabase.GetDependencies(ScenePath, true)
                .Where(path => path.StartsWith("Assets/Samples/"))
                .OrderBy(path => path)
                .ToArray();
            Directory.CreateDirectory("Evidence");
            File.WriteAllLines("Evidence/scene-sample-dependencies.txt", sampleDependencies);
            Debug.Log("[Campus Explorer] VALIDATION PASS - unique XR rig, simulator, camera, manager, teleport area, POI and info panel; no missing scripts.");
        }

        [MenuItem("Campus Explorer/Capture Increment A Preview")]
        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
                throw new System.InvalidOperationException("Cannot capture Increment A: camera not found.");

            camera.transform.position = new Vector3(0f, 1.65f, 3.15f);
            camera.transform.LookAt(new Vector3(0f, 1.2f, -1.8f));
            camera.fieldOfView = 68f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.03f, 0.045f, 0.065f);

            const int width = 1280;
            const int height = 720;
            Directory.CreateDirectory("Evidence");
            CaptureCamera(camera, "Evidence/increment-a-preview.png", width, height);
            var panel = Object.FindAnyObjectByType<InfoPanel>(FindObjectsInactive.Include);
            panel.Show(
                "Bienvenue dans Campus Explorer XR",
                "Contenu de démonstration. Cette visite fictive aide les futurs étudiants et leurs familles à découvrir un campus, ses espaces et les projets réalisés avant une visite sur place.");
            CaptureCamera(camera, "Evidence/increment-a-panel-preview.png", width, height);
            Debug.Log("[Campus Explorer] Previews captured: Evidence/increment-a-preview.png and Evidence/increment-a-panel-preview.png");
        }

        [MenuItem("Campus Explorer/Build Windows Simulator")]
        public static void BuildWindowsSimulator()
        {
            Directory.CreateDirectory("Builds/Windows");
            var report = BuildPipeline.BuildPlayer(
                new[] { ScenePath },
                "Builds/Windows/CampusExplorerXR.exe",
                BuildTarget.StandaloneWindows64,
                BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.InvalidOperationException($"Windows build failed: {report.summary.result}");

            Debug.Log($"[Campus Explorer] WINDOWS BUILD PASS - {report.summary.totalSize} bytes, {report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings.");
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
            Object.DestroyImmediate(renderTexture);
            Object.DestroyImmediate(image);
        }

        static int CountMissingScripts(GameObject gameObject)
        {
            var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            foreach (Transform child in gameObject.transform)
                count += CountMissingScripts(child.gameObject);
            return count;
        }

        static void Require(bool condition, string expectation)
        {
            if (!condition)
                throw new System.InvalidOperationException($"Increment A validation failed: expected {expectation}.");
        }

        static void CreateXRSetup()
        {
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            var simulatorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefabPath);
            if (rigPrefab == null || simulatorPrefab == null)
                throw new FileNotFoundException("Required XRI Starter Assets or XR Interaction Simulator sample is missing.");

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.name = "XR Player";
            rig.transform.position = new Vector3(0f, 0f, 2.8f);

            var simulator = (GameObject)PrefabUtility.InstantiatePrefab(simulatorPrefab);
            simulator.name = "XR Interaction Simulator (Editor)";

            if (Object.FindAnyObjectByType<XRInteractionManager>() == null)
                new GameObject("XR Interaction Manager").AddComponent<XRInteractionManager>();

            if (Object.FindAnyObjectByType<EventSystem>() == null)
            {
                var eventSystem = new GameObject("XR Event System");
                eventSystem.AddComponent<EventSystem>();
                eventSystem.AddComponent<XRUIInputModule>();
            }
        }

        static InfoPanel CreateInfoPanel(Material panelMaterial, Material accentMaterial)
        {
            var controller = new GameObject("Information Panel Controller");
            var infoPanel = controller.AddComponent<InfoPanel>();

            var canvasObject = new GameObject("Information Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
            canvasObject.transform.SetParent(controller.transform, false);
            canvasObject.transform.position = new Vector3(0f, 1.55f, -3.72f);
            canvasObject.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
            canvasObject.transform.localScale = Vector3.one * 0.0022f;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            var canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(900f, 520f);

            var background = canvasObject.AddComponent<Image>();
            background.color = panelMaterial.color;

            var title = CreateText("Title", canvasObject.transform, new Vector2(760f, 90f), new Vector2(0f, 160f), 42, TextAnchor.MiddleLeft, Color.white);
            var body = CreateText("Body", canvasObject.transform, new Vector2(760f, 235f), new Vector2(0f, 5f), 29, TextAnchor.UpperLeft, new Color(0.86f, 0.91f, 0.95f));
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;

            var closeObject = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeObject.transform.SetParent(canvasObject.transform, false);
            var closeRect = closeObject.GetComponent<RectTransform>();
            closeRect.sizeDelta = new Vector2(250f, 72f);
            closeRect.anchoredPosition = new Vector2(0f, -195f);
            closeObject.GetComponent<Image>().color = accentMaterial.color;
            var closeButton = closeObject.GetComponent<Button>();
            CreateText("Label", closeObject.transform, new Vector2(250f, 72f), Vector2.zero, 30, TextAnchor.MiddleCenter, Color.white).text = "FERMER";

            title.text = "Point d'intérêt";
            body.text = "Sélectionnez le point lumineux pour afficher son contenu.";
            infoPanel.Configure(canvasObject, title, body, closeButton);
            canvasObject.SetActive(false);
            return infoPanel;
        }

        static void CreatePointOfInterest(InfoPanel panel, Material idle, Material hover)
        {
            var root = new GameObject("POI - Découvrir le campus");
            root.transform.position = new Vector3(2.15f, 1.15f, -1.35f);

            var pedestal = CreatePrimitive("Pedestal", PrimitiveType.Cylinder, root.transform, new Vector3(0f, -0.65f, 0f), new Vector3(0.5f, 0.55f, 0.5f), idle);
            Object.DestroyImmediate(pedestal.GetComponent<Collider>());
            var target = CreatePrimitive("Interactive Target", PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * 0.65f, idle);
            target.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRSimpleInteractable>();
            var point = target.AddComponent<PointOfInterest>();
            point.Configure(
                "campus-intro",
                "Bienvenue dans Campus Explorer XR",
                "Contenu de démonstration. Cette visite fictive aide les futurs étudiants et leurs familles à découvrir un campus, ses espaces et les projets réalisés avant une visite sur place.",
                panel,
                target.GetComponent<Renderer>(),
                idle,
                hover);

            var label = CreateWorldCanvas("POI Label", root.transform, new Vector3(0f, 0.72f, 0f), Quaternion.Euler(0f, 180f, 0f), new Vector2(520f, 120f), 0.0024f);
            CreateText("Label", label.transform, new Vector2(500f, 110f), Vector2.zero, 34, TextAnchor.MiddleCenter, Color.white).text = "SÉLECTIONNER\nDécouvrir le campus";
        }

        static void CreateInstructionPanel(Material panelMaterial)
        {
            var canvas = CreateWorldCanvas("Simulator Instructions", null, new Vector3(-2.1f, 1.55f, 0.85f), Quaternion.Euler(0f, 180f, 0f), new Vector2(640f, 520f), 0.002f);
            canvas.gameObject.AddComponent<Image>().color = panelMaterial.color;
            var text = CreateText("Instructions", canvas.transform, new Vector2(560f, 440f), Vector2.zero, 31, TextAnchor.UpperLeft, Color.white);
            text.text = "SIMULATEUR XR\n\nClic droit : capturer la souris\nZQSD : déplacer le casque\nMaj gauche : main gauche\nEspace : main droite\nClic gauche : sélectionner\n\n1. Visez la dalle cyan et sélectionnez-la.\n2. Visez le point lumineux et sélectionnez-le.";
        }

        static Canvas CreateWorldCanvas(string name, Transform parent, Vector3 position, Quaternion rotation, Vector2 size, float scale)
        {
            var canvasObject = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(parent, false);
            if (parent == null)
            {
                canvasObject.transform.position = position;
                canvasObject.transform.rotation = rotation;
            }
            else
            {
                canvasObject.transform.localPosition = position;
                canvasObject.transform.localRotation = rotation;
            }
            canvasObject.transform.localScale = Vector3.one * scale;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvasObject.GetComponent<RectTransform>().sizeDelta = size;
            return canvas;
        }

        static Text CreateText(string name, Transform parent, Vector2 size, Vector2 position, int fontSize, TextAnchor alignment, Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.supportRichText = false;
            return text;
        }

        static GameObject CreatePrimitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var primitive = GameObject.CreatePrimitive(type);
            primitive.name = name;
            primitive.transform.SetParent(parent, false);
            primitive.transform.localPosition = position;
            primitive.transform.localScale = scale;
            primitive.GetComponent<Renderer>().sharedMaterial = material;
            return primitive;
        }

        static void CreateLighting()
        {
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.45f, 0.52f, 0.60f);
            RenderSettings.ambientEquatorColor = new Color(0.22f, 0.27f, 0.32f);
            RenderSettings.ambientGroundColor = new Color(0.08f, 0.10f, 0.12f);

            var lightObject = new GameObject("Directional Light");
            lightObject.transform.rotation = Quaternion.Euler(50f, -35f, 0f);
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.shadows = LightShadows.Soft;
        }

        static Material CreateMaterial(string name, Color color)
        {
            var path = $"{MaterialFolder}/{name.Replace(' ', '_')}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void EnsureFolder(string path)
        {
            var segments = path.Split('/');
            var current = segments[0];
            for (var i = 1; i < segments.Length; i++)
            {
                var next = $"{current}/{segments[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, segments[i]);
                current = next;
            }
        }
    }
}
