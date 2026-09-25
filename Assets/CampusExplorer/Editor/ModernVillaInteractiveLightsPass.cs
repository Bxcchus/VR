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
    public static class ModernVillaInteractiveLightsPass
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";

        sealed class SwitchDefinition
        {
            public readonly string room;
            public readonly string[] emissiveRendererNames;
            public readonly Vector3 realtimeLightPosition;
            public readonly Color realtimeLightColor;
            public readonly float realtimeLightIntensity;
            public readonly float realtimeLightRange;
            public readonly Vector3 switchPosition;
            public readonly Vector3 switchFacesRoom;

            public SwitchDefinition(string room, string[] emissiveRendererNames,
                Vector3 realtimeLightPosition, Color realtimeLightColor,
                float realtimeLightIntensity, float realtimeLightRange,
                Vector3 switchPosition, Vector3 switchFacesRoom)
            {
                this.room = room;
                this.emissiveRendererNames = emissiveRendererNames;
                this.realtimeLightPosition = realtimeLightPosition;
                this.realtimeLightColor = realtimeLightColor;
                this.realtimeLightIntensity = realtimeLightIntensity;
                this.realtimeLightRange = realtimeLightRange;
                this.switchPosition = switchPosition;
                this.switchFacesRoom = switchFacesRoom;
            }
        }

        static readonly SwitchDefinition[] Definitions =
        {
            new("Kitchen", new[] { "Lamp_Pendant_Kitchen_01", "Lamp_Pendant_Kitchen_02" },
                new Vector3(-0.50f, 67.05f, -24.77f), new Color(1f, 0.84f, 0.68f), 0.85f, 3.8f,
                new Vector3(0.40f, 66.15f, -27.32f), Vector3.forward),
            new("Living Room", new[] { "LED_TV_Console_Underlight", "Light_TV_Backlight_LED", "LED_TV_Backlight_01" },
                new Vector3(-7.40f, 66.70f, -24.90f), new Color(1f, 0.77f, 0.60f), 0.68f, 4.2f,
                new Vector3(-5.50f, 66.15f, -27.32f), Vector3.forward),
            new("Master Bedroom", new[] { "LED_Nightstand_01", "LED_Nightstand_02", "LED_Headboard_Accent" },
                new Vector3(-0.48f, 70.25f, -33.10f), new Color(1f, 0.76f, 0.62f), 0.32f, 3.5f,
                new Vector3(3.30f, 69.35f, -27.54f), Vector3.back),
        };

        [MenuItem("Campus Explorer/Interaction/Build Three Light Switches")]
        public static void BuildAndValidate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var lightmapCountBefore = LightmapSettings.lightmaps?.Length ?? 0;
            var ambientModeBefore = RenderSettings.ambientMode;
            var ambientIntensityBefore = RenderSettings.ambientIntensity;
            var reflectionIntensityBefore = RenderSettings.reflectionIntensity;

            var oldRoot = GameObject.Find("InteractiveLights");
            var authoredLights = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include)
                .Where(light => oldRoot == null || !light.transform.IsChildOf(oldRoot.transform))
                .ToArray();
            Debug.Log($"[Interactive Lights] Existing reusable Light components in ModernVillaTour: {authoredLights.Length}.");

            if (oldRoot != null)
                UnityEngine.Object.DestroyImmediate(oldRoot);
            EnsureXRUiEventSystem();

            var root = new GameObject("InteractiveLights").transform;
            foreach (var definition in Definitions)
                CreateRoomGroup(root, definition);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateScene(lightmapCountBefore, ambientModeBefore, ambientIntensityBefore, reflectionIntensityBefore);
            AssetDatabase.SaveAssets();
            Debug.Log("[Interactive Lights] Three room switches saved without rebaking or changing global lighting.");
        }

        [MenuItem("Campus Explorer/Interaction/Audit Existing Villa Lights")]
        public static void AuditExistingVillaLights()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var interactiveRoot = GameObject.Find("InteractiveLights");
            var existing = UnityEngine.Object.FindObjectsByType<Light>(FindObjectsInactive.Include)
                .Where(light => interactiveRoot == null || !light.transform.IsChildOf(interactiveRoot.transform))
                .OrderBy(light => light.transform.position.y)
                .ThenBy(light => light.name)
                .ToArray();
            foreach (var light in existing)
            {
                Debug.Log($"[Interactive Lights][Audit] {HierarchyPath(light.transform)} | " +
                          $"enabled={light.enabled}, active={light.gameObject.activeInHierarchy}, type={light.type}, " +
                          $"bake={light.lightmapBakeType}, intensity={light.intensity:0.###}, range={light.range:0.###}, " +
                          $"position={light.transform.position}");
            }
            Debug.Log($"[Interactive Lights][Audit] Total existing villa lights: {existing.Length}.");
        }

        static string HierarchyPath(Transform item)
        {
            var path = item.name;
            while (item.parent != null)
            {
                item = item.parent;
                path = item.name + "/" + path;
            }
            return path;
        }

        static void CreateRoomGroup(Transform parent, SwitchDefinition definition)
        {
            var group = new GameObject(definition.room.Replace(" ", string.Empty) + "Lights").transform;
            group.SetParent(parent, false);

            var realtimeRoot = new GameObject("RealtimeLights").transform;
            realtimeRoot.SetParent(group, false);
            var realtimeObject = new GameObject(definition.room + " Interactive Realtime Light", typeof(Light));
            realtimeObject.transform.SetParent(realtimeRoot, false);
            realtimeObject.transform.position = definition.realtimeLightPosition;
            var realtimeLight = realtimeObject.GetComponent<Light>();
            realtimeLight.type = LightType.Point;
            realtimeLight.lightmapBakeType = LightmapBakeType.Realtime;
            realtimeLight.renderMode = LightRenderMode.ForcePixel;
            realtimeLight.shadows = LightShadows.None;
            realtimeLight.color = definition.realtimeLightColor;
            realtimeLight.intensity = definition.realtimeLightIntensity;
            realtimeLight.range = definition.realtimeLightRange;
            realtimeLight.cullingMask = 1 << 31;

            var emissiveRoot = new GameObject("EmissiveMaterials").transform;
            emissiveRoot.SetParent(group, false);
            var allRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            var emissiveTargets = definition.emissiveRendererNames.Select(name =>
            {
                var renderer = allRenderers.SingleOrDefault(item => item.name == name && !item.transform.IsChildOf(parent));
                if (renderer == null)
                    throw new InvalidOperationException($"Missing emissive fixture {name} for {definition.room}.");
                var slot = FindBrightestEmissionSlot(renderer);
                if (slot < 0)
                    throw new InvalidOperationException($"No emissive material on fixture {name} for {definition.room}.");
                var reference = new GameObject(name + " Emission Target").transform;
                reference.SetParent(emissiveRoot, false);
                return new XRLightSwitch.EmissiveTarget(renderer, slot);
            }).ToArray();

            var switchObject = new GameObject(definition.room + " Light Switch", typeof(RectTransform),
                typeof(Canvas), typeof(CanvasScaler), typeof(TrackedDeviceGraphicRaycaster),
                typeof(Image), typeof(Button));
            switchObject.transform.SetParent(group, false);
            switchObject.transform.position = definition.switchPosition;
            switchObject.transform.rotation = Quaternion.LookRotation(-definition.switchFacesRoom, Vector3.up);
            switchObject.transform.localScale = Vector3.one * 0.001f;
            var switchRect = switchObject.GetComponent<RectTransform>();
            switchRect.sizeDelta = new Vector2(180f, 260f);
            var canvas = switchObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 24;
            switchObject.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 3f;
            var plate = switchObject.GetComponent<Image>();
            plate.color = new Color(0.075f, 0.085f, 0.105f, 0.98f);
            var button = switchObject.GetComponent<Button>();
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.88f, 0.94f, 1f, 1f);
            colors.pressedColor = new Color(1f, 0.86f, 0.52f, 1f);
            colors.selectedColor = colors.highlightedColor;
            colors.fadeDuration = 0.08f;
            button.colors = colors;

            var roomLabel = CreateText("Room Label", switchObject.transform, new Vector2(150f, 45f),
                new Vector2(0f, 91f), 22, definition.room.ToUpperInvariant(), new Color(0.82f, 0.86f, 0.92f));
            roomLabel.raycastTarget = false;

            var trackObject = new GameObject("Switch Track", typeof(RectTransform), typeof(Image));
            trackObject.transform.SetParent(switchObject.transform, false);
            var trackRect = trackObject.GetComponent<RectTransform>();
            trackRect.sizeDelta = new Vector2(72f, 116f);
            trackRect.anchoredPosition = new Vector2(0f, 5f);
            var track = trackObject.GetComponent<Image>();
            track.color = new Color(0.72f, 0.51f, 0.20f, 1f);
            track.raycastTarget = false;

            var handleObject = new GameObject("Switch Handle", typeof(RectTransform), typeof(Image));
            handleObject.transform.SetParent(trackObject.transform, false);
            var handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.sizeDelta = new Vector2(58f, 50f);
            handleRect.anchoredPosition = new Vector2(0f, 25f);
            var handleImage = handleObject.GetComponent<Image>();
            handleImage.color = new Color(0.95f, 0.95f, 0.92f, 1f);
            handleImage.raycastTarget = false;

            var state = CreateText("State Label", switchObject.transform, new Vector2(150f, 42f),
                new Vector2(0f, -92f), 21, "LIGHT ON", new Color(1f, 0.90f, 0.65f));
            state.raycastTarget = false;

            var controller = switchObject.AddComponent<XRLightSwitch>();
            controller.Configure(definition.room, new[] { realtimeLight }, emissiveTargets,
                button, track, handleRect, state);
        }

        static int FindBrightestEmissionSlot(Renderer renderer)
        {
            var bestSlot = -1;
            var bestBrightness = 0f;
            for (var slot = 0; slot < renderer.sharedMaterials.Length; slot++)
            {
                var material = renderer.sharedMaterials[slot];
                if (material == null || !material.HasColor("_EmissionColor"))
                    continue;
                var emission = material.GetColor("_EmissionColor");
                var brightness = Mathf.Max(emission.r, Mathf.Max(emission.g, emission.b));
                if (brightness > bestBrightness)
                {
                    bestBrightness = brightness;
                    bestSlot = slot;
                }
            }
            return bestSlot;
        }

        static Text CreateText(string name, Transform parent, Vector2 size, Vector2 position,
            int fontSize, string value, Color color)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            var rect = textObject.GetComponent<RectTransform>();
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            var text = textObject.GetComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.text = value;
            text.supportRichText = false;
            return text;
        }

        static void EnsureXRUiEventSystem()
        {
            var eventSystem = UnityEngine.Object.FindAnyObjectByType<EventSystem>();
            if (eventSystem == null)
                eventSystem = new GameObject("XR Event System").AddComponent<EventSystem>();
            if (eventSystem.GetComponent<XRUIInputModule>() == null)
                eventSystem.gameObject.AddComponent<XRUIInputModule>();
        }

        static void ValidateScene(int lightmapCountBefore, UnityEngine.Rendering.AmbientMode ambientModeBefore,
            float ambientIntensityBefore, float reflectionIntensityBefore)
        {
            var root = GameObject.Find("InteractiveLights");
            Require(root != null, "InteractiveLights root");
            var switches = root.GetComponentsInChildren<XRLightSwitch>(true)
                .OrderBy(item => item.RoomName).ToArray();
            Require(switches.Length == 3, "exactly three XR light switches");
            Require(switches.Select(item => item.RoomName).SequenceEqual(
                Definitions.Select(item => item.room).OrderBy(name => name)), "Kitchen, Living Room, and Master Bedroom");
            Require(switches.All(item => item.SwitchButton != null &&
                                         item.GetComponent<TrackedDeviceGraphicRaycaster>() != null),
                "Quest Touch ray-selectable UI on every switch");
            Require(switches.All(item => item.GetComponentsInChildren<Collider>(true).Length == 0),
                "no physical collider on switch UI");
            Require(switches.All(item => item.ControlledLights.Length > 0 &&
                                         item.ControlledLights.All(light => light != null && light.enabled &&
                                             light.lightmapBakeType == LightmapBakeType.Realtime)),
                "dedicated realtime interaction lights enabled by default");
            Require(root.GetComponentsInChildren<Light>(true).Length == 3,
                "one minimal realtime point light per room");
            Require(switches.All(item => item.ControlledLights.All(light =>
                        light.type == LightType.Point && light.shadows == LightShadows.None &&
                        light.renderMode == LightRenderMode.ForcePixel && light.cullingMask == (1 << 31))),
                "Quest-compatible point lights without realtime shadows");
            Require(switches.All(item => item.EmissiveTargets.Length > 0 &&
                                         item.EmissiveTargets.All(target => target.renderer != null)),
                "room-local emissive fixture targets");
            foreach (var definition in Definitions)
            {
                var lightSwitch = switches.Single(item => item.RoomName == definition.room);
                Require(lightSwitch.EmissiveTargets.Select(target => target.renderer.name)
                        .SequenceEqual(definition.emissiveRendererNames),
                    $"emissive fixture mapping for {definition.room}");
                Require(lightSwitch.transform.parent.Find("RealtimeLights") != null &&
                        lightSwitch.transform.parent.Find("EmissiveMaterials") != null,
                    $"organized realtime/emissive hierarchy for {definition.room}");
            }
            Require((LightmapSettings.lightmaps?.Length ?? 0) == lightmapCountBefore, "baked lightmaps unchanged");
            Require(RenderSettings.ambientMode == ambientModeBefore &&
                    Mathf.Approximately(RenderSettings.ambientIntensity, ambientIntensityBefore) &&
                    Mathf.Approximately(RenderSettings.reflectionIntensity, reflectionIntensityBefore),
                "global ambient and reflection settings unchanged");
        }

        static void Require(bool condition, string expected)
        {
            if (!condition)
                throw new InvalidOperationException($"Interactive light validation failed: expected {expected}.");
        }
    }
}
