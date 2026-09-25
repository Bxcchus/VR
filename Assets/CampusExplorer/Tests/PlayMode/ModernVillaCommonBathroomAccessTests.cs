using System.Collections;
using System.Linq;
using CampusExplorer;
using NUnit.Framework;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace CampusExplorer.Tests.PlayMode
{
    public sealed class ModernVillaCommonBathroomAccessTests
    {
        [UnityTest]
        public IEnumerator BathroomMenuButtonArrivesInCommonBathroomWithClearXRCapsule()
        {
            yield return SceneManager.LoadSceneAsync("ModernVillaTour", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(5f);

            var menu = Object.FindAnyObjectByType<XRRoomMenu>();
            var teleporter = Object.FindAnyObjectByType<XRRoomTeleporter>();
            var origin = Object.FindAnyObjectByType<XROrigin>();
            var collisionRoot = GameObject.Find("Collisions")?.transform;
            Assert.That(menu, Is.Not.Null);
            Assert.That(teleporter, Is.Not.Null);
            Assert.That(origin, Is.Not.Null);
            Assert.That(collisionRoot, Is.Not.Null);
            var player = origin.Origin.GetComponent<CharacterController>();
            Assert.That(player, Is.Not.Null);

            var bathroomIndex = System.Array.IndexOf(menu.RoomNames, "Bathroom");
            Assert.That(bathroomIndex, Is.GreaterThanOrEqualTo(0));
            var anchor = teleporter.RoomAnchors[bathroomIndex];
            Assert.That(anchor, Is.Not.Null);
            var expected = new Vector3(-10.60f, 68.12665f, -30.20f);
            Assert.That(Vector3.Distance(anchor.position, expected), Is.LessThan(0.03f),
                "Bathroom must arrive in the upper common bathroom beside its relocated POI.");

            var spawnGuard = origin.GetComponents<MonoBehaviour>()
                .FirstOrDefault(component => component.GetType().Name == "ModernVillaXRSpawnGuard");
            if (spawnGuard != null)
                spawnGuard.enabled = false;
            var trackedHmdLocalY = origin.Camera.transform.localPosition.y;

            menu.Open();
            yield return null;
            Assert.That(menu.IsOpen, Is.True);
            menu.RoomButtons[bathroomIndex].onClick.Invoke();
            Assert.That(menu.LastSelectedRoom, Is.EqualTo("Bathroom"));
            var timeout = Time.realtimeSinceStartup + 3f;
            while (teleporter.IsTeleporting && Time.realtimeSinceStartup < timeout)
                yield return null;
            Assert.That(teleporter.IsTeleporting, Is.False, "Bathroom teleport must complete.");
            Assert.That(teleporter.LastTeleportedRoom, Is.EqualTo("Bathroom"));
            Assert.That(teleporter.LastFadeReachedBlack, Is.True);
            Assert.That(teleporter.FadeGroup.alpha, Is.EqualTo(0f).Within(0.01f));
            Assert.That(menu.IsOpen, Is.False, "The room menu must close after arrival.");
            Assert.That(Vector2.Distance(
                    new Vector2(origin.Camera.transform.position.x, origin.Camera.transform.position.z),
                    new Vector2(anchor.position.x, anchor.position.z)), Is.LessThan(0.18f),
                "The tracked headset must arrive at the Bathroom anchor.");
            Assert.That(origin.Camera.transform.localPosition.y,
                Is.EqualTo(trackedHmdLocalY).Within(0.01f),
                "Bathroom teleport must preserve HMD tracking height.");

            Physics.SyncTransforms();
            var floorHit = Physics.RaycastAll(anchor.position + Vector3.up * 1.8f,
                    Vector3.down, 3.6f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .FirstOrDefault(hit => hit.collider.transform.IsChildOf(collisionRoot) &&
                                       hit.collider.GetComponent<TeleportationArea>() != null &&
                                       Mathf.Abs(hit.point.y - expected.y) < 0.03f);
            Assert.That(floorHit.collider, Is.Not.Null,
                "The Bathroom arrival must have a walkable upper-floor collider.");
            var capsuleBottom = anchor.position + Vector3.up * (player.radius + 0.035f);
            var capsuleTop = anchor.position + Vector3.up * (player.height - player.radius);
            var blockers = Physics.OverlapCapsule(capsuleBottom, capsuleTop, player.radius,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)
                .Where(collider => collider.transform.IsChildOf(collisionRoot) &&
                                   collider != floorHit.collider &&
                                   collider.bounds.max.y > expected.y + 0.4f)
                .Select(collider => collider.name).ToArray();
            Assert.That(blockers, Is.Empty,
                "No wall, furniture, shower glass, or stair guard may intersect the arriving XR capsule.");
        }

        [UnityTest]
        public IEnumerator OpenCommonBathroomDoorCanBeCrossedInBothDirections()
        {
            yield return SceneManager.LoadSceneAsync("ModernVillaTour", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(5f);

            var collisionRoot = GameObject.Find("Collisions")?.transform;
            var origin = Object.FindAnyObjectByType<XROrigin>();
            var door = Object.FindObjectsByType<XRInteractiveDoor>(FindObjectsInactive.Include)
                .Single(item => item.name.StartsWith("Door_Frame_04 - Door_Interior_04"));
            Assert.That(collisionRoot, Is.Not.Null);
            Assert.That(origin, Is.Not.Null);
            var player = origin.Origin.GetComponent<CharacterController>();
            Assert.That(player, Is.Not.Null);
            var bridge = collisionRoot.Find("UpperFloor/Floors/Common Bathroom Access Bridge")
                ?.GetComponent<BoxCollider>();
            Assert.That(bridge, Is.Not.Null);
            Assert.That(bridge.enabled && bridge.gameObject.activeInHierarchy && !bridge.isTrigger,
                Is.True);
            Assert.That(bridge.GetComponent<TeleportationArea>(), Is.Not.Null);
            Assert.That(bridge.bounds.max.y, Is.EqualTo(68.12665f).Within(0.01f));
            Assert.That(bridge.bounds.min.x, Is.LessThan(-9.95f));
            Assert.That(bridge.bounds.max.x, Is.GreaterThan(-7.65f));

            door.ToggleDoor();
            yield return new WaitForSecondsRealtime(door.AnimationDuration + 0.20f);
            Assert.That(door.IsOpen, Is.True, "The common bathroom door must open before crossing.");

            var probeObject = new GameObject("Common Bathroom XR Capsule Probe");
            probeObject.layer = player.gameObject.layer;
            var capsule = probeObject.AddComponent<CharacterController>();
            capsule.enabled = false;
            capsule.height = player.height;
            capsule.radius = player.radius;
            capsule.center = new Vector3(0f, capsule.height * 0.5f, 0f);
            capsule.stepOffset = player.stepOffset;
            capsule.slopeLimit = player.slopeLimit;
            capsule.skinWidth = player.skinWidth;
            probeObject.transform.position = new Vector3(-7.35f, 68.12665f, -28.242f);
            capsule.enabled = true;
            Physics.SyncTransforms();

            var lowest = probeObject.transform.position.y;
            for (var step = 0; step < 100; step++)
            {
                capsule.Move(Vector3.left * 0.04f + Vector3.down * 0.01f);
                lowest = Mathf.Min(lowest, probeObject.transform.position.y);
            }
            Assert.That(probeObject.transform.position.x, Is.LessThan(-10.65f),
                $"The opened common bathroom entrance still blocks the XR capsule at {probeObject.transform.position}.");
            Assert.That(lowest, Is.GreaterThan(68.02f),
                $"The XR capsule fell through the bathroom bridge at Y={lowest:F3}.");

            for (var step = 0; step < 100; step++)
            {
                capsule.Move(Vector3.right * 0.04f + Vector3.down * 0.01f);
                lowest = Mathf.Min(lowest, probeObject.transform.position.y);
            }
            Assert.That(probeObject.transform.position.x, Is.GreaterThan(-7.65f),
                $"The common bathroom exit still blocks the XR capsule at {probeObject.transform.position}.");
            Assert.That(lowest, Is.GreaterThan(68.02f),
                $"The XR capsule dropped at the bathroom threshold at Y={lowest:F3}.");
            Object.Destroy(probeObject);
        }

        [UnityTest]
        public IEnumerator StairVoidGuardStillProtectsTheBridgeEdge()
        {
            yield return SceneManager.LoadSceneAsync("ModernVillaTour", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(2f);
            var root = GameObject.Find("Collisions")?.transform;
            Assert.That(root, Is.Not.Null);
            var west = root.Find("UpperFloor/Railings/Stair Void West Guard")
                ?.GetComponent<BoxCollider>();
            var east = root.Find("UpperFloor/Railings/Stair Void East Guard")
                ?.GetComponent<BoxCollider>();
            var south = root.Find("UpperFloor/Railings/Stair Void Lower Guard")
                ?.GetComponent<BoxCollider>();
            Assert.That(west, Is.Not.Null);
            Assert.That(east, Is.Not.Null);
            Assert.That(south, Is.Not.Null);
            Assert.That(west.bounds.max.z, Is.EqualTo(-28.55f).Within(0.01f));
            Assert.That(east.bounds.max.z, Is.EqualTo(-28.55f).Within(0.01f));
            Assert.That(south.bounds.max.z, Is.EqualTo(-28.55f).Within(0.01f));
            Assert.That(south.bounds.min.x, Is.LessThan(-10.0f));
            Assert.That(south.bounds.max.x, Is.LessThanOrEqualTo(west.bounds.min.x + 0.01f),
                "The edge guard must leave the stair ramp open.");

            var doorway = new Vector3(-9.571f, 68.12665f, -28.242f);
            var blockers = Physics.OverlapCapsule(doorway + Vector3.up * 0.32f,
                    doorway + Vector3.up * 1.47f, 0.28f, ~0, QueryTriggerInteraction.Ignore)
                .Where(hit => hit.transform.IsChildOf(root) &&
                              hit.bounds.max.y > doorway.y + 0.5f)
                .ToArray();
            Assert.That(blockers, Is.Empty,
                "No environment wall or stair guard may cover the bathroom doorway.");
        }
    }
}
