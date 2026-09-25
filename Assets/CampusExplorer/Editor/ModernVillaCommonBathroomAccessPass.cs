using System;
using System.IO;
using System.Linq;
using CampusExplorer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace CampusExplorer.Editor
{
    /// <summary>Restores the source-visible upper-floor passage to the common bathroom.</summary>
    public static class ModernVillaCommonBathroomAccessPass
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string ReportPath = "Evidence/common-bathroom/apply-report.txt";
        const float UpperFloorY = 68.12665f;
        const float BridgeMinX = -10.11f;
        const float BridgeMaxX = -7.51f;
        const float BridgeMinZ = -28.55f;
        const float BridgeMaxZ = -26.78f;
        const float BridgeThickness = 0.04f;
        const string Description = "Salle de bains commune à l'étage, avec lavabo, toilettes et douche vitrée.";

        [MenuItem("Campus Explorer/Fixes/Open Common Bathroom And Move Bathroom POI")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty)
                throw new InvalidOperationException("Save the current scene before the bathroom access fix.");
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var root = GameObject.Find("Collisions")?.transform
                ?? throw new InvalidOperationException("Collisions root is missing.");
            var floors = root.Find("UpperFloor/Floors")
                ?? throw new InvalidOperationException("UpperFloor/Floors is missing.");
            var railings = root.Find("UpperFloor/Railings")
                ?? throw new InvalidOperationException("UpperFloor/Railings is missing.");
            var east = RequireGuard(railings, "Stair Void East Guard");
            var west = RequireGuard(railings, "Stair Void West Guard");
            var south = RequireGuard(railings, "Stair Void Lower Guard");
            var bridgeCollider = floors.Find("Common Bathroom Access Bridge")?.GetComponent<BoxCollider>();
            var originalLayout = bridgeCollider == null;
            var expectedCount = originalLayout ? 150 : 151;
            if (root.GetComponentsInChildren<BoxCollider>(true).Length != expectedCount)
                throw new InvalidOperationException($"Expected {expectedCount} environment colliders.");
            if (originalLayout && (Mathf.Abs(east.bounds.max.z + 27.595f) > 0.02f ||
                Mathf.Abs(west.bounds.max.z + 27.595f) > 0.02f))
                throw new InvalidOperationException("Stair guards differ from their validated starting layout.");

            VerifySourceFloor();

            if (originalLayout)
            {
                var bridge = new GameObject("Common Bathroom Access Bridge");
                bridge.transform.SetParent(floors, false);
                bridge.transform.position = new Vector3((BridgeMinX + BridgeMaxX) * 0.5f,
                    UpperFloorY - BridgeThickness * 0.5f, (BridgeMinZ + BridgeMaxZ) * 0.5f);
                bridgeCollider = bridge.AddComponent<BoxCollider>();
                bridgeCollider.size = new Vector3(BridgeMaxX - BridgeMinX, BridgeThickness,
                    BridgeMaxZ - BridgeMinZ);
                bridgeCollider.isTrigger = false;
                bridge.AddComponent<TeleportationArea>();

            }

            TrimSideGuard(east, BridgeMinZ);
            TrimSideGuard(west, BridgeMinZ);

            // Keep the walkable top aligned while giving the descending XR
            // capsule enough headroom where the bridge passes over the ramp.
            bridgeCollider.transform.position = new Vector3((BridgeMinX + BridgeMaxX) * 0.5f,
                UpperFloorY - BridgeThickness * 0.5f, (BridgeMinZ + BridgeMaxZ) * 0.5f);
            bridgeCollider.size = new Vector3(BridgeMaxX - BridgeMinX, BridgeThickness,
                BridgeMaxZ - BridgeMinZ);

            // Protect only the exposed west edge of the landing. A bar across
            // the full bridge would obstruct the ramp at the stair entrance.
            var westGuardLeft = west.bounds.min.x;
            south.transform.position = new Vector3((BridgeMinX + westGuardLeft) * 0.5f,
                south.transform.position.y, BridgeMinZ - south.size.z * 0.5f);
            south.size = new Vector3(westGuardLeft - BridgeMinX, south.size.y, south.size.z);

            var pointRoot = GameObject.Find("Villa POIs")?.transform.Find("POI - Bathroom")
                ?? throw new InvalidOperationException("Bathroom POI marker is missing.");
            pointRoot.position = new Vector3(-11.05f, 69.35f, -29.10f);
            var point = pointRoot.GetComponentInChildren<PointOfInterest>(true)
                ?? throw new InvalidOperationException("Bathroom PointOfInterest is missing.");
            var pointData = new SerializedObject(point);
            pointData.FindProperty("body").stringValue = Description;
            pointData.ApplyModifiedPropertiesWithoutUndo();

            var panel = GameObject.Find("Villa POI Panels")?.transform
                .Find("Panel - Bathroom/World Space Canvas")?.GetComponent<RectTransform>()
                ?? throw new InvalidOperationException("Bathroom POI panel is missing.");
            panel.anchoredPosition3D = new Vector3(-11.05f, 69.97f, -29.10f);
            panel.ForceUpdateRectTransforms();
            var info = point.Panel ?? throw new InvalidOperationException("Bathroom panel reference is missing.");
            info.BodyText.text = Description;

            Physics.SyncTransforms();
            Validate(root, bridgeCollider, east, west, south, point, panel);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath,
                "PASS: source-visible upper-floor bridge at x[-10.11,-7.51], z[-28.55,-26.78], top Y=68.12665; " +
                "stair side guards end at z=-28.55; west edge guard leaves ramp entry open; " +
                "Bathroom POI moved into the upper common bathroom at (-11.05,69.35,-29.10), " +
                "panel at (-11.05,69.97,-29.10). Bathroom teleport anchor unchanged. " +
                "No visual villa geometry, XR calibration, or locomotion changed.");
            Debug.Log("[Modern Villa] Common bathroom passage and POI saved.");
        }

        static BoxCollider RequireGuard(Transform root, string name) =>
            root.Find(name)?.GetComponent<BoxCollider>()
            ?? throw new InvalidOperationException("Missing stair guard: " + name);

        static void TrimSideGuard(BoxCollider guard, float northEnd)
        {
            var southEnd = guard.bounds.min.z;
            guard.transform.position = new Vector3(guard.transform.position.x,
                guard.transform.position.y, (southEnd + northEnd) * 0.5f);
            guard.size = new Vector3(guard.size.x, guard.size.y, northEnd - southEnd);
        }

        static void VerifySourceFloor()
        {
            var renderer = UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsInactive.Include)
                .FirstOrDefault(item => item.name == "Floor_Upper_Level_Main")
                ?? throw new InvalidOperationException("Visible upper-floor mesh is missing.");
            var source = renderer.GetComponent<MeshFilter>()?.sharedMesh
                ?? throw new InvalidOperationException("Visible upper-floor mesh data is missing.");
            var probe = renderer.gameObject.AddComponent<MeshCollider>();
            try
            {
                probe.sharedMesh = source;
                Physics.SyncTransforms();
                foreach (var x in new[] { -9.75f, -9.4f, -8.8f, -8.2f, -7.75f })
                foreach (var z in new[] { -28.7f, -28.25f, -27.75f, -27.2f })
                {
                    if (!probe.Raycast(new Ray(new Vector3(x, 72f, z), Vector3.down),
                            out var hit, 5f) || Mathf.Abs(hit.point.y - 68.10122f) > 0.05f)
                        throw new InvalidOperationException($"No source-visible floor at ({x},{z}).");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        static void Validate(Transform root, BoxCollider bridge, BoxCollider east,
            BoxCollider west, BoxCollider south, PointOfInterest point, RectTransform panel)
        {
            var boxes = root.GetComponentsInChildren<BoxCollider>(true);
            if (boxes.Length != 151 || boxes.Any(box => !box.gameObject.activeInHierarchy ||
                !box.enabled || box.isTrigger))
                throw new InvalidOperationException("Expected 151 active solid collision helpers.");
            if (bridge.GetComponent<TeleportationArea>() == null ||
                Mathf.Abs(bridge.bounds.max.y - UpperFloorY) > 0.005f)
                throw new InvalidOperationException("Bathroom bridge is not walkable at upper-floor height.");
            if (east.bounds.max.z > BridgeMinZ + 0.005f ||
                west.bounds.max.z > BridgeMinZ + 0.005f ||
                Mathf.Abs(south.bounds.max.z - BridgeMinZ) > 0.005f ||
                south.bounds.min.x > BridgeMinX + 0.005f ||
                south.bounds.max.x > west.bounds.min.x + 0.005f)
                throw new InvalidOperationException("Stair guards still obstruct the bathroom passage.");
            var doorway = new Vector3(-9.571f, UpperFloorY, -28.242f);
            var blockers = Physics.OverlapCapsule(doorway + Vector3.up * 0.32f,
                    doorway + Vector3.up * 1.47f, 0.28f, ~0, QueryTriggerInteraction.Ignore)
                .Where(collider => collider.transform.IsChildOf(root) &&
                                   collider.bounds.max.y > UpperFloorY + 0.5f)
                .ToArray();
            if (blockers.Length != 0)
                throw new InvalidOperationException("Bathroom doorway blocked by " +
                                                    string.Join(", ", blockers.Select(item => item.name)));
            if (Vector3.Distance(point.transform.parent.position,
                    new Vector3(-11.05f, 69.35f, -29.10f)) > 0.01f ||
                Vector3.Distance(panel.position, new Vector3(-11.05f, 69.97f, -29.10f)) > 0.01f ||
                point.Body != Description)
                throw new InvalidOperationException("Common bathroom POI placement or copy did not persist.");
        }
    }
}
