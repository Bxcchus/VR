using System.IO;
using System.Linq;
using CampusExplorer;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace CampusExplorer.Editor
{
    public static class CampusTourSceneBuilder
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/CampusTour.unity";
        const string MaterialFolder = "Assets/CampusExplorer/Materials";

        static readonly (string id, string name, string body, float z)[] Stops =
        {
            ("accueil", "Accueil", "Bienvenue à Epitech Paris. Ce point lance une visite courte des espaces et explique comment se repérer avant une venue sur le campus.", 9f),
            ("kb2", "KB2", "Espace polyvalent du parcours : rencontres, présentations et vie de campus. La représentation est volontairement simplifiée pour ce prototype XR.", 3f),
            ("salle-cours", "Salle de cours", "Un espace consacré au travail collectif et aux apprentissages. Les tables et écrans sont construits avec des primitives en attendant les assets autorisés.", -3f),
            ("salle-pedago", "Salle pédago", "Point de contact avec l'équipe pédagogique pour l'accompagnement, les questions de parcours et le suivi des étudiants.", -9f),
        };

        public static void BuildVerifyAndCapture()
        {
            BuildScene();
            VerifyScene();
            CapturePreview();
        }

        [MenuItem("Campus Explorer/Build Campus Tour Scene")]
        public static void BuildScene()
        {
            IncrementASceneBuilder.BuildScene();
            var scene = SceneManager.GetActiveScene();
            EditorSceneManager.SaveScene(scene, ScenePath);

            DestroyRoot("Room - primitives only");
            DestroyRoot("POI - Découvrir le campus");
            DestroyRoot("Information Panel Controller");
            DestroyRoot("Simulator Instructions");
            DestroyRoot("Environment Visuals - replaceable");
            DestroyRoot("Gameplay Geometry - keep");

            var floor = Material("Campus Floor", new Color(0.13f, 0.17f, 0.21f));
            var wall = Material("Campus Wall", new Color(0.78f, 0.80f, 0.82f));
            var trim = Material("Campus Trim", new Color(0.07f, 0.12f, 0.17f));
            var blue = Material("Epitech Blue", new Color(0.02f, 0.36f, 0.64f));
            var cyan = Material("Epitech Cyan", new Color(0.05f, 0.72f, 0.82f));
            var hover = Material("Epitech Hover", new Color(0.35f, 1f, 1f));
            var desk = Material("Furniture", new Color(0.31f, 0.35f, 0.39f));
            var screen = Material("Screen", new Color(0.04f, 0.09f, 0.13f));
            var dark = Material("Campus Panel", new Color(0.018f, 0.03f, 0.055f));

            var environmentVisuals = new GameObject("Environment Visuals - replaceable");
            var placeholderVisuals = new GameObject("Placeholder Visuals - primitives");
            placeholderVisuals.transform.SetParent(environmentVisuals.transform, false);
            var officialAssets = new GameObject("Official Assets - drop here");
            officialAssets.transform.SetParent(environmentVisuals.transform, false);
            var fullEnvironmentSlot = new GameObject("Full Environment Asset Slot");
            fullEnvironmentSlot.transform.SetParent(officialAssets.transform, false);
            foreach (var stop in Stops)
            {
                var zoneSlot = new GameObject($"Zone Asset Slot - {stop.name}");
                zoneSlot.transform.SetParent(officialAssets.transform, false);
                zoneSlot.transform.localPosition = new Vector3(0f, 0f, stop.z);
            }

            BuildShell(placeholderVisuals.transform, floor, wall, trim, blue);
            BuildFurniture(placeholderVisuals.transform, desk, screen, blue, cyan);
            RemoveColliders(placeholderVisuals);
            BuildGameplayGeometry();
            CreateRoomLights();

            var rig = GameObject.Find("XR Player");
            rig.transform.SetPositionAndRotation(new Vector3(0f, 0f, 10.3f), Quaternion.Euler(0f, 180f, 0f));

            foreach (var stop in Stops)
                CreateTeleportPad(stop.name, new Vector3(-2.55f, 0.025f, stop.z), cyan);

            var progress = CreateProgress(dark, cyan);
            foreach (var stop in Stops)
            {
                var panel = CreateInfoPanel(stop.id, stop.z, dark, cyan);
                CreatePoint(stop.id, stop.name, stop.body, stop.z, panel, blue, hover);
            }

            CreateGuidance(progress, dark, cyan);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true),
                new EditorBuildSettingsScene("Assets/CampusExplorer/Scenes/IncrementA.unity", true)
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Campus Explorer] Campus Tour generated successfully: {ScenePath}");
        }

        [MenuItem("Campus Explorer/Verify Campus Tour Scene")]
        public static void VerifyScene()
        {
            if (!File.Exists(ScenePath))
                throw new FileNotFoundException($"Scene not found: {ScenePath}");

            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            Require(roots.Count(root => root.name == "XR Player") == 1, "one XR Player");
            Require(roots.Count(root => root.name == "XR Interaction Simulator (Editor)") == 1, "one XR simulator");
            Require(Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Length == 1, "one camera");
            Require(Object.FindObjectsByType<XRInteractionManager>(FindObjectsInactive.Include).Length == 1, "one interaction manager");
            Require(Object.FindObjectsByType<XRInteractionSimulator>(FindObjectsInactive.Include).Length == 1, "one simulator component");
            Require(Object.FindObjectsByType<TeleportationArea>(FindObjectsInactive.Include).Length == Stops.Length, "four teleport areas");
            var points = Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include);
            Require(points.Length == Stops.Length, "four points of interest");
            Require(points.Select(point => point.PointId).Distinct().Count() == Stops.Length, "four unique point identifiers");
            Require(Object.FindObjectsByType<InfoPanel>(FindObjectsInactive.Include).Length == Stops.Length, "four local information panels");
            Require(Object.FindObjectsByType<TourProgress>(FindObjectsInactive.Include).Length == 1, "one tour progress controller");
            Require(roots.Count(root => root.name == "Environment Visuals - replaceable") == 1, "one replaceable visual environment");
            Require(roots.Count(root => root.name == "Gameplay Geometry - keep") == 1, "one persistent gameplay geometry root");
            Require(GameObject.Find("Placeholder Visuals - primitives") != null, "one primitive fallback visual set");
            Require(GameObject.Find("Official Assets - drop here") != null, "one official asset integration slot");
            Require(roots.Sum(CountMissingScripts) == 0, "no missing scripts");

            var dependencies = AssetDatabase.GetDependencies(ScenePath, true)
                .Where(path => path.StartsWith("Assets/Samples/"))
                .OrderBy(path => path)
                .ToArray();
            Directory.CreateDirectory("Evidence");
            File.WriteAllLines("Evidence/campus-tour-sample-dependencies.txt", dependencies);
            Debug.Log("[Campus Explorer] CAMPUS TOUR VALIDATION PASS - four spaces, four unique POIs, four teleport areas, progress, XR rig and simulator; no missing scripts.");
        }

        [MenuItem("Campus Explorer/Capture Campus Tour Preview")]
        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var camera = Object.FindAnyObjectByType<Camera>();
            if (camera == null)
                throw new System.InvalidOperationException("Cannot capture Campus Tour: camera not found.");

            var ceiling = GameObject.Find("Campus Ceiling");
            var ceilingRenderer = ceiling != null ? ceiling.GetComponent<Renderer>() : null;
            if (ceilingRenderer != null)
                ceilingRenderer.enabled = false;

            Directory.CreateDirectory("Evidence");
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.04f, 0.06f);
            camera.orthographic = true;
            camera.orthographicSize = 13f;
            camera.transform.SetPositionAndRotation(new Vector3(0f, 18f, 0f), Quaternion.Euler(90f, 0f, 0f));
            CaptureCamera(camera, "Evidence/campus-tour-plan.png", 900, 1200);

            camera.orthographic = false;
            camera.fieldOfView = 72f;
            camera.transform.position = new Vector3(0f, 1.65f, 10.55f);
            camera.transform.LookAt(new Vector3(0f, 1.3f, 4.6f));
            CaptureCamera(camera, "Evidence/campus-tour-entry.png", 1280, 720);

            if (ceilingRenderer != null)
                ceilingRenderer.enabled = true;
            Debug.Log("[Campus Explorer] Campus Tour previews captured.");
        }

        [MenuItem("Campus Explorer/Build Campus Tour Windows Simulator")]
        public static void BuildWindowsSimulator()
        {
            Directory.CreateDirectory("Builds/Windows");
            var report = BuildPipeline.BuildPlayer(
                new[] { ScenePath },
                "Builds/Windows/CampusTourXR.exe",
                BuildTarget.StandaloneWindows64,
                BuildOptions.None);
            if (report.summary.result != BuildResult.Succeeded)
                throw new System.InvalidOperationException($"Campus Tour Windows build failed: {report.summary.result}");
            Debug.Log($"[Campus Explorer] CAMPUS TOUR WINDOWS BUILD PASS - {report.summary.totalSize} bytes, {report.summary.totalErrors} errors, {report.summary.totalWarnings} warnings.");
        }

        static void BuildShell(Transform parent, Material floor, Material wall, Material trim, Material blue)
        {
            var root = new GameObject("Campus - simplified reconstruction");
            root.transform.SetParent(parent, false);
            Primitive("Campus Floor", PrimitiveType.Cube, root.transform, new Vector3(0f, -0.05f, 0f), new Vector3(8f, 0.1f, 24f), floor);
            Primitive("West Outer Wall", PrimitiveType.Cube, root.transform, new Vector3(-4f, 1.5f, 0f), new Vector3(0.12f, 3f, 24f), wall);
            Primitive("East Outer Wall", PrimitiveType.Cube, root.transform, new Vector3(4f, 1.5f, 0f), new Vector3(0.12f, 3f, 24f), wall);
            Primitive("North Outer Wall", PrimitiveType.Cube, root.transform, new Vector3(0f, 1.5f, 12f), new Vector3(8f, 3f, 0.12f), wall);
            Primitive("South Outer Wall", PrimitiveType.Cube, root.transform, new Vector3(0f, 1.5f, -12f), new Vector3(8f, 3f, 0.12f), wall);
            var ceiling = Primitive("Campus Ceiling", PrimitiveType.Cube, root.transform, new Vector3(0f, 3.05f, 0f), new Vector3(8f, 0.1f, 24f), wall);
            ceiling.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;

            foreach (var z in new[] { 6f, 0f, -6f })
            {
                Primitive($"Partition Left {z}", PrimitiveType.Cube, root.transform, new Vector3(-2.45f, 1.5f, z), new Vector3(3.1f, 3f, 0.12f), wall);
                Primitive($"Partition Right {z}", PrimitiveType.Cube, root.transform, new Vector3(2.45f, 1.5f, z), new Vector3(3.1f, 3f, 0.12f), wall);
                Primitive($"Door Lintel {z}", PrimitiveType.Cube, root.transform, new Vector3(0f, 2.65f, z), new Vector3(1.8f, 0.7f, 0.12f), blue);
            }

            foreach (var stop in Stops)
            {
                Primitive($"{stop.name} Floor Stripe", PrimitiveType.Cube, root.transform,
                    new Vector3(0f, 0.006f, stop.z + 2.72f), new Vector3(8f, 0.012f, 0.13f), blue);
                var sign = WorldCanvas($"Sign - {stop.name}", root.transform,
                    new Vector3(0f, 2.25f, stop.z + 2.83f), Quaternion.Euler(0f, 180f, 0f), new Vector2(540f, 90f), 0.002f);
                TextElement("Label", sign.transform, new Vector2(520f, 80f), Vector2.zero, 36, TextAnchor.MiddleCenter, Color.white).text = stop.name.ToUpperInvariant();
            }

            Primitive("North Trim", PrimitiveType.Cube, root.transform, new Vector3(0f, 0.13f, 11.88f), new Vector3(8f, 0.16f, 0.08f), trim);
            Primitive("South Trim", PrimitiveType.Cube, root.transform, new Vector3(0f, 0.13f, -11.88f), new Vector3(8f, 0.16f, 0.08f), trim);
        }

        static void BuildFurniture(Transform parent, Material furniture, Material screen, Material blue, Material cyan)
        {
            var root = new GameObject("Furniture - Unity primitives");
            root.transform.SetParent(parent, false);

            // Accueil
            Desk(root.transform, new Vector3(0f, 0f, 8.2f), 0f, furniture, screen, true);
            Bench(root.transform, new Vector3(-2.7f, 0f, 10f), 90f, furniture, blue);
            Bench(root.transform, new Vector3(2.7f, 0f, 10f), -90f, furniture, blue);

            // KB2 : espace ouvert de présentation et de travail.
            Desk(root.transform, new Vector3(-1.55f, 0f, 3.2f), 0f, furniture, screen, true);
            Desk(root.transform, new Vector3(1.55f, 0f, 3.2f), 0f, furniture, screen, true);
            Primitive("KB2 Presentation Screen", PrimitiveType.Cube, root.transform, new Vector3(0f, 1.65f, 0.18f), new Vector3(3.4f, 1.55f, 0.08f), screen);
            Primitive("KB2 Accent", PrimitiveType.Cube, root.transform, new Vector3(0f, 0.82f, 0.12f), new Vector3(3.6f, 0.08f, 0.11f), cyan);

            // Salle de cours : deux rangées de postes.
            foreach (var x in new[] { -1.65f, 0f, 1.65f })
            {
                Desk(root.transform, new Vector3(x, 0f, -2.2f), 0f, furniture, screen, true);
                Desk(root.transform, new Vector3(x, 0f, -4.1f), 0f, furniture, screen, false);
            }
            Primitive("Course Board", PrimitiveType.Cube, root.transform, new Vector3(0f, 1.6f, -5.78f), new Vector3(3.8f, 1.35f, 0.07f), screen);

            // Salle pédago : table de réunion et rangements.
            Primitive("Pedagogy Table", PrimitiveType.Cube, root.transform, new Vector3(0f, 0.74f, -9f), new Vector3(3.7f, 0.12f, 1.25f), furniture);
            foreach (var x in new[] { -1.25f, 0f, 1.25f })
            {
                Chair(root.transform, new Vector3(x, 0f, -8.1f), 180f, furniture, blue);
                Chair(root.transform, new Vector3(x, 0f, -9.9f), 0f, furniture, blue);
            }
            Primitive("Pedagogy Storage", PrimitiveType.Cube, root.transform, new Vector3(-3.55f, 1.05f, -9f), new Vector3(0.7f, 2.1f, 2.5f), furniture);
        }

        static void Desk(Transform parent, Vector3 position, float yaw, Material furniture, Material screen, bool computer)
        {
            var root = new GameObject("Desk");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Primitive("Top", PrimitiveType.Cube, root.transform, new Vector3(0f, 0.75f, 0f), new Vector3(1.4f, 0.1f, 0.7f), furniture);
            foreach (var x in new[] { -0.58f, 0.58f })
                foreach (var z in new[] { -0.25f, 0.25f })
                    Primitive("Leg", PrimitiveType.Cube, root.transform, new Vector3(x, 0.37f, z), new Vector3(0.08f, 0.74f, 0.08f), furniture);
            if (computer)
            {
                Primitive("Monitor", PrimitiveType.Cube, root.transform, new Vector3(0f, 1.18f, -0.18f), new Vector3(0.72f, 0.42f, 0.07f), screen);
                Primitive("Monitor Stand", PrimitiveType.Cube, root.transform, new Vector3(0f, 0.93f, -0.18f), new Vector3(0.08f, 0.38f, 0.08f), furniture);
            }
        }

        static void Bench(Transform parent, Vector3 position, float yaw, Material furniture, Material accent)
        {
            var root = new GameObject("Bench");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Primitive("Seat", PrimitiveType.Cube, root.transform, new Vector3(0f, 0.48f, 0f), new Vector3(1.7f, 0.15f, 0.55f), accent);
            Primitive("Back", PrimitiveType.Cube, root.transform, new Vector3(0f, 0.92f, 0.25f), new Vector3(1.7f, 0.82f, 0.12f), furniture);
        }

        static void Chair(Transform parent, Vector3 position, float yaw, Material furniture, Material accent)
        {
            var root = new GameObject("Chair");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(position, Quaternion.Euler(0f, yaw, 0f));
            Primitive("Seat", PrimitiveType.Cube, root.transform, new Vector3(0f, 0.48f, 0f), new Vector3(0.55f, 0.12f, 0.55f), accent);
            Primitive("Back", PrimitiveType.Cube, root.transform, new Vector3(0f, 0.85f, 0.24f), new Vector3(0.55f, 0.68f, 0.1f), furniture);
        }

        static void BuildGameplayGeometry()
        {
            var root = new GameObject("Gameplay Geometry - keep");
            CollisionBox("Floor Collision", root.transform, new Vector3(0f, -0.05f, 0f), new Vector3(8f, 0.1f, 24f));
            CollisionBox("West Wall Collision", root.transform, new Vector3(-4f, 1.5f, 0f), new Vector3(0.12f, 3f, 24f));
            CollisionBox("East Wall Collision", root.transform, new Vector3(4f, 1.5f, 0f), new Vector3(0.12f, 3f, 24f));
            CollisionBox("North Wall Collision", root.transform, new Vector3(0f, 1.5f, 12f), new Vector3(8f, 3f, 0.12f));
            CollisionBox("South Wall Collision", root.transform, new Vector3(0f, 1.5f, -12f), new Vector3(8f, 3f, 0.12f));
            CollisionBox("Ceiling Collision", root.transform, new Vector3(0f, 3.05f, 0f), new Vector3(8f, 0.1f, 24f));

            foreach (var z in new[] { 6f, 0f, -6f })
            {
                CollisionBox($"Partition Left Collision {z}", root.transform, new Vector3(-2.45f, 1.5f, z), new Vector3(3.1f, 3f, 0.12f));
                CollisionBox($"Partition Right Collision {z}", root.transform, new Vector3(2.45f, 1.5f, z), new Vector3(3.1f, 3f, 0.12f));
                CollisionBox($"Door Lintel Collision {z}", root.transform, new Vector3(0f, 2.65f, z), new Vector3(1.8f, 0.7f, 0.12f));
            }
        }

        static void CollisionBox(string name, Transform parent, Vector3 position, Vector3 size)
        {
            var go = new GameObject(name, typeof(BoxCollider));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.GetComponent<BoxCollider>().size = size;
        }

        static void RemoveColliders(GameObject root)
        {
            foreach (var collider in root.GetComponentsInChildren<Collider>(true))
                Object.DestroyImmediate(collider);
        }

        static void CreateRoomLights()
        {
            foreach (var stop in Stops)
            {
                var go = new GameObject($"Light - {stop.name}");
                go.transform.position = new Vector3(0f, 2.75f, stop.z);
                var light = go.AddComponent<Light>();
                light.type = LightType.Point;
                light.range = 7f;
                light.intensity = 1.4f;
                light.color = new Color(0.83f, 0.91f, 1f);
                light.shadows = LightShadows.None;
            }
        }

        static void CreateTeleportPad(string stopName, Vector3 position, Material material)
        {
            var pad = Primitive($"Teleport - {stopName}", PrimitiveType.Cube, null, position, new Vector3(1.45f, 0.05f, 1.45f), material);
            pad.AddComponent<TeleportationArea>();
        }

        static InfoPanel CreateInfoPanel(string id, float z, Material panelMaterial, Material accent)
        {
            var controller = new GameObject($"Information Panel - {id}");
            var infoPanel = controller.AddComponent<InfoPanel>();
            var canvasObject = new GameObject("Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
            canvasObject.transform.SetParent(controller.transform, false);
            canvasObject.transform.SetPositionAndRotation(new Vector3(3.72f, 1.55f, z + 0.45f), Quaternion.Euler(0f, 90f, 0f));
            canvasObject.transform.localScale = Vector3.one * 0.0018f;
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 10;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(720f, 440f);
            canvasObject.AddComponent<Image>().color = panelMaterial.color;

            var title = TextElement("Title", canvasObject.transform, new Vector2(620f, 80f), new Vector2(0f, 135f), 38, TextAnchor.MiddleLeft, Color.white);
            var body = TextElement("Body", canvasObject.transform, new Vector2(620f, 220f), new Vector2(0f, -10f), 27, TextAnchor.UpperLeft, new Color(0.87f, 0.92f, 0.96f));
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;
            var closeObject = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeObject.transform.SetParent(canvasObject.transform, false);
            closeObject.GetComponent<RectTransform>().sizeDelta = new Vector2(210f, 62f);
            closeObject.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -180f);
            closeObject.GetComponent<Image>().color = accent.color;
            var closeButton = closeObject.GetComponent<Button>();
            TextElement("Label", closeObject.transform, new Vector2(210f, 62f), Vector2.zero, 27, TextAnchor.MiddleCenter, Color.white).text = "FERMER";
            infoPanel.Configure(canvasObject, title, body, closeButton);
            canvasObject.SetActive(false);
            return infoPanel;
        }

        static void CreatePoint(string id, string title, string body, float z, InfoPanel panel, Material idle, Material hover)
        {
            var root = new GameObject($"POI - {title}");
            root.transform.position = new Vector3(2.65f, 1.15f, z - 1.35f);
            var pedestal = Primitive("Pedestal", PrimitiveType.Cylinder, root.transform, new Vector3(0f, -0.65f, 0f), new Vector3(0.42f, 0.55f, 0.42f), idle);
            Object.DestroyImmediate(pedestal.GetComponent<Collider>());
            var target = Primitive("Interactive Target", PrimitiveType.Sphere, root.transform, Vector3.zero, Vector3.one * 0.58f, idle);
            target.AddComponent<XRSimpleInteractable>();
            var point = target.AddComponent<PointOfInterest>();
            point.Configure(id, title, body, panel, target.GetComponent<Renderer>(), idle, hover);
            var label = WorldCanvas("POI Label", root.transform, new Vector3(0f, 0.68f, 0f), Quaternion.Euler(0f, 180f, 0f), new Vector2(430f, 100f), 0.0021f);
            TextElement("Label", label.transform, new Vector2(420f, 95f), Vector2.zero, 31, TextAnchor.MiddleCenter, Color.white).text = $"SÉLECTIONNER\n{title}";
        }

        static TourProgress CreateProgress(Material dark, Material accent)
        {
            var root = new GameObject("Tour Progress");
            var progress = root.AddComponent<TourProgress>();
            var canvas = WorldCanvas("Progress Board", root.transform, new Vector3(-3.72f, 1.95f, 9.65f), Quaternion.Euler(0f, -90f, 0f), new Vector2(410f, 120f), 0.0018f);
            canvas.gameObject.AddComponent<Image>().color = dark.color;
            var label = TextElement("Progress", canvas.transform, new Vector2(380f, 100f), Vector2.zero, 32, TextAnchor.MiddleCenter, Color.white);

            var completion = WorldCanvas("Completion Panel", root.transform, new Vector3(0f, 1.55f, -11.72f), Quaternion.Euler(0f, 180f, 0f), new Vector2(720f, 420f), 0.002f);
            completion.gameObject.AddComponent<Image>().color = dark.color;
            TextElement("Title", completion.transform, new Vector2(620f, 100f), new Vector2(0f, 100f), 42, TextAnchor.MiddleCenter, Color.white).text = "VISITE TERMINÉE";
            TextElement("Body", completion.transform, new Vector2(600f, 120f), new Vector2(0f, 5f), 28, TextAnchor.MiddleCenter, new Color(0.85f, 0.92f, 0.96f)).text = "Vous avez découvert les quatre espaces du prototype Epitech Paris.";
            var restartObject = new GameObject("Restart", typeof(RectTransform), typeof(Image), typeof(Button));
            restartObject.transform.SetParent(completion.transform, false);
            restartObject.GetComponent<RectTransform>().sizeDelta = new Vector2(300f, 72f);
            restartObject.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -125f);
            restartObject.GetComponent<Image>().color = accent.color;
            var restart = restartObject.GetComponent<Button>();
            TextElement("Label", restartObject.transform, new Vector2(300f, 72f), Vector2.zero, 29, TextAnchor.MiddleCenter, Color.white).text = "RECOMMENCER";
            completion.gameObject.SetActive(false);
            progress.Configure(Stops.Length, label, completion.gameObject, restart);
            return progress;
        }

        static void CreateGuidance(TourProgress progress, Material dark, Material accent)
        {
            var canvas = WorldCanvas("Campus Tour Instructions", null, new Vector3(-3.72f, 1.38f, 8.15f), Quaternion.Euler(0f, -90f, 0f), new Vector2(500f, 480f), 0.0017f);
            canvas.gameObject.AddComponent<Image>().color = dark.color;
            var text = TextElement("Instructions", canvas.transform, new Vector2(440f, 410f), Vector2.zero, 27, TextAnchor.UpperLeft, Color.white);
            text.text = "VISITE SIMPLIFIÉE\n\nAccueil → KB2 → Cours → Pédago\n\n1. Téléportez-vous sur les dalles cyan.\n2. Sélectionnez le point lumineux de chaque espace.\n3. Consultez les quatre panneaux.\n4. Terminez au fond du parcours.";
            Primitive("Instruction Accent", PrimitiveType.Cube, canvas.transform, new Vector3(0f, -250f, 0f), new Vector3(500f, 8f, 4f), accent);
        }

        static Canvas WorldCanvas(string name, Transform parent, Vector3 position, Quaternion rotation, Vector2 size, float scale)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(parent, false);
            if (parent == null)
                go.transform.SetPositionAndRotation(position, rotation);
            else
            {
                go.transform.localPosition = position;
                go.transform.localRotation = rotation;
            }
            go.transform.localScale = Vector3.one * scale;
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.GetComponent<RectTransform>().sizeDelta = size;
            return canvas;
        }

        static Text TextElement(string name, Transform parent, Vector2 size, Vector2 position, int fontSize, TextAnchor alignment, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var text = go.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.supportRichText = false;
            return text;
        }

        static GameObject Primitive(string name, PrimitiveType type, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        static Material Material(string name, Color color)
        {
            var path = $"{MaterialFolder}/{name.Replace(' ', '_')}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            // The project currently uses Unity's built-in render pipeline. URP is
            // installed for the eventual Quest target, but no URP pipeline asset is
            // active yet, so assigning the URP shader would render these materials
            // magenta in the editor and simulator.
            var shader = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit");
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void DestroyRoot(string name)
        {
            var root = SceneManager.GetActiveScene().GetRootGameObjects().FirstOrDefault(candidate => candidate.name == name);
            if (root != null)
                Object.DestroyImmediate(root);
        }

        static int CountMissingScripts(GameObject gameObject)
        {
            var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            foreach (Transform child in gameObject.transform)
                count += CountMissingScripts(child.gameObject);
            return count;
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

        static void Require(bool condition, string expectation)
        {
            if (!condition)
                throw new System.InvalidOperationException($"Campus Tour validation failed: expected {expectation}.");
        }
    }
}
