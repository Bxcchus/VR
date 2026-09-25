using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace CampusExplorer.Editor
{
    // Changes only authored arrival and POI/panel transforms in the existing scene.
    [InitializeOnLoad]
    public static class ModernVillaFinalPolishPlacement
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string RequestPath = "Evidence/final-polish/place.request";
        const string ReportPath = "Evidence/final-polish/placement-audit.txt";

        readonly struct Placement
        {
            public readonly string room;
            public readonly Vector3 marker;
            public readonly Vector3 anchor;
            public readonly float yaw;

            public Placement(string room, Vector3 marker, Vector3 anchor, float yaw)
            {
                this.room = room;
                this.marker = marker;
                this.anchor = anchor;
                this.yaw = yaw;
            }
        }

        static readonly Placement[] Placements =
        {
            new("Kitchen", new Vector3(-0.50f, 66.13f, -24.80f), new Vector3(1.25f, 64.87821f, -25.65f), 270f),
            new("Living Room", new Vector3(-8.00f, 66.13f, -24.10f), new Vector3(-7.10f, 64.87821f, -25.40f), 0f),
            new("Dining Room", new Vector3(-2.25f, 66.13f, -31.45f), new Vector3(-3.70f, 64.87821f, -30.45f), 90f),
            new("Master Bedroom", new Vector3(1.00f, 69.35f, -31.00f), new Vector3(0.80f, 68.12665f, -34.00f), 310f),
            new("Bathroom", new Vector3(-11.05f, 69.35f, -29.10f), new Vector3(-10.60f, 68.12665f, -30.20f), 338f),
            new("Pool / Terrace", new Vector3(-4.50f, 66.75524f, -51.65f), new Vector3(-5.30f, 65.50524f, -50.45f), 180f),
        };

        static ModernVillaFinalPolishPlacement()
        {
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (!File.Exists(RequestPath) || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            File.Delete(RequestPath);
            try
            {
                Apply();
            }
            catch (Exception exception)
            {
                File.WriteAllText(ReportPath, "Placement aborted: " + exception + Environment.NewLine);
                Debug.LogException(exception);
            }
        }

        // Batch entry point for an isolated project copy when the live Editor is in use.
        public static void ApplyInBatch()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Apply();
        }

        static Transform RequireRoot(Scene scene, string name)
        {
            var root = scene.GetRootGameObjects().FirstOrDefault(item => item.name == name);
            if (root == null) throw new InvalidOperationException("Missing root: " + name);
            return root.transform;
        }

        static Transform RequireChild(Transform parent, string name)
        {
            foreach (Transform child in parent)
                if (child.name == name)
                    return child;
            throw new InvalidOperationException("Missing child: " + parent.name + " / " + name);
        }

        static bool Walkable(Vector3 point, out string reason)
        {
            var hits = Physics.RaycastAll(point + Vector3.up * 1.8f, Vector3.down, 3.6f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .OrderBy(hit => hit.distance);
            var floorHit = hits.FirstOrDefault(hit => hit.collider.GetComponent<TeleportationArea>() != null &&
                                                      Mathf.Abs(hit.point.y - point.y) < 0.18f);
            if (floorHit.collider == null)
            {
                reason = "no TeleportationArea floor at proposed Y";
                return false;
            }

            const float radius = 0.30f;
            const float height = 1.75f;
            var bottom = floorHit.point + Vector3.up * (radius + 0.035f);
            var top = floorHit.point + Vector3.up * (height - radius);
            var blockers = Physics.OverlapCapsule(bottom, top, radius, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore)
                .Where(collider => collider != floorHit.collider).ToArray();
            reason = blockers.Length == 0 ? "clear" : string.Join(", ", blockers.Select(item => item.name));
            return blockers.Length == 0;
        }

        static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.path != ScenePath || scene.isDirty)
                throw new InvalidOperationException("Open the saved ModernVillaTour scene in Edit Mode before placement; unsaved scene changes are preserved.");

            Physics.SyncTransforms();
            var poiRoot = RequireRoot(scene, "Villa POIs");
            var panelRoot = RequireRoot(scene, "Villa POI Panels");
            var anchorsRoot = RequireChild(RequireRoot(scene, "XR Room Teleportation"), "Safe Room Anchors");
            var rows = new List<(Placement placement, Transform marker, Transform panel, Transform anchor)>();
            var report = new StringBuilder();
            report.AppendLine("Final-polish POI and anchor placement preflight");
            foreach (var placement in Placements)
            {
                var marker = RequireChild(poiRoot, "POI - " + placement.room);
                var panel = RequireChild(RequireChild(panelRoot, "Panel - " + placement.room), "World Space Canvas");
                var anchor = RequireChild(anchorsRoot, placement.room + " Anchor");
                var walkable = Walkable(placement.anchor, out var reason);
                report.AppendLine($"{placement.room}: floor/capsule={walkable} ({reason}); anchor {anchor.position} -> {placement.anchor}; POI {marker.position} -> {placement.marker}; yaw {anchor.eulerAngles.y:0.0} -> {placement.yaw:0.0}");
                if (!walkable)
                {
                    File.WriteAllText(ReportPath, report.ToString());
                    throw new InvalidOperationException("Unsafe anchor: " + placement.room + " (" + reason + ")");
                }
                rows.Add((placement, marker, panel, anchor));
            }

            foreach (var row in rows)
            {
                row.marker.position = row.placement.marker;
                // RectTransform world position can be overwritten on Canvas rebuild.
                ((RectTransform)row.panel).anchoredPosition3D = row.placement.marker + Vector3.up * 0.62f;
                row.anchor.SetPositionAndRotation(row.placement.anchor,
                    Quaternion.Euler(0f, row.placement.yaw, 0f));
            }

            ApplyButtonColors(scene);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            report.AppendLine("Saved scene. No player, collision, door, or teleport logic changed.");
            File.WriteAllText(ReportPath, report.ToString());
            Debug.Log("[Final Polish] Six POI/panel placements, arrival anchors, and eight button colors saved; all six floor/capsule checks passed.");
        }

        static void ApplyButtonColors(Scene scene)
        {
            var targets = new List<Transform>();
            var panels = RequireRoot(scene, "Villa POI Panels");
            foreach (var placement in Placements)
                targets.Add(RequireChild(RequireChild(RequireChild(panels,
                    "Panel - " + placement.room), "World Space Canvas"), "Close Button"));
            targets.Add(RequireChild(RequireChild(RequireRoot(scene, "XR Room Menu"),
                "World Space Menu Canvas"), "Guided Tour Button"));
            targets.Add(RequireChild(RequireChild(RequireChild(RequireRoot(scene,
                "XR Guided Tour"), "World Space Guided Tour Panel"),
                "Completion Content"), "Restart Tour Button"));

            var images = targets.Select(target => target.GetComponent<Image>() ??
                throw new InvalidOperationException("Missing button Image: " + target.name)).ToArray();
            foreach (var button in images)
                button.color = new Color(0.03f, 0.48f, 0.68f, 1f);
        }
    }
}
