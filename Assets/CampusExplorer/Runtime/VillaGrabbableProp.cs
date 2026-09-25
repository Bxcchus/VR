using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace CampusExplorer
{
    /// <summary>
    /// Keeps imported decorative props in their authored pose until picked up.
    /// Loose objects fall onto the furniture/floor after release; cushions stay
    /// where the hand places them because the villa uses coarse sofa colliders.
    /// </summary>
    [RequireComponent(typeof(Rigidbody), typeof(XRGrabInteractable))]
    public sealed class VillaGrabbableProp : MonoBehaviour
    {
        [SerializeField] bool fallAfterRelease;
        [SerializeField] Collider grabCollider;

        Rigidbody body;
        XRGrabInteractable grab;
        CharacterController[] playerControllers;
        Coroutine releaseRoutine;

        public bool FallAfterRelease => fallAfterRelease;
        public Collider GrabCollider => grabCollider;

        public void Configure(Collider collider, bool fall)
        {
            grabCollider = collider;
            fallAfterRelease = fall;
        }

        void Awake()
        {
            body = GetComponent<Rigidbody>();
            grab = GetComponent<XRGrabInteractable>();
            body.isKinematic = true;
            body.useGravity = false;
        }

        void OnEnable()
        {
            if (grab == null)
                grab = GetComponent<XRGrabInteractable>();
            grab.selectEntered.AddListener(OnGrabbed);
            grab.selectExited.AddListener(OnReleased);
        }

        void Start()
        {
            if (grabCollider == null)
                return;

            // POI markers use large, solid ray targets. They must never push
            // a bottle/cushion when it is picked up or placed nearby.
            foreach (var point in FindObjectsByType<PointOfInterest>(FindObjectsInactive.Include))
                foreach (var marker in point.GetComponents<Collider>())
                    Physics.IgnoreCollision(grabCollider, marker, true);

            // Prevent a tracked hand from shoving the player's locomotion capsule.
            playerControllers = FindObjectsByType<CharacterController>(FindObjectsInactive.Include);
            foreach (var controller in playerControllers)
                Physics.IgnoreCollision(grabCollider, controller, true);
        }

        void FixedUpdate()
        {
            // Teleportation may briefly disable/re-enable the XR capsule. Unity
            // clears pairwise IgnoreCollision state when a collider is disabled.
            if (grabCollider == null || playerControllers == null)
                return;
            foreach (var controller in playerControllers)
                if (controller != null && controller.enabled &&
                    !Physics.GetIgnoreCollision(grabCollider, controller))
                    Physics.IgnoreCollision(grabCollider, controller, true);
        }

        void OnDisable()
        {
            if (grab == null)
                return;
            grab.selectEntered.RemoveListener(OnGrabbed);
            grab.selectExited.RemoveListener(OnReleased);
            if (releaseRoutine != null)
            {
                StopCoroutine(releaseRoutine);
                releaseRoutine = null;
            }
        }

        void OnGrabbed(SelectEnterEventArgs args)
        {
            if (releaseRoutine != null)
            {
                StopCoroutine(releaseRoutine);
                releaseRoutine = null;
            }
            body.useGravity = false;
        }

        void OnReleased(SelectExitEventArgs args)
        {
            if (fallAfterRelease)
                releaseRoutine = StartCoroutine(EnableGravityAfterXriRelease());
        }

        IEnumerator EnableGravityAfterXriRelease()
        {
            // XRI restores its pre-grab Rigidbody state at the end of selection.
            // Switch to a dynamic body after that restoration, never mid-callback.
            yield return new WaitForFixedUpdate();
            if (grab != null && !grab.isSelected)
            {
                body.isKinematic = false;
                body.useGravity = true;
                // XRI makes this body kinematic again on the next grab.
                // Speculative collision works in both kinematic and dynamic modes.
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }
            releaseRoutine = null;
        }
    }
}
