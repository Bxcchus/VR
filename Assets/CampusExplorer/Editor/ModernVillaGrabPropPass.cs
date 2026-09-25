using System;
using System.IO;
using System.Linq;
using System.Text;
using CampusExplorer;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace CampusExplorer.Editor
{
    // Audits the imported villa props before any mesh is made movable.
    [InitializeOnLoad]
    public static class ModernVillaGrabPropPass
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string AuditRequest = "Evidence/grabbable-props/audit.request";
        const string AuditReport = "Evidence/grabbable-props/inventory.tsv";
        const string ApplyRequest = "Evidence/grabbable-props/apply.request";
        const string ApplyReport = "Evidence/grabbable-props/apply-report.txt";

        readonly struct Prop
        {
            public readonly string name;
            public readonly float mass;
            public readonly bool fallAfterRelease;

            public Prop(string name, float mass, bool fallAfterRelease)
            {
                this.name = name;
                this.mass = mass;
                this.fallAfterRelease = fallAfterRelease;
            }
        }

        // Only genuinely separate, hand-sized FBX meshes are eligible.
        // Cushions stay kinematic when placed: the existing sofa/bed helpers
        // cover their visible seats and would eject a dynamic cushion.
        static readonly Prop[] Props =
        {
            new("Cushion_02", 0.35f, false),
            // These guest pillows sit above the existing bed collision and can
            // be reached by the Quest ray. The master cushion is enclosed by
            // its coarse bed helper, so it is intentionally excluded.
            new("Bed_Pillow_Guest_01", 0.35f, false),
            new("Bed_Pillow_Guest_02", 0.35f, false),
            new("Cushion_10", 0.35f, false),
            new("Cushion_11", 0.35f, false),
            new("Cushion_12", 0.35f, false),
            new("Cushion_13", 0.35f, false),
            new("Cushion_14", 0.35f, false),
            new("Conditioner_Bottle", 0.45f, true),
            new("Shampoo_Bottle", 0.45f, true),
            new("Liquid_Soap_Dispenser", 0.45f, true),
            new("Book_Decorative_02", 0.25f, true),
        };

        static ModernVillaGrabPropPass() => EditorApplication.update += Poll;

        static void Poll()
        {
            if ((!File.Exists(AuditRequest) && !File.Exists(ApplyRequest)) ||
                EditorApplication.isPlayingOrWillChangePlaymode ||
                EditorApplication.isCompiling || EditorApplication.isUpdating)
                return;
            if (EditorSceneManager.GetActiveScene().isDirty)
                return;

            var apply = File.Exists(ApplyRequest);
            File.Delete(apply ? ApplyRequest : AuditRequest);
            try
            {
                if (apply) Apply();
                else Audit();
            }
            catch (Exception exception)
            {
                File.WriteAllText(apply ? ApplyReport : AuditReport, "FAILED\t" + exception);
                Debug.LogException(exception);
            }
        }

        [MenuItem("Campus Explorer/Interaction/Enable Villa Prop Grabbing")]
        public static void Apply()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty)
                throw new InvalidOperationException("Save the current scene before enabling prop grabbing.");
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var villa = scene.GetRootGameObjects().Single(go => go.name ==
                "Authoritative Modern Luxury Villa - Identity Transform");
            var renderers = villa.GetComponentsInChildren<MeshRenderer>(true);
            var originals = Props.Select(spec =>
            {
                var renderer = renderers.Single(item => item.name == spec.name);
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0 || renderer.GetComponentInChildren<Collider>() != null)
                    throw new InvalidOperationException("Not an isolated collider-free prop: " + spec.name);
                if (renderer.gameObject.layer != 31 || renderer.gameObject.isStatic)
                    throw new InvalidOperationException("Unexpected visual layer/static flag: " + spec.name);
                return (spec, renderer, mesh);
            }).ToArray();

            var priorEnvironment = GameObject.Find("Collisions")?
                .GetComponentsInChildren<BoxCollider>(true).Length ?? 0;
            var priorDoors = GameObject.Find("InteractiveDoors")?
                .GetComponentsInChildren<BoxCollider>(true).Length ?? 0;
            var priorPois = UnityEngine.Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include).Length;
            if (priorEnvironment != 151 || priorDoors != 16 || priorPois != 6)
                throw new InvalidOperationException(
                    $"Existing scene systems differ: environment={priorEnvironment}, doors={priorDoors}, POIs={priorPois}.");

            var report = new StringBuilder();
            report.AppendLine("Grabbable villa props: grip to hold, release grip to place.");
            foreach (var (spec, renderer, mesh) in originals)
            {
                var go = renderer.gameObject;
                if (go.GetComponent<XRGrabInteractable>() != null || go.GetComponent<Rigidbody>() != null ||
                    go.GetComponent<VillaGrabbableProp>() != null || go.transform.Find("Grab Collision") != null)
                    throw new InvalidOperationException("Prop already configured: " + spec.name);

                var target = new GameObject("Grab Collision");
                target.layer = 0; // Near/far controller physics raycasts include Default, not visual layer 31.
                target.transform.SetParent(go.transform, false);
                var box = target.AddComponent<BoxCollider>();
                box.center = mesh.bounds.center;
                box.size = mesh.bounds.size;
                box.isTrigger = false;

                var body = go.AddComponent<Rigidbody>();
                body.mass = spec.mass;
                body.isKinematic = true;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

                var grab = go.AddComponent<XRGrabInteractable>();
                grab.colliders.Clear();
                grab.colliders.Add(box);
                grab.movementType = XRBaseInteractable.MovementType.Kinematic;
                grab.useDynamicAttach = true;
                grab.attachEaseInTime = 0.08f;
                grab.throwOnDetach = false;
                grab.forceGravityOnDetach = spec.fallAfterRelease;

                go.AddComponent<VillaGrabbableProp>().Configure(box, spec.fallAfterRelease);
                report.AppendLine($"{spec.name}: center={Format(renderer.bounds.center)} " +
                                  $"bounds={Format(renderer.bounds.size)} mass={spec.mass:F2} " +
                                  $"release={(spec.fallAfterRelease ? "gravity" : "placed pose")}");
            }

            if (GameObject.Find("Collisions").GetComponentsInChildren<BoxCollider>(true).Length != priorEnvironment ||
                GameObject.Find("InteractiveDoors").GetComponentsInChildren<BoxCollider>(true).Length != priorDoors ||
                UnityEngine.Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include).Length != priorPois)
                throw new InvalidOperationException("An existing collision, door, or POI system changed.");

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Directory.CreateDirectory(Path.GetDirectoryName(ApplyReport));
            File.WriteAllText(ApplyReport, report.ToString());
            Debug.Log($"[Grab Props] Saved {Props.Length} grip-selectable props in {ScenePath}.");
        }

        [MenuItem("Campus Explorer/Interaction/Audit Grabbable Villa Props")]
        public static void Audit()
        {
            var scene = EditorSceneManager.GetActiveScene();
            if (scene.isDirty)
                throw new InvalidOperationException("Save the current scene before auditing props.");
            if (scene.path != ScenePath)
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var villa = scene.GetRootGameObjects().Single(go => go.name ==
                "Authoritative Modern Luxury Villa - Identity Transform");
            var report = new StringBuilder();
            report.AppendLine("name\tpath\tcenter\tsize\tpivot\tmesh\tvertices\tmaterials\tcolliders\tstatic");
            foreach (var renderer in villa.GetComponentsInChildren<Renderer>(true)
                         .OrderBy(item => item.name, StringComparer.Ordinal))
            {
                var mesh = renderer.GetComponent<MeshFilter>()?.sharedMesh;
                var path = renderer.name;
                for (var parent = renderer.transform.parent; parent != null && parent != villa.transform;
                     parent = parent.parent)
                    path = parent.name + "/" + path;
                var bounds = renderer.bounds;
                report.Append(renderer.name).Append('\t').Append(path).Append('\t')
                    .Append(Format(bounds.center)).Append('\t').Append(Format(bounds.size)).Append('\t')
                    .Append(Format(renderer.transform.position)).Append('\t').Append(mesh?.name ?? "-")
                    .Append('\t').Append(mesh?.vertexCount ?? 0).Append('\t')
                    .Append(string.Join(",", renderer.sharedMaterials.Select(material => material?.name ?? "null")))
                    .Append('\t').Append(renderer.GetComponentsInChildren<Collider>(true).Length)
                    .Append('\t').Append(renderer.gameObject.isStatic).AppendLine();
            }
            Directory.CreateDirectory(Path.GetDirectoryName(AuditReport));
            File.WriteAllText(AuditReport, report.ToString());
            Debug.Log("[Grab Props] Saved villa renderer inventory to " + AuditReport);
        }

        static string Format(Vector3 vector) =>
            $"({vector.x:F3},{vector.y:F3},{vector.z:F3})";
    }
}
