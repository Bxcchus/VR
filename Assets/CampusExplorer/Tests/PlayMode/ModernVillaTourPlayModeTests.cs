using System.Collections;
using System.Linq;
using System.Reflection;
using CampusExplorer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace CampusExplorer.Tests.PlayMode
{
    public sealed class ModernVillaTourPlayModeTests
    {
        [UnityTest]
        public IEnumerator CollisionHelpersAreActiveAtRuntime()
        {
            yield return SceneManager.LoadSceneAsync("ModernVillaTour", LoadSceneMode.Single);
            // The XR spawn guard deliberately waits up to three seconds for a
            // Quest Link display before selecting the simulator fallback. Audit
            // the settled runtime state rather than the transient startup pose.
            yield return new WaitForSecondsRealtime(5f);

            var collisions = GameObject.Find("Collisions");
            var origin = Object.FindAnyObjectByType<XROrigin>();
            var controller = origin != null ? origin.Origin.GetComponent<CharacterController>() : null;
            Assert.That(collisions, Is.Not.Null);
            Assert.That(origin, Is.Not.Null);
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.gameObject.activeInHierarchy, Is.True);
            Assert.That(controller.enabled, Is.True);
            Assert.That(controller.detectCollisions, Is.True);
            Assert.That(controller.height, Is.InRange(1.0f, 2.1f),
                "The settled runtime CharacterController height must be physically valid.");
            Assert.That(Mathf.Abs(controller.center.x), Is.LessThan(2f));
            Assert.That(Mathf.Abs(controller.center.z), Is.LessThan(2f));

            var generated = collisions.GetComponentsInChildren<Collider>(true);
            Assert.That(generated.Length, Is.EqualTo(151));
            Assert.That(generated.All(item => item is BoxCollider), Is.True,
                "The generated runtime hierarchy must contain only BoxColliders.");
            Assert.That(generated.All(item => item.gameObject.activeInHierarchy), Is.True,
                "Every generated collider GameObject must be active in Play Mode.");
            Assert.That(generated.All(item => item.enabled), Is.True,
                "Every generated collider component must be enabled in Play Mode.");
            Assert.That(generated.OfType<BoxCollider>().All(item => item.enabled), Is.True,
                "Every generated BoxCollider must be enabled in Play Mode.");
            Assert.That(generated.All(item => !item.isTrigger), Is.True,
                "Environment navigation colliders must be solid, not triggers.");

            var environmentLayers = generated.Select(item => item.gameObject.layer).Distinct().ToArray();
            Assert.That(environmentLayers, Has.Length.EqualTo(1));
            var environmentLayer = environmentLayers[0];
            var playerLayer = controller.gameObject.layer;
            Assert.That(Physics.GetIgnoreLayerCollision(playerLayer, environmentLayer), Is.False,
                $"Physics matrix disables {LayerMask.LayerToName(playerLayer)} -> {LayerMask.LayerToName(environmentLayer)}.");
            Debug.Log($"[Collision Runtime] Environment layer={LayerMask.LayerToName(environmentLayer)} ({environmentLayer}); " +
                      $"XR CharacterController layer={LayerMask.LayerToName(playerLayer)} ({playerLayer}); matrix=COLLIDE.");

            var debug = collisions.GetComponent<CollisionDebugVisualizer>();
            Assert.That(debug, Is.Not.Null);
            Assert.That(debug.ToggleKey, Is.EqualTo(Key.F8));
            debug.Toggle(); // Same method invoked by F8 in CollisionDebugVisualizer.Update.
            Assert.That(debug.IsVisible, Is.True, "F8 collision-helper state must be available during runtime testing.");
            var interactiveDoorColliders = GameObject.Find("InteractiveDoors")
                .GetComponentsInChildren<BoxCollider>(true);
            Assert.That(interactiveDoorColliders.Length, Is.EqualTo(16),
                "F8 must include the twelve hinged leaves and four sliding glass panels.");
            Assert.That(collisions.GetComponentsInChildren<Renderer>(true)
                .Count(renderer => renderer.name.StartsWith("Debug - ")),
                Is.EqualTo(generated.Length + interactiveDoorColliders.Length));

            var guard = origin.GetComponents<MonoBehaviour>()
                .FirstOrDefault(component => component.GetType().Name == "ModernVillaXRSpawnGuard");
            if (guard != null)
                guard.enabled = false;
            foreach (var locomotion in origin.GetComponentsInChildren<LocomotionProvider>(true))
                locomotion.enabled = false;

            Physics.SyncTransforms();
            AssertRuntimeObstacle(controller, collisions, "G Outer North 01", 64.87821f, "wall");
            AssertRuntimeObstacle(controller, collisions, "Kitchen Island", 64.87821f, "kitchen island");
            AssertRuntimeObstacle(controller, collisions, "Dining Table", 64.87821f, "dining table");
            AssertRuntimeObstacle(controller, collisions, "Sectional Sofa", 64.87821f, "sofa");
            var interactiveDoor = FindInteractiveDoor("Door_Interior_08");
            Assert.That(interactiveDoor, Is.Not.Null);
            interactiveDoor.ToggleDoor();
            yield return WaitForDoor(interactiveDoor);
            AssertRuntimeDoorPassage(controller, new Vector3(-1.182f, 64.89821f, -27.435f), Vector3.back,
                "Kitchen / dining doorway after opening its interactive door");
            AssertRuntimeObstacle(controller, collisions, "Pool North Boundary", 64.87821f, "pool boundary");
            AssertRuntimeObstacle(controller, collisions, "Stair Void East Guard", 68.10122f, "upper-floor edge guard");
            AssertWardrobeAislePassage(controller, collisions);

            debug.Toggle();
            Assert.That(debug.IsVisible, Is.False, "Collision debug helpers must be off after the runtime audit.");
        }

        [UnityTest]
        public IEnumerator VillaTourContainsWorkingFirstIncrement()
        {
            yield return SceneManager.LoadSceneAsync("ModernVillaTour", LoadSceneMode.Single);
            // Let the asynchronous headset/simulator spawn guard settle before
            // validating teleports; otherwise it can reset the origin mid-test.
            yield return new WaitForSecondsRealtime(5f);

            Assert.That(GameObject.Find("Authoritative Modern Luxury Villa - Identity Transform"), Is.Not.Null);
            var origin = Object.FindAnyObjectByType<XROrigin>();
            var simulator = Object.FindAnyObjectByType<XRInteractionSimulator>();
            Assert.That(origin, Is.Not.Null);
            Assert.That(simulator, Is.Not.Null);
            Assert.That(simulator.isActiveAndEnabled, Is.True);
            Assert.That(InputSystem.devices.OfType<XRHMD>().Any(), Is.True);
            Assert.That(InputSystem.devices.OfType<XRController>().Any(), Is.True);
            var teleportAreas = Object.FindObjectsByType<TeleportationArea>(FindObjectsInactive.Include);
            Assert.That(teleportAreas.Length, Is.GreaterThanOrEqualTo(10));

            var collisions = GameObject.Find("Collisions");
            Assert.That(collisions, Is.Not.Null);
            var expectedCollisionGroups = new[]
            {
                "GroundFloor/Floors", "GroundFloor/Walls", "GroundFloor/Doors",
                "GroundFloor/Kitchen", "GroundFloor/LivingRoom", "GroundFloor/DiningRoom",
                "GroundFloor/Bathrooms", "GroundFloor/Staircase",
                "UpperFloor/Floors", "UpperFloor/Walls", "UpperFloor/Doors",
                "UpperFloor/Bedrooms", "UpperFloor/Bathrooms", "UpperFloor/Railings",
                "Exterior/Terrace", "Exterior/Garden", "Exterior/PoolDeck", "Exterior/Pool",
                "Exterior/Cabin", "Exterior/Transitions", "Exterior/Boundaries"
            };
            foreach (var groupName in expectedCollisionGroups)
                Assert.That(collisions.transform.Find(groupName), Is.Not.Null, $"Missing Collisions/{groupName}.");
            Assert.That(collisions.transform.Find("GroundFloor/Staircase/Left Stair Edge Guard"), Is.Null);
            Assert.That(collisions.transform.Find("GroundFloor/Staircase/Right Stair Edge Guard"), Is.Null);
            Assert.That(collisions.transform.Find("GroundFloor/Kitchen/Base Cabinets"), Is.Null,
                "The former room-sized aggregate kitchen collider must stay removed.");
            Assert.That(collisions.transform.Find("UpperFloor/Railings/Stair Void East Guard"), Is.Not.Null);
            Assert.That(collisions.transform.Find("UpperFloor/Railings/Stair Void West Guard"), Is.Not.Null);

            var debug = collisions.GetComponent<CollisionDebugVisualizer>();
            Assert.That(debug, Is.Not.Null);
            Assert.That(debug.IsVisible, Is.False, "Collision debug visualization must be off by default.");
            debug.SetVisible(true);
            Assert.That(debug.IsVisible, Is.True);
            Assert.That(collisions.GetComponentsInChildren<Renderer>(true)
                .Count(renderer => renderer.name.StartsWith("Debug - ")), Is.GreaterThanOrEqualTo(50));
            debug.SetVisible(false);
            Assert.That(debug.IsVisible, Is.False, "Collision debug visualization must return to its production-off state.");

            var generatedColliders = collisions.GetComponentsInChildren<Collider>(true);
            Assert.That(generatedColliders.Length, Is.GreaterThanOrEqualTo(50));
            Assert.That(generatedColliders.All(collider => collider is BoxCollider || collider is CapsuleCollider), Is.True);
            Assert.That(collisions.GetComponentsInChildren<MeshCollider>(true), Is.Empty);
            Assert.That(collisions.transform.Find("Exterior/Pool").GetComponentsInChildren<TeleportationArea>(true), Is.Empty);
            Assert.That(collisions.transform.Find("Exterior/Garden").GetComponentsInChildren<BoxCollider>(true).Length,
                Is.EqualTo(10));
            Assert.That(collisions.transform.Find("Exterior/Garden").GetComponentsInChildren<TeleportationArea>(true).Length,
                Is.EqualTo(10));
            Assert.That(collisions.transform.Find("Exterior/PoolDeck").GetComponentsInChildren<TeleportationArea>(true).Length,
                Is.EqualTo(4));
            Assert.That(collisions.transform.Find("Exterior/Cabin").GetComponentsInChildren<TeleportationArea>(true).Length,
                Is.EqualTo(2));
            Assert.That(collisions.transform.Find("Exterior/Transitions").GetComponentsInChildren<TeleportationArea>(true).Length,
                Is.EqualTo(4));
            Assert.That(collisions.transform.Find("GroundFloor/Kitchen").GetComponentsInChildren<TeleportationArea>(true), Is.Empty);
            Assert.That(teleportAreas.All(area =>
            {
                var path = HierarchyPath(area.transform);
                return path.Contains("/Floors/") ||
                       path.Contains("/Exterior/Terrace/") ||
                       path.Contains("/Exterior/Garden/") ||
                       path.Contains("/Exterior/PoolDeck/") ||
                       path.Contains("/Exterior/Cabin/") ||
                       path.Contains("/Exterior/Transitions/");
            }), Is.True, "TeleportationArea components must only exist on deliberate walkable floor colliders.");

            var points = Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include)
                .OrderBy(item => item.PointId).ToArray();
            Assert.That(points.Select(item => item.PointId), Is.EqualTo(new[]
            {
                "bathroom", "dining-room", "kitchen", "living-room", "master-bedroom", "pool-terrace"
            }));
            Assert.That(points.Select(item => item.Title), Is.EquivalentTo(new[]
            {
                "Kitchen", "Living Room", "Dining Room", "Master Bedroom", "Bathroom", "Pool / Terrace"
            }));
            Assert.That(Object.FindObjectsByType<InfoPanel>(FindObjectsInactive.Include).Length, Is.EqualTo(6));
            Assert.That(EventSystem.current, Is.Not.Null);
            Assert.That(EventSystem.current.GetComponent<XRUIInputModule>(), Is.Not.Null,
                "Quest Touch UI rays require XRUIInputModule on the active EventSystem.");
            foreach (var point in points)
            {
                var interactable = point.GetComponent<XRSimpleInteractable>();
                var markerButton = point.GetComponent<Button>();
                var panel = point.Panel;
                Assert.That(interactable, Is.Not.Null);
                Assert.That(interactable.isActiveAndEnabled, Is.True);
                var triggerAction = (InputAction)typeof(PointOfInterest)
                    .GetField("triggerAction", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(point);
                Assert.That(triggerAction, Is.Not.Null);
                Assert.That(triggerAction.enabled, Is.True);
                Assert.That(triggerAction.bindings.Select(binding => binding.path), Does.Contain(
                    "<XRController>{LeftHand}/triggerPressed"));
                Assert.That(triggerAction.bindings.Select(binding => binding.path), Does.Contain(
                    "<XRController>{RightHand}/triggerPressed"));
                var gripAction = (InputAction)typeof(PointOfInterest)
                    .GetField("gripAction", BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.GetValue(point);
                Assert.That(gripAction, Is.Not.Null);
                Assert.That(gripAction.enabled, Is.True);
                Assert.That(gripAction.bindings.Select(binding => binding.path), Does.Contain(
                    "<XRController>{LeftHand}/{GripButton}"));
                Assert.That(gripAction.bindings.Select(binding => binding.path), Does.Contain(
                    "<XRController>{RightHand}/{GripButton}"));
                Assert.That(point.GetComponent<Collider>(), Is.Null,
                    $"{point.PointId} must not add a physical navigation obstacle.");
                Assert.That(markerButton, Is.Not.Null);
                Assert.That(markerButton.interactable, Is.True);
                Assert.That(point.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Not.Null,
                    $"{point.PointId} marker must be reachable by a Quest Touch UI ray.");
                Assert.That(panel, Is.Not.Null);
                Assert.That(panel.gameObject.scene.name, Is.EqualTo("ModernVillaTour"));
                Assert.That(panel.GetComponentInChildren<TrackedDeviceGraphicRaycaster>(true), Is.Not.Null);
                Assert.That(panel.CloseButton, Is.Not.Null);
                Assert.That(panel.IsOpen, Is.False);

                markerButton.onClick.Invoke();
                yield return null;
                Assert.That(panel.IsOpen, Is.True, $"Selecting {point.PointId} must open its panel.");
                Assert.That(panel.TitleText.text, Is.EqualTo(point.Title));
                Assert.That(panel.BodyText.text, Is.EqualTo(point.Body));

                panel.CloseButton.onClick.Invoke();
                yield return null;
                Assert.That(panel.IsOpen, Is.False, $"The Close button must close {point.PointId}.");

                if (point.PointId == "master-bedroom")
                {
                    var pointer = new PointerEventData(EventSystem.current) { pointerId = 17 };
                    point.OnPointerEnter(pointer);
                    typeof(PointOfInterest).GetMethod("OnGripPressed", BindingFlags.Instance | BindingFlags.NonPublic)
                        ?.Invoke(point, new object[] { default(InputAction.CallbackContext) });
                    Assert.That(panel.IsOpen, Is.True, "Grip on the hovered bedroom marker must open its panel.");
                    markerButton.onClick.Invoke();
                    Assert.That(panel.IsOpen, Is.True,
                        "A second UI event must not immediately hide the opened panel.");
                    Assert.That(panel.PanelRoot.transform.position.x, Is.LessThan(1.75f),
                        "The bedroom panel must sit on the room side of the wardrobe cabinet.");
                    point.OnPointerExit(pointer);
                    panel.CloseButton.onClick.Invoke();
                    Assert.That(panel.IsOpen, Is.False);
                }
            }

            var menus = Object.FindObjectsByType<XRRoomMenu>(FindObjectsInactive.Include);
            Assert.That(menus, Has.Length.EqualTo(1));
            var roomMenu = menus[0];
            Assert.That(roomMenu.IsOpen, Is.False);
            Assert.That(roomMenu.RoomNames, Is.EqualTo(new[]
            {
                "Kitchen", "Living Room", "Dining Room", "Master Bedroom", "Bathroom", "Pool / Terrace"
            }));
            Assert.That(roomMenu.RoomButtons, Has.Length.EqualTo(6));
            Assert.That(roomMenu.RoomButtons.All(button => button != null && button.interactable), Is.True);
            Assert.That(roomMenu.CloseButton, Is.Not.Null);
            Assert.That(roomMenu.MenuRoot.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Not.Null);
            Assert.That(roomMenu.GetComponentsInChildren<Collider>(true), Is.Empty,
                "The XR menu must not block locomotion.");

            var menuAction = (InputAction)typeof(XRRoomMenu)
                .GetField("toggleAction", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(roomMenu);
            Assert.That(menuAction, Is.Not.Null);
            Assert.That(menuAction.enabled, Is.True);
            Assert.That(menuAction.bindings.Select(binding => binding.path), Does.Contain(
                "<XRController>{LeftHand}/menuButton"));
            Assert.That(menuAction.bindings.Select(binding => binding.path), Does.Contain("<Keyboard>/m"));

            roomMenu.Open();
            yield return null;
            Assert.That(roomMenu.IsOpen, Is.True);
            Assert.That(roomMenu.LastPlacementClearance, Is.GreaterThan(0.45f),
                "The menu must choose a direction with enough free space.");
            roomMenu.Open();
            Assert.That(Object.FindObjectsByType<XRRoomMenu>(FindObjectsInactive.Include), Has.Length.EqualTo(1),
                "Opening the menu twice must not create a second instance.");
            roomMenu.CloseButton.onClick.Invoke();
            yield return null;
            Assert.That(roomMenu.IsOpen, Is.False);

            var provider = Object.FindAnyObjectByType<TeleportationProvider>();
            Assert.That(provider, Is.Not.Null);
            var spawnGuard = origin.GetComponents<MonoBehaviour>()
                .FirstOrDefault(component => component.GetType().Name == "ModernVillaXRSpawnGuard");
            if (spawnGuard != null)
                spawnGuard.enabled = false;

            var roomTeleporter = Object.FindAnyObjectByType<XRRoomTeleporter>();
            Assert.That(roomTeleporter, Is.Not.Null);
            Assert.That(roomTeleporter.RoomNames, Is.EqualTo(roomMenu.RoomNames));
            Assert.That(roomTeleporter.RoomAnchors, Has.Length.EqualTo(6));
            Assert.That(roomTeleporter.RoomAnchors.All(anchor => anchor != null), Is.True);
            Assert.That(roomTeleporter.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(roomTeleporter.FadeGroup, Is.Not.Null);
            var trackedCameraLocalY = origin.Camera.transform.localPosition.y;
            var trackedControllerCount = InputSystem.devices.OfType<XRController>().Count();

            for (var index = 0; index < roomMenu.RoomButtons.Length; index++)
            {
                roomMenu.Open();
                roomMenu.RoomButtons[index].onClick.Invoke();
                Assert.That(roomMenu.LastSelectedRoom, Is.EqualTo(roomMenu.RoomNames[index]));
                var timeout = Time.realtimeSinceStartup + 3f;
                while (roomTeleporter.IsTeleporting && Time.realtimeSinceStartup < timeout)
                    yield return null;
                Assert.That(roomTeleporter.IsTeleporting, Is.False,
                    $"{roomMenu.RoomNames[index]} teleport must complete.");
                Assert.That(roomTeleporter.LastTeleportedRoom, Is.EqualTo(roomMenu.RoomNames[index]));
                Assert.That(roomTeleporter.LastFadeReachedBlack, Is.True);
                Assert.That(roomTeleporter.FadeGroup.alpha, Is.EqualTo(0f).Within(0.01f));
                Assert.That(roomMenu.IsOpen, Is.False);
                var anchor = roomTeleporter.RoomAnchors[index];
                var cameraPlanarDistance = Vector2.Distance(
                    new Vector2(origin.Camera.transform.position.x, origin.Camera.transform.position.z),
                    new Vector2(anchor.position.x, anchor.position.z));
                Assert.That(cameraPlanarDistance, Is.LessThan(0.18f),
                    $"The tracked head must arrive on the {roomMenu.RoomNames[index]} anchor.");
                Assert.That(origin.Camera.transform.localPosition.y, Is.EqualTo(trackedCameraLocalY).Within(0.01f),
                    "Room teleportation must preserve the HMD local height.");
                Assert.That(InputSystem.devices.OfType<XRController>().Count(), Is.EqualTo(trackedControllerCount),
                    "Room teleportation must keep both tracked controllers.");
            }

            var start = origin.transform.position;
            Assert.That(provider.QueueTeleportRequest(new TeleportRequest
            {
                destinationPosition = new Vector3(-2.95f, 64.91f, -21.15f),
                destinationRotation = Quaternion.identity,
                matchOrientation = MatchOrientation.WorldSpaceUp,
                requestTime = Time.time
            }), Is.True);
            yield return null;
            yield return null;
            Assert.That(Vector3.Distance(start, origin.transform.position), Is.GreaterThan(1f));

            Physics.SyncTransforms();
            var doorwayBottom = new Vector3(-1.182f, 65.19821f, -27.435f);
            var doorwayTop = new Vector3(-1.182f, 66.35821f, -27.435f);
            var doorwayBlockers = Physics.OverlapCapsule(doorwayBottom, doorwayTop, 0.28f)
                .Where(collider => collider.transform.IsChildOf(collisions.transform))
                .ToArray();
            Assert.That(doorwayBlockers, Is.Empty, "The kitchen/dining doorway must remain clear for the XR capsule.");

            AssertGeneratedCollision(collisions, new Vector3(-2.50f, 65.65f, -26.70f), Vector3.back, 1.2f, "ground-floor wall");
            AssertGeneratedCollision(collisions, new Vector3(-0.68f, 65.35f, -23.50f), Vector3.back, 1.5f, "kitchen island");
            AssertGeneratedCollision(collisions, new Vector3(-9.70f, 65.35f, -23.70f), Vector3.back, 2.0f, "living-room sofa");
            AssertGeneratedCollision(collisions, new Vector3(-1.20f, 65.45f, -29.30f), Vector3.back, 2.0f, "dining table");
            AssertGeneratedCollision(collisions, new Vector3(3.48f, 68.75f, -23.00f), Vector3.back, 2.8f, "bedroom bed");
            AssertGeneratedCollision(collisions, new Vector3(-9.54f, 68.45f, -32.80f), Vector3.back, 3.6f, "bathtub");
            AssertGeneratedCollision(collisions, new Vector3(-0.25f, 65.75f, -57.70f), Vector3.back, 1.2f, "pool boundary");
            AssertWalkableTerraceGardenTransition(collisions);
            AssertGeneratedCollision(collisions, new Vector3(-7.20f, 68.75f, -29.45f), Vector3.left, 1.2f, "upper stair-void guard");

            var controller = origin.Origin.GetComponent<CharacterController>();
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.slopeLimit, Is.EqualTo(45f).Within(0.01f));
            Assert.That(controller.stepOffset, Is.EqualTo(0.30f).Within(0.01f));
            Assert.That(controller.skinWidth, Is.EqualTo(0.025f).Within(0.005f));

            foreach (var locomotion in origin.GetComponentsInChildren<LocomotionProvider>(true))
                locomotion.enabled = false;

            var interactiveDoors = Object.FindObjectsByType<XRInteractiveDoor>(FindObjectsInactive.Include);
            Assert.That(interactiveDoors, Has.Length.EqualTo(12));
            foreach (var interactiveDoor in interactiveDoors)
            {
                interactiveDoor.ToggleDoor();
                yield return WaitForDoor(interactiveDoor);
            }
            var slidingDoors = Object.FindObjectsByType<XRInteractiveSlidingDoor>(FindObjectsInactive.Include);
            Assert.That(slidingDoors, Has.Length.EqualTo(2));
            foreach (var slidingDoor in slidingDoors)
            {
                slidingDoor.ToggleDoor();
                yield return WaitForSlidingDoor(slidingDoor);
            }

            var doorways = new[]
            {
                ("Kitchen / dining", new Vector3(-1.182f, 64.87821f, -27.435f), Vector3.back),
                ("Living / hall", new Vector3(-7.558f, 64.87821f, -27.435f), Vector3.back),
                ("Kitchen / living", new Vector3(-3.620f, 64.87821f, -24.753f), Vector3.right),
                ("Dining / hall", new Vector3(-6.123f, 64.87821f, -30.474f), Vector3.right),
                ("Hall / bathroom", new Vector3(-7.558f, 64.87821f, -32.321f), Vector3.back),
                ("Foyer / living north archway", new Vector3(-5.0225f, 64.87821f, -22.114f), Vector3.forward),
                ("Kitchen / garage north opening", new Vector3(-2.6765f, 64.87821f, -22.114f), Vector3.forward),
                ("Ground exterior entry", new Vector3(-5.116f, 64.87821f, -17.970f), Vector3.back),
                ("Living exterior sliding door", new Vector3(-13.182f, 64.87821f, -24.807f), Vector3.left),
                ("Upper balcony", new Vector3(-7.882f, 68.10122f, -21.950f), Vector3.back),
                ("Upper bedroom 01", new Vector3(-1.538f, 68.10122f, -27.426f), Vector3.back),
                ("Upper bedroom 02", new Vector3(1.935f, 68.10122f, -27.426f), Vector3.back),
                ("Upper east bedroom", new Vector3(-4.463f, 68.10122f, -23.665f), Vector3.right),
                ("Upper bathroom 01", new Vector3(-5.807f, 68.10122f, -28.731f), Vector3.right),
                ("Upper bathroom 02", new Vector3(-5.807f, 68.10122f, -31.939f), Vector3.right),
                ("Upper bathroom 03", new Vector3(-5.807f, 68.10122f, -36.969f), Vector3.right)
            };
            foreach (var doorway in doorways)
                AssertCharacterControllerPassage(controller, doorway.Item2, doorway.Item3, doorway.Item1);

            AssertStairTraversal(controller);
        }

        [UnityTest]
        public IEnumerator GuidedTourUsesExistingRoomTeleportation()
        {
            yield return SceneManager.LoadSceneAsync("ModernVillaTour", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(5f);

            var origin = Object.FindAnyObjectByType<XROrigin>();
            var menu = Object.FindAnyObjectByType<XRRoomMenu>();
            var teleporter = Object.FindAnyObjectByType<XRRoomTeleporter>();
            var tour = Object.FindAnyObjectByType<XRGuidedTour>();
            Assert.That(origin, Is.Not.Null);
            Assert.That(menu, Is.Not.Null);
            Assert.That(teleporter, Is.Not.Null);
            Assert.That(tour, Is.Not.Null);
            Assert.That(menu.GuidedTourButton, Is.Not.Null);
            Assert.That(tour.StepCount, Is.EqualTo(6));
            Assert.That(tour.RoomSequence, Is.EqualTo(new[]
            {
                "Kitchen", "Living Room", "Dining Room", "Master Bedroom", "Bathroom", "Pool / Terrace"
            }));
            Assert.That(tour.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(tour.PanelRoot.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Not.Null);
            Assert.That(tour.PanelRoot.activeSelf, Is.False);

            var spawnGuard = origin.GetComponents<MonoBehaviour>()
                .FirstOrDefault(component => component.GetType().Name == "ModernVillaXRSpawnGuard");
            if (spawnGuard != null)
                spawnGuard.enabled = false;

            var trackedCameraLocalY = origin.Camera.transform.localPosition.y;
            var trackedControllerCount = InputSystem.devices.OfType<XRController>().Count();
            menu.Open();
            menu.GuidedTourButton.onClick.Invoke();
            Assert.That(menu.IsOpen, Is.False);
            yield return WaitForTeleport(teleporter);
            Assert.That(tour.IsTourActive, Is.True);
            Assert.That(tour.IsComplete, Is.False);
            Assert.That(tour.CurrentStep, Is.EqualTo(0));
            Assert.That(tour.CurrentRoom, Is.EqualTo("Kitchen"));
            Assert.That(tour.PanelRoot.activeSelf, Is.True);
            Assert.That(tour.TourContent.activeSelf, Is.True);
            Assert.That(tour.CompletionContent.activeSelf, Is.False);
            Assert.That(tour.PreviousButton.interactable, Is.False);
            Assert.That(tour.LastPlacementClearance, Is.GreaterThan(0.45f));

            for (var expectedStep = 1; expectedStep < tour.StepCount; expectedStep++)
            {
                tour.NextButton.onClick.Invoke();
                yield return WaitForTeleport(teleporter);
                Assert.That(tour.CurrentStep, Is.EqualTo(expectedStep));
                Assert.That(tour.CurrentRoom, Is.EqualTo(tour.RoomSequence[expectedStep]));
                Assert.That(teleporter.LastTeleportedRoom, Is.EqualTo(tour.RoomSequence[expectedStep]));
                Assert.That(tour.PanelRoot.activeSelf, Is.True);
                Assert.That(tour.PreviousButton.interactable, Is.True);
                Assert.That(origin.Camera.transform.localPosition.y, Is.EqualTo(trackedCameraLocalY).Within(0.01f));
                Assert.That(InputSystem.devices.OfType<XRController>().Count(), Is.EqualTo(trackedControllerCount));

                if (expectedStep == 4)
                {
                    var stepLabel = tour.TourContent.transform.Find("Step")?.GetComponent<Text>();
                    var roomLabel = tour.TourContent.transform.Find("Room Name")?.GetComponent<Text>();
                    var descriptionLabel = tour.TourContent.transform.Find("Description")?.GetComponent<Text>();
                    Assert.That(stepLabel, Is.Not.Null);
                    Assert.That(roomLabel, Is.Not.Null);
                    Assert.That(descriptionLabel, Is.Not.Null);
                    Assert.That(stepLabel.text, Is.EqualTo("Step 5 / 6"));
                    Assert.That(roomLabel.text, Is.EqualTo("BATHROOM"));
                    Assert.That(descriptionLabel.text, Is.EqualTo(
                        "Modern bathroom combining dark finishes, glass surfaces and dedicated washing and shower areas."));
                    Assert.That(tour.TourContent.activeSelf, Is.True);
                    Assert.That(tour.CompletionContent.activeSelf, Is.False);
                    Assert.That(tour.PanelRoot.activeSelf, Is.True);
                    Assert.That(tour.LastPlacementClearance, Is.GreaterThan(1.30f),
                        "Bathroom panel must use the measured clear side of the room.");
                    var cameraToPanel = Vector3.ProjectOnPlane(
                        tour.PanelRoot.transform.position - origin.Camera.transform.position, Vector3.up);
                    Assert.That(cameraToPanel.magnitude, Is.EqualTo(0.80f).Within(0.03f));
                    Assert.That(Vector3.Dot(cameraToPanel.normalized, tour.PanelRoot.transform.forward),
                        Is.GreaterThan(0.99f), "Bathroom panel must face back toward the viewer.");

                    tour.PreviousButton.onClick.Invoke();
                    yield return WaitForTeleport(teleporter);
                    Assert.That(tour.CurrentStep, Is.EqualTo(3));
                    Assert.That(tour.CurrentRoom, Is.EqualTo("Master Bedroom"));
                    tour.NextButton.onClick.Invoke();
                    yield return WaitForTeleport(teleporter);
                    Assert.That(tour.CurrentStep, Is.EqualTo(4));
                    Assert.That(tour.CurrentRoom, Is.EqualTo("Bathroom"));

                    tour.ExitButton.onClick.Invoke();
                    yield return null;
                    Assert.That(tour.IsTourActive, Is.False);
                    Assert.That(tour.PanelRoot.activeSelf, Is.False);

                    tour.StartTour();
                    yield return WaitForTeleport(teleporter);
                    for (var replayStep = 1; replayStep <= 4; replayStep++)
                    {
                        tour.NextButton.onClick.Invoke();
                        yield return WaitForTeleport(teleporter);
                    }
                    Assert.That(tour.CurrentStep, Is.EqualTo(4));
                    Assert.That(tour.CurrentRoom, Is.EqualTo("Bathroom"));
                }
            }

            tour.NextButton.onClick.Invoke();
            yield return null;
            Assert.That(tour.IsComplete, Is.True);
            Assert.That(tour.TourContent.activeSelf, Is.False);
            Assert.That(tour.CompletionContent.activeSelf, Is.True);
            Assert.That(teleporter.LastTeleportedRoom, Is.EqualTo("Pool / Terrace"));

            tour.FreeExplorationButton.onClick.Invoke();
            yield return null;
            Assert.That(tour.IsTourActive, Is.False);
            Assert.That(tour.PanelRoot.activeSelf, Is.False);
            Assert.That(teleporter.LastTeleportedRoom, Is.EqualTo("Pool / Terrace"));

            tour.RestartTour();
            yield return WaitForTeleport(teleporter);
            Assert.That(tour.IsTourActive, Is.True);
            Assert.That(tour.IsComplete, Is.False);
            Assert.That(tour.CurrentStep, Is.EqualTo(0));
            Assert.That(teleporter.LastTeleportedRoom, Is.EqualTo("Kitchen"));

            tour.ExitButton.onClick.Invoke();
            yield return null;
            Assert.That(tour.IsTourActive, Is.False);
            Assert.That(tour.PanelRoot.activeSelf, Is.False);
        }

        [UnityTest]
        public IEnumerator InteractiveLightSwitchesToggleRealtimeAndFixtureEmission()
        {
            yield return SceneManager.LoadSceneAsync("ModernVillaTour", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(5f);

            var root = GameObject.Find("InteractiveLights");
            Assert.That(root, Is.Not.Null);
            Assert.That(root.transform.Find("KitchenLights"), Is.Not.Null);
            Assert.That(root.transform.Find("LivingRoomLights"), Is.Not.Null);
            Assert.That(root.transform.Find("MasterBedroomLights"), Is.Not.Null);
            Assert.That(root.GetComponentsInChildren<Light>(true), Has.Length.EqualTo(3),
                "There must be one minimal dedicated realtime light per interactive room.");

            var expectedMappings = new System.Collections.Generic.Dictionary<string, string[]>
            {
                ["Kitchen"] = new[] { "Lamp_Pendant_Kitchen_01", "Lamp_Pendant_Kitchen_02" },
                ["Living Room"] = new[] { "LED_TV_Console_Underlight", "Light_TV_Backlight_LED", "LED_TV_Backlight_01" },
                ["Master Bedroom"] = new[] { "LED_Nightstand_01", "LED_Nightstand_02", "LED_Headboard_Accent" },
            };
            var switches = root.GetComponentsInChildren<XRLightSwitch>(true)
                .OrderBy(item => item.RoomName).ToArray();
            Assert.That(switches, Has.Length.EqualTo(3));

            foreach (var lightSwitch in switches)
            {
                Assert.That(expectedMappings.ContainsKey(lightSwitch.RoomName), Is.True);
                Assert.That(lightSwitch.SwitchButton, Is.Not.Null);
                Assert.That(lightSwitch.GetComponent<TrackedDeviceGraphicRaycaster>(), Is.Not.Null,
                    $"{lightSwitch.RoomName} must be selectable by the Quest Touch UI ray.");
                Assert.That(lightSwitch.GetComponentsInChildren<Collider>(true), Is.Empty,
                    $"{lightSwitch.RoomName} switch must not affect player collision.");
                Assert.That(lightSwitch.EmissiveTargets.Select(target => target.renderer.name),
                    Is.EqualTo(expectedMappings[lightSwitch.RoomName]));
                Assert.That(lightSwitch.ControlledLights.All(light => light != null && light.enabled), Is.True);
                Assert.That(lightSwitch.ControlledLights.All(light =>
                    light.lightmapBakeType == LightmapBakeType.Realtime), Is.True);
                Assert.That(lightSwitch.ControlledLights.All(light =>
                    light.transform.IsChildOf(root.transform) && light.type == LightType.Point &&
                    light.shadows == LightShadows.None && light.renderMode == LightRenderMode.ForcePixel), Is.True,
                    "Controlled lights must be minimal Quest-compatible realtime point lights.");
                Assert.That(lightSwitch.EmissiveTargets.All(target =>
                    target.runtimeMaterial != null && target.runtimeMaterial != target.originalMaterial), Is.True,
                    "Every controlled fixture must use its own runtime material instance.");
                Assert.That(lightSwitch.IsOn, Is.True);
                Assert.That(lightSwitch.ToggleCount, Is.EqualTo(0));

                for (var cycle = 0; cycle < 3; cycle++)
                {
                    lightSwitch.SwitchButton.onClick.Invoke();
                    yield return null;
                    Assert.That(lightSwitch.IsOn, Is.False);
                    Assert.That(lightSwitch.ControlledLights.All(light => !light.enabled), Is.True);
                    Assert.That(lightSwitch.EmissiveTargets.All(target =>
                        target.runtimeMaterial.GetColor(target.emissionProperty).maxColorComponent <= 0.001f), Is.True,
                        "OFF must visibly remove fixture emission.");
                    Assert.That(lightSwitch.ToggleCount, Is.EqualTo(cycle * 2 + 1),
                        "One trigger press must cause exactly one toggle.");

                    lightSwitch.SwitchButton.onClick.Invoke();
                    yield return null;
                    Assert.That(lightSwitch.IsOn, Is.True);
                    Assert.That(lightSwitch.ControlledLights.All(light => light.enabled), Is.True);
                    Assert.That(lightSwitch.EmissiveTargets.All(target =>
                        target.runtimeMaterial.GetColor(target.emissionProperty).maxColorComponent > 0.01f), Is.True,
                        "ON must restore each fixture's authored emission.");
                    Assert.That(lightSwitch.ToggleCount, Is.EqualTo(cycle * 2 + 2),
                        "One trigger press must cause exactly one toggle.");
                }
            }

            Assert.That(Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include), Has.Length.EqualTo(6));
            Assert.That(Object.FindObjectsByType<XRRoomMenu>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XRGuidedTour>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XRRoomTeleporter>(FindObjectsInactive.Include), Has.Length.EqualTo(1));

            var origin = Object.FindAnyObjectByType<XROrigin>();
            var controller = origin != null ? origin.Origin.GetComponent<CharacterController>() : null;
            Assert.That(controller, Is.Not.Null);
            Assert.That(controller.height, Is.InRange(1.0f, 2.1f));
            Assert.That(GameObject.Find("Collisions")?.GetComponentsInChildren<Collider>(true),
                Has.Length.EqualTo(151));
        }

        [UnityTest]
        public IEnumerator InteractiveKitchenDoorAnimatesAndChangesPassageCollision()
        {
            yield return SceneManager.LoadSceneAsync("ModernVillaTour", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(5f);

            var allDoors = Object.FindObjectsByType<XRInteractiveDoor>(FindObjectsInactive.Include)
                .OrderBy(item => item.DoorMesh.name).ToArray();
            Assert.That(allDoors.Select(item => item.DoorMesh.name), Is.EqualTo(new[]
            {
                "Door_Interior_01.001", "Door_Interior_02", "Door_Interior_03.001", "Door_Interior_04",
                "Door_Interior_05", "Door_Interior_06", "Door_Interior_07", "Door_Interior_08",
                "Door_Interior_09", "Door_Interior_10", "Door_Interior_11", "Door_Interior_12"
            }));
            foreach (var configuredDoor in allDoors)
            {
                Assert.That(configuredDoor.DoorMesh.parent.name, Does.StartWith("Door_Frame_"));
                Assert.That(configuredDoor.DoorCollider, Is.Not.Null);
                Assert.That(configuredDoor.DoorCollider.enabled, Is.True);
                Assert.That(configuredDoor.DoorCollider.isTrigger, Is.False);
                Assert.That(configuredDoor.Interactable, Is.Not.Null);
                Assert.That(configuredDoor.Interactable.colliders, Does.Contain(configuredDoor.DoorCollider));
                Assert.That(configuredDoor.HasQuestTriggerBindings, Is.True);
                Assert.That(configuredDoor.GetComponent<Rigidbody>(), Is.Null);
                Assert.That(Mathf.Abs(configuredDoor.OpenAngle), Is.EqualTo(90f).Within(0.01f));
                Assert.That(configuredDoor.AnimationDuration, Is.EqualTo(0.6f).Within(0.01f));
            }

            var door = FindInteractiveDoor("Door_Interior_08");
            Assert.That(door, Is.Not.Null);
            Assert.That(door.DoorMesh.name, Is.EqualTo("Door_Interior_08"));
            Assert.That(door.DoorMesh.parent.name, Is.EqualTo("Door_Frame_08"),
                "The source FBX door must preserve its original hierarchy at runtime.");
            Assert.That(door.DoorCollider, Is.Not.Null);
            Assert.That(door.DoorCollider.enabled, Is.True);
            Assert.That(door.DoorCollider.isTrigger, Is.False);
            Assert.That(door.Interactable, Is.Not.Null);
            Assert.That(door.Interactable.colliders, Does.Contain(door.DoorCollider));
            Assert.That(door.HasQuestTriggerBindings, Is.True);
            Assert.That(door.GetComponent<Rigidbody>(), Is.Null);
            Assert.That(door.IsOpen, Is.False);

            var origin = Object.FindAnyObjectByType<XROrigin>();
            var controller = origin != null ? origin.Origin.GetComponent<CharacterController>() : null;
            Assert.That(controller, Is.Not.Null);
            var start = new Vector3(-1.19f, 64.89821f, -28.25f);
            var closedProgress = MoveControllerThroughDoor(controller, start, Vector3.forward, 90, 0.02f);
            Assert.That(closedProgress, Is.LessThan(0.9f), "The closed door must block the XR capsule.");

            var closedColliderCenter = door.DoorCollider.bounds.center;
            var closedRotation = door.transform.rotation;
            door.ToggleDoor();
            yield return WaitForDoor(door);
            Assert.That(door.IsOpen, Is.True);
            Assert.That(door.ToggleCount, Is.EqualTo(1));
            Assert.That(Quaternion.Angle(closedRotation, door.transform.rotation), Is.EqualTo(90f).Within(0.5f));
            Assert.That(Vector3.Distance(closedColliderCenter, door.DoorCollider.bounds.center), Is.GreaterThan(0.45f),
                "The solid collider must follow the opening leaf.");
            Assert.That(Vector3.Distance(door.DoorMesh.GetComponent<Renderer>().bounds.center,
                door.DoorCollider.bounds.center), Is.LessThan(0.15f),
                "Visual mesh and collider must move together.");

            var collisionRoot = GameObject.Find("Collisions");
            var openCenter = door.DoorCollider.transform.TransformPoint(door.DoorCollider.center);
            var openHalfExtents = Vector3.Scale(door.DoorCollider.size,
                door.DoorCollider.transform.lossyScale) * 0.45f;
            var openBlockers = Physics.OverlapBox(openCenter, openHalfExtents,
                    door.DoorCollider.transform.rotation, ~0, QueryTriggerInteraction.Ignore)
                .Where(collider => collider != door.DoorCollider &&
                                   collider.transform.IsChildOf(collisionRoot.transform))
                .ToArray();
            Assert.That(openBlockers, Is.Empty, "The opened door must not rotate through environment collision helpers.");

            var openProgress = MoveControllerThroughDoor(controller, start, Vector3.forward, 90, 0.02f);
            Assert.That(openProgress, Is.GreaterThan(1.45f), "The opened doorway must be traversable by the XR capsule.");

            door.ToggleDoor();
            yield return WaitForDoor(door);
            Assert.That(door.IsOpen, Is.False);
            Assert.That(door.ToggleCount, Is.EqualTo(2));
            var blockedAgain = MoveControllerThroughDoor(controller, start, Vector3.forward, 90, 0.02f);
            Assert.That(blockedAgain, Is.LessThan(0.9f), "Closing again must restore the doorway collision.");

            door.ToggleDoor();
            yield return WaitForDoor(door);
            Assert.That(door.IsOpen, Is.True);
            Assert.That(door.ToggleCount, Is.EqualTo(3));

            foreach (var otherDoor in allDoors.Where(item => item != door))
            {
                var closedCenter = otherDoor.DoorCollider.bounds.center;
                var otherClosedRotation = otherDoor.transform.rotation;
                otherDoor.ToggleDoor();
                yield return WaitForDoor(otherDoor);
                Assert.That(otherDoor.IsOpen, Is.True, $"{otherDoor.DoorMesh.name} must open.");
                Assert.That(Quaternion.Angle(otherClosedRotation, otherDoor.transform.rotation),
                    Is.EqualTo(90f).Within(0.5f));
                Assert.That(Vector3.Distance(closedCenter, otherDoor.DoorCollider.bounds.center),
                    Is.GreaterThan(0.35f), $"{otherDoor.DoorMesh.name} collider must follow its leaf.");
                Assert.That(Vector3.Distance(otherDoor.DoorMesh.GetComponent<Renderer>().bounds.center,
                    otherDoor.DoorCollider.bounds.center), Is.LessThan(0.18f),
                    $"{otherDoor.DoorMesh.name} visual and collider must remain aligned.");

                var otherOpenCenter = otherDoor.DoorCollider.transform.TransformPoint(otherDoor.DoorCollider.center);
                var otherOpenHalfExtents = Vector3.Scale(otherDoor.DoorCollider.size,
                    otherDoor.DoorCollider.transform.lossyScale) * 0.44f;
                var blockers = Physics.OverlapBox(otherOpenCenter, otherOpenHalfExtents,
                        otherDoor.DoorCollider.transform.rotation, ~0, QueryTriggerInteraction.Ignore)
                    .Where(collider => collider != otherDoor.DoorCollider &&
                                       collider.transform.IsChildOf(collisionRoot.transform))
                    .ToArray();
                if (otherDoor.DoorMesh.name == "Door_Interior_09")
                    Assert.That(blockers.All(item => item.name == "Traversable Stair Ramp"), Is.True,
                        "Door_Interior_09 may only touch the adjacent low-complexity walkable stair ramp.");
                else
                    Assert.That(blockers, Is.Empty,
                        $"{otherDoor.DoorMesh.name} must not open through an environment collision helper.");

                otherDoor.ToggleDoor();
                yield return WaitForDoor(otherDoor);
                Assert.That(otherDoor.IsOpen, Is.False, $"{otherDoor.DoorMesh.name} must close repeatedly.");
            }

            var slidingDoors = Object.FindObjectsByType<XRInteractiveSlidingDoor>(FindObjectsInactive.Include)
                .OrderBy(item => item.DoorId).ToArray();
            Assert.That(slidingDoors.Select(item => item.DoorId), Is.EqualTo(new[]
                { "Ground Patio Glass Doors", "Upper Balcony Glass Doors" }));
            foreach (var slidingDoor in slidingDoors)
            {
                Assert.That(slidingDoor.FirstPanel, Is.Not.Null);
                Assert.That(slidingDoor.SecondPanel, Is.Not.Null);
                Assert.That(slidingDoor.FirstCollider.enabled && !slidingDoor.FirstCollider.isTrigger, Is.True);
                Assert.That(slidingDoor.SecondCollider.enabled && !slidingDoor.SecondCollider.isTrigger, Is.True);
                Assert.That(slidingDoor.Interactable.colliders, Does.Contain(slidingDoor.FirstCollider));
                Assert.That(slidingDoor.Interactable.colliders, Does.Contain(slidingDoor.SecondCollider));
                Assert.That(slidingDoor.HasQuestTriggerBindings, Is.True);
                Assert.That(slidingDoor.GetComponent<Rigidbody>(), Is.Null);
                Assert.That(slidingDoor.AnimationDuration, Is.EqualTo(0.7f).Within(0.01f));

                var passageStart = slidingDoor.DoorId == "Ground Patio Glass Doors"
                    ? new Vector3(-12.25f, 64.89821f, -24.807f)
                    : new Vector3(-7.882f, 68.12122f, -21.10f);
                var passageDirection = slidingDoor.DoorId == "Ground Patio Glass Doors"
                    ? Vector3.left
                    : Vector3.back;
                var closedPassageProgress = MoveControllerThroughDoor(controller, passageStart,
                    passageDirection, 100, 0.02f);
                Assert.That(closedPassageProgress, Is.LessThan(0.9f),
                    $"{slidingDoor.DoorId} must block the XR capsule while closed.");

                var firstClosedPanel = slidingDoor.FirstPanel.position;
                var secondClosedPanel = slidingDoor.SecondPanel.position;
                slidingDoor.ToggleDoor();
                yield return WaitForSlidingDoor(slidingDoor);
                Assert.That(slidingDoor.IsOpen, Is.True);
                Assert.That(Vector3.Distance(firstClosedPanel, slidingDoor.FirstPanel.position),
                    Is.EqualTo(0.68f).Within(0.02f));
                Assert.That(Vector3.Distance(secondClosedPanel, slidingDoor.SecondPanel.position),
                    Is.EqualTo(0.68f).Within(0.02f));
                Assert.That(Vector3.Dot(slidingDoor.FirstOpenWorldOffset.normalized,
                    slidingDoor.SecondOpenWorldOffset.normalized), Is.EqualTo(-1f).Within(0.01f));
                Assert.That(Vector3.Distance(slidingDoor.FirstPanel.GetComponent<Renderer>().bounds.center,
                    slidingDoor.FirstCollider.bounds.center), Is.LessThan(0.08f));
                Assert.That(Vector3.Distance(slidingDoor.SecondPanel.GetComponent<Renderer>().bounds.center,
                    slidingDoor.SecondCollider.bounds.center), Is.LessThan(0.08f));
                var openPassageProgress = MoveControllerThroughDoor(controller, passageStart,
                    passageDirection, 100, 0.02f);
                Assert.That(openPassageProgress, Is.GreaterThan(1.55f),
                    $"{slidingDoor.DoorId} must become traversable while open.");

                slidingDoor.ToggleDoor();
                yield return WaitForSlidingDoor(slidingDoor);
                Assert.That(slidingDoor.IsOpen, Is.False);
                Assert.That(Vector3.Distance(firstClosedPanel, slidingDoor.FirstPanel.position), Is.LessThan(0.01f));
                Assert.That(Vector3.Distance(secondClosedPanel, slidingDoor.SecondPanel.position), Is.LessThan(0.01f));
            }

            Assert.That(Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include), Has.Length.EqualTo(6));
            Assert.That(Object.FindObjectsByType<XRRoomMenu>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XRGuidedTour>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XRRoomTeleporter>(FindObjectsInactive.Include), Has.Length.EqualTo(1));
            Assert.That(Object.FindObjectsByType<XRLightSwitch>(FindObjectsInactive.Include), Has.Length.EqualTo(3));
            Assert.That(GameObject.Find("Collisions")?.GetComponentsInChildren<Collider>(true),
                Has.Length.EqualTo(151));
            Assert.That(controller.height, Is.InRange(1.0f, 2.1f));
        }

        static IEnumerator WaitForDoor(XRInteractiveDoor door)
        {
            var timeout = Time.realtimeSinceStartup + 2f;
            while (door.IsAnimating && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.That(door.IsAnimating, Is.False, "Door animation must finish within two seconds.");
        }

        static IEnumerator WaitForSlidingDoor(XRInteractiveSlidingDoor door)
        {
            var timeout = Time.realtimeSinceStartup + 2f;
            while (door.IsAnimating && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.That(door.IsAnimating, Is.False, "Sliding-door animation must finish within two seconds.");
        }

        static XRInteractiveDoor FindInteractiveDoor(string sourceMeshName)
        {
            return Object.FindObjectsByType<XRInteractiveDoor>(FindObjectsInactive.Include)
                .Single(item => item.DoorMesh != null && item.DoorMesh.name == sourceMeshName);
        }

        static float MoveControllerThroughDoor(CharacterController controller, Vector3 start,
            Vector3 direction, int steps, float distancePerStep)
        {
            controller.enabled = false;
            controller.transform.SetPositionAndRotation(start, Quaternion.identity);
            controller.enabled = true;
            Physics.SyncTransforms();
            var initial = controller.transform.position;
            for (var index = 0; index < steps; index++)
                controller.Move(direction * distancePerStep);
            return Vector3.Dot(controller.transform.position - initial, direction.normalized);
        }

        static IEnumerator WaitForTeleport(XRRoomTeleporter teleporter)
        {
            var timeout = Time.realtimeSinceStartup + 3f;
            while (teleporter.IsTeleporting && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.That(teleporter.IsTeleporting, Is.False, "Guided-tour teleport must complete.");
            Assert.That(teleporter.LastFadeReachedBlack, Is.True);
            Assert.That(teleporter.FadeGroup.alpha, Is.EqualTo(0f).Within(0.01f));
        }

        static void AssertGeneratedCollision(GameObject root, Vector3 origin, Vector3 direction, float distance, string label)
        {
            var hits = Physics.RaycastAll(origin, direction, distance)
                .Where(hit => hit.collider.transform.IsChildOf(root.transform))
                .ToArray();
            Assert.That(hits, Is.Not.Empty, $"Expected a generated collision for {label}.");
        }

        static void AssertWalkableTerraceGardenTransition(GameObject root)
        {
            // The terrace opens onto the garden. A former horizontal wall-ray
            // assertion expected a blocker in this intended walking route.
            float? previousFloorY = null;
            for (var sample = 0; sample <= 14; sample++)
            {
                var z = -52.0f - sample * 0.05f;
                var floor = Physics.RaycastAll(new Vector3(-5.28f, 67f, z), Vector3.down, 3f)
                    .Where(hit => hit.collider.transform.IsChildOf(root.transform) &&
                                  hit.collider.GetComponent<TeleportationArea>() != null)
                    .OrderBy(hit => hit.distance)
                    .FirstOrDefault();
                Assert.That(floor.collider, Is.Not.Null,
                    $"Walkable terrace / garden floor is missing at z={z:0.00}.");
                if (previousFloorY.HasValue)
                    Assert.That(Mathf.Abs(floor.point.y - previousFloorY.Value), Is.LessThan(0.30f),
                        $"Terrace / garden floor has an unwalkable height change at z={z:0.00}.");
                previousFloorY = floor.point.y;
            }
        }

        static void AssertRuntimeObstacle(CharacterController controller, GameObject collisionRoot,
            string colliderName, float floorY, string label)
        {
            var target = collisionRoot.GetComponentsInChildren<BoxCollider>(true)
                .Single(item => item.name == colliderName);
            var direction = target.bounds.size.x < target.bounds.size.z ? Vector3.right : Vector3.forward;
            var extent = direction == Vector3.right ? target.bounds.extents.x : target.bounds.extents.z;
            var start = target.bounds.center - direction * (extent + controller.radius + 0.55f);
            start.y = floorY + 0.02f;

            controller.enabled = false;
            controller.transform.SetPositionAndRotation(start, Quaternion.identity);
            controller.enabled = true;
            Physics.SyncTransforms();

            var initial = controller.transform.position;
            Debug.Log($"[Collision Runtime] TRY {label} ({colliderName}); target={target.bounds}; " +
                      $"start={initial}; direction={direction}; controllerHeight={controller.height:F3}; " +
                      $"radius={controller.radius:F3}; center={controller.center}; detect={controller.detectCollisions}.");
            for (var step = 0; step < 40; step++)
                controller.Move(direction * 0.04f);
            var progress = Vector3.Dot(controller.transform.position - initial, direction);
            Assert.That(progress, Is.LessThan(0.72f),
                $"XR CharacterController crossed {label} ({colliderName}); progress={progress:F3} m.");
            Debug.Log($"[Collision Runtime] BLOCKED by {label} ({colliderName}); progress={progress:F3} m.");
        }

        static void AssertRuntimeDoorPassage(CharacterController controller, Vector3 doorway, Vector3 direction, string label)
        {
            controller.enabled = false;
            controller.transform.SetPositionAndRotation(doorway - direction * 0.72f, Quaternion.identity);
            controller.enabled = true;
            Physics.SyncTransforms();

            var start = controller.transform.position;
            for (var step = 0; step < 42; step++)
                controller.Move(direction * 0.04f);
            var progress = Vector3.Dot(controller.transform.position - start, direction);
            Assert.That(progress, Is.GreaterThan(1.20f),
                $"XR CharacterController was blocked in {label}; progress={progress:F3} m.");
            Debug.Log($"[Collision Runtime] PASSED through {label}; progress={progress:F3} m.");
        }

        static void AssertWardrobeAislePassage(CharacterController controller, GameObject collisionRoot)
        {
            var bedrooms = collisionRoot.transform.Find("UpperFloor/Bedrooms");
            Assert.That(bedrooms.Find("Built-in Wardrobe"), Is.Null,
                "The old aggregate box must not fill the walk-in closet aisle.");
            var cabinets = new[]
            {
                bedrooms.Find("Built-in Wardrobe - Long Cabinet")?.GetComponent<BoxCollider>(),
                bedrooms.Find("Built-in Wardrobe - Short Cabinet")?.GetComponent<BoxCollider>()
            };
            Assert.That(cabinets.All(box => box != null && box.enabled && !box.isTrigger), Is.True);
            Assert.That(cabinets[0].bounds.min.x - cabinets[1].bounds.max.x,
                Is.GreaterThan(1.35f), "The visual cabinets have a walkable aisle between them.");

            controller.enabled = false;
            controller.transform.SetPositionAndRotation(new Vector3(3.12f, 68.12122f, -29.60f),
                Quaternion.identity);
            controller.enabled = true;
            Physics.SyncTransforms();
            var start = controller.transform.position;
            for (var step = 0; step < 120; step++)
                controller.Move(Vector3.back * 0.04f + Vector3.down * 0.002f);
            var progress = start.z - controller.transform.position.z;
            Assert.That(progress, Is.GreaterThan(4.2f),
                $"The XR capsule could not enter the room beside the bed; progress={progress:F3} m.");
            var returnStart = controller.transform.position;
            for (var step = 0; step < 120; step++)
                controller.Move(Vector3.forward * 0.04f + Vector3.down * 0.002f);
            Assert.That(controller.transform.position.z - returnStart.z, Is.GreaterThan(4.2f),
                "The XR capsule could not leave the wardrobe aisle.");
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

        static void AssertCharacterControllerPassage(CharacterController controller, Vector3 doorway, Vector3 direction, string label)
        {
            const float height = 1.75f;
            controller.enabled = false;
            controller.transform.SetPositionAndRotation(doorway - direction * 0.72f + Vector3.up * 0.02f, Quaternion.identity);
            controller.height = height;
            controller.radius = 0.10f;
            controller.center = new Vector3(0f, height * 0.5f, 0f);
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.30f;
            controller.skinWidth = 0.025f;
            controller.enabled = true;

            var start = controller.transform.position;
            for (var step = 0; step < 42; step++)
                controller.Move(direction * 0.04f + Vector3.down * 0.002f);
            var progress = Vector3.Dot(controller.transform.position - start, direction);
            Assert.That(progress, Is.GreaterThan(1.20f), $"XR CharacterController was blocked in {label}; progress={progress:F3} m.");
        }

        static void AssertStairTraversal(CharacterController controller)
        {
            const float height = 1.75f;
            var stairRamp = GameObject.Find("Traversable Stair Ramp").GetComponent<BoxCollider>();
            Assert.That(stairRamp, Is.Not.Null);
            var lowerEndpoint = stairRamp.transform.TransformPoint(new Vector3(0f, stairRamp.size.y * 0.5f, -stairRamp.size.z * 0.5f));
            var upperEndpoint = stairRamp.transform.TransformPoint(new Vector3(0f, stairRamp.size.y * 0.5f, stairRamp.size.z * 0.5f));
            Assert.That(lowerEndpoint.y, Is.EqualTo(64.87821f).Within(0.01f));
            Assert.That(upperEndpoint.y, Is.EqualTo(68.10122f).Within(0.01f));
            Assert.That(upperEndpoint.z, Is.LessThan(lowerEndpoint.z),
                $"Ramp rises in the wrong horizontal direction: lower={lowerEndpoint}, upper={upperEndpoint}.");

            var bottom = new Vector3(-8.827f, 64.89821f, lowerEndpoint.z + 0.20f);
            controller.enabled = false;
            controller.transform.SetPositionAndRotation(bottom, Quaternion.identity);
            controller.height = height;
            controller.radius = 0.10f;
            controller.center = new Vector3(0f, height * 0.5f, 0f);
            controller.slopeLimit = 45f;
            controller.stepOffset = 0.30f;
            controller.skinWidth = 0.025f;
            controller.enabled = true;

            var previousY = controller.transform.position.y;
            var maximumVerticalStep = 0f;
            var maximumVerticalStepIndex = -1;
            var maximumVerticalStepPosition = Vector3.zero;
            var maximumVerticalStepNearby = string.Empty;
            var groundedRampSamples = 0;
            for (var step = 0; step < 175; step++)
            {
                controller.Move(Vector3.back * 0.04f + Vector3.down * 0.015f);
                var verticalStep = Mathf.Abs(controller.transform.position.y - previousY);
                if (verticalStep > maximumVerticalStep)
                {
                    maximumVerticalStep = verticalStep;
                    maximumVerticalStepIndex = step;
                    maximumVerticalStepPosition = controller.transform.position;
                    maximumVerticalStepNearby = string.Join(", ", Physics.OverlapBox(
                            controller.transform.position + Vector3.down * 0.08f,
                            new Vector3(0.30f, 0.22f, 0.30f))
                        .Select(collider => collider.name)
                        .Distinct());
                }
                var rampHit = Physics.RaycastAll(controller.transform.position + Vector3.up * 0.50f, Vector3.down, 1f)
                    .FirstOrDefault(hit => hit.collider == stairRamp);
                if (rampHit.collider != null)
                {
                    groundedRampSamples++;
                    var capsuleBottomGap = controller.transform.position.y - rampHit.point.y;
                    Assert.That(capsuleBottomGap, Is.InRange(-0.02f, 0.08f),
                        $"Capsule left or entered the ramp at step {step}; gap={capsuleBottomGap:F3} m.");
                }
                previousY = controller.transform.position.y;
            }
            Assert.That(groundedRampSamples, Is.GreaterThan(80), "Capsule did not remain grounded above the stair ramp.");
            Assert.That(controller.transform.position.y, Is.GreaterThan(67.95f),
                $"XR CharacterController did not reach the upper landing. Final position={controller.transform.position}.");
            Assert.That(maximumVerticalStep, Is.LessThan(0.12f),
                $"Stair ascent contains a bounce or hard step at iteration {maximumVerticalStepIndex}, position {maximumVerticalStepPosition}; nearby={maximumVerticalStepNearby}.");

            controller.enabled = false;
            controller.transform.position = new Vector3(-8.827f, 68.12122f, upperEndpoint.z - 0.20f);
            controller.enabled = true;
            previousY = controller.transform.position.y;
            maximumVerticalStep = 0f;
            for (var step = 0; step < 180; step++)
            {
                controller.Move(Vector3.forward * 0.04f + Vector3.down * 0.06f);
                maximumVerticalStep = Mathf.Max(maximumVerticalStep,
                    Mathf.Abs(controller.transform.position.y - previousY));
                previousY = controller.transform.position.y;
            }
            Assert.That(controller.transform.position.y, Is.LessThan(65.15f),
                $"XR CharacterController did not descend to the ground landing. Final position={controller.transform.position}.");
            Assert.That(maximumVerticalStep, Is.LessThan(0.12f),
                $"Stair descent contains a bounce or hard step; maximum vertical step={maximumVerticalStep:F3} m.");
        }

        static void AssertUnderStairPassage(CharacterController controller)
        {
            const float height = 1.75f;
            var start = new Vector3(-10.12f, 64.89821f, -29.05f);
            controller.enabled = false;
            controller.transform.SetPositionAndRotation(start, Quaternion.identity);
            controller.height = height;
            controller.radius = 0.10f;
            controller.center = new Vector3(0f, height * 0.5f, 0f);
            controller.enabled = true;

            for (var step = 0; step < 75; step++)
                controller.Move(Vector3.right * 0.04f + Vector3.down * 0.01f);

            var progress = controller.transform.position.x - start.x;
            var blockers = Physics.CapsuleCastAll(
                    start + Vector3.up * controller.radius,
                    start + Vector3.up * (height - controller.radius),
                    controller.radius,
                    Vector3.right,
                    3f)
                .OrderBy(hit => hit.distance)
                .Select(hit => $"{hit.collider.name}@{hit.distance:F3}")
                .ToArray();
            Assert.That(progress, Is.GreaterThan(2.40f),
                $"Open under-stair passage is blocked; lateral progress={progress:F3} m; blockers={string.Join(", ", blockers)}.");
        }

    }
}
