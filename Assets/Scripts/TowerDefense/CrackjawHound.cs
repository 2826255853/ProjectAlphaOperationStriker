using UnityEngine;

[DisallowMultipleComponent]
[RequireComponent(typeof(MonsterPathFollower), typeof(EnemyHealth))]
[DefaultExecutionOrder(-10)]
public sealed class CrackjawHound : MonoBehaviour
{
    [SerializeField, Min(0.01f)] private float obstructionDelay = 1.25f;
    [SerializeField, Min(0f)] private float biteDamage = 10f;
    [SerializeField] private Animation motion;

    private const float BiteContact = 17f / 30f;
    private const float Reach = 0.79f;
    private const float Radius = 0.14f;
    private Collider[] contacts = new Collider[12];
    private MonsterPathFollower route;
    private EnemyHealth health;
    private Component blocker;
    private float blockedTime;
    private bool biting;
    private bool contactApplied;

    private void Awake()
    {
        route = GetComponent<MonsterPathFollower>();
        health = GetComponent<EnemyHealth>();
    }

    private void Update()
    {
        if (!route.IsInitialized || route.HasArrived || route.IsFlying || health.IsDead)
        {
            ResetMotion();
            return;
        }

        Vector3 step = route.NextRouteStep;
        Component next = FindObstruction(step);
        if (next != blocker || !next)
        {
            blockedTime = 0f;
            if (biting) motion.CrossFade("Run", 0.10f);
            biting = false;
            blocker = next;
        }
        route.MissionMovementBlocked = blocker != null;
        if (blocker != null) blockedTime += Time.deltaTime;
        if (!blocker || blockedTime < obstructionDelay)
        {
            motion["Run"].speed = blocker ? 0.65f : Mathf.Clamp(route.MoveSpeed / 1.25f, 0.4f, 2.5f);
            if (!motion.IsPlaying("Run")) motion.CrossFade("Run", 0.10f);
            return;
        }

        Vector3 facing = Vector3.ProjectOnPlane(step, Vector3.up);
        if (facing.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.RotateTowards(transform.rotation,
                Quaternion.LookRotation(facing), 720f * Time.deltaTime);
        if (!biting || !motion.IsPlaying("Bite"))
        {
            motion["Bite"].time = 0f;
            motion.CrossFade("Bite", 0.09f);
            biting = true;
            contactApplied = false;
        }
        if (contactApplied || motion["Bite"].time < BiteContact) return;
        contactApplied = true;
        if (blocker is PlayerHealth player) player.TakeDamage(biteDamage, transform.position);
        else if (blocker is TrapInstance trap) trap.TakeDamage(biteDamage);
    }

    private Component FindObstruction(Vector3 step)
    {
        float distance = step.magnitude;
        if (distance <= 0.05f) return null;
        Vector3 direction = step / distance;
        Vector3 start = transform.position + Vector3.up * 0.39f;
        Vector3 end = start + direction * Mathf.Min(Reach - Radius, distance);
        int count;
        while ((count = Physics.OverlapCapsuleNonAlloc(start, end, Radius, contacts,
            Physics.AllLayers, QueryTriggerInteraction.Ignore)) == contacts.Length)
            System.Array.Resize(ref contacts, contacts.Length * 2);
        Component closest = null;
        float best = float.PositiveInfinity;
        for (int i = 0; i < count; ++i)
        {
            Collider col = contacts[i];
            PlayerHealth player = col.GetComponentInParent<PlayerHealth>();
            Component owner = player;
            if (!player || !player.isActiveAndEnabled || player.IsDead)
            {
                TrapInstance trap = col.GetComponentInParent<TrapInstance>();
                if (!trap || !trap.isActiveAndEnabled || trap.IsDestroyed ||
                    (trap.Definition && trap.Definition.WalkableFloorTrap)) continue;
                owner = trap;
            }
            float squared = col.bounds.SqrDistance(start);
            if (squared > best || (squared == best && closest == blocker)) continue;
            best = squared;
            closest = owner;
        }
        return closest;
    }

    private void ResetMotion()
    {
        route.MissionMovementBlocked = false;
        blocker = null;
        blockedTime = 0f;
        biting = false;
        if (motion && motion.isPlaying) motion.Stop();
    }

    private void OnDisable()
    {
        if (route) ResetMotion();
    }
}
