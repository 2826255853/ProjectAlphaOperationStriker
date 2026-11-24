using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class FPSGrenadeVisuals : MonoBehaviour
{
    private const float ThrowDelay = 0.48f;
    private const float ProjectileLifetime = 8f;

    private static Material cachedGrenadeMaterial;
    private static Material cachedMetalMaterial;

    private InputAction throwAction;
    private Transform viewTransform;
    private GameObject heldGrenade;
    private Coroutine throwRoutine;

    public void Bind(PlayerInput playerInput, Transform view)
    {
        InputAction nextAction = playerInput != null
            ? playerInput.actions?.FindAction("Player/ThrowGrenade", false)
            : null;

        if (throwAction != nextAction)
        {
            if (throwAction != null) throwAction.performed -= OnThrowPerformed;
            throwAction = nextAction;
            if (throwAction != null) throwAction.performed += OnThrowPerformed;
        }

        viewTransform = view;
        if (heldGrenade == null && viewTransform != null)
        {
            heldGrenade = CreateGrenadeModel("Held Grenade", viewTransform, false);
            heldGrenade.transform.localPosition = new Vector3(0.24f, -0.2f, 0.52f);
            heldGrenade.transform.localRotation = Quaternion.Euler(15f, 0f, -12f);
            heldGrenade.SetActive(false);
        }
    }

    private void OnDestroy()
    {
        if (throwAction != null) throwAction.performed -= OnThrowPerformed;
    }

    private void OnThrowPerformed(InputAction.CallbackContext context)
    {
        if (!isActiveAndEnabled || viewTransform == null || throwRoutine != null) return;
        throwRoutine = StartCoroutine(ThrowGrenade());
    }

    private IEnumerator ThrowGrenade()
    {
        heldGrenade.SetActive(true);
        yield return new WaitForSeconds(ThrowDelay);

        heldGrenade.SetActive(false);
        Vector3 spawnPosition = viewTransform.position
                                + viewTransform.forward * 0.55f
                                + viewTransform.right * 0.12f
                                - viewTransform.up * 0.12f;
        GameObject projectile = CreateGrenadeModel("Thrown Grenade", null, true);
        projectile.transform.SetPositionAndRotation(spawnPosition, viewTransform.rotation);

        Rigidbody body = projectile.GetComponent<Rigidbody>();
        body.AddForce(viewTransform.forward * 8f + viewTransform.up * 3f, ForceMode.VelocityChange);
        body.AddTorque(viewTransform.right * 2f, ForceMode.VelocityChange);
        Destroy(projectile, ProjectileLifetime);

        throwRoutine = null;
    }

    private static GameObject CreateGrenadeModel(string objectName, Transform parent, bool addPhysics)
    {
        var root = new GameObject(objectName);
        if (parent != null) root.transform.SetParent(parent, false);

        if (addPhysics)
        {
            root.AddComponent<SphereCollider>().radius = 0.12f;
            root.AddComponent<Rigidbody>().collisionDetectionMode = CollisionDetectionMode.Continuous;
        }

        AddPart(root.transform, PrimitiveType.Cylinder, "Body", new Vector3(0f, 0f, 0f),
            new Vector3(0.09f, 0.13f, 0.09f), grenadeMaterial);
        AddPart(root.transform, PrimitiveType.Cylinder, "Neck", new Vector3(0f, 0.14f, 0f),
            new Vector3(0.047f, 0.025f, 0.047f), metalMaterial);
        AddPart(root.transform, PrimitiveType.Cylinder, "Fuse", new Vector3(0f, 0.18f, 0f),
            new Vector3(0.025f, 0.018f, 0.025f), metalMaterial);
        AddPart(root.transform, PrimitiveType.Cube, "Safety Lever", new Vector3(0.045f, 0.12f, 0f),
            new Vector3(0.018f, 0.12f, 0.065f), metalMaterial).transform.localRotation =
            Quaternion.Euler(0f, 0f, -18f);

        return root;
    }

    private static GameObject AddPart(Transform parent, PrimitiveType primitive, string partName,
        Vector3 localPosition, Vector3 localScale, Material material)
    {
        GameObject part = GameObject.CreatePrimitive(primitive);
        part.name = partName;
        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;

        Collider partCollider = part.GetComponent<Collider>();
        if (partCollider != null) Destroy(partCollider);

        Renderer renderer = part.GetComponent<Renderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        return part;
    }

    private static Material grenadeMaterial
    {
        get
        {
            if (cachedGrenadeMaterial == null)
                cachedGrenadeMaterial = CreateMaterial(new Color(0.19f, 0.24f, 0.12f));
            return cachedGrenadeMaterial;
        }
    }

    private static Material metalMaterial
    {
        get
        {
            if (cachedMetalMaterial == null)
                cachedMetalMaterial = CreateMaterial(new Color(0.32f, 0.34f, 0.3f));
            return cachedMetalMaterial;
        }
    }

    private static Material CreateMaterial(Color color)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
        var material = new Material(shader) { color = color };
        return material;
    }
}
