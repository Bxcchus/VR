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
    // Applies the source-verified navigation repairs to the saved tour scene
    // without rebuilding the existing interior, doors, stair, or XR rig.
    [InitializeOnLoad]
    public static class ModernVillaObservedNavigationFix
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string RequestPath = "Evidence/navigation-observation-fix/apply.request";
        const string ReportPath = "Evidence/navigation-observation-fix/apply-report.txt";
        const float Thickness = 0.14f;

        static ModernVillaObservedNavigationFix() => EditorApplication.update += Poll;

        static void Poll()
        {
            if (!File.Exists(RequestPath) || EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            if (EditorSceneManager.GetActiveScene().isDirty)
            {
                File.WriteAllText(ReportPath, "WAITING: the active scene has unsaved changes.");
                return;
            }

            File.Delete(RequestPath);
            try { Apply(); }
            catch (Exception exception)
            {
                File.WriteAllText(ReportPath, "FAILED: " + exception);
                Debug.LogException(exception);
            }
        }

        [MenuItem("Campus Explorer/Fixes/Repair Observed Navigation And Bathroom POI")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty)
                throw new InvalidOperationException("Save the open scene before this targeted repair.");
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var root = GameObject.Find("Collisions")?.transform
                ?? throw new InvalidOperationException("Collisions hierarchy is missing.");
            var originalCount = root.GetComponentsInChildren<BoxCollider>(true).Length;
            if (originalCount != 139)
                throw new InvalidOperationException($"Expected the observed 139-helper scene; found {originalCount}.");

            var ground = RequireGroup(root, "GroundFloor/Floors");
            var balcony = RequireGroup(root, "UpperFloor/Railings");
            var garden = RequireGroup(root, "Exterior/Garden");
            var transitions = RequireGroup(root, "Exterior/Transitions");
            var boundaries = RequireGroup(root, "Exterior/Boundaries");
            var oldRise = RequireBox(garden, "West Side Garden Rise");
            var oldEastBoundary = RequireBox(boundaries, "Property East Boundary");

            var bathroomCanvas = GameObject.Find("Villa POI Panels")?.transform
                .Find("Panel - Bathroom/World Space Canvas")?.GetComponent<RectTransform>()
                ?? throw new InvalidOperationException("Bathroom POI panel is missing.");

            AddFloor(ground, "Under Stair Landscape Passage", -11.525f, -29.88f,
                4.35f, 4.88f, 64.865f, true);

            const float balconyY = 68.12665f;
            const float guardHeight = 1.15f;
            AddBox(balcony, "Upper Balcony Front Guard",
                new Vector3(-8.3396f, balconyY + guardHeight * 0.5f, -17.100f),
                new Vector3(9.017f, guardHeight, 0.12f), Quaternion.identity);
            AddBox(balcony, "Upper Balcony West Guard",
                new Vector3(-12.848f, balconyY + guardHeight * 0.5f, -19.132f),
                new Vector3(0.12f, guardHeight, 4.148f), Quaternion.identity);
            AddBox(balcony, "Upper Balcony East Guard",
                new Vector3(-3.831f, balconyY + guardHeight * 0.5f, -19.132f),
                new Vector3(0.12f, guardHeight, 4.148f), Quaternion.identity);

            UnityEngine.Object.DestroyImmediate(oldRise.gameObject);
            AddRamp(garden, "West Side Garden Rise Lower", -23.195f, 19.31f,
                -40f, 64.85f, -41f, 65.28f);
            AddRamp(garden, "West Side Garden Rise Crest", -23.195f, 19.31f,
                -41f, 65.28f, -41.5f, 65.35f);
            AddRamp(garden, "West Side Garden Rise Flat", -23.195f, 19.31f,
                -41.5f, 65.35f, -44.95f, 65.3352f);

            AddFloor(transitions, "Living Patio Threshold", -13.36f, -24.807f,
                0.70f, 3.40f, 64.87821f, true);
            AddRamp(transitions, "South Villa Landscape Apron", -1.72f, 8.80f,
                -33.37f, 64.82f, -36.16f, 64.79f);
            AddFloor(transitions, "West Terrace Ground Bridge", -13.39f, -43.225f,
                0.42f, 3.65f, 65.35f, true);

            UnityEngine.Object.DestroyImmediate(oldEastBoundary.gameObject);
            const float outerY = 66.4f;
            AddBox(boundaries, "Property East Front Boundary",
                new Vector3(22.20f, outerY, -10.675f), new Vector3(0.20f, 4f, 23.25f), Quaternion.identity);
            AddBox(boundaries, "Property East Side Boundary",
                new Vector3(22.39f, outerY, -33.60f), new Vector3(0.20f, 4f, 23.00f), Quaternion.identity);
            AddBox(boundaries, "Property East Rear Boundary",
                new Vector3(22.48f, outerY, -57.68f), new Vector3(0.20f, 4f, 25.76f), Quaternion.identity);

            // The old panel was only 0.30 m from the Bathroom arrival anchor.
            // The marker is at x=-8.15, providing 1.20 m of readable distance.
            bathroomCanvas.anchoredPosition3D = new Vector3(-8.15f, 69.97f, -35.75f);
            bathroomCanvas.ForceUpdateRectTransforms();

            Physics.SyncTransforms();
            Validate(root, bathroomCanvas);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText(ReportPath,
                "PASS: 150 environment helpers; west patio, under-stair passage, south apron, " +
                "west terrace edge and garden rise supported; east boundary follows visible terrain; " +
                "upper balcony guarded; Bathroom POI panel moved to (-8.15, 69.97, -35.75). " +
                "XR rig, player height, visual villa, stairs and door logic unchanged.");
            Debug.Log("[Modern Villa] Observed navigation and Bathroom POI fixes saved.");
        }

        static Transform RequireGroup(Transform root, string path) =>
            root.Find(path) ?? throw new InvalidOperationException("Missing group: " + path);

        static BoxCollider RequireBox(Transform group, string name) =>
            group.Find(name)?.GetComponent<BoxCollider>()
            ?? throw new InvalidOperationException("Missing original helper: " + name);

        static BoxCollider AddBox(Transform parent, string name, Vector3 center,
            Vector3 size, Quaternion rotation)
        {
            if (parent.Find(name) != null)
                throw new InvalidOperationException("Helper already exists: " + name);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, rotation);
            var box = go.AddComponent<BoxCollider>();
            box.size = size;
            box.isTrigger = false;
            return box;
        }

        static void AddFloor(Transform parent, string name, float x, float z,
            float width, float depth, float topY, bool teleportable)
        {
            var box = AddBox(parent, name, new Vector3(x, topY - Thickness * 0.5f, z),
                new Vector3(width, Thickness, depth), Quaternion.identity);
            if (teleportable)
                box.gameObject.AddComponent<TeleportationArea>();
        }

        static void AddRamp(Transform parent, string name, float x, float width,
            float startZ, float startY, float endZ, float endY)
        {
            var start = new Vector3(x, startY, startZ);
            var end = new Vector3(x, endY, endZ);
            var direction = (end - start).normalized;
            var normal = Vector3.Cross(Vector3.right, direction).normalized;
            if (normal.y < 0f) normal = -normal;
            var rotation = Quaternion.LookRotation(direction, normal);
            var center = (start + end) * 0.5f - normal * (Thickness * 0.5f);
            var box = AddBox(parent, name, center,
                new Vector3(width, Thickness, Vector3.Distance(start, end)), rotation);
            box.gameObject.AddComponent<TeleportationArea>();
        }

        static void Validate(Transform root, RectTransform bathroomCanvas)
        {
            var boxes = root.GetComponentsInChildren<BoxCollider>(true);
            if (boxes.Length != 150 || boxes.Any(box => !box.enabled || !box.gameObject.activeInHierarchy || box.isTrigger))
                throw new InvalidOperationException("Expected 150 active, solid environment BoxColliders.");
            if (Mathf.Abs(bathroomCanvas.position.x + 8.15f) > 0.01f)
                throw new InvalidOperationException("Bathroom POI panel position did not persist.");
            AssertFloor(root, -13.35f, -24.80f, 64.87821f, "living patio threshold");
            AssertFloor(root, -11f, -29.88f, 64.865f, "under-stair passage");
            AssertFloor(root, -5.43f, -35.5f, 64.80f, "south villa apron");
            AssertFloor(root, -13.4f, -43f, 65.35f, "west terrace edge");
            AssertFloor(root, -23.2f, -41f, 65.28f, "west garden rise");
            var boundary = RequireBox(RequireGroup(root, "Exterior/Boundaries"),
                "Property East Rear Boundary");
            if (boundary.bounds.min.x > 22.40f)
                throw new InvalidOperationException("East rear boundary still leaves a fall gap.");
            var balcony = RequireBox(RequireGroup(root, "UpperFloor/Railings"),
                "Upper Balcony Front Guard");
            if (balcony.bounds.max.z < -17.1f || balcony.bounds.min.z > -17.1f)
                throw new InvalidOperationException("Upper balcony exposed edge is unguarded.");
        }

        static void AssertFloor(Transform root, float x, float z, float expectedY, string label)
        {
            var floor = Physics.RaycastAll(new Vector3(x, 72f, z), Vector3.down, 9f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Where(hit => hit.collider.transform.IsChildOf(root) &&
                              hit.collider.GetComponent<TeleportationArea>() != null &&
                              Mathf.Abs(hit.point.y - expectedY) < 0.12f)
                .OrderBy(hit => Mathf.Abs(hit.point.y - expectedY)).FirstOrDefault();
            if (floor.collider == null)
                throw new InvalidOperationException("No walkable support at " + label);
        }
    }
}
