using System;
using System.Linq;
using CampusExplorer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace CampusExplorer.Editor
{
    public static class ModernVillaMenuPass
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        static readonly string[] RoomNames =
        {
            "Kitchen", "Living Room", "Dining Room", "Master Bedroom", "Bathroom", "Pool / Terrace"
        };

        [MenuItem("Campus Explorer/XR Menu/Build Simple Room Menu")]
        public static void BuildAndValidate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var oldMenu = GameObject.Find("XR Room Menu");
            if (oldMenu != null)
                UnityEngine.Object.DestroyImmediate(oldMenu);

            var eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
                eventSystem = new GameObject("XR Event System").AddComponent<EventSystem>();
            if (eventSystem.GetComponent<XRUIInputModule>() == null)
                eventSystem.gameObject.AddComponent<XRUIInputModule>();

            var controllerObject = new GameObject("XR Room Menu");
            var controller = controllerObject.AddComponent<XRRoomMenu>();

            var canvasObject = new GameObject("World Space Menu Canvas", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster), typeof(Image));
            canvasObject.transform.SetParent(controllerObject.transform, false);
            canvasObject.transform.position = new Vector3(-3.45f, 66.40f, -22.10f);
            canvasObject.transform.localScale = Vector3.one * 0.001f;
            var rect = canvasObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(680f, 860f);
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 50;
            canvasObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            canvasObject.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.055f, 0.97f);

            var title = CreateText("Title", canvasObject.transform, new Vector2(580f, 70f),
                new Vector2(0f, 355f), 42, TextAnchor.MiddleCenter, Color.white);
            title.text = "VILLA ROOMS";
            var status = CreateText("Selection Status", canvasObject.transform, new Vector2(580f, 45f),
                new Vector2(0f, 302f), 25, TextAnchor.MiddleCenter, new Color(0.60f, 0.85f, 0.95f));
            status.text = "Select a room";

            var buttons = new Button[RoomNames.Length];
            for (var index = 0; index < RoomNames.Length; index++)
                buttons[index] = CreateButton(RoomNames[index], canvasObject.transform,
                    new Vector2(0f, 225f - index * 85f), false);
            var close = CreateButton("Close", canvasObject.transform, new Vector2(0f, -330f), true);

            controller.Configure(canvasObject, canvasObject.transform, status, close, buttons, RoomNames.ToArray());
            canvasObject.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[XR Room Menu] Simple menu saved and validated.");
        }

        static Button CreateButton(string label, Transform parent, Vector2 position, bool closeStyle)
        {
            var buttonObject = new GameObject($"{label} Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(540f, 68f);
            rect.anchoredPosition = position;
            var image = buttonObject.GetComponent<Image>();
            image.color = closeStyle
                ? new Color(0.72f, 0.20f, 0.18f, 1f)
                : new Color(0.03f, 0.48f, 0.68f, 1f);
            var button = buttonObject.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.68f, 0.95f, 1f, 1f);
            colors.pressedColor = new Color(1f, 0.85f, 0.30f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            var text = CreateText("Label", buttonObject.transform, rect.sizeDelta, Vector2.zero,
                29, TextAnchor.MiddleCenter, Color.white);
            text.text = label.ToUpperInvariant();
            text.raycastTarget = false;
            return button;
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
            var menus = UnityEngine.Object.FindObjectsByType<XRRoomMenu>(FindObjectsInactive.Include);
            Require(menus.Length == 1, "exactly one XR room menu");
            var menu = menus[0];
            Require(menu.MenuRoot != null && !menu.MenuRoot.activeSelf, "menu closed by default");
            Require(menu.RoomButtons != null && menu.RoomButtons.Length == 6, "six room buttons");
            Require(menu.RoomNames.SequenceEqual(RoomNames), "the six requested room names");
            Require(menu.CloseButton != null, "Close button");
            Require(menu.MenuRoot.GetComponent<TrackedDeviceGraphicRaycaster>() != null,
                "TrackedDeviceGraphicRaycaster for Quest Touch rays");
            Require(menu.GetComponentsInChildren<Collider>(true).Length == 0,
                "no physical collider in the menu hierarchy");
        }

        static void Require(bool condition, string expected)
        {
            if (!condition)
                throw new InvalidOperationException($"XR room menu validation failed: expected {expected}.");
        }
    }
}
