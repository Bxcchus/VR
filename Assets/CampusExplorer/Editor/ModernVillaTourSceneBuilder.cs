using System.IO;
using System.Linq;
using CampusExplorer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace CampusExplorer.Editor
{
    /// <summary>
    /// Adds the first XR increment around the untouched Modern Luxury Villa import.
    /// The villa geometry, transforms and materials remain sourced from the FBX.
    /// </summary>
    public static class ModernVillaTourSceneBuilder
    {
        const string SourceScenePath = "Assets/CampusExplorer/Scenes/ModernVillaSource.unity";
        const string TourScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string MaterialFolder = "Assets/CampusExplorer/Materials";
        const string RigPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.6.0/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
        const string SimulatorPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.6.0/XR Interaction Simulator/XR Interaction Simulator.prefab";

        // The source floor is at y ~= 64.878 m. These points sit in the kitchen,
        // which is one of the original FBX camera locations and has been captured in Evidence/.
        static readonly Vector3 StartFloorPosition = new(-3.45f, 64.87821f, -23.05f);
        static readonly Vector3 StartForward = Quaternion.Euler(0f, 128.8543f, 0f) * Vector3.forward;
        static readonly Vector3 KitchenFloorPosition = new(-2.50f, 64.81821f, -24.00f);
        static readonly Vector3 KitchenFloorSize = new(12.00f, 0.12f, 12.00f);
        static readonly Vector3 PoiPosition = new(1.55f, 65.75f, -23.60f);

        [MenuItem("Campus Explorer/Build Modern Villa XR Tour")]
        public static void BuildAndVerify()
        {
            BuildScene();
            VerifyScene();
        }

        public static void BuildScene()
        {
            if (!File.Exists(SourceScenePath))
                throw new FileNotFoundException("Run the Modern Villa import audit first.", SourceScenePath);

            var scene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
            DisableSourceCameras();
            CreateXRSetup();
            CreateTeleportArea();

            var accent = CreateMaterial("Modern Villa Accent", new Color(0.04f, 0.56f, 0.78f));
            var hover = CreateMaterial("Modern Villa Accent Hover", new Color(0.24f, 0.93f, 1f));
            var panel = CreateInfoPanel();
            CreatePointOfInterest(panel, accent, hover);
            new GameObject("Standalone Smoke Probe").AddComponent<StandaloneSmokeProbe>();

            EditorSceneManager.SaveScene(scene, TourScenePath);
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(TourScenePath, true),
                new EditorBuildSettingsScene(SourceScenePath, false)
            };
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[Modern Villa] XR tour generated: {TourScenePath}");
        }

        public static void VerifyScene()
        {
            var scene = EditorSceneManager.OpenScene(TourScenePath, OpenSceneMode.Single);
            var roots = scene.GetRootGameObjects();
            Require(roots.Any(root => root.name == "Authoritative Modern Luxury Villa - Identity Transform"), "authoritative villa root");
            Require(roots.Count(root => root.name == "XR Player") == 1, "one XR Player");
            Require(roots.Count(root => root.name == "XR Interaction Simulator (Editor)") == 1, "one XR simulator");
            Require(Object.FindObjectsByType<Camera>(FindObjectsInactive.Include).Count(camera => camera.isActiveAndEnabled) == 1, "one active XR camera");
            Require(Object.FindObjectsByType<XRInteractionSimulator>(FindObjectsInactive.Include).Length == 1, "simulator component");
            Require(Object.FindObjectsByType<XRInteractionManager>(FindObjectsInactive.Include).Length == 1, "interaction manager");
            Require(Object.FindObjectsByType<TeleportationArea>(FindObjectsInactive.Include).Length == 1, "teleportation area");
            Require(Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include).Length == 1, "point of interest");
            Require(Object.FindObjectsByType<InfoPanel>(FindObjectsInactive.Include).Length == 1, "information panel");
            Require(roots.Sum(CountMissingScripts) == 0, "no missing MonoBehaviour scripts");
            Debug.Log("[Modern Villa] XR VALIDATION PASS - villa, player, simulator, teleportation and POI panel are present; no missing scripts.");
        }

        static void DisableSourceCameras()
        {
            foreach (var camera in Object.FindObjectsByType<Camera>(FindObjectsInactive.Include))
            {
                camera.enabled = false;
                var listener = camera.GetComponent<AudioListener>();
                if (listener != null) listener.enabled = false;
            }
        }

        static void CreateXRSetup()
        {
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            var simulatorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefabPath);
            if (rigPrefab == null || simulatorPrefab == null)
                throw new FileNotFoundException("XRI Starter Assets or XR Interaction Simulator sample is missing.");

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.name = "XR Player";
            rig.transform.SetPositionAndRotation(StartFloorPosition, Quaternion.Euler(0f, 128.8543f, 0f));
            var origin = rig.GetComponent<XROrigin>();
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.CameraYOffset = 0f;
            rig.AddComponent<ModernVillaXRSpawnGuard>().Configure(StartFloorPosition, StartForward);

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

        static void CreateTeleportArea()
        {
            var area = new GameObject("Teleportation Area - Kitchen");
            area.transform.position = KitchenFloorPosition;
            var collider = area.AddComponent<BoxCollider>();
            collider.size = KitchenFloorSize;
            collider.isTrigger = false;
            area.AddComponent<TeleportationArea>();
        }

        static InfoPanel CreateInfoPanel()
        {
            var controller = new GameObject("Information Panel Controller");
            var infoPanel = controller.AddComponent<InfoPanel>();
            var canvasObject = new GameObject("Information Panel", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster));
            canvasObject.transform.SetParent(controller.transform, false);
            canvasObject.transform.position = new Vector3(1.55f, 66.65f, -23.55f);
            canvasObject.transform.rotation = Quaternion.Euler(0f, 270f, 0f);
            canvasObject.transform.localScale = Vector3.one * 0.00145f;

            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 20;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, 520f);
            canvasObject.AddComponent<Image>().color = new Color(0.025f, 0.035f, 0.05f, 0.96f);

            var title = CreateText("Title", canvasObject.transform, new Vector2(760f, 90f), new Vector2(0f, 160f), 42, TextAnchor.MiddleLeft, Color.white);
            var body = CreateText("Body", canvasObject.transform, new Vector2(760f, 235f), new Vector2(0f, 5f), 28, TextAnchor.UpperLeft, new Color(0.86f, 0.91f, 0.95f));
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;

            var closeObject = new GameObject("Close", typeof(RectTransform), typeof(Image), typeof(Button));
            closeObject.transform.SetParent(canvasObject.transform, false);
            closeObject.GetComponent<RectTransform>().sizeDelta = new Vector2(250f, 72f);
            closeObject.GetComponent<RectTransform>().anchoredPosition = new Vector2(0f, -195f);
            closeObject.GetComponent<Image>().color = new Color(0.04f, 0.56f, 0.78f);
            var close = closeObject.GetComponent<Button>();
            CreateText("Label", closeObject.transform, new Vector2(250f, 72f), Vector2.zero, 30, TextAnchor.MiddleCenter, Color.white).text = "FERMER";

            title.text = "Point d'intérêt";
            body.text = "Sélectionnez le repère pour afficher son contenu.";
            infoPanel.Configure(canvasObject, title, body, close);
            canvasObject.SetActive(false);
            return infoPanel;
        }

        static void CreatePointOfInterest(InfoPanel panel, Material idle, Material hover)
        {
            var root = new GameObject("POI - Cuisine et matériaux");
            root.transform.position = PoiPosition;

            var pedestal = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pedestal.name = "POI Pedestal";
            pedestal.transform.SetParent(root.transform, false);
            pedestal.transform.localPosition = new Vector3(0f, -0.55f, 0f);
            pedestal.transform.localScale = new Vector3(0.18f, 0.42f, 0.18f);
            pedestal.GetComponent<Renderer>().sharedMaterial = idle;
            Object.DestroyImmediate(pedestal.GetComponent<Collider>());

            var target = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            target.name = "Interactive Target";
            target.transform.SetParent(root.transform, false);
            target.transform.localScale = Vector3.one * 0.32f;
            target.GetComponent<Renderer>().sharedMaterial = idle;
            target.AddComponent<XRSimpleInteractable>();
            var poi = target.AddComponent<PointOfInterest>();
            poi.Configure(
                "villa-kitchen-materials",
                "Cuisine et matériaux",
                "Cette cuisine fait partie du modèle Modern Luxury Villa original. Les meshes, proportions et UV du FBX sont conservés. Les textures PBR de bois, marbre, métal et tissu ont été reconnectées dans Unity.",
                panel,
                target.GetComponent<Renderer>(),
                idle,
                hover);
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

        static Material CreateMaterial(string name, Color color)
        {
            var path = $"{MaterialFolder}/{name.Replace(' ', '_')}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        static int CountMissingScripts(GameObject gameObject)
        {
            var count = GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(gameObject);
            foreach (Transform child in gameObject.transform)
                count += CountMissingScripts(child.gameObject);
            return count;
        }

        static void Require(bool condition, string expected)
        {
            if (!condition)
                throw new System.InvalidOperationException($"Modern Villa XR validation failed: expected {expected}.");
        }
    }
}
