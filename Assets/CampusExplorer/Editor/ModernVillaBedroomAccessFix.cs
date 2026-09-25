using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CampusExplorer.Editor
{
    // Targeted scene migration: preserve every existing helper except the one
    // wardrobe box that fills the walkable aisle beside the master bed.
    [InitializeOnLoad]
    public static class ModernVillaBedroomAccessFix
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string RequestPath = "Evidence/bedroom-poi-wardrobe-fix/apply.request";
        const string ReportPath = "Evidence/bedroom-poi-wardrobe-fix/apply-report.txt";

        static ModernVillaBedroomAccessFix()
        {
            EditorApplication.update += Poll;
        }

        static void Poll()
        {
            if (!File.Exists(RequestPath))
                return;
            if (EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;

            var active = EditorSceneManager.GetActiveScene();
            if (active.isDirty)
            {
                File.WriteAllText(ReportPath,
                    "Waiting: the open scene has unsaved changes. Save it or discard those changes before this targeted fix runs.");
                return;
            }

            File.Delete(RequestPath);
            try
            {
                Apply();
            }
            catch (Exception exception)
            {
                File.WriteAllText(ReportPath, "FAILED: " + exception);
                Debug.LogException(exception);
            }
        }

        [MenuItem("Campus Explorer/Fixes/Fix Bedroom POI and Wardrobe Aisle")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty)
                throw new InvalidOperationException("Save the current scene before applying the targeted fix.");
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var bedrooms = GameObject.Find("Collisions")?.transform.Find("UpperFloor/Bedrooms")
                ?? throw new InvalidOperationException("UpperFloor/Bedrooms collision group is missing.");
            var old = bedrooms.Find("Built-in Wardrobe");
            if (old == null)
            {
                if (bedrooms.Find("Built-in Wardrobe - Long Cabinet") == null ||
                    bedrooms.Find("Built-in Wardrobe - Short Cabinet") == null)
                    throw new InvalidOperationException("The original wardrobe collision helper is missing.");
            }
            else
            {
                if (old.GetComponents<BoxCollider>().Length != 1)
                    throw new InvalidOperationException("Expected exactly one original wardrobe BoxCollider.");
                UnityEngine.Object.DestroyImmediate(old.gameObject);
                CreateBox(bedrooms, "Built-in Wardrobe - Long Cabinet",
                    new Vector3(4.173439f, 69.387130f, -32.492465f),
                    new Vector3(0.614464f, 2.571831f, 4.821728f));
                CreateBox(bedrooms, "Built-in Wardrobe - Short Cabinet",
                    new Vector3(2.062623f, 69.387130f, -31.574530f),
                    new Vector3(0.620229f, 2.571831f, 2.978372f));
            }

            var canvas = GameObject.Find("Villa POI Panels")?.transform
                .Find("Panel - Master Bedroom/World Space Canvas")
                ?? throw new InvalidOperationException("Master Bedroom POI canvas is missing.");
            // World-space UI canvases serialize their XY through RectTransform
            // anchoredPosition. Setting Transform.position alone is overwritten
            // when Unity rebuilds the canvas on scene load.
            var panelRect = canvas.GetComponent<RectTransform>()
                ?? throw new InvalidOperationException("Master Bedroom POI canvas has no RectTransform.");
            panelRect.anchoredPosition3D = new Vector3(1.00f, 69.97f, -31.00f);
            panelRect.ForceUpdateRectTransforms();

            Physics.SyncTransforms();
            var colliders = GameObject.Find("Collisions").GetComponentsInChildren<Collider>(true);
            if (colliders.Length != 151)
                throw new InvalidOperationException($"Expected 151 collision helpers after the split; found {colliders.Length}.");
            foreach (var z in new[] { -30.25f, -31f, -32f, -33f, -34f, -34.65f })
            {
                var feet = new Vector3(3.12f, 68.10122f, z);
                var blockers = Physics.OverlapCapsule(feet + Vector3.up * 0.32f,
                        feet + Vector3.up * 1.48f, 0.28f, Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore)
                    .Where(hit => hit.transform.IsChildOf(bedrooms) && hit.bounds.max.y > feet.y + 0.5f)
                    .ToArray();
                if (blockers.Length != 0)
                    throw new InvalidOperationException($"Wardrobe aisle blocked at z={z}: " +
                                                        string.Join(", ", blockers.Select(hit => hit.name)));
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            File.WriteAllText(ReportPath,
                "PASS: split Built-in Wardrobe into two source-matched cabinet colliders; " +
                "1.49 m aisle clear at six positions; 151 environment colliders; " +
                "Master Bedroom POI panel moved to (1.00, 69.97, -31.00) outside the furniture. " +
                "No visual mesh, XR rig, door, staircase, or other collision helper changed.");
            Debug.Log("[Modern Villa] Master Bedroom POI panel and wardrobe aisle fixed.");
        }

        static void CreateBox(Transform parent, string name, Vector3 center, Vector3 size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = center;
            var collider = go.AddComponent<BoxCollider>();
            collider.size = size;
            collider.isTrigger = false;
        }
    }
}
