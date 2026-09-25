using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace CampusExplorer.Editor
{
    /// <summary>Moves only the existing Bathroom room-menu arrival into the common bathroom.</summary>
    public static class ModernVillaBathroomTeleportFix
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string ReportPath = "Evidence/common-bathroom/teleport-fix-report.txt";
        static readonly Vector3 Arrival = new(-10.60f, 68.12665f, -30.20f);
        const float ArrivalYaw = 338f;

        [MenuItem("Campus Explorer/Fixes/Move Bathroom Teleport To Common Bathroom")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty)
                throw new InvalidOperationException("Save the active scene before moving the Bathroom anchor.");
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var teleporters = UnityEngine.Object.FindObjectsByType<XRRoomTeleporter>(FindObjectsInactive.Include);
            if (teleporters.Length != 1)
                throw new InvalidOperationException("Expected exactly one XR room teleporter.");
            var teleporter = teleporters[0];
            var roomIndex = Array.IndexOf(teleporter.RoomNames, "Bathroom");
            if (roomIndex < 0 || roomIndex >= teleporter.RoomAnchors.Length)
                throw new InvalidOperationException("Bathroom room-menu mapping is missing.");
            var anchor = teleporter.RoomAnchors[roomIndex];
            if (anchor == null || anchor.name != "Bathroom Anchor" ||
                anchor.parent == null || anchor.parent.name != "Safe Room Anchors")
                throw new InvalidOperationException("Bathroom room-menu anchor is missing or unexpected.");

            Physics.SyncTransforms();
            var floorHit = Physics.RaycastAll(Arrival + Vector3.up * 1.8f, Vector3.down,
                    3.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .OrderBy(hit => hit.distance)
                .FirstOrDefault(hit => hit.collider.GetComponent<TeleportationArea>() != null &&
                                       Mathf.Abs(hit.point.y - Arrival.y) < 0.02f);
            if (floorHit.collider == null)
                throw new InvalidOperationException("No teleportable common-bathroom floor at the proposed arrival.");
            if (!floorHit.collider.gameObject.activeInHierarchy || !floorHit.collider.enabled ||
                floorHit.collider.isTrigger)
                throw new InvalidOperationException("The common-bathroom floor collider is not active and solid.");

            const float radius = 0.30f;
            const float height = 1.75f;
            var bottom = floorHit.point + Vector3.up * (radius + 0.035f);
            var top = floorHit.point + Vector3.up * (height - radius);
            var blockers = Physics.OverlapCapsule(bottom, top, radius,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Where(collider => collider != floorHit.collider)
                .Select(collider => collider.name)
                .Distinct()
                .ToArray();
            if (blockers.Length != 0)
                throw new InvalidOperationException("Bathroom arrival intersects: " + string.Join(", ", blockers));

            var previousPosition = anchor.position;
            var previousYaw = anchor.eulerAngles.y;
            anchor.SetPositionAndRotation(floorHit.point,
                Quaternion.Euler(0f, ArrivalYaw, 0f));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath,
                $"Bathroom room-menu and guided-tour arrival: {previousPosition} / yaw {previousYaw:F1} " +
                $"-> {anchor.position} / yaw {anchor.eulerAngles.y:F1}. " +
                $"Floor: {floorHit.collider.name}, top Y={floorHit.point.y:F5}; " +
                "1.75 m XR capsule with 0.30 m radius is clear. " +
                "No other anchor, collider, POI, door, geometry, or XR calibration changed.\n");
            Debug.Log($"[Modern Villa] Bathroom arrival moved to {anchor.position}, yaw {anchor.eulerAngles.y:F1}.");
        }
    }
}
