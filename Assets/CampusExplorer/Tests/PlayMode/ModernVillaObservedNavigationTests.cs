using System.Collections;
using System.Linq;
using CampusExplorer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Unity.XR.CoreUtils;

namespace CampusExplorer.Tests.PlayMode
{
    /// <summary>
    /// Regression checks for the fall and balcony routes observed during the
    /// Quest Link play session. These sample the actual runtime physics scene,
    /// including the narrow seams that broad collider-count checks miss.
    /// </summary>
    public sealed class ModernVillaObservedNavigationTests
    {
        [UnityTest]
        public IEnumerator BathroomPoiPanelAppearsInTheCommonBathroomAndCloses()
        {
            yield return SceneManager.LoadSceneAsync("ModernVillaTour", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(5f);

            var origin = Object.FindAnyObjectByType<XROrigin>();
            var point = Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include)
                .Single(item => item.PointId == "bathroom");
            Assert.That(origin, Is.Not.Null);
            var guard = origin.GetComponents<MonoBehaviour>()
                .FirstOrDefault(component => component.GetType().Name == "ModernVillaXRSpawnGuard");
            if (guard != null)
                guard.enabled = false;
            origin.Origin.transform.position = new Vector3(-10.15f, 68.12665f, -28.40f);
            yield return null;

            var panel = point.Panel;
            Assert.That(panel, Is.Not.Null);
            Assert.That(panel.IsOpen, Is.False);
            Assert.That(Vector3.Distance(point.transform.position,
                    new Vector3(-11.05f, 69.35f, -29.10f)), Is.LessThan(0.03f),
                "Bathroom POI must be in the common upstairs bathroom, not the master bathroom.");
            Assert.That(point.Body, Does.Contain("Salle de bains commune"));
            var markerButton = point.GetComponent<Button>();
            Assert.That(markerButton, Is.Not.Null);
            markerButton.onClick.Invoke();
            yield return null;

            Assert.That(panel.IsOpen && panel.PanelRoot.activeInHierarchy, Is.True,
                "Selecting the Bathroom marker must show its information window.");
            Assert.That(panel.TitleText.text, Is.EqualTo(point.Title));
            Assert.That(panel.BodyText.text, Is.EqualTo(point.Body));
            var canvasPosition = panel.PanelRoot.transform.position;
            Assert.That(Vector3.Distance(canvasPosition, new Vector3(-11.05f, 69.97f, -29.10f)),
                Is.LessThan(0.03f),
                $"Bathroom panel is misplaced in the common bathroom: {canvasPosition}.");

            var eyeToPanel = canvasPosition - origin.Camera.transform.position;
            var horizontalView = Vector3.ProjectOnPlane(eyeToPanel, Vector3.up);
            Assert.That(horizontalView.magnitude, Is.GreaterThan(0.50f));
            Assert.That(Vector3.Dot(horizontalView.normalized, panel.PanelRoot.transform.forward),
                Is.GreaterThan(0.98f), "Bathroom panel text must face the viewer.");

            var collisionRoot = GameObject.Find("Collisions")?.transform;
            Assert.That(collisionRoot, Is.Not.Null);
            Physics.SyncTransforms();
            var obstructions = Physics.RaycastAll(origin.Camera.transform.position,
                    eyeToPanel.normalized, eyeToPanel.magnitude - 0.08f,
                    ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => hit.collider.transform.IsChildOf(collisionRoot))
                .Select(hit => hit.collider.name).Distinct().ToArray();
            Assert.That(obstructions, Is.Empty,
                $"Bathroom panel is hidden behind collision geometry: {string.Join(", ", obstructions)}.");

            panel.CloseButton.onClick.Invoke();
            yield return null;
            Assert.That(panel.IsOpen, Is.False);
        }

        [UnityTest]
        public IEnumerator OpenPatioDoorCanBeCrossedWithoutDroppingAtTheThreshold()
        {
            yield return SceneManager.LoadSceneAsync("ModernVillaTour", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(5f);

            var door = Object.FindObjectsByType<XRInteractiveSlidingDoor>(FindObjectsInactive.Include)
                .Single(item => item.DoorId == "Ground Patio Glass Doors");
            var origin = Object.FindAnyObjectByType<XROrigin>();
            Assert.That(origin, Is.Not.Null);
            var player = origin.Origin.GetComponent<CharacterController>();
            Assert.That(player, Is.Not.Null);
            door.ToggleDoor();
            yield return new WaitForSecondsRealtime(door.AnimationDuration + 0.15f);
            Assert.That(door.IsOpen, Is.True);

            var probe = new GameObject("Patio Threshold Capsule Probe");
            probe.layer = player.gameObject.layer;
            var capsule = probe.AddComponent<CharacterController>();
            capsule.enabled = false;
            capsule.height = player.height;
            capsule.radius = player.radius;
            capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
            capsule.slopeLimit = player.slopeLimit;
            capsule.stepOffset = player.stepOffset;
            capsule.skinWidth = player.skinWidth;
            probe.transform.position = new Vector3(-12.25f, 64.898f, -24.807f);
            capsule.enabled = true;
            Physics.SyncTransforms();

            var lowestY = probe.transform.position.y;
            for (var step = 0; step < 55; step++)
            {
                capsule.Move(Vector3.left * 0.04f + Vector3.down * 0.015f);
                lowestY = Mathf.Min(lowestY, probe.transform.position.y);
            }
            Assert.That(probe.transform.position.x, Is.LessThan(-13.85f),
                $"The opened patio door or threshold blocks the XR capsule at {probe.transform.position}.");
            Assert.That(lowestY, Is.GreaterThan(64.82f),
                $"The XR capsule fell into the living/patio floor seam; lowestY={lowestY:F3}.");
            Assert.That(probe.transform.position.y, Is.LessThan(65.10f),
                "The threshold should be a small step, not a raised invisible platform.");
            Object.Destroy(probe);
        }

        [UnityTest]
        public IEnumerator ObservedGroundAndGardenRoutesHaveWalkableSupport()
        {
            yield return SceneManager.LoadSceneAsync("ModernVillaTour", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(5f);

            var root = GameObject.Find("Collisions")?.transform;
            Assert.That(root, Is.Not.Null);
            AssertSolidBox(root, "GroundFloor/Floors/Under Stair Landscape Passage");
            AssertSolidBox(root, "Exterior/Transitions/Living Patio Threshold");
            AssertSolidBox(root, "Exterior/Transitions/South Villa Landscape Apron");
            AssertSolidBox(root, "Exterior/Transitions/West Terrace Ground Bridge");
            AssertSolidBox(root, "Exterior/Garden/West Side Garden Rise Lower");
            AssertSolidBox(root, "Exterior/Garden/West Side Garden Rise Crest");
            AssertSolidBox(root, "Exterior/Garden/West Side Garden Rise Flat");

            var floors = WalkableFloorGroups(root);
            Physics.SyncTransforms();

            // Sample both sides of the living-room glass opening as well as
            // the formerly unsupported 36 cm strip across its threshold.
            AssertSupport(floors, -13.15f, -24.807f, 64.878f, 0.08f, "living floor at patio door");
            AssertSupport(floors, -13.35f, -24.807f, 64.91f, 0.08f, "living/patio threshold seam");
            AssertSupport(floors, -13.55f, -24.807f, 64.934f, 0.08f, "garden outside patio door");

            AssertSupport(floors, -11.0f, -29.88f, 64.878f, 0.08f, "under-stair opening");
            AssertSupport(floors, -5.43f, -35.50f, 64.797f, 0.08f, "south villa apron");
            AssertSupport(floors, -13.40f, -43.0f, 65.351f, 0.12f, "west terrace edge");

            // Source terrain measurements across the steeper western rise.
            // A single linear ramp used to sit 15-25 cm below this surface.
            var rise = new[]
            {
                (z: -40.0f, y: 64.850f),
                (z: -40.5f, y: 65.047f),
                (z: -41.0f, y: 65.282f),
                (z: -41.5f, y: 65.347f),
                (z: -43.0f, y: 65.345f),
                (z: -44.90f, y: 65.335f)
            };
            foreach (var sample in rise)
                AssertSupport(floors, -18.0f, sample.z, sample.y, 0.09f,
                    $"west garden rise at z={sample.z:F2}");
        }

        [UnityTest]
        public IEnumerator BalconyAndEastEdgeBlockTheCapsuleWithoutClosingTheDoorway()
        {
            yield return SceneManager.LoadSceneAsync("ModernVillaTour", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(5f);

            var root = GameObject.Find("Collisions")?.transform;
            var origin = Object.FindAnyObjectByType<XROrigin>();
            Assert.That(root, Is.Not.Null);
            Assert.That(origin, Is.Not.Null);
            var player = origin.Origin.GetComponent<CharacterController>();
            Assert.That(player, Is.Not.Null);

            var front = AssertSolidBox(root, "UpperFloor/Railings/Upper Balcony Front Guard");
            AssertSolidBox(root, "UpperFloor/Railings/Upper Balcony West Guard");
            AssertSolidBox(root, "UpperFloor/Railings/Upper Balcony East Guard");
            AssertSolidBox(root, "Exterior/Boundaries/Property East Front Boundary");
            AssertSolidBox(root, "Exterior/Boundaries/Property East Side Boundary");
            var eastRear = AssertSolidBox(root, "Exterior/Boundaries/Property East Rear Boundary");
            var floors = WalkableFloorGroups(root);
            Physics.SyncTransforms();

            AssertSupport(floors, -7.88f, -21.94f, 68.127f, 0.08f,
                "upper balcony doorway floor");
            AssertSupport(floors, -7.88f, -18.20f, 68.127f, 0.08f,
                "upper balcony floor near outer railing");

            // The floor helper and wall header must leave a full-height
            // passage through the intended sliding-door opening.
            var doorwayBlockers = Physics.OverlapCapsule(
                    new Vector3(-7.88f, 68.45f, -21.94f),
                    new Vector3(-7.88f, 69.55f, -21.94f), 0.22f,
                    ~0, QueryTriggerInteraction.Ignore)
                .Where(collider => collider.transform.IsChildOf(root)).ToArray();
            Assert.That(doorwayBlockers, Is.Empty,
                "Collision helpers must not close the upper balcony doorway.");

            Assert.That(Physics.RaycastAll(new Vector3(-7.88f, 68.85f, -18.0f),
                    Vector3.forward, 1.5f, ~0, QueryTriggerInteraction.Ignore)
                .Any(hit => hit.collider == front), Is.True,
                "The visible balcony front needs a solid outer guard.");
            Assert.That(Physics.RaycastAll(new Vector3(21.8f, 66.0f, -50.0f),
                    Vector3.right, 1.2f, ~0, QueryTriggerInteraction.Ignore)
                .Any(hit => hit.collider == eastRear), Is.True,
                "The east property edge must stop the player before the garden floor ends.");

            // Use the live XR capsule dimensions and physics layer, while
            // keeping this probe independent of headset tracking and input.
            var probe = new GameObject("Observed Navigation Capsule Probe");
            probe.layer = player.gameObject.layer;
            var capsule = probe.AddComponent<CharacterController>();
            capsule.height = player.height;
            capsule.radius = player.radius;
            capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
            capsule.slopeLimit = player.slopeLimit;
            capsule.stepOffset = player.stepOffset;
            capsule.skinWidth = player.skinWidth;

            capsule.enabled = false;
            probe.transform.position = new Vector3(-7.88f, 68.145f, -17.80f);
            capsule.enabled = true;
            Physics.SyncTransforms();
            for (var step = 0; step < 45; step++)
                capsule.Move(Vector3.forward * 0.04f + Vector3.down * 0.004f);
            Assert.That(probe.transform.position.z,
                Is.LessThan(front.bounds.min.z - capsule.radius + 0.14f),
                $"XR-sized capsule crossed the balcony guard: {probe.transform.position}.");
            Assert.That(probe.transform.position.y, Is.GreaterThan(68.05f),
                "The capsule dropped off the upper balcony while approaching its rail.");

            capsule.enabled = false;
            probe.transform.position = new Vector3(21.80f, 65.36f, -50.0f);
            capsule.enabled = true;
            Physics.SyncTransforms();
            for (var step = 0; step < 45; step++)
                capsule.Move(Vector3.right * 0.04f + Vector3.down * 0.004f);
            Assert.That(probe.transform.position.x,
                Is.LessThan(eastRear.bounds.min.x - capsule.radius + 0.14f),
                $"XR-sized capsule crossed the east property guard: {probe.transform.position}.");
            Assert.That(probe.transform.position.y, Is.GreaterThan(65.20f),
                "The capsule dropped through the east garden while approaching its boundary.");
            Object.Destroy(probe);
        }

        static Transform[] WalkableFloorGroups(Transform root)
        {
            var paths = new[]
            {
                "GroundFloor/Floors", "UpperFloor/Floors", "Exterior/Terrace",
                "Exterior/Garden", "Exterior/PoolDeck", "Exterior/Cabin", "Exterior/Transitions"
            };
            var groups = paths.Select(root.Find).ToArray();
            Assert.That(groups.All(group => group != null), Is.True,
                "A required walkable collision group is missing.");
            return groups;
        }

        static BoxCollider AssertSolidBox(Transform root, string path)
        {
            var box = root.Find(path)?.GetComponent<BoxCollider>();
            Assert.That(box, Is.Not.Null, $"Missing Collisions/{path}.");
            Assert.That(box.gameObject.activeInHierarchy && box.enabled && !box.isTrigger,
                Is.True, $"Collisions/{path} must be an active, solid BoxCollider.");
            return box;
        }

        static void AssertSupport(Transform[] floorGroups, float x, float z,
            float expectedY, float tolerance, string label)
        {
            var hits = Physics.RaycastAll(new Vector3(x, expectedY + 2f, z), Vector3.down,
                    3f, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => hit.normal.y > 0.7f &&
                    floorGroups.Any(group => hit.collider.transform.IsChildOf(group)))
                .OrderByDescending(hit => hit.point.y).ToArray();
            Assert.That(hits, Is.Not.Empty,
                $"No walkable collision floor supports {label} at ({x:F2}, {z:F2}).");
            var surfaceY = hits[0].point.y;
            Assert.That(surfaceY, Is.EqualTo(expectedY).Within(tolerance),
                $"Collision floor for {label} is not aligned with the visible surface. " +
                $"Collider={hits[0].collider.name}; actualY={surfaceY:F3}; expectedY={expectedY:F3}.");
        }
    }
}
