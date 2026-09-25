using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampusExplorer;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace CampusExplorer.Editor
{
    public static class MainMenuSceneBuilder
    {
        const string MenuScenePath = "Assets/CampusExplorer/Scenes/MainMenu.unity";
        const string TourScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string RigPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.6.0/Starter Assets/Prefabs/XR Origin (XR Rig).prefab";
        const string SimulatorPrefabPath = "Assets/Samples/XR Interaction Toolkit/3.6.0/XR Interaction Simulator/XR Interaction Simulator.prefab";

        static readonly Color Background = new(0.024f, 0.034f, 0.048f, 1f);
        static readonly Color PanelBackground = new(0.035f, 0.048f, 0.065f, 0.985f);
        static readonly Color Accent = new(0.13f, 0.50f, 0.72f, 1f);
        static readonly Color TextSoft = new(0.74f, 0.82f, 0.88f, 1f);

        [MenuItem("Campus Explorer/Main Menu/Build Main Menu Scene")]
        public static void BuildAndValidate()
        {
            var rigPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(RigPrefabPath);
            var simulatorPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SimulatorPrefabPath);
            if (rigPrefab == null || simulatorPrefab == null)
                throw new InvalidOperationException("Known-good XR rig and simulator prefabs are required.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(TourScenePath) == null)
                throw new InvalidOperationException("ModernVillaTour scene is missing.");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("MainMenu");

            var rig = (GameObject)PrefabUtility.InstantiatePrefab(rigPrefab);
            rig.name = "XR Origin";
            rig.transform.SetParent(root.transform, false);
            rig.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var origin = rig.GetComponent<XROrigin>();
            origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;
            origin.CameraYOffset = 1.65f; // Device-mode fallback for Editor simulator only.
            foreach (var locomotion in rig.GetComponentsInChildren<LocomotionProvider>(true))
                locomotion.enabled = false;
            var camera = origin.Camera;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Background;

            var simulator = (GameObject)PrefabUtility.InstantiatePrefab(simulatorPrefab);
            simulator.name = "XR Interaction Simulator (Editor)";
            simulator.transform.SetParent(root.transform, false);
            simulator.SetActive(false); // MainMenuSimulatorGuard opts in only for desktop simulation.

            new GameObject("XR Interaction Manager", typeof(XRInteractionManager)).transform.SetParent(root.transform, false);
            var eventSystem = new GameObject("XR Event System", typeof(EventSystem), typeof(XRUIInputModule));
            eventSystem.transform.SetParent(root.transform, false);

            var light = new GameObject("Menu Lighting", typeof(Light));
            light.transform.SetParent(root.transform, false);
            light.transform.rotation = Quaternion.Euler(45f, -35f, 0f);
            light.GetComponent<Light>().type = LightType.Directional;
            light.GetComponent<Light>().intensity = 0.8f;
            light.GetComponent<Light>().shadows = LightShadows.None;
            RenderSettings.skybox = null;
            RenderSettings.ambientLight = new Color(0.56f, 0.63f, 0.72f);

            var managerObject = new GameObject("MainMenuManager", typeof(MainMenuManager));
            managerObject.transform.SetParent(root.transform, false);
            var manager = managerObject.GetComponent<MainMenuManager>();

            var canvasObject = new GameObject("Main Menu Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster), typeof(Image));
            canvasObject.transform.SetParent(root.transform, false);
            canvasObject.transform.localPosition = new Vector3(0f, 1.55f, 1.75f);
            canvasObject.transform.localRotation = Quaternion.identity;
            canvasObject.transform.localScale = Vector3.one * 0.0012f;
            canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(780f, 1000f);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 50;
            canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            canvasObject.GetComponent<Image>().color = PanelBackground;
            canvasObject.GetComponent<Image>().raycastTarget = false;

            var mainButtons = CreateContainer("Main Buttons", canvasObject.transform);

            var accentLine = new GameObject("Blue Accent", typeof(RectTransform), typeof(Image));
            accentLine.transform.SetParent(mainButtons.transform, false);
            SetRect(accentLine.GetComponent<RectTransform>(), new Vector2(630f, 5f), new Vector2(0f, 324f));
            accentLine.GetComponent<Image>().color = Accent;
            accentLine.GetComponent<Image>().raycastTarget = false;

            var title = CreateText("Title", mainButtons.transform, new Vector2(680f, 80f),
                new Vector2(0f, 424f), 52, TextAnchor.MiddleCenter, Color.white);
            title.text = "MODERN VILLA XR";
            var subtitle = CreateText("Subtitle", mainButtons.transform, new Vector2(680f, 58f),
                new Vector2(0f, 359f), 29, TextAnchor.MiddleCenter, TextSoft);
            subtitle.text = "Virtual Open House";

            CreateButton("Start Free Tour Button", "START FREE TOUR", mainButtons.transform,
                new Vector2(0f, 237f), manager.StartFreeTour, true);
            CreateButton("Guided Tour Button", "GUIDED TOUR", mainButtons.transform,
                new Vector2(0f, 134f), manager.StartGuidedTour, true);
            CreateButton("Controls Button", "CONTROLS", mainButtons.transform,
                new Vector2(0f, 31f), manager.OpenControls, false);
            CreateButton("Comfort Button", "COMFORT", mainButtons.transform,
                new Vector2(0f, -72f), manager.OpenComfort, false);
            CreateButton("Credits Button", "CREDITS", mainButtons.transform,
                new Vector2(0f, -175f), manager.OpenCredits, false);
            CreateButton("Quit Button", "QUIT", mainButtons.transform,
                new Vector2(0f, -278f), manager.Quit, false);

            var controls = CreateContainer("Controls Panel", canvasObject.transform);
            CreatePanelTitle("CONTROLS  1 / 2", controls.transform);
            var controlsBody = CreateText("Controls Content", controls.transform, new Vector2(630f, 630f),
                new Vector2(0f, -30f), 27, TextAnchor.UpperLeft, TextSoft);
            controlsBody.text = "MOVE\nLeft Stick\n\nTURN\nRight Stick / Snap Turn\n\nINTERACT\nPoint with controller ray + Index Trigger\n\nOPEN IN-GAME MENU\nLeft Controller Menu Button\n\nTELEPORT\nAvailable teleport surfaces / room menu";
            CreateButton("Controls Buttons Page Button", "QUEST TOUCH BUTTONS  >", controls.transform,
                new Vector2(0f, -302f), manager.OpenControlsButtons, false);
            CreateButton("Controls Back Button", "BACK", controls.transform,
                new Vector2(0f, -394f), manager.Back, false);

            var controlsButtons = CreateContainer("Controls Buttons Panel", canvasObject.transform);
            CreatePanelTitle("CONTROLS  2 / 2", controlsButtons.transform);
            var buttonsBody = CreateText("Quest Touch Button Guide", controlsButtons.transform,
                new Vector2(630f, 615f), new Vector2(0f, -24f), 26,
                TextAnchor.UpperLeft, TextSoft);
            buttonsBody.text = "LEFT CONTROLLER\n" +
                "Grip  Hold to pick up; release to place.\n" +
                "X / Y  No villa action assigned.\n" +
                "Menu  Open / close the room menu.\n\n" +
                "RIGHT CONTROLLER\n" +
                "Grip  Hold to pick up; release to place.\n" +
                "A  Jump in the villa.\n" +
                "B  No villa action assigned.\n\n" +
                "BOTH CONTROLLERS\n" +
                "Index trigger  Point and press for UI, doors,\n" +
                "lights and POIs.\n" +
                "Grip on POI  Open its information.";
            CreateButton("Controls Previous Page Button", "<  BASICS  1 / 2", controlsButtons.transform,
                new Vector2(0f, -302f), manager.OpenControls, false);
            CreateButton("Controls Buttons Back Button", "BACK", controlsButtons.transform,
                new Vector2(0f, -394f), manager.Back, false);

            var comfort = CreateContainer("Comfort Panel", canvasObject.transform);
            CreatePanelTitle("COMFORT", comfort.transform);
            var status = CreateText("Comfort Status", comfort.transform, new Vector2(640f, 62f),
                new Vector2(0f, 285f), 27, TextAnchor.MiddleCenter, TextSoft);
            CreateText("Turning Label", comfort.transform, new Vector2(620f, 50f),
                new Vector2(0f, 198f), 27, TextAnchor.MiddleCenter, Color.white).text = "TURNING";
            CreateButton("Snap 30 Button", "SNAP TURN 30°", comfort.transform,
                new Vector2(0f, 127f), manager.SetSnap30, false);
            CreateButton("Snap 45 Button", "SNAP TURN 45°", comfort.transform,
                new Vector2(0f, 34f), manager.SetSnap45, false);
            CreateText("Movement Label", comfort.transform, new Vector2(620f, 50f),
                new Vector2(0f, -39f), 27, TextAnchor.MiddleCenter, Color.white).text = "MOVEMENT";
            CreateButton("Teleport Movement Button", "TELEPORTATION / COMFORT", comfort.transform,
                new Vector2(0f, -110f), manager.SetTeleportMovement, false);
            CreateButton("Continuous Movement Button", "CONTINUOUS MOVEMENT", comfort.transform,
                new Vector2(0f, -203f), manager.SetContinuousMovement, false);
            CreateButton("Comfort Back Button", "BACK", comfort.transform,
                new Vector2(0f, -394f), manager.Back, false);

            var credits = CreateContainer("Credits Panel", canvasObject.transform);
            CreatePanelTitle("CREDITS", credits.transform);
            var creditsBody = CreateText("Credits Content", credits.transform, new Vector2(650f, 630f),
                new Vector2(0f, -30f), 27, TextAnchor.UpperLeft, TextSoft);
            creditsBody.text = "Modern Luxury Villa asset\nCreator: acrts076\nSource: CGTrader, model #6876788\nLicense: Royalty Free License (no AI)\n\nModern Villa XR project\nAuthor: [PROJECT AUTHOR TO COMPLETE]\n\nOther assets: Poly Haven / ambientCG (CC0)\nFurther texture credits: [TO COMPLETE]";
            CreateButton("Credits Back Button", "BACK", credits.transform,
                new Vector2(0f, -394f), manager.Back, false);

            manager.Configure(canvasObject, mainButtons, controls, controlsButtons, comfort, credits, status);
            root.AddComponent<MainMenuSimulatorGuard>().Configure(simulator, origin, manager);
            controls.SetActive(false);
            controlsButtons.SetActive(false);
            comfort.SetActive(false);
            credits.SetActive(false);

            EditorSceneManager.SaveScene(scene, MenuScenePath);
            SetBuildSettings();
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            ValidateScene();
            Debug.Log("[Main Menu] Scene created and validated; MainMenu is build scene 0.");
        }

        [MenuItem("Campus Explorer/Main Menu/Capture Preview")]
        public static void CapturePreview()
        {
            EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
            var manager = UnityEngine.Object.FindAnyObjectByType<MainMenuManager>();
            var camera = UnityEngine.Object.FindAnyObjectByType<XROrigin>()?.Camera;
            if (manager == null || camera == null)
                throw new InvalidOperationException("Main menu scene or XR camera is missing.");

            var oldCameraPosition = camera.transform.position;
            var oldCameraRotation = camera.transform.rotation;
            var oldCanvasPosition = manager.CanvasRoot.transform.position;
            var oldCanvasRotation = manager.CanvasRoot.transform.rotation;
            var oldTarget = camera.targetTexture;
            var oldActive = RenderTexture.active;
            var target = new RenderTexture(1400, 1400, 24);
            Texture2D image = null;
            try
            {
                camera.transform.SetPositionAndRotation(new Vector3(0f, 1.65f, 0f), Quaternion.identity);
                manager.CanvasRoot.transform.SetPositionAndRotation(
                    new Vector3(0f, 1.53f, 1.75f), Quaternion.identity);
                manager.SetSnap45();
                manager.SetContinuousMovement();
                camera.targetTexture = target;
                RenderTexture.active = target;
                image = new Texture2D(target.width, target.height, TextureFormat.RGB24, false);
                Directory.CreateDirectory("Evidence");
                void SaveFrame(string path)
                {
                    Canvas.ForceUpdateCanvases();
                    camera.Render();
                    image.ReadPixels(new Rect(0f, 0f, target.width, target.height), 0, 0);
                    image.Apply();
                    File.WriteAllBytes(path, image.EncodeToPNG());
                }

                manager.Back();
                SaveFrame("Evidence/main-menu-preview.png");
                manager.OpenControls();
                SaveFrame("Evidence/main-menu-controls-preview.png");
                manager.OpenControlsButtons();
                SaveFrame("Evidence/main-menu-controls-buttons-preview.png");
                manager.OpenComfort();
                SaveFrame("Evidence/main-menu-comfort-preview.png");
                manager.OpenCredits();
                SaveFrame("Evidence/main-menu-credits-preview.png");
                manager.Back();
                Debug.Log("[Main Menu] Five panel previews captured in Evidence.");
            }
            finally
            {
                camera.targetTexture = oldTarget;
                RenderTexture.active = oldActive;
                camera.transform.SetPositionAndRotation(oldCameraPosition, oldCameraRotation);
                manager.CanvasRoot.transform.SetPositionAndRotation(oldCanvasPosition, oldCanvasRotation);
                UnityEngine.Object.DestroyImmediate(target);
                if (image != null) UnityEngine.Object.DestroyImmediate(image);
            }
        }

        static GameObject CreateContainer(string name, Transform parent)
        {
            var obj = new GameObject(name, typeof(RectTransform));
            obj.transform.SetParent(parent, false);
            SetRect(obj.GetComponent<RectTransform>(), new Vector2(780f, 1000f), Vector2.zero);
            return obj;
        }

        static void CreatePanelTitle(string label, Transform parent)
        {
            CreateText(label + " Header", parent, new Vector2(630f, 64f),
                new Vector2(0f, 380f), 39, TextAnchor.MiddleCenter, Color.white).text = label;
        }

        static Button CreateButton(string name, string label, Transform parent, Vector2 position,
            UnityAction callback, bool primary)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
            obj.transform.SetParent(parent, false);
            SetRect(obj.GetComponent<RectTransform>(), new Vector2(610f, 77f), position);
            obj.GetComponent<Image>().color = primary ? Accent : new Color(0.07f, 0.14f, 0.19f, 1f);
            var button = obj.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.72f, 0.91f, 1f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = new Color(0.68f, 0.81f, 0.87f, 1f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            CreateText("Label", obj.transform, new Vector2(590f, 75f), Vector2.zero,
                30, TextAnchor.MiddleCenter, Color.white).text = label;
            UnityEventTools.AddPersistentListener(button.onClick, callback);
            return button;
        }

        static Text CreateText(string name, Transform parent, Vector2 size, Vector2 position,
            int fontSize, TextAnchor alignment, Color color)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(Text));
            obj.transform.SetParent(parent, false);
            SetRect(obj.GetComponent<RectTransform>(), size, position);
            var text = obj.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            return text;
        }

        static void SetRect(RectTransform rect, Vector2 size, Vector2 position)
        {
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
        }

        static void SetBuildSettings()
        {
            var scenes = new List<EditorBuildSettingsScene>
            {
                new(MenuScenePath, true),
                new(TourScenePath, true),
            };
            scenes.AddRange(EditorBuildSettings.scenes.Where(entry =>
                entry.path != MenuScenePath && entry.path != TourScenePath));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        static void ValidateScene()
        {
            var managers = UnityEngine.Object.FindObjectsByType<MainMenuManager>(FindObjectsInactive.Include);
            Require(managers.Length == 1, "one MainMenuManager");
            Require(UnityEngine.Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Include).Length == 1, "one XR Origin");
            Require(UnityEngine.Object.FindObjectsByType<XRInteractionManager>(FindObjectsInactive.Include).Length == 1, "one XR Interaction Manager");
            Require(UnityEngine.Object.FindObjectsByType<XRInteractionSimulator>(FindObjectsInactive.Include).Length == 1, "one XR simulator");
            Require(UnityEngine.Object.FindObjectsByType<EventSystem>(FindObjectsInactive.Include).Length == 1, "one EventSystem");
            Require(UnityEngine.Object.FindObjectsByType<XRUIInputModule>(FindObjectsInactive.Include).Length == 1, "one XRUIInputModule");
            Require(managers[0].CanvasRoot.GetComponent<TrackedDeviceGraphicRaycaster>() != null, "tracked-device UI raycaster");
            Require(managers[0].CanvasRoot.GetComponentsInChildren<Collider>(true).Length == 0, "no menu UI colliders");
            Require(managers[0].MainButtonsRoot.GetComponentsInChildren<Button>(true).Length == 6, "six main buttons");
            Require(managers[0].ControlsButtonsPanel != null, "Quest Touch controls page");
            Require(managers[0].ControlsPanel.GetComponentsInChildren<Button>(true).Length == 2 &&
                managers[0].ControlsButtonsPanel.GetComponentsInChildren<Button>(true).Length == 2,
                "controls page navigation buttons");
            Require(!managers[0].ControlsPanel.activeSelf && !managers[0].ControlsButtonsPanel.activeSelf &&
                !managers[0].ComfortPanel.activeSelf && !managers[0].CreditsPanel.activeSelf,
                "secondary panels closed by default");
            Require(EditorBuildSettings.scenes.Length >= 2 && EditorBuildSettings.scenes[0].path == MenuScenePath &&
                EditorBuildSettings.scenes[1].path == TourScenePath, "menu and tour build order");
        }

        static void Require(bool condition, string description)
        {
            if (!condition) throw new InvalidOperationException("Main menu validation failed: " + description);
        }
    }
}
