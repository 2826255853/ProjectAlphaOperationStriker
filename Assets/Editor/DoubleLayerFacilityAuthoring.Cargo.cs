using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public static partial class DoubleLayerFacilityAuthoring
{
    // Production geometry checks are kept with authoring, not in imported test sources.
    private static void ValidateCargoRoutes()
    {
        Physics.SyncTransforms();
        ValidateCargoDimensions();
        ValidateDefenseReservations();
        var document = MapForgeSceneOrganization.Parse(File.ReadAllText(SourcePath));
        var authored = document.objects.Where(node => node.id.StartsWith("route-cargo-", StringComparison.Ordinal))
            .ToDictionary(node => node.id);
        var instances = UnityEngine.Object.FindObjectsByType<MapForgeObjectProperties>(FindObjectsSortMode.None)
            .ToDictionary(item => item.objectId);
        var rearReports = new List<CargoRouteReport>();
        foreach (string side in new[] { "left", "right" })
            VisitCargoRoute("route-cargo-upper-" + side + "-start", new List<string>(), authored, instances, rearReports);
        Debug.Log($"FACILITY_REAR_ROUTES_PASS: {rearReports.Count} routes from the rear perimeter through the upper loading doors to the core corridor; " +
            $"{rearReports.Min(route => route.lengthMeters):F2}..{rearReports.Max(route => route.lengthMeters):F2} m.");
        var pumpReports = new List<CargoRouteReport>();
        VisitCargoRoute("route-cargo-pump-start", new List<string>(), authored, instances, pumpReports);
        var workshopReports = new List<CargoRouteReport>();
        VisitCargoRoute("route-cargo-workshop-start", new List<string>(), authored, instances, workshopReports);
        Debug.Log($"FACILITY_WORKSHOP_ROUTES_PASS: {workshopReports.Count} routes through the east doorway; " +
            $"{workshopReports.Min(route => route.lengthMeters):F2}..{workshopReports.Max(route => route.lengthMeters):F2} m.");
        Debug.Log($"FACILITY_PUMP_ROUTES_PASS: {pumpReports.Count} routes from the pump intake through the west doorway; " +
            $"{pumpReports.Min(route => route.lengthMeters):F2}..{pumpReports.Max(route => route.lengthMeters):F2} m.");
        var reports = new List<CargoRouteReport>();
        foreach (string entrance in new[] { "left", "middle", "right" })
        {
            string start = "route-cargo-" + entrance + "-start";
            if (!authored.ContainsKey(start))
                throw new InvalidOperationException("Missing authored cargo route start " + start);
            VisitCargoRoute(start, new List<string>(), authored, instances, reports);
        }
        float shortest = reports.Min(route => route.lengthMeters);
        float longest = reports.Max(route => route.lengthMeters);
        if ((longest - shortest) / shortest > .15f)
            throw new InvalidOperationException($"Cargo entrance routes differ by more than 15%: {shortest:F2}..{longest:F2} m.");
        Directory.CreateDirectory(OutputDirectory);
        File.WriteAllText(Path.Combine(OutputDirectory, "cargo-geometry-validation.json"),
            JsonUtility.ToJson(new CargoValidationReport { routes = reports.ToArray(), rearRoutes = rearReports.ToArray(),
                pumpRoutes = pumpReports.ToArray(), workshopRoutes = workshopReports.ToArray(),
                shortestMeters = shortest, longestMeters = longest }, true));
        Debug.Log($"FACILITY_CARGO_ROUTES_PASS: {reports.Count} authored geometry routes, {shortest:F2}..{longest:F2} m; " +
            "8 x 12 x 4.2 m proxy with 0.6 m headroom, full footprint support and swept turns. No runtime AI pathfinding claim.");
    }

    private static void ValidateCargoDimensions()
    {
        var slab = UnityEngine.Object.FindObjectsByType<MapForgeObjectProperties>(FindObjectsSortMode.None)
            .FirstOrDefault(item => item.objectId == "ground-slab");
        var shape = slab != null ? slab.GetComponentInChildren<Renderer>() : null;
        if (shape == null || Vector3.Distance(shape.bounds.center, new Vector3(0f, -.25f, 8f)) > .01f ||
            Vector3.Distance(shape.bounds.size, new Vector3(72f, .5f, 96f)) > .01f)
            throw new InvalidOperationException("Facility ground slab must be 72 x 96 m, centered at z=8.");
    }

    private static void ValidateDefenseReservations()
    {
        var pads = UnityEngine.Object.FindObjectsByType<MapForgeObjectProperties>(FindObjectsSortMode.None)
            .Where(item => item.tags != null && item.tags.Contains("defense-reservation")).ToArray();
        if (pads.Length != 27) throw new InvalidOperationException("Expected 27 facility defense reservations.");
        float area = 0f;
        foreach (var pad in pads)
        {
            Bounds bounds = pad.GetComponentInChildren<Renderer>().bounds;
            float floor = pad.transform.position.y - .0125f;
            var blockers = Physics.OverlapBox(new Vector3(bounds.center.x, floor + 2.45f, bounds.center.z),
                new Vector3(bounds.extents.x - .1f, 2.35f, bounds.extents.z - .1f), Quaternion.identity,
                ~0, QueryTriggerInteraction.Ignore);
            if (blockers.Length != 0)
                throw new InvalidOperationException("Defense reservation blocked: " + pad.objectId + ": " +
                    string.Join(", ", blockers.Select(item => item.GetComponentInParent<MapForgeObjectProperties>()?.objectId ?? item.name)));
            foreach (float x in new[] { bounds.min.x + .1f, bounds.center.x, bounds.max.x - .1f })
            foreach (float z in new[] { bounds.min.z + .1f, bounds.center.z, bounds.max.z - .1f })
                CheckFloor(new Vector3(x, floor + .5f, z), floor);
            area += bounds.size.x * bounds.size.z;
        }
        Debug.Log($"FACILITY_DEFENSE_RESERVATIONS_PASS: {pads.Length} obstacle-free pads, {area:F0} square meters; runtime placement cells are validated separately.");
    }

    private static void VisitCargoRoute(string id, List<string> route,
        Dictionary<string, MapForgeSceneOrganization.Node> authored,
        Dictionary<string, MapForgeObjectProperties> instances, List<CargoRouteReport> reports)
    {
        if (route.Contains(id) || route.Count > 200)
            throw new InvalidOperationException("Cargo route has a cycle at " + id);
        if (!authored.TryGetValue(id, out var node) || !instances.ContainsKey(id))
            throw new InvalidOperationException("Cargo route links to missing node " + id);
        route.Add(id);
        var links = node.unity != null && node.unity.pathNode != null ? node.unity.pathNode.links : null;
        if (links == null || links.Length == 0)
        {
            if (node.unity == null || node.unity.pathNode == null || node.unity.pathNode.role != "end")
                throw new InvalidOperationException("Cargo route must end explicitly: " + id);
            var points = route.Select(key => instances[key].transform.position).ToArray();
            ValidateCargoPolyline(points, route[0] + " -> " + id);
            float length = 0f;
            for (int i = 1; i < points.Length; i++) length += Vector3.Distance(points[i - 1], points[i]);
            reports.Add(new CargoRouteReport { id = route[0] + " -> " + id, lengthMeters = length, nodeIds = route.ToArray() });
        }
        else
            foreach (string link in links) VisitCargoRoute(link, route, authored, instances, reports);
        route.RemoveAt(route.Count - 1);
    }

    private static void ValidateCargoPolyline(Vector3[] points, string label)
    {
        if (points.Length < 2) throw new InvalidOperationException("Cargo route is too short: " + label);
        for (int segment = 1; segment < points.Length; segment++)
        {
            Vector3 direction = points[segment] - points[segment - 1];
            if (direction.sqrMagnitude < .0001f) continue;
            Quaternion facing = Quaternion.LookRotation(direction.normalized, Vector3.up);
            int steps = Mathf.Max(1, Mathf.CeilToInt(direction.magnitude / .5f));
            for (int i = 0; i <= steps; i++)
                CheckCargoPose(Vector3.Lerp(points[segment - 1], points[segment], i / (float)steps),
                    facing, label + " segment " + segment);
            if (segment < points.Length - 1)
            {
                Vector3 next = points[segment + 1] - points[segment];
                if (next.sqrMagnitude < .0001f) continue;
                Quaternion nextFacing = Quaternion.LookRotation(next.normalized, Vector3.up);
                int turns = Mathf.Max(1, Mathf.CeilToInt(Quaternion.Angle(facing, nextFacing) / 5f));
                for (int i = 0; i <= turns; i++)
                    CheckCargoPose(points[segment], Quaternion.Slerp(facing, nextFacing, i / (float)turns),
                        label + " turn " + segment);
            }
        }
    }

    private static void CheckCargoPose(Vector3 foot, Quaternion facing, string label)
    {
        const float halfWidth = 4f, halfLength = 6f, clearance = 4.8f;
        if (!Physics.Raycast(foot + Vector3.up * .45f, Vector3.down, out var centerFloor, 1f,
                ~0, QueryTriggerInteraction.Ignore) || Mathf.Abs(centerFloor.point.y - foot.y) > .15f)
            throw new InvalidOperationException($"Cargo route height is not on the authored floor at {foot}: {label}.");
        var supports = new HashSet<Collider>();
        float lift = 0f;
        // Check the whole footprint, including the ends during changes of grade.
        foreach (float x in new[] { -halfWidth, 0f, halfWidth })
        foreach (float z in new[] { -halfLength, -3f, 0f, 3f, halfLength })
        {
            Vector3 sample = foot + facing * new Vector3(x, 0f, z);
            // Segment end faces can win a ray exactly on a ramp seam. Probe just
            // inside both adjacent pieces and choose the upward-facing surface.
            var hits = new[] { Vector3.zero, Vector3.forward * .003f, Vector3.back * .003f }
                .SelectMany(offset => Physics.RaycastAll(sample + offset + Vector3.up * 1.3f,
                    Vector3.down, 3f, ~0, QueryTriggerInteraction.Ignore))
                .Where(candidate => candidate.normal.y >= .8f && Mathf.Abs(candidate.point.y - sample.y) <= 1.1f)
                .OrderBy(candidate => candidate.distance).ToArray();
            if (hits.Length == 0)
                throw new InvalidOperationException($"Cargo footprint lacks nearby support at {sample}: {label}. " +
                    string.Join(", ", Physics.RaycastAll(sample + Vector3.up * 1.3f, Vector3.down, 3f)
                        .Select(candidate => $"{candidate.collider.name} y={candidate.point.y:F3} normal={candidate.normal}")));
            var hit = hits[0];
            supports.Add(hit.collider);
            lift = Mathf.Max(lift, hit.point.y - sample.y);
        }
        // Extra lift at a grade break is measured from actual supports, not ignored as a collision.
        Vector3 center = foot + Vector3.up * (lift + .08f) + facing * new Vector3(0f, clearance * .5f, 0f);
        var blocked = Physics.OverlapBox(center, new Vector3(halfWidth, clearance * .5f - .02f, halfLength),
                facing, ~0, QueryTriggerInteraction.Ignore).Where(collider => !supports.Contains(collider)).ToArray();
        if (blocked.Length > 0)
            throw new InvalidOperationException($"Cargo proxy blocked at {foot}: {label}: " +
                string.Join(", ", blocked.Select(collider => collider.name + " " + collider.bounds)));
    }

    [Serializable] private sealed class CargoRouteReport
    {
        public string id;
        public float lengthMeters;
        public string[] nodeIds;
    }

    [Serializable] private sealed class CargoValidationReport
    {
        public string validation = "Authored geometry sweep, not runtime AI pathfinding";
        public float proxyWidth = 8f, proxyLength = 12f, proxyHeight = 4.2f, headroom = .6f;
        public float shortestMeters, longestMeters;
        public CargoRouteReport[] routes;
        public CargoRouteReport[] rearRoutes;
        public CargoRouteReport[] pumpRoutes;
        public CargoRouteReport[] workshopRoutes;
    }

    private static void RenderExpansionPreviews(Camera camera, RenderTexture target)
    {
        float fov = camera.fieldOfView;
        bool orthographic = camera.orthographic;
        float size = camera.orthographicSize;
        try
        {
            camera.orthographic = true;
            camera.orthographicSize = 54f;
            RenderShot(camera, target, new Vector3(0f, 110f, 8f), new Vector3(0f, 0f, 8.01f), "07-upper-plan.png");
            WithHiddenRenderers(renderer => renderer.bounds.min.y > 4.9f,
                () => RenderShot(camera, target, new Vector3(0f, 110f, 8f), new Vector3(0f, 0f, 8.01f), "08-ground-plan.png"));
            camera.orthographic = false;
            camera.fieldOfView = 55f;
            RenderShot(camera, target, new Vector3(0f, 2.3f, -10f), new Vector3(0f, 5f, 34f), "09-cargo-ramp.png");
            RenderShot(camera, target, new Vector3(0f, 13f, 54f), new Vector3(0f, 5.5f, 30f), "10-cargo-junction.png");
            camera.orthographic = true;
            camera.orthographicSize = 35f;
            WithHiddenRenderers(renderer => renderer.bounds.min.x > 0f ||
                    renderer.GetComponentInParent<MapForgeObjectProperties>()?.objectId == "side-wall-r",
                () => RenderShot(camera, target, new Vector3(100f, 10f, 13f), new Vector3(0f, 3f, 13f), "11-longitudinal-section.png"));
        }
        finally
        {
            camera.fieldOfView = fov;
            camera.orthographic = orthographic;
            camera.orthographicSize = size;
        }
    }

    private static void WithHiddenRenderers(Func<Renderer, bool> predicate, Action action)
    {
        var renderers = UnityEngine.Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None)
            .Where(renderer => renderer.enabled && predicate(renderer)).ToArray();
        try
        {
            foreach (var renderer in renderers) renderer.enabled = false;
            action();
        }
        finally { foreach (var renderer in renderers) renderer.enabled = true; }
    }
}
