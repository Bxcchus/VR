using System;
using System.Linq;
using CampusExplorer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace CampusExplorer.Editor
{
    public static class ModernVillaGuidedTourPass
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";

        static readonly string[] ExpectedRooms =
        {
            "Kitchen", "Living Room", "Dining Room", "Master Bedroom", "Bathroom", "Pool / Terrace"
        };

        [MenuItem("Campus Explorer/Guided Tour/Build Guided Tour")]
        public static void BuildAndValidate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RemovePreviousGuidedTour();

            var roomMenu = UnityEngine.Object.FindAnyObjectByType<XRRoomMenu>(FindObjectsInactive.Include);
            var teleporter = UnityEngine.Object.FindAnyObjectByType<XRRoomTeleporter>(FindObjectsInactive.Include);
            if (roomMenu == null || teleporter == null)
                throw new InvalidOperationException("Guided Tour requires the existing XR room menu and room teleporter.");

            var menuCanvas = roomMenu.MenuRoot != null ? roomMenu.MenuRoot.GetComponent<RectTransform>() : null;
            if (menuCanvas == null)
                throw new InvalidOperationException("The existing XR room menu canvas was not found.");

            // Keep every existing control at its validated position. Only extend the background
            // and append the requested Guided Tour button below the existing Close button.
            menuCanvas.sizeDelta = new Vector2(menuCanvas.sizeDelta.x, 1040f);
            var guidedTourButton = CreateButton("Guided Tour", menuCanvas, new Vector2(0f, -425f),
                new Vector2(540f, 68f), new Color(0.03f, 0.48f, 0.68f, 1f), 29);
            roomMenu.ConfigureGuidedTourButton(guidedTourButton);

            var root = new GameObject("XR Guided Tour");
            var controller = root.AddComponent<XRGuidedTour>();

            var panel = new GameObject("World Space Guided Tour Panel", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster), typeof(Image));
            panel.transform.SetParent(root.transform, false);
            panel.transform.position = new Vector3(-3.45f, 66.40f, -22.10f);
            panel.transform.localScale = Vector3.one * 0.001f;
            var panelRect = panel.GetComponent<RectTransform>();
            panelRect.sizeDelta = new Vector2(760f, 720f);
            var canvas = panel.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 55;
            panel.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2f;
            panel.GetComponent<Image>().color = new Color(0.025f, 0.035f, 0.055f, 0.98f);

            var tourContent = new GameObject("Tour Content", typeof(RectTransform));
            tourContent.transform.SetParent(panel.transform, false);
            Stretch(tourContent.GetComponent<RectTransform>());

            var header = CreateText("Header", tourContent.transform, new Vector2(650f, 65f),
                new Vector2(0f, 290f), 38, TextAnchor.MiddleCenter, Color.white);
            header.text = "GUIDED TOUR";
            var step = CreateText("Step", tourContent.transform, new Vector2(650f, 45f),
                new Vector2(0f, 240f), 25, TextAnchor.MiddleCenter, new Color(0.60f, 0.85f, 0.95f));
            var title = CreateText("Room Name", tourContent.transform, new Vector2(650f, 65f),
                new Vector2(0f, 165f), 34, TextAnchor.MiddleCenter, Color.white);
            var description = CreateText("Description", tourContent.transform, new Vector2(620f, 190f),
                new Vector2(0f, 45f), 28, TextAnchor.MiddleCenter, new Color(0.88f, 0.91f, 0.95f));
            description.horizontalOverflow = HorizontalWrapMode.Wrap;
            description.verticalOverflow = VerticalWrapMode.Truncate;

            var previous = CreateButton("Previous", tourContent.transform, new Vector2(-175f, -125f),
                new Vector2(290f, 72f), new Color(0.03f, 0.48f, 0.68f, 1f), 27);
            var next = CreateButton("Next", tourContent.transform, new Vector2(175f, -125f),
                new Vector2(290f, 72f), new Color(0.03f, 0.48f, 0.68f, 1f), 27);
            var exit = CreateButton("Exit", tourContent.transform, new Vector2(0f, -230f),
                new Vector2(420f, 68f), new Color(0.72f, 0.20f, 0.18f, 1f), 27);

            var completionContent = new GameObject("Completion Content", typeof(RectTransform));
            completionContent.transform.SetParent(panel.transform, false);
            Stretch(completionContent.GetComponent<RectTransform>());
            var completionTitle = CreateText("Completion Title", completionContent.transform,
                new Vector2(650f, 80f), new Vector2(0f, 210f), 42, TextAnchor.MiddleCenter, Color.white);
            completionTitle.text = "TOUR COMPLETE";
            var completionMessage = CreateText("Completion Message", completionContent.transform,
                new Vector2(620f, 130f), new Vector2(0f, 95f), 29, TextAnchor.MiddleCenter,
                new Color(0.88f, 0.91f, 0.95f));
            completionMessage.text = "You have completed the guided tour of the villa.";
            var freeExploration = CreateButton("Free Exploration", completionContent.transform,
                new Vector2(0f, -60f), new Vector2(540f, 76f), new Color(0.03f, 0.48f, 0.68f, 1f), 29);
            var restart = CreateButton("Restart Tour", completionContent.transform,
                new Vector2(0f, -165f), new Vector2(540f, 76f), new Color(0.03f, 0.48f, 0.68f, 1f), 29);

            completionContent.SetActive(false);
            controller.Configure(roomMenu, teleporter, panel, panel.transform, tourContent, completionContent,
                step, title, description, previous, next, next.GetComponentInChildren<Text>(true), exit,
                freeExploration, restart);
            panel.SetActive(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateScene();
            AssetDatabase.SaveAssets();
            Debug.Log("[Guided Tour] Six-step guided tour saved and validated.");
        }

        [MenuItem("Campus Explorer/Guided Tour/Audit Bathroom Panel Space")]
        public static void AuditBathroomPanelSpace()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Physics.SyncTransforms();
            var anchor = GameObject.Find("Bathroom Anchor")?.transform;
            if (anchor == null)
                throw new InvalidOperationException("Bathroom Anchor was not found.");

            var origin = anchor.position + Vector3.up * 1.65f;
            var forward = Vector3.ProjectOnPlane(anchor.forward, Vector3.up).normalized;
            var right = Vector3.Cross(Vector3.up, forward).normalized;
            var directions = new[]
            {
                (name: "Forward", direction: forward),
                (name: "ForwardRight", direction: (forward + right).normalized),
                (name: "ForwardRight60", direction: (forward * 0.5f + right * 0.8660254f).normalized),
                (name: "ForwardRight75", direction: (forward * 0.258819f + right * 0.9659258f).normalized),
                (name: "Right", direction: right),
                (name: "BackRight", direction: (-forward + right).normalized),
                (name: "ForwardLeft", direction: (forward - right).normalized),
                (name: "ForwardLeft60", direction: (forward * 0.5f - right * 0.8660254f).normalized),
                (name: "ForwardLeft75", direction: (forward * 0.258819f - right * 0.9659258f).normalized),
                (name: "Left", direction: -right),
                (name: "BackLeft", direction: (-forward - right).normalized),
                (name: "Back", direction: -forward),
            };

            foreach (var candidate in directions)
            {
                var hits = Physics.SphereCastAll(origin, 0.40f, candidate.direction, 1.35f,
                        Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                    .OrderBy(hit => hit.distance)
                    .Select(hit => $"{hit.collider.name}@{hit.distance:0.000}")
                    .ToArray();
                Debug.Log($"[Guided Tour][Bathroom Audit] {candidate.name} {candidate.direction}: " +
                          (hits.Length == 0 ? "clear 1.350 m" : string.Join(", ", hits)));
            }
        }

        static void RemovePreviousGuidedTour()
        {
            var oldRoot = GameObject.Find("XR Guided Tour");
            if (oldRoot != null)
                UnityEngine.Object.DestroyImmediate(oldRoot);

            var oldButtons = UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Include)
                .Where(button => button.name == "Guided Tour Button")
                .ToArray();
            foreach (var button in oldButtons)
                UnityEngine.Object.DestroyImmediate(button.gameObject);
        }

        static Button CreateButton(string label, Transform parent, Vector2 position, Vector2 size,
            Color color, int fontSize)
        {
            var buttonObject = new GameObject($"{label} Button", typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            var rect = buttonObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            buttonObject.GetComponent<Image>().color = color;
            var button = buttonObject.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.68f, 0.95f, 1f, 1f);
            colors.pressedColor = new Color(1f, 0.85f, 0.30f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(0.45f, 0.48f, 0.52f, 0.65f);
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            var text = CreateText("Label", buttonObject.transform, size, Vector2.zero, fontSize,
                TextAnchor.MiddleCenter, Color.white);
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

        static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        static void ValidateScene()
        {
            var tours = UnityEngine.Object.FindObjectsByType<XRGuidedTour>(FindObjectsInactive.Include);
            Require(tours.Length == 1, "exactly one guided-tour controller");
            var tour = tours[0];
            Require(tour.RoomSequence.SequenceEqual(ExpectedRooms), "the requested six-room sequence");
            Require(tour.PanelRoot != null && !tour.PanelRoot.activeSelf, "tour panel closed by default");
            Require(tour.PanelRoot.GetComponent<TrackedDeviceGraphicRaycaster>() != null,
                "TrackedDeviceGraphicRaycaster on the tour panel");
            Require(tour.GetComponentsInChildren<Collider>(true).Length == 0,
                "no physical collider in the guided-tour hierarchy");
            Require(tour.PreviousButton != null && tour.NextButton != null && tour.ExitButton != null,
                "Previous, Next, and Exit controls");
            Require(tour.FreeExplorationButton != null && tour.RestartButton != null,
                "Free Exploration and Restart Tour controls");

            var menu = UnityEngine.Object.FindAnyObjectByType<XRRoomMenu>(FindObjectsInactive.Include);
            Require(menu != null && menu.GuidedTourButton != null, "Guided Tour button in existing room menu");
            Require(menu.RoomButtons.Length == 6 && menu.RoomNames.SequenceEqual(ExpectedRooms),
                "existing room menu unchanged");

            var teleporter = UnityEngine.Object.FindAnyObjectByType<XRRoomTeleporter>(FindObjectsInactive.Include);
            Require(teleporter != null && teleporter.RoomAnchors.Length == 6,
                "existing six-anchor teleporter reused");
        }

        static void Require(bool condition, string expected)
        {
            if (!condition)
                throw new InvalidOperationException($"Guided Tour validation failed: expected {expected}.");
        }
    }
}
