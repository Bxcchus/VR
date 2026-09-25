using System;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

namespace CampusExplorer
{
    public sealed class XRRoomTeleporter : MonoBehaviour
    {
        public event Action<string> RoomTeleportCompleted;

        [SerializeField] XRRoomMenu roomMenu;
        [SerializeField] XROrigin xrOrigin;
        [SerializeField] TeleportationProvider teleportationProvider;
        [SerializeField] CanvasGroup fadeGroup;
        [SerializeField] string[] roomNames;
        [SerializeField] Transform[] roomAnchors;
        [SerializeField, Min(0f)] float fadeDuration = 0.15f;

        enum TeleportPhase
        {
            Idle,
            FadingOut,
            WaitingForProvider,
            FadingIn,
        }

        TeleportPhase phase;
        Transform pendingAnchor;
        string pendingRoom;
        int fadeFrame;
        int waitFrame;

        public bool IsTeleporting { get; private set; }
        public bool LastFadeReachedBlack { get; private set; }
        public string LastTeleportedRoom { get; private set; }
        public string[] RoomNames => roomNames;
        public Transform[] RoomAnchors => roomAnchors;
        public CanvasGroup FadeGroup => fadeGroup;

        public void Configure(XRRoomMenu menu, XROrigin origin, TeleportationProvider provider,
            CanvasGroup fade, string[] names, Transform[] anchors)
        {
            roomMenu = menu;
            xrOrigin = origin;
            teleportationProvider = provider;
            fadeGroup = fade;
            roomNames = names;
            roomAnchors = anchors;
        }

        void Awake()
        {
            if (fadeGroup != null)
            {
                fadeGroup.alpha = 0f;
                fadeGroup.blocksRaycasts = false;
                fadeGroup.interactable = false;
            }
        }

        void OnEnable()
        {
            if (roomMenu != null)
                roomMenu.RoomSelected += OnRoomSelected;
        }

        void OnDisable()
        {
            if (roomMenu != null)
                roomMenu.RoomSelected -= OnRoomSelected;
            if (fadeGroup != null)
                fadeGroup.alpha = 0f;
            phase = TeleportPhase.Idle;
            IsTeleporting = false;
        }

        void OnRoomSelected(string roomName)
        {
            TeleportToRoom(roomName);
        }

        public bool TeleportToRoom(string roomName)
        {
            if (IsTeleporting || roomNames == null || roomAnchors == null)
                return false;

            for (var index = 0; index < Mathf.Min(roomNames.Length, roomAnchors.Length); index++)
            {
                if (roomNames[index] != roomName || roomAnchors[index] == null)
                    continue;
                pendingRoom = roomName;
                pendingAnchor = roomAnchors[index];
                fadeFrame = 0;
                LastFadeReachedBlack = false;
                IsTeleporting = true;
                phase = TeleportPhase.FadingOut;
                return true;
            }

            Debug.LogWarning($"[XR Room Teleport] No anchor found for {roomName}.");
            return false;
        }

        void Update()
        {
            switch (phase)
            {
                case TeleportPhase.FadingOut:
                    StepFade(0f, 1f);
                    if (fadeFrame < FadeFrameCount)
                        break;
                    LastFadeReachedBlack = fadeGroup == null || fadeGroup.alpha >= 0.99f;
                    var request = new TeleportRequest
                    {
                        destinationPosition = pendingAnchor.position,
                        destinationRotation = pendingAnchor.rotation,
                        matchOrientation = MatchOrientation.TargetUpAndForward,
                        requestTime = Time.time,
                    };
                    if (teleportationProvider == null || !teleportationProvider.QueueTeleportRequest(request))
                    {
                        Debug.LogError($"[XR Room Teleport] Teleport request rejected for {pendingRoom}.");
                        BeginFadeIn();
                        break;
                    }
                    waitFrame = 0;
                    phase = TeleportPhase.WaitingForProvider;
                    break;

                case TeleportPhase.WaitingForProvider:
                    waitFrame++;
                    if (waitFrame < 2)
                        break;
                    LastTeleportedRoom = pendingRoom;
                    BeginFadeIn();
                    break;

                case TeleportPhase.FadingIn:
                    StepFade(1f, 0f);
                    if (fadeFrame < FadeFrameCount)
                        break;
                    roomMenu?.Close();
                    phase = TeleportPhase.Idle;
                    IsTeleporting = false;
                    Debug.Log($"[XR Room Teleport] Arrived in {pendingRoom} at {pendingAnchor.position}.");
                    RoomTeleportCompleted?.Invoke(pendingRoom);
                    break;
            }
        }

        int FadeFrameCount => fadeDuration <= 0f ? 1 : Mathf.Max(1, Mathf.RoundToInt(fadeDuration * 72f));

        void BeginFadeIn()
        {
            fadeFrame = 0;
            phase = TeleportPhase.FadingIn;
        }

        void StepFade(float from, float to)
        {
            fadeFrame++;
            if (fadeGroup != null)
                fadeGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(fadeFrame / (float)FadeFrameCount));
        }
    }
}
