using UnityEngine;
using KINEMATION.FPSAnimationPack.Scripts.Player;

public static class FirstPersonPlayerBootstrap
{
    private const float SpawnClearance = 0.05f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void CreatePlayerIfMissing()
    {
        // The KINEMATION prefab owns the camera, PlayerInput, weapon animation,
        // recoil and ADS state. Do not create a second player when a scene
        // already contains either the package player or the legacy controller.
        FPSPlayer existingPackagePlayer = Object.FindAnyObjectByType<FPSPlayer>();
        if (existingPackagePlayer != null)
        {
            EnsureBridgeComponents(existingPackagePlayer.transform.root.gameObject);
            return;
        }

        if (Object.FindAnyObjectByType<FirstPersonController>() != null)
        {
            return;
        }

        Camera sceneCamera = Camera.main;
        Vector3 cameraPosition;
        Quaternion cameraRotation;
        if (sceneCamera != null)
        {
            cameraPosition = sceneCamera.transform.position;
            cameraRotation = sceneCamera.transform.rotation;
        }
        else
        {
            cameraPosition = Vector3.up * 2f;
            cameraRotation = Quaternion.identity;
        }

        GameObject playerPrefab = Resources.Load<GameObject>("FPSPlayer");
        if (playerPrefab == null)
        {
            Debug.LogError("FPSPlayer prefab was not found in Assets/Resources. "
                           + "The KINEMATION FPS package cannot be bootstrapped.");
            return;
        }

        Vector3 spawnPosition = FindSpawnPosition(cameraPosition);
        Quaternion spawnRotation = Quaternion.Euler(0f, cameraRotation.eulerAngles.y, 0f);
        GameObject player = Object.Instantiate(playerPrefab, spawnPosition, spawnRotation);
        player.name = "FPS Player";
        EnsureBridgeComponents(player);

        Camera packageCamera = player.GetComponentInChildren<Camera>(true);
        if (packageCamera != null)
        {
            packageCamera.tag = "MainCamera";
            packageCamera.enabled = true;
        }

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        // The scene camera is only used as an authored spawn marker. Keep it
        // disabled (rather than destroying it) so scene references remain valid
        // while ensuring there is a single active view and audio listener.
        if (sceneCamera != null && sceneCamera != packageCamera)
        {
            sceneCamera.tag = "Untagged";
            sceneCamera.enabled = false;
            AudioListener sceneListener = sceneCamera.GetComponent<AudioListener>();
            if (sceneListener != null)
            {
                sceneListener.enabled = false;
            }
        }
    }

    private static void EnsureBridgeComponents(GameObject player)
    {
        if (player.GetComponent<FPSPackagePlayerMotion>() == null)
        {
            player.AddComponent<FPSPackagePlayerMotion>();
        }

        if (player.GetComponent<FPSHitscanShooter>() == null)
        {
            player.AddComponent<FPSHitscanShooter>();
        }

        if (player.GetComponent<AmmoDisplayUI>() == null)
        {
            player.AddComponent<AmmoDisplayUI>();
        }
    }

    private static Vector3 FindSpawnPosition(Vector3 cameraPosition)
    {
        Vector3 rayOrigin = cameraPosition + Vector3.up * 2f;
        if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 1000f, ~0,
                QueryTriggerInteraction.Ignore))
        {
            return hit.point + Vector3.up * SpawnClearance;
        }

        Collider[] colliders = Object.FindObjectsByType<Collider>();
        Collider closest = null;
        float closestSqrDistance = float.PositiveInfinity;
        Vector2 cameraPlanar = new Vector2(cameraPosition.x, cameraPosition.z);

        foreach (Collider candidate in colliders)
        {
            if (!candidate.enabled || candidate.isTrigger)
            {
                continue;
            }

            Bounds bounds = candidate.bounds;
            Vector2 candidatePlanar = new Vector2(bounds.center.x, bounds.center.z);
            float sqrDistance = (candidatePlanar - cameraPlanar).sqrMagnitude;
            if (sqrDistance < closestSqrDistance)
            {
                closestSqrDistance = sqrDistance;
                closest = candidate;
            }
        }

        if (closest != null)
        {
            Bounds bounds = closest.bounds;
            return new Vector3(bounds.center.x, bounds.max.y + SpawnClearance, bounds.center.z);
        }

        return new Vector3(cameraPosition.x, Mathf.Max(SpawnClearance, cameraPosition.y), cameraPosition.z);
    }
}
