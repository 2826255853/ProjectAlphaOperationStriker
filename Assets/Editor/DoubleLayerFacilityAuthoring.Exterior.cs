using System;
using System.Linq;
using UnityEngine;

public static partial class DoubleLayerFacilityAuthoring
{
    private static void ValidateExterior()
    {
        Physics.SyncTransforms();
        var objects = UnityEngine.Object.FindObjectsByType<MapForgeObjectProperties>(FindObjectsSortMode.None)
            .ToDictionary(item => item.objectId);
        if (!objects.TryGetValue("exterior-yard-slab", out var yard))
            throw new InvalidOperationException("Missing exterior defense yard.");
        var renderer = yard.GetComponentInChildren<Renderer>();
        if (renderer == null || Vector3.Distance(renderer.bounds.size, new Vector3(96f, .5f, 64f)) > .02f ||
            Vector3.Distance(renderer.bounds.center, new Vector3(0f, -.25f, -72f)) > .02f)
            throw new InvalidOperationException("Exterior yard must measure 96 x 64 m, centered at z=-72.");

        foreach (float side in new[] { -1f, 1f })
        {
            string id = side < 0f ? "wing-left-slab" : "wing-right-slab";
            Bounds wing = objects[id].GetComponentInChildren<Renderer>().bounds;
            if (Vector3.Distance(wing.size, new Vector3(32f, .5f, 96f)) > .02f ||
                Vector3.Distance(wing.center, new Vector3(side * 60f, -.25f, 8f)) > .02f)
                throw new InvalidOperationException("Invalid wing slab dimensions: " + id);
            foreach (float x in new[] { 43.9f, 44.1f, 74f })
            foreach (float z in new[] { -39f, 39f, 55f })
                RequireExteriorSupport(new Vector3(side * x, 0f, z));
        }

        foreach (float x in new[] { -46f, -24f, 0f, 24f, 46f })
        foreach (float z in new[] { -102f, -92f, -82f })
            RequireExteriorSupport(new Vector3(x, 0f, z));
        foreach (float x in new[] { -30f, 0f, 30f })
        foreach (float z in new[] { -40.1f, -40f, -39.9f, -35f })
            RequireExteriorSupport(new Vector3(x, 0f, z));

        var pads = objects.Values.Where(item => item.objectId.StartsWith("exterior-defense-", StringComparison.Ordinal))
            .ToArray();
        if (pads.Length != 8)
            throw new InvalidOperationException("Expected eight exterior ground defense pads.");
        foreach (var pad in pads)
        {
            Bounds bounds = pad.GetComponentInChildren<Renderer>().bounds;
            if (Mathf.Abs(bounds.size.x - 6f) > .02f || Mathf.Abs(bounds.size.z - 8f) > .02f)
                throw new InvalidOperationException("Exterior ground defense pad must measure 6 x 8 m: " + pad.objectId);
        }

        foreach (string side in new[] { "left", "right" })
        {
            string id = "exterior-aa-" + side + "-slab";
            if (!objects.TryGetValue(id, out var platform))
                throw new InvalidOperationException("Missing exterior AA platform: " + id);
            var platformRenderer = platform.GetComponentInChildren<Renderer>();
            if (platformRenderer == null || Mathf.Abs(platformRenderer.bounds.max.y - 2.4f) > .02f ||
                Mathf.Abs(platformRenderer.bounds.size.x - 12f) > .02f ||
                Mathf.Abs(platformRenderer.bounds.size.z - 10f) > .02f)
                throw new InvalidOperationException("Exterior AA platform must measure 12 x 10 m with a 2.4 m walk surface: " + id);
        }

        ValidateExteriorPlayerMovement();
        Debug.Log("FACILITY_EXTERIOR_PASS: 96 x 64 m supported yard, eight 6 x 8 m ground pads, " +
            "two 12 x 10 m AA platforms, actual FPS capsule walked both ramps and crossed the entrance apron.");
        Debug.Log("FACILITY_REAR_WALK_PASS: actual FPS capsule walked both side roads, both freight ramps in both directions and the upper crosswalk.");
        Debug.Log("FACILITY_WING_WALK_PASS: actual FPS capsule crossed the pump hall and west balcony, and walked the east steel stair to the control cab and upper corridor in both directions.");
    }

    private static void RequireExteriorSupport(Vector3 expected)
    {
        var hits = Physics.RaycastAll(expected + Vector3.up * .6f, Vector3.down, 1.1f,
                ~0, QueryTriggerInteraction.Ignore)
            .Where(hit => hit.normal.y > .9f && Mathf.Abs(hit.point.y - expected.y) < .08f).ToArray();
        if (hits.Length == 0)
            throw new InvalidOperationException("Exterior floor has a gap at " + expected);
    }

    private static void ValidateExteriorPlayerMovement()
    {
        var prefab = Resources.Load<GameObject>("FPSPlayer");
        var source = prefab != null ? prefab.GetComponentInChildren<CharacterController>(true) : null;
        if (source == null) throw new InvalidOperationException("FPSPlayer has no CharacterController.");
        var probe = new GameObject("Temporary Exterior Walk Validation");
        try
        {
            var controller = probe.AddComponent<CharacterController>();
            controller.height = source.height;
            controller.radius = source.radius;
            controller.center = source.center;
            controller.stepOffset = source.stepOffset;
            controller.slopeLimit = source.slopeLimit;
            controller.skinWidth = source.skinWidth;
            controller.minMoveDistance = 0f;
            foreach (float x in new[] { -36f, 36f })
            {
                var route = new[]
                {
                    new Vector3(x, 0f, -47f),
                    new Vector3(x, 2.4f, -62f),
                    new Vector3(x, 2.4f, -66f)
                };
                controller.enabled = false;
                probe.transform.position = route[0] + Vector3.up * .05f;
                controller.enabled = true;
                Physics.SyncTransforms();
                for (int i = 1; i < route.Length; i++)
                    WalkTo(controller, route[i], "exterior AA platform ascent " + x + " segment " + i);
                for (int i = route.Length - 2; i >= 0; i--)
                    WalkTo(controller, route[i], "exterior AA platform descent " + x + " segment " + i);
            }
            controller.enabled = false;
            probe.transform.position = new Vector3(-30f, .05f, -35f);
            controller.enabled = true;
            Physics.SyncTransforms();
            WalkTo(controller, new Vector3(30f, 0f, -35f), "entrance apron transverse passage");
            WalkTo(controller, new Vector3(-30f, 0f, -35f), "entrance apron return passage");

            foreach (float side in new[] { -1f, 1f })
            {
                float road = 40f * side;
                float lane = 27f * side;
                controller.enabled = false;
                probe.transform.position = new Vector3(road, .05f, -42f);
                controller.enabled = true;
                Physics.SyncTransforms();
                WalkTo(controller, new Vector3(road, 0f, 8f), "side service road");
                WalkTo(controller, new Vector3(road, 0f, 65f), "rear yard access");
                WalkTo(controller, new Vector3(road, 0f, 116f), "perimeter apron");
                WalkTo(controller, new Vector3(lane, 0f, 116f), "rear freight approach");
                WalkTo(controller, new Vector3(lane, UpperFloorHeight, 63f), "rear freight ramp ascent");
                WalkTo(controller, new Vector3(lane, UpperFloorHeight, 48f), "upper loading door");
                WalkTo(controller, new Vector3(lane, UpperFloorHeight, 63f), "return to upper loading apron");
                WalkTo(controller, new Vector3(-lane, UpperFloorHeight, 63f), "upper service crosswalk");
                WalkTo(controller, new Vector3(-lane, 0f, 116f), "rear freight ramp descent");
            }

            WalkWingRoute(controller, "west pump hall access", new Vector3(-31f, 0f, 12f),
                new Vector3(-49f, 0f, 12f), new Vector3(-53f, 0f, 12f));
            WalkWingRoute(controller, "west valve gallery", new Vector3(-31f, UpperFloorHeight, 17.5f),
                new Vector3(-41f, UpperFloorHeight, 17.5f));
            WalkWingRoute(controller, "east workshop door", new Vector3(27f, 0f, 11.2f),
                new Vector3(46f, 0f, 11.2f));
            WalkWingRoute(controller, "east external steel stair", new Vector3(54f, 0f, 23.5f),
                new Vector3(54f, HalfStairHeight, 33.45f), new Vector3(50.8f, HalfStairHeight, 33.45f),
                new Vector3(50.8f, UpperFloorHeight, 23.5f), new Vector3(39.2f, UpperFloorHeight, 23.5f),
                new Vector3(39.2f, UpperFloorHeight, 28f));
            WalkWingRoute(controller, "east bridge to upper corridor", new Vector3(49f, UpperFloorHeight, 23.5f),
                new Vector3(31f, UpperFloorHeight, 23.5f), new Vector3(31f, UpperFloorHeight, 33f));
        }
        finally { UnityEngine.Object.DestroyImmediate(probe); }
    }

    private static void WalkWingRoute(CharacterController controller, string label, params Vector3[] route)
    {
        controller.enabled = false;
        controller.transform.position = route[0] + Vector3.up * .05f;
        controller.enabled = true;
        Physics.SyncTransforms();
        for (int i = 1; i < route.Length; i++)
            WalkTo(controller, route[i], label + " outbound segment " + i);
        for (int i = route.Length - 2; i >= 0; i--)
            WalkTo(controller, route[i], label + " return segment " + i);
    }

    private static void RenderExteriorPreviews(Camera camera, RenderTexture target)
    {
        camera.fieldOfView = 48f;
        RenderShot(camera, target, new Vector3(86f, 98f, -151f), new Vector3(0f, 2f, -42f), "12-exterior-overview.png");
        camera.fieldOfView = 75f;
        RenderShot(camera, target, new Vector3(0f, 1.7f, -90f), new Vector3(0f, 3f, -29.5f), "13-exterior-player.png");
        RenderShot(camera, target, new Vector3(-45f, 7f, -43f), new Vector3(-37f, 2.4f, -65f), "14-aa-platform.png");
    }

    private static void RenderRearPreviews(Camera camera, RenderTexture target)
    {
        camera.fieldOfView = 50f;
        RenderShot(camera, target, new Vector3(88f, 68f, 147f), new Vector3(0f, 3f, 79f), "15-rear-passage.png");
        camera.fieldOfView = 68f;
        RenderShot(camera, target, new Vector3(-28f, 2.8f, 123f), new Vector3(-27f, 7f, 54f), "16-rear-freight-approach.png");
        RenderShot(camera, target, new Vector3(13f, 8.3f, 63f), new Vector3(27f, 7.7f, 55f), "17-rear-loading-door.png");
    }

    private static void RenderWingPreviews(Camera camera, RenderTexture target)
    {
        camera.farClipPlane = 420f;
        camera.fieldOfView = 57f;
        RenderShot(camera, target, new Vector3(115f, 176f, -122f), new Vector3(0f, 0f, 9f), "18-wing-site-overview.png");
        camera.fieldOfView = 64f;
        RenderShot(camera, target, new Vector3(-82f, 30f, -38f), new Vector3(-48f, 2f, 7f), "19-cooling-wing.png");
        RenderShot(camera, target, new Vector3(83f, 29f, -26f), new Vector3(47f, 3f, 20f), "20-heavy-workshop-wing.png");
        RenderShot(camera, target, new Vector3(39.5f, 8.1f, 27.2f), new Vector3(30f, 7.5f, 73f), "21-crane-cab-view.png");
    }
}
