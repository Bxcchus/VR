using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CampusExplorer;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace CampusExplorer.Editor
{
    public static class ModernVillaInteractiveDoorPass
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const float Duration = 0.6f;
        const float SwingAngle = 90f;
        const float SlidingDuration = 0.7f;
        const float SlidingDistance = 0.68f;

        static readonly string[] SourceDoorNames =
        {
            "Door_Interior_01.001",
            "Door_Interior_02",
            "Door_Interior_03.001",
            "Door_Interior_04",
            "Door_Interior_05",
            "Door_Interior_06",
            "Door_Interior_07",
            "Door_Interior_08",
            "Door_Interior_09",
            "Door_Interior_10",
            "Door_Interior_11",
            "Door_Interior_12"
        };

        [MenuItem("Campus Explorer/Interaction/Build All Interactive Interior Doors")]
        public static void BuildAndValidate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var lightmapCount = LightmapSettings.lightmaps?.Length ?? 0;
            var ambientIntensity = RenderSettings.ambientIntensity;
            var reflectionIntensity = RenderSettings.reflectionIntensity;

            var oldRoot = GameObject.Find("InteractiveDoors");
            if (oldRoot != null)
                UnityEngine.Object.DestroyImmediate(oldRoot);

            var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include);
            var sources = SourceDoorNames.Select(name => renderers.Single(renderer => renderer.name == name)).ToArray();
            var collisionRoot = GameObject.Find("Collisions")?.transform;
            if (collisionRoot == null)
                throw new InvalidOperationException("The existing Collisions hierarchy is missing.");

            var root = new GameObject("InteractiveDoors").transform;
            var report = new List<string>
            {
                "# All interactive villa doors",
                string.Empty,
                $"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}",
                $"Scene: {ScenePath}",
                string.Empty,
                "| Door | Original frame | Hinge position | Open angle | Open collision overlaps |",
                "|---|---|---:|---:|---:|"
            };

            foreach (var sourceRenderer in sources)
            {
                if (sourceRenderer.transform.parent == null ||
                    !sourceRenderer.transform.parent.name.StartsWith("Door_Frame_", StringComparison.Ordinal))
                    throw new InvalidOperationException($"{sourceRenderer.name} is no longer under its original Door_Frame object.");
                if (sourceRenderer.GetComponentsInChildren<Collider>(true).Length != 0)
                    throw new InvalidOperationException($"{sourceRenderer.name} unexpectedly already has a collider.");

                var sourceTransform = sourceRenderer.transform;
                var bounds = sourceRenderer.bounds;
                var hingePosition = DetermineHingePosition(sourceTransform, bounds);
                var colliderSize = DetermineColliderSize(bounds);
                var angle = ChooseOpenAngle(hingePosition, bounds.center, colliderSize, collisionRoot,
                    out var overlapCount);

                var hinge = new GameObject($"{sourceRenderer.transform.parent.name} - {sourceRenderer.name} Hinge").transform;
                hinge.SetParent(root, false);
                hinge.position = hingePosition;
                hinge.rotation = Quaternion.identity;

                var target = new GameObject("Door Collision and Interaction Target").transform;
                target.SetParent(hinge, false);
                target.position = bounds.center;
                target.rotation = Quaternion.identity;
                var collider = target.gameObject.AddComponent<BoxCollider>();
                collider.size = colliderSize;
                collider.isTrigger = false;

                var interactable = hinge.gameObject.AddComponent<XRSimpleInteractable>();
                interactable.colliders.Clear();
                interactable.colliders.Add(collider);
                var door = hinge.gameObject.AddComponent<XRInteractiveDoor>();
                door.Configure(sourceTransform, collider, interactable, angle, Duration);

                report.Add($"| {sourceRenderer.name} | {sourceRenderer.transform.parent.name} | " +
                           $"({hingePosition.x:0.000}, {hingePosition.y:0.000}, {hingePosition.z:0.000}) | " +
                           $"{angle:+0;-0}° | {overlapCount} |");
            }

            var patio = BuildSlidingPair(root, renderers, "Ground Patio Glass Doors",
                "Sliding_Patio_Door_01", "Sliding_Patio_Door_02");
            var balcony = BuildSlidingPair(root, renderers, "Upper Balcony Glass Doors",
                "Balcony_Door_01", "Balcony_Door_02");
            report.Add(string.Empty);
            report.Add("## Sliding glass doors");
            report.Add(string.Empty);
            report.Add($"- {patio.DoorId}: {patio.FirstPanel.name} / {patio.SecondPanel.name}; " +
                       $"offsets {patio.FirstOpenWorldOffset} and {patio.SecondOpenWorldOffset}");
            report.Add($"- {balcony.DoorId}: {balcony.FirstPanel.name} / {balcony.SecondPanel.name}; " +
                       $"offsets {balcony.FirstOpenWorldOffset} and {balcony.SecondOpenWorldOffset}");

            Directory.CreateDirectory("Evidence");
            File.WriteAllLines("Evidence/interactive-doors-report.md", report);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            ValidateScene(lightmapCount, ambientIntensity, reflectionIntensity);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Interactive Doors] Saved and validated {SourceDoorNames.Length} existing interior doors.");
        }

        public static void OpenSceneForEditing()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        static Vector3 DetermineHingePosition(Transform source, Bounds bounds)
        {
            var alongX = bounds.size.x >= bounds.size.z;
            var negativeEdge = alongX
                ? new Vector3(bounds.min.x, bounds.center.y, bounds.center.z)
                : new Vector3(bounds.center.x, bounds.center.y, bounds.min.z);
            var positiveEdge = alongX
                ? new Vector3(bounds.max.x, bounds.center.y, bounds.center.z)
                : new Vector3(bounds.center.x, bounds.center.y, bounds.max.z);

            var sourceProjected = alongX
                ? new Vector3(source.position.x, bounds.center.y, bounds.center.z)
                : new Vector3(bounds.center.x, bounds.center.y, source.position.z);
            var negativeDistance = Vector3.Distance(sourceProjected, negativeEdge);
            var positiveDistance = Vector3.Distance(sourceProjected, positiveEdge);

            // Most imported doors expose their real hinge pivot. Door_Interior_03.001
            // has an FBX origin at world zero, so its hinge is derived from renderer bounds.
            if (Mathf.Min(negativeDistance, positiveDistance) <= 0.15f)
                return sourceProjected;
            return negativeEdge;
        }

        static XRInteractiveSlidingDoor BuildSlidingPair(Transform root, Renderer[] renderers,
            string id, string firstPanelName, string secondPanelName)
        {
            var firstRenderer = renderers.Single(renderer => renderer.name == firstPanelName);
            var secondRenderer = renderers.Single(renderer => renderer.name == secondPanelName);
            if (firstRenderer.GetComponentsInChildren<Collider>(true).Length != 0 ||
                secondRenderer.GetComponentsInChildren<Collider>(true).Length != 0)
                throw new InvalidOperationException($"{id} source panels unexpectedly already have colliders.");

            var pairRoot = new GameObject(id).transform;
            pairRoot.SetParent(root, false);
            var firstTarget = CreateSlidingCollisionTarget(pairRoot, firstRenderer, "First Panel Collision Target",
                out var firstCollider);
            var secondTarget = CreateSlidingCollisionTarget(pairRoot, secondRenderer, "Second Panel Collision Target",
                out var secondCollider);

            var firstCenter = firstRenderer.bounds.center;
            var secondCenter = secondRenderer.bounds.center;
            var separation = firstCenter - secondCenter;
            separation.y = 0f;
            if (separation.sqrMagnitude < 0.1f)
                throw new InvalidOperationException($"{id} panels do not expose a valid sliding axis.");
            var firstOffset = separation.normalized * SlidingDistance;
            var secondOffset = -firstOffset;

            var interactable = pairRoot.gameObject.AddComponent<XRSimpleInteractable>();
            interactable.colliders.Clear();
            interactable.colliders.Add(firstCollider);
            interactable.colliders.Add(secondCollider);
            var slidingDoor = pairRoot.gameObject.AddComponent<XRInteractiveSlidingDoor>();
            slidingDoor.Configure(id, firstRenderer.transform, secondRenderer.transform,
                firstTarget, secondTarget, firstCollider, secondCollider, interactable,
                firstOffset, secondOffset, SlidingDuration);
            return slidingDoor;
        }

        static Transform CreateSlidingCollisionTarget(Transform parent, Renderer renderer, string name,
            out BoxCollider collider)
        {
            var target = new GameObject(name).transform;
            target.SetParent(parent, false);
            target.position = renderer.bounds.center;
            target.rotation = Quaternion.identity;
            collider = target.gameObject.AddComponent<BoxCollider>();
            var size = renderer.bounds.size;
            size.x = Mathf.Max(0.10f, size.x - (size.x > size.z ? 0.04f : 0f));
            size.z = Mathf.Max(0.10f, size.z - (size.z > size.x ? 0.04f : 0f));
            size.y = Mathf.Max(1.9f, size.y - 0.06f);
            collider.size = size;
            collider.isTrigger = false;
            return target;
        }

        static Vector3 DetermineColliderSize(Bounds bounds)
        {
            var size = bounds.size;
            if (size.x >= size.z)
                size.x = Mathf.Max(0.82f, size.x - 0.12f);
            else
                size.z = Mathf.Max(0.82f, size.z - 0.12f);
            size.y = Mathf.Max(1.85f, size.y - 0.06f);
            if (size.x < size.z)
                size.x = Mathf.Max(0.12f, size.x);
            else
                size.z = Mathf.Max(0.12f, size.z);
            return size;
        }

        static float ChooseOpenAngle(Vector3 hinge, Vector3 closedCenter, Vector3 size,
            Transform collisionRoot, out int selectedOverlapCount)
        {
            var bestAngle = -SwingAngle;
            var bestCount = int.MaxValue;
            foreach (var angle in new[] { -SwingAngle, SwingAngle })
            {
                var rotation = Quaternion.AngleAxis(angle, Vector3.up);
                var center = hinge + rotation * (closedCenter - hinge);
                var overlapObjects = Physics.OverlapBox(center, size * 0.47f, rotation, ~0,
                        QueryTriggerInteraction.Ignore)
                    .Where(item => item.transform.IsChildOf(collisionRoot)).ToArray();
                var overlaps = overlapObjects.Length;
                if (overlaps > 0)
                    Debug.Log($"[Interactive Doors][Clearance] hinge={hinge} angle={angle:+0;-0} " +
                              $"blockers=[{string.Join(", ", overlapObjects.Select(item => item.name))}]");
                if (overlaps < bestCount)
                {
                    bestCount = overlaps;
                    bestAngle = angle;
                }
            }

            selectedOverlapCount = bestCount;
            return bestAngle;
        }

        static void ValidateScene(int lightmapCount, float ambientIntensity, float reflectionIntensity)
        {
            var doors = UnityEngine.Object.FindObjectsByType<XRInteractiveDoor>(FindObjectsInactive.Include)
                .OrderBy(item => item.DoorMesh.name).ToArray();
            Require(doors.Length == SourceDoorNames.Length, $"exactly {SourceDoorNames.Length} interactive interior doors");
            Require(doors.Select(item => item.DoorMesh.name).SequenceEqual(SourceDoorNames),
                "all twelve existing interior door meshes");
            Require(doors.All(item => item.DoorMesh.parent != null &&
                                      item.DoorMesh.parent.name.StartsWith("Door_Frame_", StringComparison.Ordinal)),
                "every source FBX door remains under its original frame");
            Require(doors.All(item => item.DoorCollider != null && item.DoorCollider.enabled &&
                                      !item.DoorCollider.isTrigger),
                "one enabled solid moving collider per door");
            Require(doors.All(item => item.Interactable != null &&
                                      item.Interactable.colliders.Contains(item.DoorCollider)),
                "an XRSimpleInteractable targeting every moving collider");
            Require(doors.All(item => Mathf.Approximately(Mathf.Abs(item.OpenAngle), SwingAngle) &&
                                      Mathf.Approximately(item.AnimationDuration, Duration)),
                "90 degree, 0.6 second eased animation for every door");
            Require(doors.All(item => item.GetComponent<Rigidbody>() == null &&
                                      item.GetComponentsInChildren<Rigidbody>(true).Length == 0),
                "kinematic transform animation without Rigidbody forces");
            var slidingDoors = UnityEngine.Object.FindObjectsByType<XRInteractiveSlidingDoor>(FindObjectsInactive.Include)
                .OrderBy(item => item.DoorId).ToArray();
            Require(slidingDoors.Length == 2, "two interactive sliding glass door pairs");
            Require(slidingDoors.Select(item => item.DoorId).SequenceEqual(new[]
                { "Ground Patio Glass Doors", "Upper Balcony Glass Doors" }),
                "ground patio and upper balcony sliding doors");
            Require(slidingDoors.All(item => item.FirstPanel != null && item.SecondPanel != null &&
                                             item.FirstCollider != null && item.SecondCollider != null &&
                                             item.FirstCollider.enabled && item.SecondCollider.enabled &&
                                             !item.FirstCollider.isTrigger && !item.SecondCollider.isTrigger),
                "two enabled solid moving glass-panel colliders per opening");
            Require(slidingDoors.All(item => item.Interactable != null &&
                                             item.Interactable.colliders.Contains(item.FirstCollider) &&
                                             item.Interactable.colliders.Contains(item.SecondCollider) &&
                                             Mathf.Approximately(item.AnimationDuration, SlidingDuration)),
                "one XRSimpleInteractable and a 0.7 second animation per glass opening");
            Require(GameObject.Find("Collisions")?.GetComponentsInChildren<Collider>(true).Length == 151,
                "all 151 existing environment collision helpers unchanged");
            Require(UnityEngine.Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include).Length == 6,
                "six existing POIs unchanged");
            Require(UnityEngine.Object.FindObjectsByType<XRRoomMenu>(FindObjectsInactive.Include).Length == 1 &&
                    UnityEngine.Object.FindObjectsByType<XRRoomTeleporter>(FindObjectsInactive.Include).Length == 1 &&
                    UnityEngine.Object.FindObjectsByType<XRGuidedTour>(FindObjectsInactive.Include).Length == 1,
                "room menu, teleporter, and guided tour unchanged");
            Require(UnityEngine.Object.FindObjectsByType<XRLightSwitch>(FindObjectsInactive.Include).Length == 3,
                "three interactive light switches unchanged");
            var origin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            var controller = origin != null ? origin.Origin.GetComponent<CharacterController>() : null;
            Require(controller != null && controller.height >= 1f && controller.height <= 2.1f,
                "XR player height unchanged");
            Require((LightmapSettings.lightmaps?.Length ?? 0) == lightmapCount &&
                    Mathf.Approximately(RenderSettings.ambientIntensity, ambientIntensity) &&
                    Mathf.Approximately(RenderSettings.reflectionIntensity, reflectionIntensity),
                "baked and global lighting unchanged");
        }

        static void Require(bool condition, string expectation)
        {
            if (!condition)
                throw new InvalidOperationException("Interactive doors validation failed: expected " + expectation + ".");
        }
    }
}
