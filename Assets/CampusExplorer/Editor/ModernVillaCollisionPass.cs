using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using CampusExplorer;

namespace CampusExplorer.Editor
{
    public static class ModernVillaCollisionPass
    {
        const string ScenePath = "Assets/CampusExplorer/Scenes/ModernVillaTour.unity";
        const string MarkerPath = "Evidence/modern-villa-collision-pass.request";
        const string ReportPath = "Evidence/modern-villa-collision-pass/collision-validation.md";
        const string ExteriorReportPath = "Evidence/modern-villa-exterior-collision/exterior-walkable-audit.md";
        const float GroundY = 64.87821f;
        const float UpperY = 68.10122f;
        const float WallHeight = 3.12f;
        const float WallThickness = 0.12f;
        const float DoorClearHeight = 2.70f;
        const float DoorSidePadding = 0.12f;

        static readonly string[] GroupPaths =
        {
            "GroundFloor/Floors", "GroundFloor/Walls", "GroundFloor/Doors",
            "GroundFloor/Kitchen", "GroundFloor/LivingRoom", "GroundFloor/DiningRoom",
            "GroundFloor/Bathrooms", "GroundFloor/Staircase",
            "UpperFloor/Floors", "UpperFloor/Walls", "UpperFloor/Doors",
            "UpperFloor/Bedrooms", "UpperFloor/Bathrooms", "UpperFloor/Railings",
            "Exterior/Terrace", "Exterior/Garden", "Exterior/PoolDeck", "Exterior/Pool",
            "Exterior/Cabin", "Exterior/Transitions", "Exterior/Boundaries"
        };

        static readonly string[] ExteriorWalkableGroups =
        {
            "Exterior/Terrace", "Exterior/Garden", "Exterior/PoolDeck",
            "Exterior/Cabin", "Exterior/Transitions"
        };

        sealed class Opening
        {
            public float center;
            public float width;
            public bool hasHeader;
            public Opening(float center, float width, bool hasHeader = true)
            {
                this.center = center;
                this.width = width;
                this.hasHeader = hasHeader;
            }
        }

        sealed class RasterRect
        {
            public int x;
            public int z;
            public int width;
            public int depth;
        }

        [InitializeOnLoadMethod]
        static void RunRequestedPassAfterReload()
        {
            if (File.Exists(MarkerPath))
                EditorApplication.delayCall += TryRunRequestedPass;
        }

        static void TryRunRequestedPass()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                EditorApplication.isPlaying = false;
                EditorApplication.delayCall += TryRunRequestedPass;
                return;
            }

            try
            {
                BuildAndValidate();
                File.Delete(MarkerPath);
                Debug.Log("[Modern Villa] COMPLETE COLLISION PASS finished successfully.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        [MenuItem("Campus Explorer/Collision Pass/Build And Validate")]
        public static void BuildAndValidate()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var existing = GameObject.Find("Collisions");
            if (existing != null)
                UnityEngine.Object.DestroyImmediate(existing);
            var legacyTeleport = GameObject.Find("Teleportation Area - Kitchen");
            if (legacyTeleport != null)
                UnityEngine.Object.DestroyImmediate(legacyTeleport);

            var root = new GameObject("Collisions");
            var groups = GroupPaths.ToDictionary(path => path, path => CreateGroupPath(path, root.transform));

            BuildGroundFloorFloors(groups["GroundFloor/Floors"]);
            BuildUpperFloorFloors(groups["UpperFloor/Floors"]);
            BuildWalls(groups["GroundFloor/Walls"], groups["GroundFloor/Doors"],
                groups["UpperFloor/Walls"], groups["UpperFloor/Doors"]);
            BuildKitchen(groups["GroundFloor/Kitchen"]);
            BuildLivingRoom(groups["GroundFloor/LivingRoom"]);
            BuildDiningRoom(groups["GroundFloor/DiningRoom"]);
            BuildStairs(groups["GroundFloor/Staircase"], groups["UpperFloor/Floors"]);
            BuildBedroom(groups["UpperFloor/Bedrooms"]);
            BuildBathrooms(groups["GroundFloor/Bathrooms"], groups["UpperFloor/Bathrooms"]);
            BuildUpperSafety(groups["UpperFloor/Railings"]);
            BuildExterior(groups["Exterior/Terrace"], groups["Exterior/Garden"],
                groups["Exterior/PoolDeck"], groups["Exterior/Pool"],
                groups["Exterior/Cabin"], groups["Exterior/Transitions"],
                groups["Exterior/Boundaries"]);
            ConfigureTeleportationSurfaces(groups);
            ConfigureXRCharacterController();
            EnsureRuntimeSpawnGuard();
            var debug = root.AddComponent<CollisionDebugVisualizer>();
            debug.SetVisible(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Physics.SyncTransforms();
            ValidateAndReport(root, groups);
            AssetDatabase.SaveAssets();
            Debug.Log($"[Modern Villa] Collision hierarchy saved in {ScenePath}.");
        }

        [MenuItem("Campus Explorer/Collision Pass/Open Scene For Review")]
        public static void OpenSceneForReview()
        {
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Selection.activeGameObject = GameObject.Find("Collisions");
            EditorApplication.ExecuteMenuItem("Window/General/Hierarchy");
            Debug.Log("[Modern Villa] Collision scene opened for review.");
        }

        [MenuItem("Campus Explorer/Collision Pass/Ensure Runtime Guard")]
        public static void EnsureRuntimeGuardInScene()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            EnsureRuntimeSpawnGuard();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Modern Villa] Runtime XR spawn guard is serialized in the tour scene.");
        }

        [MenuItem("Campus Explorer/Collision Pass/Rebuild Exterior Walkable Only")]
        public static void RebuildExteriorOnly()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var root = GameObject.Find("Collisions");
            if (root == null)
                throw new InvalidOperationException("The existing Collisions hierarchy is missing.");

            var existingExterior = root.transform.Find("Exterior");
            if (existingExterior != null)
                UnityEngine.Object.DestroyImmediate(existingExterior.gameObject);

            var exteriorPaths = GroupPaths.Where(path => path.StartsWith("Exterior/", StringComparison.Ordinal)).ToArray();
            var groups = exteriorPaths.ToDictionary(path => path, path => CreateGroupPath(path, root.transform));
            BuildExterior(groups["Exterior/Terrace"], groups["Exterior/Garden"],
                groups["Exterior/PoolDeck"], groups["Exterior/Pool"],
                groups["Exterior/Cabin"], groups["Exterior/Transitions"],
                groups["Exterior/Boundaries"]);
            ConfigureTeleportationForGroups(groups, ExteriorWalkableGroups);

            var debug = root.GetComponent<CollisionDebugVisualizer>();
            if (debug == null)
                debug = root.AddComponent<CollisionDebugVisualizer>();
            debug.SetVisible(false);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Physics.SyncTransforms();
            ValidateExteriorAndReport(root, groups);
            AssetDatabase.SaveAssets();
            Debug.Log("[Modern Villa] Exterior walkable collision pass saved without rebuilding interior collision.");
        }

        [MenuItem("Campus Explorer/Collision Pass/Capture Debug Views")]
        public static void CaptureDebugViews()
        {
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var collisions = GameObject.Find("Collisions");
            var debug = collisions != null ? collisions.GetComponent<CollisionDebugVisualizer>() : null;
            if (debug == null)
                throw new InvalidOperationException("CollisionDebugVisualizer is missing from the collision hierarchy.");

            debug.SetVisible(true);
            var villa = scene.GetRootGameObjects().First(item => item.name == "Authoritative Modern Luxury Villa - Identity Transform");
            var sourceCameras = villa.GetComponentsInChildren<Camera>(true);
            var views = new[]
            {
                (file: "ground-floor", camera: "cocina", eyeY: 66.58f),
                (file: "staircase", camera: "pasillo", eyeY: 66.58f),
                (file: "upper-floor", camera: "suit principal", eyeY: 69.80f),
                (file: "exterior-pool", camera: "Camera.006", eyeY: 66.50f)
            };

            var folder = "Evidence/modern-villa-collision-pass/debug";
            Directory.CreateDirectory(folder);
            foreach (var view in views)
            {
                var source = sourceCameras.FirstOrDefault(camera =>
                    string.Equals(camera.name, view.camera, StringComparison.OrdinalIgnoreCase));
                if (source == null)
                    throw new InvalidOperationException($"Debug capture camera was not found: {view.camera}");

                var cameraObject = new GameObject("Collision Debug Capture - " + view.file);
                var camera = cameraObject.AddComponent<Camera>();
                camera.CopyFrom(source);
                camera.transform.SetPositionAndRotation(
                    new Vector3(source.transform.position.x, view.eyeY, source.transform.position.z),
                    source.transform.rotation);
                camera.cullingMask = ~0;
                camera.fieldOfView = 70f;
                camera.nearClipPlane = 0.05f;
                camera.farClipPlane = 350f;
                camera.enabled = false;
                CaptureCamera(camera, Path.Combine(folder, view.file + ".png"));
                UnityEngine.Object.DestroyImmediate(cameraObject);
            }

            var overviewObject = new GameObject("Collision Debug Capture - overview");
            var overview = overviewObject.AddComponent<Camera>();
            overview.CopyFrom(sourceCameras[0]);
            overview.transform.SetPositionAndRotation(new Vector3(-5f, 105f, -35f),
                Quaternion.LookRotation(Vector3.down, Vector3.forward));
            overview.orthographic = true;
            overview.orthographicSize = 39f;
            overview.nearClipPlane = 0.05f;
            overview.farClipPlane = 80f;
            overview.cullingMask = ~0;
            overview.useOcclusionCulling = false;
            overview.clearFlags = CameraClearFlags.SolidColor;
            overview.backgroundColor = new Color(0.055f, 0.065f, 0.085f, 1f);
            overview.enabled = false;
            // Isolate the generated helpers so the overview remains readable even
            // when the source villa's material exposure is very bright.
            var debugVisualRoot = collisions.transform.Find("Collision Debug Visuals");
            var debugRenderers = debugVisualRoot != null
                ? new HashSet<Renderer>(debugVisualRoot.GetComponentsInChildren<Renderer>(true))
                : new HashSet<Renderer>();
            Debug.Log($"[Modern Villa] Overview contains {debugRenderers.Count} collision debug renderers.");
            var hiddenRenderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Where(renderer => !debugRenderers.Contains(renderer))
                .Select(renderer => (renderer, renderer.enabled))
                .ToArray();
            foreach (var entry in hiddenRenderers)
                entry.renderer.enabled = false;
            CaptureCamera(overview, Path.Combine(folder, "collision-overview.png"));
            foreach (var entry in hiddenRenderers)
                entry.renderer.enabled = entry.enabled;
            UnityEngine.Object.DestroyImmediate(overviewObject);

            debug.SetVisible(false);
            Debug.Log($"[Modern Villa] Collision debug captures written to {folder}.");
        }

        static void CaptureCamera(Camera camera, string path)
        {
            var previous = RenderTexture.active;
            var target = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32)
            {
                antiAliasing = 1,
                name = "Modern Villa collision debug evidence"
            };
            var image = new Texture2D(1600, 900, TextureFormat.RGB24, false, false);
            try
            {
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, 1600, 900), 0, 0);
                image.Apply(false, false);
                File.WriteAllBytes(path, image.EncodeToPNG());
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(image);
                UnityEngine.Object.DestroyImmediate(target);
            }
        }

        static Transform CreateGroupPath(string path, Transform root)
        {
            var current = root;
            foreach (var name in path.Split('/'))
            {
                var child = current.Find(name);
                if (child == null)
                {
                    var group = new GameObject(name);
                    group.transform.SetParent(current, false);
                    child = group.transform;
                }
                current = child;
            }
            return current;
        }

        static void ConfigureXRCharacterController()
        {
            var controller = UnityEngine.Object.FindAnyObjectByType<CharacterController>();
            if (controller == null)
                throw new InvalidOperationException("XR CharacterController was not found.");

            controller.slopeLimit = 45f;
            controller.stepOffset = 0.30f;
            controller.skinWidth = 0.025f;
            controller.minMoveDistance = 0f;
            EditorUtility.SetDirty(controller);
        }

        static void EnsureRuntimeSpawnGuard()
        {
            var origin = UnityEngine.Object.FindAnyObjectByType<XROrigin>();
            if (origin == null)
                throw new InvalidOperationException("XR Origin was not found.");

            var guard = origin.GetComponent<ModernVillaXRSpawnGuard>();
            if (guard == null)
                guard = origin.gameObject.AddComponent<ModernVillaXRSpawnGuard>();
            guard.Configure(origin.Origin.transform.position, origin.Origin.transform.forward);
            EditorUtility.SetDirty(guard);
        }

        static void BuildGroundFloorFloors(Transform group)
        {
            AddFloorFromRenderer(group, "Bathroom Floor", "Floor_Bathroom_Tiles");
            AddFloorFromRenderer(group, "Dining Floor", "Floor_Dining_Marble");
            AddFloorFromRenderer(group, "Foyer Floor", "Floor_Foyer_Entry");
            AddFloorFromRenderer(group, "Hallway Floor", "Floor_Hallway_Marble");
            AddFloorFromRenderer(group, "Teleportation Area - Kitchen", "Floor_Kitchen_Tiles");
            AddFloorFromRenderer(group, "Living Room Floor", "Floor_Living_Room_Main");
            // The visible landscape continues through the wide opening beside
            // the stair, but neither the hallway nor garden helper covered it.
            AddFloorBox(group, "Under Stair Landscape Passage", -11.525f, -29.88f,
                4.35f, 4.88f, 64.865f);
        }

        static void BuildUpperFloorFloors(Transform group)
        {
            AddRasterFloor(group, "Upper Floor", "Floor_Upper_Level_Main", 0.25f);
            // The stair-headroom cutout also removed the source-visible floor
            // between the upper hall and Door_Interior_04 (common bathroom).
            // Restore only the northern strip, where the ramp below still has
            // enough headroom for the XR capsule.
            AddBox(group, "Common Bathroom Access Bridge",
                new Vector3(-8.81f, UpperY - 0.02f, -27.665f),
                new Vector3(2.60f, 0.04f, 1.77f), Quaternion.identity);
        }

        static void BuildWalls(Transform groundWalls, Transform groundDoors, Transform upperWalls, Transform upperDoors)
        {
            // Ground-floor outer shell.
            AddCutWallX(groundWalls, groundDoors, "G Outer North", -22.114f, -13.182f, 2.623f, GroundY,
                new Opening(-5.0225f, 1.12f),
                new Opening(-2.6765f, 1.12f));
            AddCutWallZ(groundWalls, groundDoors, "G Outer East", 2.623f, -33.370f, -22.114f, GroundY);
            AddCutWallX(groundWalls, groundDoors, "G Outer Dining South", -33.370f, -6.123f, 2.623f, GroundY);
            AddCutWallX(groundWalls, groundDoors, "G Outer Bathroom South", -36.049f, -12.518f, -6.123f, GroundY);
            AddCutWallZ(groundWalls, groundDoors, "G Outer Bathroom West", -12.518f, -36.049f, -32.321f, GroundY);
            AddCutWallZ(groundWalls, groundDoors, "G Outer Living West", -13.182f, -27.435f, -22.114f, GroundY,
                new Opening(-24.807f, 3.24f));
            AddCutWallX(groundWalls, groundDoors, "G Outer West Link", -27.435f, -13.182f, -9.519f, GroundY);

            // Ground-floor partitions. Each door is cut from the wall span.
            AddCutWallX(groundWalls, groundDoors, "G Kitchen Dining", -27.435f, -3.620f, 2.623f, GroundY,
                new Opening(-1.182f, 1.16f));
            AddCutWallX(groundWalls, groundDoors, "G Living Hall", -27.435f, -9.519f, -3.620f, GroundY,
                new Opening(-8.230f, 2.50f, false));
            AddCutWallZ(groundWalls, groundDoors, "G Kitchen Living", -3.620f, -27.435f, -22.114f, GroundY,
                new Opening(-24.753f, 1.54f));
            AddCutWallZ(groundWalls, groundDoors, "G Dining Hall", -6.123f, -33.370f, -27.435f, GroundY,
                new Opening(-30.474f, 1.16f));
            AddCutWallX(groundWalls, groundDoors, "G Hall Bathroom", -32.321f, -12.518f, -6.123f, GroundY,
                new Opening(-8.250f, 2.80f));
            AddCutWallZ(groundWalls, groundDoors, "G Hall Stair", -9.519f, -32.321f, -27.435f, GroundY,
                new Opening(-29.878f, 4.30f));

            // Entry foyer shell.
            AddCutWallX(groundWalls, groundDoors, "G Foyer Front", -17.970f, -6.657f, -3.575f, GroundY,
                new Opening(-5.116f, 1.20f));
            AddCutWallZ(groundWalls, groundDoors, "G Foyer West", -6.657f, -22.114f, -17.970f, GroundY);
            AddCutWallZ(groundWalls, groundDoors, "G Foyer East", -3.575f, -22.114f, -17.970f, GroundY);

            // Upper shell and partitions, based on the upper-level wall and door geometry.
            AddCutWallX(upperWalls, upperDoors, "U Outer North", -21.950f, -12.574f, 4.589f, UpperY,
                new Opening(-7.882f, 1.88f));
            AddCutWallZ(upperWalls, upperDoors, "U Outer East", 4.589f, -39.100f, -21.950f, UpperY);
            AddCutWallX(upperWalls, upperDoors, "U Outer South", -39.100f, -12.574f, 4.589f, UpperY);
            AddCutWallZ(upperWalls, upperDoors, "U Outer West", -12.574f, -39.100f, -21.950f, UpperY);
            AddCutWallX(upperWalls, upperDoors, "U Main Partition", -27.426f, -12.574f, 4.589f, UpperY,
                new Opening(-8.080f, 2.92f), new Opening(-1.538f, 1.18f), new Opening(1.935f, 1.18f));
            AddCutWallZ(upperWalls, upperDoors, "U East Bedroom Partition", -4.463f, -27.426f, -21.950f, UpperY,
                new Opening(-23.665f, 1.18f));
            AddCutWallZ(upperWalls, upperDoors, "U West Hall Partition", -9.571f, -39.100f, -27.426f, UpperY,
                new Opening(-28.242f, 1.18f));
            AddCutWallZ(upperWalls, upperDoors, "U Bedroom Bathroom Partition", -5.807f, -39.100f, -27.426f, UpperY,
                new Opening(-28.731f, 1.18f), new Opening(-31.939f, 1.18f), new Opening(-36.969f, 1.18f));
        }

        static void BuildKitchen(Transform group)
        {
            AddFurniture(group, "Kitchen Island", GroundY, "Island_Countertop_Marble");
            // Do not combine Base_Cabinet_01 and Wall_Cabinet_02. They are far
            // apart in the imported FBX and their aggregate bounds create a
            // room-sized invisible block across the kitchen circulation area.
            AddFurniture(group, "Tall Cabinet East", GroundY, "Base_Cabinet_03", "Wall_Cabinet_01");
            AddFurniture(group, "Refrigerator", GroundY, "Refrigerator");
        }

        static void BuildLivingRoom(Transform group)
        {
            AddFurniture(group, "Sectional Sofa", GroundY,
                "Sofa_Section_01", "Sofa_Section_02", "Sofa_Section_03", "Sofa_Section_04", "Sofa_Section_05");
            AddFurniture(group, "Coffee Table", GroundY, "Coffee_Table");
            AddFurniture(group, "TV Furniture", GroundY, "TV_Stand");
            AddFurniture(group, "Living Bookshelf West", GroundY, "Bookshelf_01");
            AddFurniture(group, "Living Bookshelf North", GroundY, "Bookshelf_02");
        }

        static void BuildDiningRoom(Transform group)
        {
            AddFurniture(group, "Dining Table", GroundY, "Dining_Table_Top", "Dining_Table_Leg");
            AddFurniture(group, "Dining Sideboard", GroundY, "Dining_Sideboard");
        }

        static void BuildStairs(Transform group, Transform upperFloorGroup)
        {
            var source = FindRenderer("Stairs_Main_Structure");
            var b = source.bounds;
            // Up-facing triangles in the source mesh rise toward negative Z.
            // Begin on the lower floor before the first tread and meet the top
            // tread at the existing upper-floor height.
            var rampBottomZ = b.max.z + 0.58f;
            var rampTopZ = b.min.z + 0.20f;
            var rise = UpperY - GroundY;
            const float rampThickness = 0.035f;
            var rampWidth = b.size.x;
            var rampSurfaceStart = new Vector3(b.center.x, GroundY, rampBottomZ);
            var rampSurfaceEnd = new Vector3(b.center.x, UpperY, rampTopZ);
            var rampDirection = (rampSurfaceEnd - rampSurfaceStart).normalized;
            var rampNormal = Vector3.Cross(Vector3.right, rampDirection).normalized;
            var rampRotation = Quaternion.LookRotation(rampDirection, rampNormal);
            var length = Vector3.Distance(rampSurfaceStart, rampSurfaceEnd);
            var rampCenter = (rampSurfaceStart + rampSurfaceEnd) * 0.5f - rampNormal * (rampThickness * 0.5f);
            AddBox(group, "Traversable Stair Ramp", rampCenter,
                new Vector3(rampWidth, rampThickness, length), rampRotation);

            // Keep only the thin walkable slope. Side safety is handled by the
            // visible stair structure and the upper-floor void guards; additional
            // sloped bars made the open underside and stair approach misleading.

            // Bridge the corrected high end to the existing upper-floor support.
            // This landing keeps the same Y and only follows the ramp to the
            // opposite horizontal end after the orientation correction.
            var upperFloorSupport = upperFloorGroup.GetComponentsInChildren<BoxCollider>(true)
                .Where(collider => Mathf.Abs(collider.bounds.max.y - UpperY) < 0.05f)
                .Where(collider => collider.bounds.min.x <= b.center.x && collider.bounds.max.x >= b.center.x)
                .Where(collider => collider.bounds.max.z < rampTopZ)
                .OrderByDescending(collider => collider.bounds.max.z)
                .First();
            const float landingOverlap = 0.04f;
            var landingFarZ = upperFloorSupport.bounds.max.z - landingOverlap;
            var landingNearZ = rampTopZ + landingOverlap;
            var landingCenterZ = (landingNearZ + landingFarZ) * 0.5f;
            var landingDepth = Mathf.Abs(landingNearZ - landingFarZ);
            AddBox(group, "Upper Stair Landing", new Vector3(-8.23f, UpperY - 0.06f, landingCenterZ),
                new Vector3(2.72f, 0.12f, landingDepth), Quaternion.identity);

            // Do not place full-height side boxes here. They close the usable
            // passage below the open staircase.
        }

        static void BuildBedroom(Transform group)
        {
            AddFurniture(group, "Guest Bed East", UpperY, "Bed_Frame_01", "Bed_Matress_01");
            AddFurniture(group, "Guest Bed West", UpperY, "Bed_Frame_02", "Bed_Mattress_02");
            AddFurniture(group, "Master Bed", UpperY, "Bed_Frame_Master", "Bed_Mattress");
            AddFurniture(group, "Storage Cabinet 01", UpperY, "Storage_Cabinet_Large_01");
            AddFurniture(group, "Storage Cabinet 02", UpperY, "Storage_Cabinet_Large_02");
            AddFurniture(group, "Bedroom Sofa 01", UpperY, "Bedroom_Lounge_Sofa_01");
            AddFurniture(group, "Bedroom Sofa 02", UpperY, "Bedroom_Lounge_Sofa_02");
            // The source wardrobe mesh contains two disconnected cabinet banks.
            // Its aggregate bounds cover the 1.49 m aisle between them.
            AddBox(group, "Built-in Wardrobe - Long Cabinet",
                new Vector3(4.173439f, 69.387130f, -32.492465f),
                new Vector3(0.614464f, 2.571831f, 4.821728f), Quaternion.identity);
            AddBox(group, "Built-in Wardrobe - Short Cabinet",
                new Vector3(2.062623f, 69.387130f, -31.574530f),
                new Vector3(0.620229f, 2.571831f, 2.978372f), Quaternion.identity);
            AddFurniture(group, "Bedroom Bookshelf", UpperY, "Bedroom_Bookshelf");
            AddFurniture(group, "Upper Hall Bookshelf", UpperY, "Bookshelf_02.001");
        }

        static void BuildBathrooms(Transform groundGroup, Transform upperGroup)
        {
            AddFurniture(groundGroup, "Ground Shower Enclosure", GroundY, "Shower_Glass_Panel_01", "Shower_Glass_Panel_02");
            AddFurniture(groundGroup, "Ground Vanity", GroundY, "Vanity_Countertop");
            AddFurniture(groundGroup, "Ground Toilet", GroundY, "Toilet_Structure");

            AddFurniture(upperGroup, "Freestanding Bathtub", UpperY, "Bathtub_Freestanding");
            AddFurniture(upperGroup, "Vanity Cabinet", UpperY, "Vanity_Cabinet_Main");
            AddFurniture(upperGroup, "Upper Vanity", UpperY, "Vanity_Countertop_02");
            AddFurniture(upperGroup, "Upper Toilet Master", UpperY, "Toilet_Unit_Master");
            AddFurniture(upperGroup, "Upper Toilet West", UpperY, "Toilet_Structure_02");
            AddFurniture(upperGroup, "Upper Shower Glass 04", UpperY, "Shower_Glass_Panel_04");
            AddFurniture(upperGroup, "Upper Shower Glass 05", UpperY, "Shower_Glass_Panel_05");
            AddFurniture(upperGroup, "Upper Shower Glass 06", UpperY, "Shower_Glass_Panel_06");
            AddFurniture(upperGroup, "Upper Shower Glass 07", UpperY, "Shower_Glass_Panel_07");
            AddFurniture(upperGroup, "Upper Shower Glass 08", UpperY, "Shower_Glass_Panel_08");
        }

        static void BuildUpperSafety(Transform group)
        {
            AddSafetyBarrierFromRenderer(group, "Upper Hall Railing", "Handrail_Stairs_02", UpperY, 1.15f);
            var stair = FindRenderer("Stairs_Main_Structure").bounds;
            const float guardThickness = 0.08f;
            const float guardHeight = 1.15f;
            const float sideInset = 0.14f;
            const float endInset = 0.16f;
            var guardY = UpperY + guardHeight * 0.5f;
            const float bathroomBridgeSouthZ = -28.55f;
            const float bathroomBridgeWestX = -10.11f;
            var guardStartZ = stair.min.z + endInset;
            var guardCenterZ = (guardStartZ + bathroomBridgeSouthZ) * 0.5f;
            var guardLength = bathroomBridgeSouthZ - guardStartZ;
            var westGuardLeftX = stair.min.x + sideInset - guardThickness * 0.5f;

            // Pull the debug/safety guards slightly inside the measured stair
            // bounds so they hug the visible opening instead of reading as a
            // second, wider corridor around it. The remaining clear span is
            // still wider than one metre for the XR capsule.
            AddBox(group, "Stair Void East Guard",
                new Vector3(stair.max.x - sideInset, guardY, guardCenterZ),
                new Vector3(guardThickness, guardHeight, guardLength), Quaternion.identity);
            AddBox(group, "Stair Void West Guard",
                new Vector3(stair.min.x + sideInset, guardY, guardCenterZ),
                new Vector3(guardThickness, guardHeight, guardLength), Quaternion.identity);
            AddBox(group, "Stair Void Lower Guard",
                new Vector3((bathroomBridgeWestX + westGuardLeftX) * 0.5f,
                    guardY, bathroomBridgeSouthZ - guardThickness * 0.5f),
                new Vector3(westGuardLeftX - bathroomBridgeWestX,
                    guardHeight, guardThickness), Quaternion.identity);

            // The upper balcony has a walkable rasterized floor, yet the
            // visible U-shaped railing had no collision along its exposed edges.
            // Keep the glass-door side open at z=-21.94.
            const float balconyFloorY = 68.12665f;
            var balconyGuardY = balconyFloorY + guardHeight * 0.5f;
            AddBox(group, "Upper Balcony Front Guard",
                new Vector3(-8.3396f, balconyGuardY, -17.100f),
                new Vector3(9.017f, guardHeight, 0.12f), Quaternion.identity);
            AddBox(group, "Upper Balcony West Guard",
                new Vector3(-12.848f, balconyGuardY, -19.132f),
                new Vector3(0.12f, guardHeight, 4.148f), Quaternion.identity);
            AddBox(group, "Upper Balcony East Guard",
                new Vector3(-3.831f, balconyGuardY, -19.132f),
                new Vector3(0.12f, guardHeight, 4.148f), Quaternion.identity);
        }

        static void BuildExterior(Transform terrace, Transform garden, Transform poolDeck, Transform pool,
            Transform cabin, Transform transitions, Transform boundaries)
        {
            const float gardenY = 65.3352f;
            const float poolDeckY = 65.4586f;
            const float floorThickness = 0.14f;

            // Front drive / entrance. Its authored surface is already covered by
            // one simple collider fitted from the source renderer bounds.
            AddFloorFromRenderer(terrace, "Garage Exterior Ground", "Floor_Garage_Concrete");
            AddFloorFromRenderer(terrace, "Pool Terrace Wooden Deck", "Wooden_Deck_Platform");

            // The source lawn rises gradually behind the villa. Broad ramps keep
            // the capsule supported at the authored height without a forest of
            // small colliders or a vertical seam at the existing front drive.
            AddSurfaceRamp(garden, "West Side Garden North", -23.195f, 19.31f,
                -22.18f, 64.9343f, -40.0f, 64.85f, floorThickness);
            // The source terrain rises over the first 1.5 m, then levels out.
            // One long ramp left the XR capsule 20-30 cm inside the visible grass.
            AddSurfaceRamp(garden, "West Side Garden Rise Lower", -23.195f, 19.31f,
                -40.0f, 64.85f, -41.0f, 65.28f, floorThickness);
            AddSurfaceRamp(garden, "West Side Garden Rise Crest", -23.195f, 19.31f,
                -41.0f, 65.28f, -41.5f, 65.35f, floorThickness);
            AddSurfaceRamp(garden, "West Side Garden Rise Flat", -23.195f, 19.31f,
                -41.5f, 65.35f, -44.95f, gardenY, floorThickness);
            AddSurfaceRamp(garden, "East Side Garden North", 12.565f, 19.77f,
                -22.18f, 64.9343f, -40.0f, 65.27f, floorThickness);
            AddSurfaceRamp(garden, "East Side Garden Rise", 12.565f, 19.77f,
                -40.0f, 65.27f, -44.95f, gardenY, floorThickness);

            // Cut the rear lawn around the water footprint. This prevents a
            // teleport ray from selecting a hidden floor below the pool.
            AddFloorBox(garden, "Rear Garden North", -0.0625f, -51.0975f, 45.205f, 12.295f, gardenY);
            AddFloorBox(garden, "Rear Garden South", -0.0625f, -69.884f, 45.205f, 1.152f, gardenY);
            AddFloorBox(garden, "Rear Garden West", -21.933f, -63.2763f, 1.464f, 9.2633f, gardenY);
            AddFloorBox(garden, "Rear Garden East", 21.6215f, -63.2763f, 1.837f, 9.2633f, gardenY);

            // A gentle transition joins the villa threshold directly to the
            // raised wooden terrace. Its ends overlap both supporting floors.
            AddSurfaceRamp(transitions, "Villa To Rear Terrace", -5.43f, 16.22f,
                -36.08f, GroundY, -41.67f, 65.5052f, floorThickness);
            // The west living sliding-door opening straddles a 0.358 m gap
            // between the room floor and the west garden helper.
            AddFloorBox(transitions, "Living Patio Threshold", -13.36f, -24.807f,
                0.70f, 3.40f, GroundY);
            // Source landscape remains visible on the south side of the villa,
            // between the dining wall and the terrace transition.
            AddSurfaceRamp(transitions, "South Villa Landscape Apron", -1.72f, 8.80f,
                -33.37f, 64.82f, -36.16f, 64.79f, floorThickness);
            // Bridge the narrow gap from the west lawn to the wooden terrace.
            // Its 0.155 m step to the deck is below the controller step offset.
            AddFloorBox(transitions, "West Terrace Ground Bridge", -13.39f, -43.225f,
                0.42f, 3.65f, 65.35f);

            // Four simple deck strips surround the water. The water footprint
            // itself intentionally has no floor or TeleportationArea.
            AddFloorBox(poolDeck, "Pool Deck North", -0.249f, -57.945f, 41.904f, 1.40f, poolDeckY);
            AddFloorBox(poolDeck, "Pool Deck South", -0.249f, -68.608f, 41.904f, 1.40f, poolDeckY);
            AddFloorBox(poolDeck, "Pool Deck West", -20.501f, -63.2763f, 1.40f, 9.2633f, poolDeckY);
            AddFloorBox(poolDeck, "Pool Deck East", 20.003f, -63.2763f, 1.40f, 9.2633f, poolDeckY);

            // The source calls the small exterior pavilion "Quincho". It is an
            // open, intended destination, so its real floor and the approach to
            // its open side are both walkable.
            AddFloorBox(cabin, "Cabin Approach Path", -27.7075f, -48.695f, 10.285f, 7.49f, gardenY);
            AddFloorFromRenderer(cabin, "Cabin Quincho Floor", "Quincho_Floor_Tiles");

            var water = FindRenderer("Pool_Water_Surface").bounds;
            const float poolBarrierHeight = 1.25f;
            const float poolBarrierThickness = 0.18f;
            var poolBarrierY = water.max.y + poolBarrierHeight * 0.5f;
            AddBox(pool, "Pool North Boundary", new Vector3(water.center.x, poolBarrierY, water.max.z),
                new Vector3(water.size.x + 0.36f, poolBarrierHeight, poolBarrierThickness), Quaternion.identity);
            AddBox(pool, "Pool South Boundary", new Vector3(water.center.x, poolBarrierY, water.min.z),
                new Vector3(water.size.x + 0.36f, poolBarrierHeight, poolBarrierThickness), Quaternion.identity);
            AddBox(pool, "Pool West Boundary", new Vector3(water.min.x, poolBarrierY, water.center.z),
                new Vector3(poolBarrierThickness, poolBarrierHeight, water.size.z), Quaternion.identity);
            AddBox(pool, "Pool East Boundary", new Vector3(water.max.x, poolBarrierY, water.center.z),
                new Vector3(poolBarrierThickness, poolBarrierHeight, water.size.z), Quaternion.identity);

            const float outerY = 66.4f;
            AddBox(boundaries, "Property West Boundary", new Vector3(-32.95f, outerY, -34.81f), new Vector3(0.20f, 4f, 71.50f), Quaternion.identity);
            // The original east boundary stood beyond the source landscape.
            // Follow the changing visible edge so the player meets a boundary
            // before reaching unsupported ground.
            AddBox(boundaries, "Property East Front Boundary",
                new Vector3(22.20f, outerY, -10.675f),
                new Vector3(0.20f, 4f, 23.25f), Quaternion.identity);
            AddBox(boundaries, "Property East Side Boundary",
                new Vector3(22.39f, outerY, -33.60f),
                new Vector3(0.20f, 4f, 23.00f), Quaternion.identity);
            AddBox(boundaries, "Property East Rear Boundary",
                new Vector3(22.48f, outerY, -57.68f),
                new Vector3(0.20f, 4f, 25.76f), Quaternion.identity);
            AddBox(boundaries, "Property North Boundary", new Vector3(-4.97f, outerY, 0.84f), new Vector3(56.16f, 4f, 0.20f), Quaternion.identity);
            AddBox(boundaries, "Property South Boundary", new Vector3(-4.97f, outerY, -70.46f), new Vector3(56.16f, 4f, 0.20f), Quaternion.identity);
        }

        static void AddFloorBox(Transform parent, string name, float centerX, float centerZ,
            float width, float depth, float surfaceY)
        {
            const float thickness = 0.14f;
            AddBox(parent, name, new Vector3(centerX, surfaceY - thickness * 0.5f, centerZ),
                new Vector3(width, thickness, depth), Quaternion.identity);
        }

        static void AddSurfaceRamp(Transform parent, string name, float centerX, float width,
            float startZ, float startY, float endZ, float endY, float thickness)
        {
            var start = new Vector3(centerX, startY, startZ);
            var end = new Vector3(centerX, endY, endZ);
            var direction = (end - start).normalized;
            var normal = Vector3.Cross(Vector3.right, direction).normalized;
            if (normal.y < 0f)
                normal = -normal;
            var rotation = Quaternion.LookRotation(direction, normal);
            var center = (start + end) * 0.5f - normal * (thickness * 0.5f);
            AddBox(parent, name, center,
                new Vector3(width, thickness, Vector3.Distance(start, end)), rotation);
        }

        static void AddFloorFromRenderer(Transform parent, string name, string rendererName)
        {
            var bounds = FindRenderer(rendererName).bounds;
            AddBox(parent, name, new Vector3(bounds.center.x, bounds.max.y - 0.06f, bounds.center.z),
                new Vector3(bounds.size.x, 0.12f, bounds.size.z), Quaternion.identity);
        }

        static void AddFurniture(Transform parent, string name, float floorY, params string[] rendererNames)
        {
            var renderers = rendererNames.Select(FindRenderer).ToArray();
            var bounds = renderers[0].bounds;
            for (var index = 1; index < renderers.Length; index++)
                bounds.Encapsulate(renderers[index].bounds);

            var top = Mathf.Max(floorY + 0.22f, bounds.max.y);
            var center = new Vector3(bounds.center.x, (floorY + top) * 0.5f, bounds.center.z);
            var size = new Vector3(Mathf.Max(0.08f, bounds.size.x), top - floorY, Mathf.Max(0.08f, bounds.size.z));
            AddBox(parent, name, center, size, Quaternion.identity);
        }

        static void AddSafetyBarrierFromRenderer(Transform parent, string name, string rendererName, float floorY, float height)
        {
            var bounds = FindRenderer(rendererName).bounds;
            AddBox(parent, name, new Vector3(bounds.center.x, floorY + height * 0.5f, bounds.center.z),
                new Vector3(Mathf.Max(0.10f, bounds.size.x), height, Mathf.Max(0.10f, bounds.size.z)), Quaternion.identity);
        }

        static void ConfigureTeleportationSurfaces(Dictionary<string, Transform> groups)
        {
            ConfigureTeleportationForGroups(groups,
                new[] { "GroundFloor/Floors", "UpperFloor/Floors" }.Concat(ExteriorWalkableGroups));
        }

        static void ConfigureTeleportationForGroups(Dictionary<string, Transform> groups,
            IEnumerable<string> groupPaths)
        {
            foreach (var groupPath in groupPaths)
            {
                if (!groups.TryGetValue(groupPath, out var group))
                    throw new InvalidOperationException($"Walkable collision group is missing: {groupPath}");

            foreach (var floor in group.GetComponentsInChildren<BoxCollider>(true))
            {
                if (floor.GetComponent<TeleportationArea>() == null)
                    floor.gameObject.AddComponent<TeleportationArea>();
            }
            }
        }

        static void AddCutWallX(Transform wallParent, Transform doorParent, string name, float z, float minX, float maxX, float floorY, params Opening[] openings)
        {
            AddCutWall(wallParent, name, minX, maxX, openings, (segmentName, a, b) =>
                AddBox(wallParent, segmentName, new Vector3((a + b) * 0.5f, floorY + WallHeight * 0.5f, z),
                    new Vector3(b - a, WallHeight, WallThickness), Quaternion.identity));
            AddDoorHeaders(doorParent, name, floorY, openings, opening =>
                AddBox(doorParent, opening.name, new Vector3(opening.center, opening.y, z),
                    new Vector3(opening.width, opening.height, WallThickness), Quaternion.identity));
        }

        static void AddCutWallZ(Transform wallParent, Transform doorParent, string name, float x, float minZ, float maxZ, float floorY, params Opening[] openings)
        {
            AddCutWall(wallParent, name, minZ, maxZ, openings, (segmentName, a, b) =>
                AddBox(wallParent, segmentName, new Vector3(x, floorY + WallHeight * 0.5f, (a + b) * 0.5f),
                    new Vector3(WallThickness, WallHeight, b - a), Quaternion.identity));
            AddDoorHeaders(doorParent, name, floorY, openings, opening =>
                AddBox(doorParent, opening.name, new Vector3(x, opening.y, opening.center),
                    new Vector3(WallThickness, opening.height, opening.width), Quaternion.identity));
        }

        static void AddCutWall(Transform parent, string name, float minimum, float maximum, Opening[] openings, Action<string, float, float> addSegment)
        {
            var cursor = minimum;
            var segment = 1;
            foreach (var opening in openings.OrderBy(item => item.center))
            {
                var half = opening.width * 0.5f + DoorSidePadding;
                var left = Mathf.Clamp(opening.center - half, minimum, maximum);
                var right = Mathf.Clamp(opening.center + half, minimum, maximum);
                if (left - cursor > 0.16f)
                    addSegment($"{name} {segment++:00}", cursor, left);
                cursor = Mathf.Max(cursor, right);
            }
            if (maximum - cursor > 0.16f)
                addSegment($"{name} {segment:00}", cursor, maximum);
        }

        static void AddDoorHeaders(Transform parent, string wallName, float floorY, Opening[] openings,
            Action<(string name, float center, float width, float y, float height)> addHeader)
        {
            var headerHeight = WallHeight - DoorClearHeight;
            for (var index = 0; index < openings.Length; index++)
            {
                var opening = openings[index];
                if (!opening.hasHeader)
                    continue;
                addHeader(($"{wallName} Door {index + 1:00} Header", opening.center,
                    opening.width + DoorSidePadding * 2f, floorY + DoorClearHeight + headerHeight * 0.5f, headerHeight));
            }
        }

        static BoxCollider AddBox(Transform parent, string name, Vector3 center, Vector3 size, Quaternion rotation)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(center, rotation);
            var box = go.AddComponent<BoxCollider>();
            box.center = Vector3.zero;
            box.size = size;
            box.isTrigger = false;
            return box;
        }

        static Renderer FindRenderer(string name)
        {
            var renderer = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(item => item.name == name && item.transform.root.name == "Authoritative Modern Luxury Villa - Identity Transform");
            if (renderer == null)
                throw new InvalidOperationException($"Required renderer not found: {name}");
            return renderer;
        }

        static void AddRasterFloor(Transform parent, string namePrefix, string rendererName, float grid)
        {
            var renderer = FindRenderer(rendererName);
            var filter = renderer.GetComponent<MeshFilter>();
            if (filter == null || filter.sharedMesh == null)
                throw new InvalidOperationException($"Floor renderer has no mesh: {rendererName}");

            var mesh = filter.sharedMesh;
            var vertices = mesh.vertices;
            var triangles = mesh.triangles;
            var bounds = renderer.bounds;
            var cells = new HashSet<Vector2Int>();
            var highestSurface = float.NegativeInfinity;
            var horizontal = new List<Vector3[]>();
            for (var index = 0; index < triangles.Length; index += 3)
            {
                var a = filter.transform.TransformPoint(vertices[triangles[index]]);
                var b = filter.transform.TransformPoint(vertices[triangles[index + 1]]);
                var c = filter.transform.TransformPoint(vertices[triangles[index + 2]]);
                var normal = Vector3.Cross(b - a, c - a).normalized;
                if (Mathf.Abs(normal.y) < 0.82f)
                    continue;
                var averageY = (a.y + b.y + c.y) / 3f;
                if (averageY < bounds.max.y - 0.16f)
                    continue;
                highestSurface = Mathf.Max(highestSurface, averageY);
                horizontal.Add(new[] { a, b, c });
            }

            foreach (var triangle in horizontal)
            {
                var a = triangle[0]; var b = triangle[1]; var c = triangle[2];
                var minX = Mathf.FloorToInt((Mathf.Min(a.x, Mathf.Min(b.x, c.x)) - bounds.min.x) / grid);
                var maxX = Mathf.CeilToInt((Mathf.Max(a.x, Mathf.Max(b.x, c.x)) - bounds.min.x) / grid);
                var minZ = Mathf.FloorToInt((Mathf.Min(a.z, Mathf.Min(b.z, c.z)) - bounds.min.z) / grid);
                var maxZ = Mathf.CeilToInt((Mathf.Max(a.z, Mathf.Max(b.z, c.z)) - bounds.min.z) / grid);
                for (var z = minZ; z < maxZ; z++)
                for (var x = minX; x < maxX; x++)
                {
                    var point = new Vector2(bounds.min.x + (x + 0.5f) * grid, bounds.min.z + (z + 0.5f) * grid);
                    // The source upper-floor mesh visually spans parts of the stair
                    // void. Keep the XR capsule's full headroom clear over the ramp;
                    // the dedicated upper landing restores the walkable exit.
                    if (rendererName == "Floor_Upper_Level_Main" &&
                        point.x > -9.82f && point.x < -7.78f &&
                        point.y > -32.82f && point.y < -26.92f)
                        continue;
                    if (PointInTriangle(point, new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z)))
                        cells.Add(new Vector2Int(x, z));
                }
            }

            if (cells.Count == 0 || !float.IsFinite(highestSurface))
                throw new InvalidOperationException($"No walkable surface extracted from {rendererName}");

            var rectangles = MergeCells(cells);
            for (var index = 0; index < rectangles.Count; index++)
            {
                var rect = rectangles[index];
                var center = new Vector3(
                    bounds.min.x + (rect.x + rect.width * 0.5f) * grid,
                    highestSurface - 0.06f,
                    bounds.min.z + (rect.z + rect.depth * 0.5f) * grid);
                var size = new Vector3(rect.width * grid, 0.12f, rect.depth * grid);
                AddBox(parent, $"{namePrefix} {index + 1:00}", center, size, Quaternion.identity);
            }
        }

        static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c)
        {
            var d1 = Sign(point, a, b);
            var d2 = Sign(point, b, c);
            var d3 = Sign(point, c, a);
            var hasNegative = d1 < 0f || d2 < 0f || d3 < 0f;
            var hasPositive = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNegative && hasPositive);
        }

        static float Sign(Vector2 p1, Vector2 p2, Vector2 p3) =>
            (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);

        static List<RasterRect> MergeCells(HashSet<Vector2Int> cells)
        {
            var result = new List<RasterRect>();
            var rows = cells.GroupBy(cell => cell.y).OrderBy(row => row.Key);
            foreach (var row in rows)
            {
                var xs = row.Select(cell => cell.x).OrderBy(value => value).ToArray();
                var start = xs[0];
                var previous = xs[0];
                for (var index = 1; index <= xs.Length; index++)
                {
                    if (index < xs.Length && xs[index] == previous + 1)
                    {
                        previous = xs[index];
                        continue;
                    }

                    var width = previous - start + 1;
                    var merge = result.LastOrDefault(rect => rect.x == start && rect.width == width && rect.z + rect.depth == row.Key);
                    if (merge != null)
                        merge.depth++;
                    else
                        result.Add(new RasterRect { x = start, z = row.Key, width = width, depth = 1 });

                    if (index < xs.Length)
                    {
                        start = xs[index];
                        previous = xs[index];
                    }
                }
            }
            return result;
        }

        static void ValidateAndReport(GameObject root, Dictionary<string, Transform> groups)
        {
            var allColliders = root.GetComponentsInChildren<Collider>(true);
            Require(allColliders.Length >= 50, "at least 50 deliberate simple colliders");
            Require(allColliders.All(collider => collider is BoxCollider || collider is CapsuleCollider), "only simple collider types");
            Require(root.GetComponentsInChildren<MeshCollider>(true).Length == 0, "no MeshCollider in generated hierarchy");
            Require(GroupPaths.All(path => groups.ContainsKey(path)), "complete collision hierarchy");

            var stairColliders = groups["GroundFloor/Staircase"].GetComponentsInChildren<Collider>(true);
            Require(stairColliders.Length == 2, "one stair ramp and one upper landing without extra sloped bars");
            var ramp = groups["GroundFloor/Staircase"].Find("Traversable Stair Ramp")?.GetComponent<BoxCollider>();
            Require(ramp != null, "single traversable stair ramp exists");
            var visualStair = FindRenderer("Stairs_Main_Structure");
            var visualStairColliders = visualStair.GetComponentsInChildren<Collider>(true);
            Require(visualStairColliders.Length == 0, "visual staircase has no detailed colliders");
            Require(Mathf.Abs(ramp.size.x - visualStair.bounds.size.x) < 0.01f, "ramp width matches visual staircase");
            var rampLowerSurface = ramp.transform.TransformPoint(new Vector3(0f, ramp.size.y * 0.5f, -ramp.size.z * 0.5f));
            var rampUpperSurface = ramp.transform.TransformPoint(new Vector3(0f, ramp.size.y * 0.5f, ramp.size.z * 0.5f));
            Require(Mathf.Abs(rampLowerSurface.y - GroundY) < 0.01f, "ramp begins at lower floor without a vertical gap");
            Require(Mathf.Abs(rampUpperSurface.y - UpperY) < 0.01f, "ramp ends at upper floor without a vertical gap");
            var stairMesh = visualStair.GetComponent<MeshFilter>();
            Require(stairMesh != null && stairMesh.sharedMesh != null, "visual staircase mesh is available for direction validation");
            var vertices = stairMesh.sharedMesh.vertices;
            var triangles = stairMesh.sharedMesh.triangles;
            var visualSamples = Enumerable.Range(0, triangles.Length / 3)
                .Select(index =>
                {
                    var a = stairMesh.transform.TransformPoint(vertices[triangles[index * 3]]);
                    var b = stairMesh.transform.TransformPoint(vertices[triangles[index * 3 + 1]]);
                    var c = stairMesh.transform.TransformPoint(vertices[triangles[index * 3 + 2]]);
                    var normal = Vector3.Cross(b - a, c - a).normalized;
                    return new { Center = (a + b + c) / 3f, Up = Mathf.Abs(Vector3.Dot(normal, Vector3.up)) };
                })
                .Where(sample => sample.Up > 0.9f && sample.Center.y >= GroundY && sample.Center.y <= UpperY + 0.02f)
                .ToArray();
            var lowVisualZ = visualSamples.Where(sample => sample.Center.y < GroundY + 0.8f).Average(sample => sample.Center.z);
            var highVisualZ = visualSamples.Where(sample => sample.Center.y > UpperY - 0.8f).Average(sample => sample.Center.z);
            var visualHorizontalDirection = new Vector3(0f, 0f, highVisualZ - lowVisualZ).normalized;
            var rampHorizontalDirection = Vector3.ProjectOnPlane(rampUpperSurface - rampLowerSurface, Vector3.up).normalized;
            Require(Vector3.Dot(visualHorizontalDirection, rampHorizontalDirection) > 0.99f,
                "ramp rises in the same horizontal direction as the visual staircase");
            var rampHorizontal = Vector3.ProjectOnPlane(rampUpperSurface - rampLowerSurface, Vector3.up);
            var worstTreadClearance = float.MaxValue;
            var worstTreadPoint = Vector3.zero;
            foreach (var sample in visualSamples)
            {
                var sampleHorizontal = Vector3.ProjectOnPlane(sample.Center - rampLowerSurface, Vector3.up);
                var t = Vector3.Dot(sampleHorizontal, rampHorizontal) / rampHorizontal.sqrMagnitude;
                if (t < 0f || t > 1f)
                    continue;
                var clearance = Mathf.Lerp(GroundY, UpperY, t) - sample.Center.y;
                if (clearance < worstTreadClearance)
                {
                    worstTreadClearance = clearance;
                    worstTreadPoint = sample.Center;
                }
            }
            Require(worstTreadClearance >= -0.025f,
                $"ramp surface stays above the visible stair treads; worst clearance={worstTreadClearance:F3} at {worstTreadPoint:F3}");
            var rampSlope = Vector3.Angle(Vector3.ProjectOnPlane(ramp.transform.forward, Vector3.up), ramp.transform.forward);
            var upperLanding = groups["GroundFloor/Staircase"].Find("Upper Stair Landing")?.GetComponent<BoxCollider>();
            var upperFloorSupportAtStair = groups["UpperFloor/Floors"].GetComponentsInChildren<BoxCollider>(true)
                .Where(collider => Mathf.Abs(collider.bounds.max.y - UpperY) < 0.05f)
                .Where(collider => collider.bounds.min.x <= visualStair.bounds.center.x && collider.bounds.max.x >= visualStair.bounds.center.x)
                .Where(collider => collider.bounds.max.z < rampUpperSurface.z)
                .OrderByDescending(collider => collider.bounds.max.z)
                .First();
            Require(upperLanding != null && upperLanding.bounds.max.z >= rampUpperSurface.z - 0.05f,
                "upper landing overlaps the ramp endpoint");
            Require(upperLanding.bounds.min.z <= upperFloorSupportAtStair.bounds.max.z + 0.05f,
                "upper landing overlaps the upper-floor support");
            var xrController = UnityEngine.Object.FindAnyObjectByType<CharacterController>();
            Require(xrController != null && rampSlope <= xrController.slopeLimit, "CharacterController slope limit allows ramp");
            Require(xrController.stepOffset >= 0.20f && xrController.stepOffset <= 0.35f, "CharacterController step offset remains reasonable");

            var upperRailings = groups["UpperFloor/Railings"];
            var eastGuard = upperRailings.Find("Stair Void East Guard")?.GetComponent<BoxCollider>();
            var westGuard = upperRailings.Find("Stair Void West Guard")?.GetComponent<BoxCollider>();
            var lowerGuard = upperRailings.Find("Stair Void Lower Guard")?.GetComponent<BoxCollider>();
            Require(eastGuard != null && westGuard != null && lowerGuard != null,
                "three upper stair-void guards exist");
            Require(Mathf.Abs(eastGuard.transform.position.x - (visualStair.bounds.max.x - 0.14f)) < 0.01f,
                "east stair-void guard hugs the real stair opening");
            Require(Mathf.Abs(westGuard.transform.position.x - (visualStair.bounds.min.x + 0.14f)) < 0.01f,
                "west stair-void guard hugs the real stair opening");
            Require(Mathf.Abs(lowerGuard.bounds.max.z + 28.55f) < 0.01f,
                "bridge south edge remains guarded above the stair opening");
            Require(lowerGuard.bounds.min.x <= -10.10f &&
                    lowerGuard.bounds.max.x <= westGuard.bounds.min.x + 0.01f,
                "bridge edge guard leaves the stair entrance open");
            Require(Mathf.Abs(eastGuard.bounds.max.z + 28.55f) < 0.01f &&
                    Mathf.Abs(westGuard.bounds.max.z + 28.55f) < 0.01f,
                "stair side guards stop before the common bathroom passage");
            var bathroomBridge = groups["UpperFloor/Floors"].Find("Common Bathroom Access Bridge")
                ?.GetComponent<BoxCollider>();
            Require(bathroomBridge != null && Mathf.Abs(bathroomBridge.bounds.max.y - UpperY) < 0.01f &&
                    bathroomBridge.bounds.min.x < -9.95f && bathroomBridge.bounds.max.x > -7.65f,
                "source-visible common bathroom threshold is supported at upper-floor height");

            var teleport = GameObject.Find("Teleportation Area - Kitchen");
            Require(teleport != null && teleport.GetComponent<BoxCollider>() != null, "kitchen teleportation floor collider preserved");
            Require(teleport.GetComponent<TeleportationArea>() != null, "teleportation remains configured");
            var teleportAreas = root.GetComponentsInChildren<TeleportationArea>(true);
            var walkableFloorCount = groups["GroundFloor/Floors"].GetComponentsInChildren<BoxCollider>(true).Length
                                     + groups["UpperFloor/Floors"].GetComponentsInChildren<BoxCollider>(true).Length
                                     + ExteriorWalkableGroups.Sum(path =>
                                         groups[path].GetComponentsInChildren<BoxCollider>(true).Length);
            Require(teleportAreas.Length == walkableFloorCount, "teleportation is restricted to deliberate walkable floor colliders");
            Require(groups["Exterior/Pool"].GetComponentsInChildren<TeleportationArea>(true).Length == 0,
                "pool has no teleportation surface");
            Require(groups["GroundFloor/Kitchen"].GetComponentsInChildren<TeleportationArea>(true).Length == 0 &&
                    groups["GroundFloor/LivingRoom"].GetComponentsInChildren<TeleportationArea>(true).Length == 0 &&
                    groups["UpperFloor/Bedrooms"].GetComponentsInChildren<TeleportationArea>(true).Length == 0,
                "furniture is not teleportable");
            Require(groups["UpperFloor/Railings"].GetComponentsInChildren<BoxCollider>(true).Length >= 3,
                "upper floor stair void and railing safety barriers exist");

            var doorChecks = new Dictionary<string, Vector3>
            {
                ["Kitchen / Dining"] = new(-1.182f, GroundY, -27.435f),
                ["Living / Hall"] = new(-7.558f, GroundY, -27.435f),
                ["Kitchen / Living"] = new(-3.620f, GroundY, -24.753f),
                ["Dining / Hall"] = new(-6.123f, GroundY, -30.474f),
                ["Hall / Bathroom"] = new(-7.558f, GroundY, -32.321f),
                ["Foyer / Living Archway"] = new(-5.0225f, GroundY, -22.114f),
                ["Kitchen / Garage"] = new(-2.6765f, GroundY, -22.114f),
                ["Upper Bedroom 01"] = new(-1.538f, UpperY, -27.426f),
                ["Upper Bedroom 02"] = new(1.935f, UpperY, -27.426f),
                ["Upper Common Bathroom"] = new(-9.571f, UpperY, -28.242f)
            };

            var clearDoors = new List<string>();
            foreach (var pair in doorChecks)
            {
                var bottom = pair.Value + Vector3.up * 0.32f;
                var top = pair.Value + Vector3.up * 1.48f;
                var overlaps = Physics.OverlapCapsule(bottom, top, 0.28f)
                    .Where(collider => collider.transform.IsChildOf(root.transform))
                    .Where(collider => collider.bounds.max.y > pair.Value.y + 0.65f)
                    .ToArray();
                Require(overlaps.Length == 0,
                    $"doorway clear: {pair.Key}; blockers={string.Join(", ", overlaps.Select(collider => collider.name))}");
                clearDoors.Add(pair.Key);
            }

            var report = new StringBuilder();
            report.AppendLine("# Modern Villa - collision pass validation");
            report.AppendLine();
            report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"Scene: `{ScenePath}`");
            report.AppendLine();
            report.AppendLine("## Generated hierarchy");
            report.AppendLine();
            foreach (var groupName in GroupPaths)
            {
                var count = groups[groupName].GetComponentsInChildren<Collider>(true).Length;
                report.AppendLine($"- `Collisions/{groupName}`: {count} simple collider(s)");
            }
            report.AppendLine($"- Total: {allColliders.Length} simple collider(s)");
            report.AppendLine("- MeshColliders in generated hierarchy: 0");
            report.AppendLine();
            report.AppendLine("## Automated checks");
            report.AppendLine();
            report.AppendLine("- PASS: required hierarchy exists.");
            report.AppendLine("- PASS: only BoxCollider/CapsuleCollider components are used.");
            report.AppendLine($"- PASS: {teleportAreas.Length} TeleportationArea components exist only on deliberate floor colliders.");
            report.AppendLine("- PASS: pool is intentionally inaccessible and has no teleportation surface.");
            report.AppendLine("- PASS: upper-floor stair void and railing safety barriers are present.");
            report.AppendLine("- PASS: upper floor was rasterized into merged simple boxes, preserving its openings.");
            report.AppendLine($"- PASS: one {rampSlope:F1}-degree stair ramp links lower Y={rampLowerSurface.y:F3} to upper Y={rampUpperSurface.y:F3}.");
            report.AppendLine($"- PASS: ramp world endpoints are lower {rampLowerSurface:F3} and upper {rampUpperSurface:F3}.");
            report.AppendLine("- PASS: ramp ascent direction matches the visual staircase and its surface clears the visible treads.");
            report.AppendLine($"- PASS: ramp width {ramp.size.x:F3} m matches the visual staircase width.");
            report.AppendLine("- PASS: the visual villa/stair hierarchy has no detailed colliders.");
            report.AppendLine($"- PASS: CharacterController Slope Limit={xrController.slopeLimit:F1}, Step Offset={xrController.stepOffset:F2}, Skin Width={xrController.skinWidth:F3}.");
            report.AppendLine("- PASS: no full-height stair-side collider blocks the open under-stair passage.");
            foreach (var doorway in clearDoors)
                report.AppendLine($"- PASS: 0.56 m capsule clearance through {doorway}.");
            report.AppendLine();
            report.AppendLine("## Scope requiring headset validation");
            report.AppendLine();
            report.AppendLine("- Physical comfort and collision feel on Quest 3.");
            report.AppendLine("- Final controller-driven walk through every door and around every furniture item.");
            report.AppendLine("- Teleport ray targeting across intended walkable surfaces.");

            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, report.ToString(), new UTF8Encoding(false));
        }

        static void ValidateExteriorAndReport(GameObject root, Dictionary<string, Transform> groups)
        {
            const int expectedExteriorColliderCount = 32;
            const int expectedExteriorTeleportCount = 22;

            var exterior = root.transform.Find("Exterior");
            Require(exterior != null, "Collisions/Exterior exists");
            var exteriorColliders = exterior.GetComponentsInChildren<BoxCollider>(true);
            Require(exteriorColliders.Length == expectedExteriorColliderCount,
                $"exactly {expectedExteriorColliderCount} deliberate exterior BoxColliders; found {exteriorColliders.Length}");
            Require(exteriorColliders.All(collider => collider.enabled && collider.gameObject.activeInHierarchy),
                "all exterior collider GameObjects and BoxCollider components are enabled");
            Require(exteriorColliders.All(collider => !collider.isTrigger),
                "all exterior walkable and boundary colliders are solid");
            Require(exteriorColliders.All(collider => collider.gameObject.layer == 0),
                "all exterior colliders remain on the same Default layer as the existing collision hierarchy");
            Require(exterior.GetComponentsInChildren<MeshCollider>(true).Length == 0,
                "no MeshCollider was introduced outside");

            foreach (var path in ExteriorWalkableGroups)
            {
                var boxes = groups[path].GetComponentsInChildren<BoxCollider>(true);
                var areas = groups[path].GetComponentsInChildren<TeleportationArea>(true);
                Require(boxes.Length > 0, $"{path} has walkable collision coverage");
                Require(areas.Length == boxes.Length, $"every collider in {path} is teleportable");
            }

            var exteriorTeleportAreas = exterior.GetComponentsInChildren<TeleportationArea>(true);
            Require(exteriorTeleportAreas.Length == expectedExteriorTeleportCount,
                $"exactly {expectedExteriorTeleportCount} exterior walkable teleport surfaces; found {exteriorTeleportAreas.Length}");
            Require(groups["Exterior/Pool"].GetComponentsInChildren<TeleportationArea>(true).Length == 0,
                "pool-water enclosure is not teleportable");
            Require(groups["Exterior/Boundaries"].GetComponentsInChildren<TeleportationArea>(true).Length == 0,
                "property boundaries are not teleportable");

            var water = FindRenderer("Pool_Water_Surface").bounds;
            var waterProbe = new Vector3(water.center.x, water.max.y + 0.30f, water.center.z);
            var waterHits = Physics.RaycastAll(waterProbe, Vector3.down, 0.70f)
                .Where(hit => hit.collider.transform.IsChildOf(exterior))
                .Where(hit => hit.collider.GetComponent<TeleportationArea>() != null)
                .ToArray();
            Require(waterHits.Length == 0, "no hidden exterior teleport floor exists below the pool water");

            var pathSamples = 0;
            pathSamples += RequireWalkablePath(exterior, "villa to terrace and garden",
                new Vector2(-5.43f, -36.08f), new Vector2(-5.43f, -41.70f),
                new Vector2(-5.43f, -51.50f), new Vector2(0f, -54.0f));
            pathSamples += RequireWalkablePath(exterior, "west-side garden",
                new Vector2(-23.20f, -22.18f), new Vector2(-23.20f, -44.95f),
                new Vector2(-18.0f, -50.0f));
            pathSamples += RequireWalkablePath(exterior, "east-side garden",
                new Vector2(12.57f, -22.18f), new Vector2(12.57f, -44.95f),
                new Vector2(16.0f, -50.0f));
            pathSamples += RequireWalkablePath(exterior, "complete pool perimeter",
                new Vector2(0f, -57.90f), new Vector2(-20.50f, -57.90f),
                new Vector2(-20.50f, -68.60f), new Vector2(20.0f, -68.60f),
                new Vector2(20.0f, -57.90f), new Vector2(0f, -57.90f));
            pathSamples += RequireWalkablePath(exterior, "garden to cabin",
                new Vector2(-10.0f, -50.0f), new Vector2(-22.60f, -50.0f),
                new Vector2(-27.70f, -50.0f), new Vector2(-27.70f, -55.0f),
                new Vector2(-27.70f, -65.0f));

            var debug = root.GetComponent<CollisionDebugVisualizer>();
            Require(debug != null && debug.ToggleKey == UnityEngine.InputSystem.Key.F8,
                "F8 collision debug remains available");

            var report = new StringBuilder();
            report.AppendLine("# Modern Villa - exterior walkable collision audit");
            report.AppendLine();
            report.AppendLine($"Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            report.AppendLine($"Scene: `{ScenePath}`");
            report.AppendLine();
            report.AppendLine("## Surface audit");
            report.AppendLine();
            report.AppendLine("| Zone | Visible source mesh | Previous coverage | Missing coverage found | Final walkable surface Y |");
            report.AppendLine("|---|---|---|---|---:|");
            report.AppendLine("| Front drive / entrance | `Floor_Garage_Concrete` | Existing single fitted floor | None on the authored concrete | 64.934 m |");
            report.AppendLine("| Villa to rear terrace | `Landscape_Ground` + `Wooden_Deck_Platform` | Flat rear-patio helper did not follow the raised deck | Threshold-to-deck vertical gap | 64.878 -> 65.505 m |");
            report.AppendLine("| Wooden terrace | `Wooden_Deck_Platform` | Broad helper sat below part of the visible deck | Exact raised deck surface | 65.505 m |");
            report.AppendLine("| Side gardens | `Landscape_Ground` / `Grass_Field` | No walkable ground; safety walls blocked access | Both side routes and terrain rise | 64.934 -> 65.335 m |");
            report.AppendLine("| Rear garden | `Landscape_Ground` / `Grass_Field` / `Garden_Pathway` | No floor collision | Continuous rear lawn around the pool | 65.335 m |");
            report.AppendLine("| Pool deck | `Pool_Structure` / `Garden_Pathway` | Pool barriers only | Four walkable strips surrounding water | 65.459 m |");
            report.AppendLine("| Cabin path | `Garden_Pathway` / `Landscape_Ground` | No collision | Continuous approach to pavilion | 65.335 m |");
            report.AppendLine("| Cabin / quincho | `Quincho_Floor_Tiles` | Floor was grouped as generic terrace | Explicit accessible cabin floor | 65.582 m |");
            report.AppendLine();
            report.AppendLine("The source model names the small open exterior structure `Quincho`; it is intentionally accessible.");
            report.AppendLine();
            report.AppendLine("## Exterior hierarchy");
            report.AppendLine();
            foreach (var path in groups.Keys.OrderBy(path => path, StringComparer.Ordinal))
            {
                var boxes = groups[path].GetComponentsInChildren<BoxCollider>(true).Length;
                var areas = groups[path].GetComponentsInChildren<TeleportationArea>(true).Length;
                report.AppendLine($"- `Collisions/{path}`: {boxes} BoxCollider(s), {areas} TeleportationArea(s)");
            }
            report.AppendLine($"- Total exterior: {exteriorColliders.Length} BoxColliders");
            report.AppendLine();
            report.AppendLine("## Automated validation");
            report.AppendLine();
            report.AppendLine("- PASS: exterior hierarchy was rebuilt independently; the interior hierarchy was not rebuilt.");
            report.AppendLine("- PASS: all exterior helpers are enabled, solid BoxColliders on the existing collision layer.");
            report.AppendLine("- PASS: garden safety walls that hid missing ground were removed.");
            report.AppendLine("- PASS: walkable zones overlap at transitions to prevent capsule-sized gaps.");
            report.AppendLine("- PASS: 22 exterior walkable surfaces support teleportation.");
            report.AppendLine("- PASS: pool water and property boundaries are not teleportable.");
            report.AppendLine("- PASS: pool center contains no hidden walkable helper beneath the water.");
            report.AppendLine($"- PASS: {pathSamples} sampled points form continuous routes from villa to terrace, both side gardens, around the pool and into the cabin.");
            report.AppendLine("- PASS: F8 visualization distinguishes terrace, garden, pool deck, cabin, transitions and safety boundaries.");
            report.AppendLine();
            report.AppendLine("## Quest 3 physical checklist");
            report.AppendLine();
            report.AppendLine("- [ ] Exit villa onto terrace without a drop");
            report.AppendLine("- [ ] Walk across terrace and both side gardens");
            report.AppendLine("- [ ] Walk around all four sides of the pool");
            report.AppendLine("- [ ] Walk to and onto the cabin / quincho floor");
            report.AppendLine("- [ ] Return continuously to the villa");
            report.AppendLine("- [ ] Confirm pool barriers prevent accidental entry");
            report.AppendLine("- [ ] Confirm teleport ray lands on every exterior walkable zone and never on water");

            Directory.CreateDirectory(Path.GetDirectoryName(ExteriorReportPath));
            File.WriteAllText(ExteriorReportPath, report.ToString(), new UTF8Encoding(false));
        }

        static int RequireWalkablePath(Transform exterior, string label, params Vector2[] waypoints)
        {
            const float sampleSpacing = 0.25f;
            const float maximumStep = 0.31f;
            var sampleCount = 0;
            float? previousSurfaceY = null;

            for (var segment = 0; segment < waypoints.Length - 1; segment++)
            {
                var from = waypoints[segment];
                var to = waypoints[segment + 1];
                var distance = Vector2.Distance(from, to);
                var steps = Mathf.Max(1, Mathf.CeilToInt(distance / sampleSpacing));
                for (var step = segment == 0 ? 0 : 1; step <= steps; step++)
                {
                    var point = Vector2.Lerp(from, to, step / (float)steps);
                    var hit = Physics.RaycastAll(new Vector3(point.x, 70f, point.y), Vector3.down, 10f)
                        .Where(candidate => candidate.collider.transform.IsChildOf(exterior))
                        .Where(candidate => candidate.collider.GetComponent<TeleportationArea>() != null)
                        .OrderBy(candidate => candidate.distance)
                        .FirstOrDefault();
                    Require(hit.collider != null,
                        $"continuous exterior route '{label}' has walkable support at X={point.x:F2}, Z={point.y:F2}");
                    if (previousSurfaceY.HasValue)
                        Require(Mathf.Abs(hit.point.y - previousSurfaceY.Value) <= maximumStep,
                            $"continuous exterior route '{label}' has no vertical seam above {maximumStep:F2} m at X={point.x:F2}, Z={point.y:F2}");
                    previousSurfaceY = hit.point.y;
                    sampleCount++;
                }
            }

            return sampleCount;
        }

        static void Require(bool condition, string description)
        {
            if (!condition)
                throw new InvalidOperationException("Collision validation failed: " + description);
        }
    }
}
