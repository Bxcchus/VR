using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace CampusExplorer.Editor
{
    public static class ModernVillaDoorAudit
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";

        [MenuItem("Campus Explorer/Interaction/Audit Existing Villa Doors")]
        public static void Run()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var candidates = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include)
                .Where(renderer => IsDoorName(renderer.name) ||
                                   renderer.transform.Cast<Transform>().Any(child => IsDoorName(child.name)))
                .OrderBy(renderer => renderer.bounds.center.y)
                .ThenBy(renderer => renderer.name)
                .ToArray();

            Directory.CreateDirectory("Evidence");
            using var writer = new StreamWriter("Evidence/interactive-door-audit.txt", false);
            writer.WriteLine($"Door renderer candidates: {candidates.Length}");
            foreach (var renderer in candidates)
            {
                var transform = renderer.transform;
                var meshFilter = renderer.GetComponent<MeshFilter>();
                var mesh = meshFilter != null ? meshFilter.sharedMesh : null;
                var localBounds = mesh != null ? mesh.bounds : new Bounds();
                var colliders = transform.GetComponentsInChildren<Collider>(true);
                var children = transform.Cast<Transform>().Select(child => child.name).ToArray();
                var line = $"path={HierarchyPath(transform)} | position={transform.position} rotation={transform.rotation.eulerAngles} " +
                           $"scale={transform.lossyScale} | worldCenter={renderer.bounds.center} worldSize={renderer.bounds.size} | " +
                           $"mesh={(mesh != null ? mesh.name : "none")} localCenter={localBounds.center} localSize={localBounds.size} " +
                           $"vertices={(mesh != null ? mesh.vertexCount : 0)} | colliders={colliders.Length} " +
                           $"children=[{string.Join(",", children)}] parent={(transform.parent != null ? transform.parent.name : "none")}";
                writer.WriteLine(line);
                Debug.Log("[Interactive Door][Audit] " + line);
            }
            var selected = candidates.Single(renderer => renderer.name == "Door_Interior_08");
            Physics.SyncTransforms();
            foreach (var angle in new[] { -90f, 90f })
            {
                var rotation = Quaternion.AngleAxis(angle, Vector3.up);
                var hinge = selected.transform.position;
                var center = hinge + rotation * (selected.bounds.center - hinge);
                var extents = selected.bounds.extents - new Vector3(0.08f, 0.05f, 0.01f);
                var openExtents = new Vector3(extents.x, extents.y, Mathf.Max(extents.z, 0.05f));
                var overlaps = Physics.OverlapBox(center, openExtents, rotation, ~0, QueryTriggerInteraction.Ignore)
                    .Where(collider => !collider.transform.IsChildOf(selected.transform))
                    .Select(collider => HierarchyPath(collider.transform))
                    .Distinct().ToArray();
                var clearance = $"Door_Interior_08 angle={angle:+0;-0} center={center} overlaps={overlaps.Length} " +
                                $"[{string.Join(",", overlaps)}]";
                writer.WriteLine(clearance);
                Debug.Log("[Interactive Door][Clearance] " + clearance);
            }
            writer.WriteLine($"All scene colliders: {UnityEngine.Object.FindObjectsByType<Collider>(FindObjectsInactive.Include).Length}");
            Debug.Log($"[Interactive Door][Audit] {candidates.Length} candidate renderers written to Evidence/interactive-door-audit.txt.");
        }

        static bool IsDoorName(string value)
        {
            return value.Contains("door", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("puerta", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("porte", StringComparison.OrdinalIgnoreCase);
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
    }
}
