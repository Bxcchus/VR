using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampusExplorer;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace CampusExplorer.Editor
{
    public static class ModernVillaPoiPass
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string IdleMaterialPath = "Assets/CampusExplorer/Materials/Modern_Villa_Accent.mat";
        const string HoverMaterialPath = "Assets/CampusExplorer/Materials/Modern_Villa_Accent_Hover.mat";
        const string MarkerPath = "Temp/modern-villa-poi-pass.pending";

        sealed class PoiDefinition
        {
            public string id;
            public string title;
            public string body;
            public Vector3 markerPosition;

            public PoiDefinition(string id, string title, string body, Vector3 markerPosition)
            {
                this.id = id;
                this.title = title;
                this.body = body;
                this.markerPosition = markerPosition;
            }
        }

        static readonly PoiDefinition[] Definitions =
        {
            new("kitchen", "Kitchen",
                "Cuisine contemporaine organisée autour d'un îlot central, avec des finitions en bois, pierre et métal.",
                new Vector3(-0.50f, 66.13f, -24.80f)),
            new("living-room", "Living Room",
                "Salon principal de la villa, conçu comme un espace ouvert autour du canapé, du téléviseur et des œuvres murales.",
                new Vector3(-8.00f, 66.13f, -24.10f)),
            new("dining-room", "Dining Room",
                "Salle à manger attenante à la cuisine, centrée sur une grande table pour les repas et les réceptions.",
                new Vector3(-2.25f, 66.13f, -31.45f)),
            new("master-bedroom", "Master Bedroom",
                "Suite principale de l'étage, avec un grand lit, des rangements intégrés et un accès direct aux espaces privés.",
                new Vector3(1.00f, 69.35f, -31.00f)),
            new("bathroom", "Bathroom",
                "Salle de bains commune à l'étage, avec lavabo, toilettes et douche vitrée.",
                new Vector3(-11.05f, 69.35f, -29.10f)),
            new("pool-terrace", "Pool / Terrace",
                "Terrasse extérieure offrant un point de vue sur la piscine et les espaces paysagers de la villa.",
                new Vector3(-4.50f, 66.75524f, -51.65f)),
        };

        static ModernVillaPoiPass()
        {
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        [InitializeOnLoadMethod]
        static void ApplyPendingPassAfterReload()
        {
            if (File.Exists(MarkerPath))
                EditorApplication.delayCall += TryApplyPendingPass;
        }

        [DidReloadScripts]
        static void OnScriptsReloaded()
        {
            if (File.Exists(MarkerPath))
                EditorApplication.delayCall += TryApplyPendingPass;
        }

        static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredEditMode && File.Exists(MarkerPath))
                EditorApplication.delayCall += TryApplyPendingPass;
        }

        [MenuItem("Campus Explorer/POI Pass/Build Six Villa POIs")]
        public static void BuildAndValidate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RemoveExistingPoiSystem();
            EnsureXRUiEventSystem();

            var poiRoot = new GameObject("Villa POIs").transform;
            var panelRoot = new GameObject("Villa POI Panels").transform;
            foreach (var definition in Definitions)
            {
                var panel = CreateInfoPanel(panelRoot, definition);
                CreatePointOfInterest(poiRoot, definition, panel);
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Modern Villa POI] Six-POI pass saved and validated.");
        }

        static void TryApplyPendingPass()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return;
            try
            {
                BuildAndValidate();
                File.Delete(MarkerPath);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        static void RemoveExistingPoiSystem()
        {
            DestroyNamedRoot("Villa POIs");
            DestroyNamedRoot("Villa POI Panels");

            foreach (var point in UnityEngine.Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include).ToArray())
            {
                if (point == null)
                    continue;
                var target = point.transform.parent != null ? point.transform.parent.gameObject : point.gameObject;
                UnityEngine.Object.DestroyImmediate(target);
            }

            foreach (var panel in UnityEngine.Object.FindObjectsByType<InfoPanel>(FindObjectsInactive.Include).ToArray())
            {
                if (panel != null)
                    UnityEngine.Object.DestroyImmediate(panel.gameObject);
            }
        }

        static void DestroyNamedRoot(string name)
        {
            var root = GameObject.Find(name);
            if (root != null)
                UnityEngine.Object.DestroyImmediate(root);
        }

        static void EnsureXRUiEventSystem()
        {
            var eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
                eventSystem = new GameObject("XR Event System").AddComponent<EventSystem>();
            if (eventSystem.GetComponent<XRUIInputModule>() == null)
                eventSystem.gameObject.AddComponent<XRUIInputModule>();
        }

        static InfoPanel CreateInfoPanel(Transform parent, PoiDefinition definition)
        {
            var controller = new GameObject($"Panel - {definition.title}");
            controller.transform.SetParent(parent, false);
            var infoPanel = controller.AddComponent<InfoPanel>();

            var canvasObject = new GameObject("World Space Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster), typeof(Image));
            canvasObject.transform.SetParent(controller.transform, false);
            canvasObject.transform.position = definition.markerPosition + Vector3.up * 0.62f;
            canvasObject.transform.localScale = Vector3.one * 0.0012f;

            var rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(720f, 400f);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 30;
            canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            canvasObject.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.05f, 0.96f);

            var title = CreateText("Title", canvasObject.transform, new Vector2(610f, 72f),
                new Vector2(0f, 120f), 38, TextAnchor.MiddleLeft, Color.white);
            var body = CreateText("Description", canvasObject.transform, new Vector2(610f, 170f),
                new Vector2(0f, 15f), 25, TextAnchor.UpperLeft, new Color(0.86f, 0.91f, 0.95f));
            body.horizontalOverflow = HorizontalWrapMode.Wrap;
            body.verticalOverflow = VerticalWrapMode.Truncate;

            var closeObject = new GameObject("Close Button", typeof(RectTransform), typeof(Image), typeof(Button));
            closeObject.transform.SetParent(canvasObject.transform, false);
            var closeRect = closeObject.GetComponent<RectTransform>();
            closeRect.sizeDelta = new Vector2(210f, 62f);
            closeRect.anchoredPosition = new Vector2(0f, -150f);
            closeObject.GetComponent<Image>().color = new Color(0.03f, 0.48f, 0.68f, 1f);
            var close = closeObject.GetComponent<Button>();
            CreateText("Label", closeObject.transform, closeRect.sizeDelta, Vector2.zero,
                27, TextAnchor.MiddleCenter, Color.white).text = "CLOSE";

            title.text = definition.title;
            body.text = definition.body;
            infoPanel.Configure(canvasObject, title, body, close);
            canvasObject.SetActive(false);
            return infoPanel;
        }

        static void CreatePointOfInterest(Transform parent, PoiDefinition definition, InfoPanel panel)
        {
            var root = new GameObject($"POI - {definition.title}");
            root.transform.SetParent(parent, false);
            root.transform.position = definition.markerPosition;

            var marker = new GameObject("POI Marker Button", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster), typeof(Image), typeof(Button));
            marker.transform.SetParent(root.transform, false);
            marker.transform.localScale = Vector3.one * 0.0015f;
            var rect = marker.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(150f, 150f);
            var canvas = marker.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 25;
            marker.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            var image = marker.GetComponent<Image>();
            image.sprite = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");
            image.color = new Color(0.02f, 0.64f, 0.84f, 0.96f);
            var button = marker.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.65f, 0.95f, 1f, 1f);
            colors.pressedColor = new Color(1f, 0.85f, 0.25f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var label = CreateText("Info Icon", marker.transform, rect.sizeDelta, Vector2.zero,
                92, TextAnchor.MiddleCenter, Color.white);
            label.text = "i";
            label.fontStyle = FontStyle.Bold;
            label.raycastTarget = false;

            marker.AddComponent<XRSimpleInteractable>();
            var point = marker.AddComponent<PointOfInterest>();
            point.Configure(definition.id, definition.title, definition.body, panel, null, null, null);
            point.ConfigureUi(button, marker.transform);
        }

        static Text CreateText(string name, Transform parent, Vector2 size, Vector2 position,
            int fontSize, TextAnchor alignment, Color color)
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

        static void ValidateScene()
        {
            var points = UnityEngine.Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include)
                .OrderBy(point => point.PointId).ToArray();
            var panels = UnityEngine.Object.FindObjectsByType<InfoPanel>(FindObjectsInactive.Include);
            Require(points.Length == 6, "exactly six POIs");
            Require(panels.Length == 6, "exactly six information panels");
            Require(points.Select(point => point.PointId).SequenceEqual(
                Definitions.Select(definition => definition.id).OrderBy(id => id)), "the six requested POI identifiers");
            Require(points.All(point => point.GetComponent<XRSimpleInteractable>() != null),
                "an XRSimpleInteractable on every POI marker");
            Require(points.All(point => point.GetComponent<Collider>() == null),
                "no physical collider on UI POI markers");
            Require(points.All(point => point.GetComponent<Button>() != null &&
                                       point.GetComponent<TrackedDeviceGraphicRaycaster>() != null),
                "Quest-ray UI button on every POI marker");
            Require(points.All(point => point.Panel != null && point.Panel.PanelRoot != null),
                "a local panel assigned to every POI");
            Require(panels.All(panel => panel.GetComponentInChildren<TrackedDeviceGraphicRaycaster>(true) != null),
                "TrackedDeviceGraphicRaycaster on every POI panel");
            Require(panels.All(panel => panel.CloseButton != null), "Close button on every POI panel");
            Require(UnityEngine.Object.FindAnyObjectByType<EventSystem>()?.GetComponent<XRUIInputModule>() != null,
                "XRUIInputModule for Quest Touch UI rays");
        }

        static void Require(bool condition, string expected)
        {
            if (!condition)
                throw new InvalidOperationException($"Modern Villa POI validation failed: expected {expected}.");
        }
    }
}
