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
        }
        finally { UnityEngine.Object.DestroyImmediate(probe); }
    }

    private static void RenderExteriorPreviews(Camera camera, RenderTexture target)
    {
        camera.fieldOfView = 48f;
        RenderShot(camera, target, new Vector3(86f, 98f, -151f), new Vector3(0f, 2f, -42f), "12-exterior-overview.png");
        camera.fieldOfView = 75f;
        RenderShot(camera, target, new Vector3(0f, 1.7f, -90f), new Vector3(0f, 3f, -29.5f), "13-exterior-player.png");
        RenderShot(camera, target, new Vector3(-45f, 7f, -43f), new Vector3(-37f, 2.4f, -65f), "14-aa-platform.png");
    }
}
