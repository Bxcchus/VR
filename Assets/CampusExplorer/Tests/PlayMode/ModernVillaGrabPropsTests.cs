using System.Collections;
using System.Linq;
using CampusExplorer;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace CampusExplorer.Tests.PlayMode
{
    public sealed class ModernVillaGrabPropsTests
    {
        [UnityTest]
        public IEnumerator ImportedPropsHaveWorkingXrTargetsWithoutChangingNavigation()
        {
            yield return SceneManager.LoadSceneAsync("ModernVillaTour", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(5f);

            var props = Object.FindObjectsByType<VillaGrabbableProp>(FindObjectsInactive.Include);
            var manager = Object.FindAnyObjectByType<XRInteractionManager>();
            Assert.That(manager, Is.Not.Null);
            Assert.That(props.Length, Is.EqualTo(12));
            Assert.That(props.Select(prop => prop.name).Distinct().Count(), Is.EqualTo(12));
            foreach (var prop in props)
            {
                Assert.That(prop.gameObject.activeInHierarchy, Is.True, prop.name);
                Assert.That(prop.GetComponent<Renderer>().enabled, Is.True, prop.name);
                Assert.That(prop.gameObject.layer, Is.EqualTo(31), "Visual lighting layer changed: " + prop.name);
                var box = prop.GrabCollider as BoxCollider;
                Assert.That(box, Is.Not.Null, prop.name);
                Assert.That(box.gameObject.layer, Is.EqualTo(0), "Quest ray cannot hit: " + prop.name);
                Assert.That(box.enabled && !box.isTrigger, Is.True, prop.name);
                Assert.That(box.bounds.size.x * box.bounds.size.y * box.bounds.size.z,
                    Is.GreaterThan(0.00001f), "The world-space grab target is too small: " + prop.name);
                Assert.That(Vector3.Distance(box.bounds.center, prop.GetComponent<Renderer>().bounds.center),
                    Is.LessThan(0.08f), "The grab target misses its visual mesh: " + prop.name);

                var body = prop.GetComponent<Rigidbody>();
                Assert.That(body.isKinematic && !body.useGravity, Is.True,
                    "The imported prop must stay in its authored pose before the first grab: " + prop.name);
                var grab = prop.GetComponent<XRGrabInteractable>();
                Assert.That(grab.enabled && grab.colliders.Count == 1 && grab.colliders[0] == box,
                    Is.True, prop.name);
                Assert.That(grab.movementType, Is.EqualTo(XRBaseInteractable.MovementType.Kinematic));
                Assert.That(grab.throwOnDetach, Is.False, prop.name);
                Assert.That(manager.TryGetInteractableForCollider(box, out var mapped) &&
                            ReferenceEquals(mapped, grab), Is.True,
                    "The Quest ray must resolve the collider to this grabbable: " + prop.name);

                var center = box.bounds.center;
                var rayStart = center + Vector3.up * (box.bounds.extents.y + 0.3f);
                var hits = Physics.RaycastAll(rayStart, Vector3.down, 1.5f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                Assert.That(hits.Any(hit => hit.collider == box), Is.True,
                    "The controller ray must be able to hit the target: " + prop.name);
                if (prop.name.StartsWith("Bed_Pillow_Guest_"))
                {
                    var nearest = hits.OrderBy(hit => hit.distance).FirstOrDefault();
                    Assert.That(nearest.collider, Is.EqualTo(box),
                        "The bed helper must not hide the pillow from the Quest ray: " + prop.name);
                }
            }

            Assert.That(GameObject.Find("Collisions").GetComponentsInChildren<BoxCollider>(true).Length,
                Is.EqualTo(151));
            Assert.That(GameObject.Find("InteractiveDoors").GetComponentsInChildren<BoxCollider>(true).Length,
                Is.EqualTo(16));
            Assert.That(Object.FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include).Length,
                Is.EqualTo(6));

            var player = GameObject.Find("XR Player").GetComponent<CharacterController>();
            Assert.That(player, Is.Not.Null);
            player.enabled = false;
            yield return null;
            player.enabled = true;
            yield return new WaitForFixedUpdate();
            foreach (var prop in props)
                Assert.That(Physics.GetIgnoreCollision(prop.GrabCollider, player), Is.True,
                    "The prop must not block the player after a teleport: " + prop.name);
        }

        [UnityTest]
        public IEnumerator GripSelectionCanHoldAndReleaseCushionAndBottle()
        {
            yield return SceneManager.LoadSceneAsync("ModernVillaTour", LoadSceneMode.Single);
            yield return new WaitForSecondsRealtime(5f);

            var manager = Object.FindAnyObjectByType<XRInteractionManager>();
            var hands = GameObject.Find("XR Player").GetComponentsInChildren<NearFarInteractor>(true);
            Assert.That(manager, Is.Not.Null);
            Assert.That(hands.Length, Is.EqualTo(2), "Both authored Quest near/far interactors must remain present.");

            // Headless Play Mode has no HMD, so the authored hand interactors
            // are intentionally inactive. A test hand exercises the same XRI
            // selection callbacks without pretending to verify Quest hardware.
            var handObject = new GameObject("Grab test hand");
            var handVolume = handObject.AddComponent<SphereCollider>();
            handVolume.isTrigger = true;
            handVolume.radius = 0.1f;
            var handBody = handObject.AddComponent<Rigidbody>();
            handBody.isKinematic = true;
            handBody.useGravity = false;
            var hand = handObject.AddComponent<XRDirectInteractor>();
            hand.interactionManager = manager;
            yield return null;

            var cushion = Object.FindObjectsByType<VillaGrabbableProp>(FindObjectsInactive.Include)
                .Single(item => item.name == "Cushion_02");
            var cushionGrab = cushion.GetComponent<XRGrabInteractable>();
            handObject.transform.position = cushion.transform.position;
            var cushionStart = cushion.transform.position;
            manager.SelectEnterUnconditionally(hand, cushionGrab);
            Assert.That(cushionGrab.isSelected, Is.True, "Grip must select the cushion.");
            handObject.transform.position += Vector3.up * 0.35f;
            for (var frame = 0; frame < 8; frame++)
                yield return new WaitForFixedUpdate();
            Assert.That(cushion.transform.position.y, Is.GreaterThan(cushionStart.y + 0.10f),
                "The selected cushion must follow the hand.");
            manager.SelectExit((IXRSelectInteractor)hand, (IXRSelectInteractable)cushionGrab);
            yield return new WaitForFixedUpdate();
            Assert.That(cushionGrab.isSelected, Is.False);
            Assert.That(cushion.GetComponent<Rigidbody>().isKinematic, Is.True,
                "The cushion should remain in the position where it is released.");
            Assert.That(cushion.transform.position.y, Is.GreaterThan(cushionStart.y + 0.10f),
                "The released cushion must stay at its new placement.");

            var bottle = Object.FindObjectsByType<VillaGrabbableProp>(FindObjectsInactive.Include)
                .Single(item => item.name == "Shampoo_Bottle");
            var bottleGrab = bottle.GetComponent<XRGrabInteractable>();
            handObject.transform.position = bottle.transform.position;
            manager.SelectEnterUnconditionally(hand, bottleGrab);
            Assert.That(bottleGrab.isSelected, Is.True, "Grip must select the bottle.");
            manager.SelectExit((IXRSelectInteractor)hand, (IXRSelectInteractable)bottleGrab);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(bottleGrab.isSelected, Is.False);
            var body = bottle.GetComponent<Rigidbody>();
            Assert.That(body.isKinematic, Is.False, "A released bottle must become physical.");
            Assert.That(body.useGravity, Is.True, "A released bottle must fall onto a support.");
            Assert.That(body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.ContinuousSpeculative));

            handObject.transform.position = bottle.transform.position;
            manager.SelectEnterUnconditionally(hand, bottleGrab);
            yield return new WaitForFixedUpdate();
            Assert.That(bottleGrab.isSelected, Is.True, "The bottle must be selectable again after placement.");
            Assert.That(body.collisionDetectionMode, Is.EqualTo(CollisionDetectionMode.ContinuousSpeculative));
            manager.SelectExit((IXRSelectInteractor)hand, (IXRSelectInteractable)bottleGrab);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(body.useGravity && !body.isKinematic, Is.True,
                "A bottle must remain placeable after a second grab.");
            Object.Destroy(handObject);
        }
    }
}
