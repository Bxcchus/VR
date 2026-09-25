using System;
using System.Collections.Generic;
using System.Linq;
using CampusExplorer;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace CampusExplorer.Editor
{
    public static class ModernVillaRoomTeleportPass
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";

        sealed class AnchorDefinition
        {
            public readonly string room;
            public readonly Vector3 seed;
            public readonly float yaw;

            public AnchorDefinition(string room, Vector3 seed, float yaw)
            {
                this.room = room;
                this.seed = seed;
                this.yaw = yaw;
            }
        }

        static readonly AnchorDefinition[] Definitions =
        {
            new("Kitchen", new Vector3(1.25f, 64.87821f, -25.65f), -90f),
            new("Living Room", new Vector3(-7.10f, 64.87821f, -25.40f), 0f),
            new("Dining Room", new Vector3(-3.70f, 64.87821f, -30.45f), 90f),
            new("Master Bedroom", new Vector3(0.80f, 68.12665f, -34.00f), 310f),
            new("Bathroom", new Vector3(-10.60f, 68.12665f, -30.20f), 338f),
            new("Pool / Terrace", new Vector3(-5.30f, 65.50524f, -50.45f), 180f),
        };

        [MenuItem("Campus Explorer/XR Menu/Build Room Teleportation")]
        public static void BuildAndValidate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            RemoveExistingTeleportSystem();

            var menu = UnityEngine.Object.FindAnyObjectByType<XRRoomMenu>();
            var origin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            var provider = UnityEngine.Object.FindAnyObjectByType<TeleportationProvider>();
            var camera = Camera.main != null ? Camera.main : UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (menu == null || origin == null || provider == null || camera == null)
                throw new InvalidOperationException("Room teleportation requires the existing XR menu, XROrigin, TeleportationProvider, and camera.");

            Physics.SyncTransforms();
            var root = new GameObject("XR Room Teleportation");
            var anchorsRoot = new GameObject("Safe Room Anchors").transform;
            anchorsRoot.SetParent(root.transform, false);
            var anchors = new Transform[Definitions.Length];
            for (var index = 0; index < Definitions.Length; index++)
            {
                var definition = Definitions[index];
                var position = FindSafePosition(definition);
                var anchor = new GameObject($"{definition.room} Anchor").transform;
                anchor.SetParent(anchorsRoot, false);
                anchor.position = position;
                anchor.rotation = Quaternion.Euler(0f, definition.yaw, 0f);
                anchors[index] = anchor;
            }

            var fade = CreateFadeCanvas(camera.transform);
            var teleporter = root.AddComponent<XRRoomTeleporter>();
            teleporter.Configure(menu, origin, provider, fade,
                Definitions.Select(item => item.room).ToArray(), anchors);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateScene();
            AssetDatabase.SaveAssets();

            foreach (var anchor in anchors)
                Debug.Log($"[XR Room Teleport] {anchor.name}: position={anchor.position}, yaw={anchor.eulerAngles.y:0.0}°");
            Debug.Log("[XR Room Teleport] Six safe room anchors saved and validated.");
        }

        static void RemoveExistingTeleportSystem()
        {
            var existingRoot = GameObject.Find("XR Room Teleportation");
            if (existingRoot != null)
                UnityEngine.Object.DestroyImmediate(existingRoot);
            var camera = Camera.main != null ? Camera.main : UnityEngine.Object.FindAnyObjectByType<Camera>();
            if (camera == null)
                return;
            var existingFade = camera.transform.Find("XR Room Teleport Fade");
            if (existingFade != null)
                UnityEngine.Object.DestroyImmediate(existingFade.gameObject);
        }

        static CanvasGroup CreateFadeCanvas(Transform cameraTransform)
        {
            var fadeObject = new GameObject("XR Room Teleport Fade", typeof(RectTransform), typeof(Canvas),
                typeof(CanvasGroup), typeof(Image));
            fadeObject.transform.SetParent(cameraTransform, false);
            fadeObject.transform.localPosition = new Vector3(0f, 0f, 0.15f);
            fadeObject.transform.localRotation = Quaternion.identity;
            fadeObject.transform.localScale = Vector3.one * 0.001f;
            var rect = fadeObject.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(2400f, 1800f);
            var canvas = fadeObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = short.MaxValue;
            var image = fadeObject.GetComponent<Image>();
            image.color = Color.black;
            image.raycastTarget = false;
            var group = fadeObject.GetComponent<CanvasGroup>();
            group.alpha = 0f;
            group.interactable = false;
            group.blocksRaycasts = false;
            return group;
        }

        static Vector3 FindSafePosition(AnchorDefinition definition)
        {
            var offsets = new List<Vector2>();
            const float step = 0.30f;
            for (var x = -7; x <= 7; x++)
            for (var z = -7; z <= 7; z++)
                offsets.Add(new Vector2(x * step, z * step));

            foreach (var offset in offsets.OrderBy(item => item.sqrMagnitude))
            {
                var candidate = definition.seed + new Vector3(offset.x, 0f, offset.y);
                if (TryValidateWalkablePoint(candidate, out var floorPoint, out _))
                    return floorPoint;
            }

            throw new InvalidOperationException($"No safe walkable anchor found near {definition.room} seed {definition.seed}.");
        }

        static bool TryValidateWalkablePoint(Vector3 candidate, out Vector3 floorPoint, out string reason)
        {
            floorPoint = candidate;
            reason = string.Empty;
            var hits = Physics.RaycastAll(candidate + Vector3.up * 1.8f, Vector3.down, 3.6f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .OrderBy(hit => hit.distance);
            var floorHit = hits.FirstOrDefault(hit => hit.collider.GetComponent<TeleportationArea>() != null &&
                                                      Mathf.Abs(hit.point.y - candidate.y) < 0.18f);
            if (floorHit.collider == null)
            {
                reason = "no validated TeleportationArea floor";
                return false;
            }

            floorPoint = floorHit.point;
            const float radius = 0.30f;
            const float height = 1.75f;
            var bottom = floorPoint + Vector3.up * (radius + 0.035f);
            var top = floorPoint + Vector3.up * (height - radius);
            var blockers = Physics.OverlapCapsule(bottom, top, radius, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore)
                .Where(collider => collider != floorHit.collider)
                .ToArray();
            if (blockers.Length == 0)
                return true;

            reason = string.Join(", ", blockers.Select(item => item.name));
            return false;
        }

        static void ValidateScene()
        {
            var teleporters = UnityEngine.Object.FindObjectsByType<XRRoomTeleporter>(FindObjectsInactive.Include);
            Require(teleporters.Length == 1, "exactly one room teleporter");
            var teleporter = teleporters[0];
            Require(teleporter.RoomNames.SequenceEqual(Definitions.Select(item => item.room)),
                "six requested room mappings");
            Require(teleporter.RoomAnchors.Length == 6 && teleporter.RoomAnchors.All(item => item != null),
                "six safe anchors");
            Require(teleporter.FadeGroup != null && Mathf.Approximately(teleporter.FadeGroup.alpha, 0f),
                "transparent camera fade by default");
            Require(teleporter.GetComponentsInChildren<Collider>(true).Length == 0,
                "no physical collider in teleport helper hierarchy");
            for (var index = 0; index < teleporter.RoomAnchors.Length; index++)
            {
                var anchor = teleporter.RoomAnchors[index];
                Require(TryValidateWalkablePoint(anchor.position, out _, out var reason),
                    $"safe {teleporter.RoomNames[index]} anchor ({reason})");
            }
        }

        static void Require(bool condition, string expected)
        {
            if (!condition)
                throw new InvalidOperationException($"XR room teleport validation failed: expected {expected}.");
        }
    }
}
