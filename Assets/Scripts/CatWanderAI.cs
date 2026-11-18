using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Autonomous wandering controller for the Animals_FREE Kitty prefab.
/// Uses NavMeshAgent to roam around the room and drives Kitty.controller's
/// "Vert" (idle -> move) and "State" (walk -> run) blend parameters.
/// </summary>
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(NavMeshAgent))]
public class CatWanderAI : MonoBehaviour
{
    [Header("Movement")]
    [SerializeField] private float walkSpeed = 0.9f;
    [SerializeField] private float runSpeed = 2.2f;
    [SerializeField] private float wanderRadius = 5.5f;
    [SerializeField] [Range(0f, 1f)] private float runChance = 0.25f;

    [Header("Idle Timing")]
    [SerializeField] private float minIdleSeconds = 1.5f;
    [SerializeField] private float maxIdleSeconds = 4.0f;

    [Header("Animator Parameters")]
    [SerializeField] private string vertParam = "Vert";
    [SerializeField] private string stateParam = "State";
    [SerializeField] private float animDampSpeed = 8f;

    private Animator animator;
    private NavMeshAgent agent;
    private NavMeshPath navPath;

    private bool isMoving;
    private bool isRunning;
    private float idleTimer;
    private float stuckTimer;
    private float animVert;
    private float animState;

    private void Awake()
    {
        animator = GetComponent<Animator>();
        agent = GetComponent<NavMeshAgent>();
        navPath = new NavMeshPath();

        // Disable player-input & CharacterController-based scripts from the asset pack
        // so they do not compete with NavMeshAgent or read WASD keys.
        DisableIfPresent("ithappy.Animals_FREE.MovePlayerInput");
        DisableIfPresent("ithappy.Animals_FREE.CreatureMover");
        CharacterController cc = GetComponent<CharacterController>();
        if (cc != null) cc.enabled = false;

        if (agent != null)
        {
            agent.speed = walkSpeed;
            agent.angularSpeed = 360f;
            agent.acceleration = 6f;
            agent.stoppingDistance = 0.15f;
            agent.radius = 0.2f;
            agent.height = 0.45f;
        }
    }

    private void Start()
    {
        if (animator != null && animator.runtimeAnimatorController == null)
        {
            Debug.LogWarning("[CatWanderAI] Animator has no RuntimeAnimatorController assigned.", this);
        }

        if (agent != null && !agent.isOnNavMesh)
        {
            if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 3.0f, NavMesh.AllAreas))
            {
                agent.Warp(hit.position);
            }
        }

        EnterIdle(Random.Range(0.4f, 1.2f));
    }

    private void Update()
    {
        if (agent == null || !agent.isOnNavMesh)
        {
            UpdateAnimator(0f);
            return;
        }

        if (isMoving)
        {
            if (!agent.pathPending && agent.remainingDistance <= agent.stoppingDistance + 0.05f)
            {
                EnterIdle(Random.Range(minIdleSeconds, maxIdleSeconds));
            }
            else
            {
                float speedSq = new Vector3(agent.velocity.x, 0f, agent.velocity.z).sqrMagnitude;
                if (speedSq < 0.005f && !agent.pathPending)
                {
                    stuckTimer += Time.deltaTime;
                    if (stuckTimer > 1.5f)
                    {
                        EnterIdle(Random.Range(0.5f, 1.5f));
                    }
                }
                else
                {
                    stuckTimer = 0f;
                }
            }
        }
        else
        {
            idleTimer -= Time.deltaTime;
            if (idleTimer <= 0f)
            {
                PickNextDestination();
            }
        }

        float planarSpeed = new Vector3(agent.velocity.x, 0f, agent.velocity.z).magnitude;
        float refSpeed = Mathf.Max(0.1f, isRunning ? runSpeed : walkSpeed);
        float targetVert = isMoving ? Mathf.Clamp01(planarSpeed / refSpeed) : 0f;
        UpdateAnimator(targetVert);
    }

    private void EnterIdle(float duration)
    {
        isMoving = false;
        isRunning = false;
        idleTimer = duration;
        stuckTimer = 0f;
        if (agent != null && agent.isOnNavMesh)
        {
            agent.ResetPath();
        }
    }

    private void PickNextDestination()
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            Vector2 dir2D = Random.insideUnitCircle.normalized;
            if (dir2D.sqrMagnitude < 0.01f) dir2D = Vector2.right;

            float dist = Random.Range(1.5f, wanderRadius);
            Vector3 candidate = transform.position + new Vector3(dir2D.x, 0f, dir2D.y) * dist;

            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
            {
                if (Vector3.Distance(transform.position, hit.position) < 1.0f) continue;

                if (agent.CalculatePath(hit.position, navPath) && navPath.status == NavMeshPathStatus.PathComplete)
                {
                    isRunning = Random.value < runChance;
                    agent.speed = isRunning ? runSpeed : walkSpeed;
                    agent.SetDestination(hit.position);
                    isMoving = true;
                    stuckTimer = 0f;
                    return;
                }
            }
        }

        // Retry shortly if no valid destination was found this frame
        EnterIdle(0.75f);
    }

    private void UpdateAnimator(float targetVert)
    {
        if (animator == null) return;

        float dt = Time.deltaTime * animDampSpeed;
        animVert = Mathf.Lerp(animVert, targetVert, dt);
        animState = Mathf.Lerp(animState, isRunning ? 1f : 0f, dt);

        animator.SetFloat(vertParam, animVert);
        animator.SetFloat(stateParam, animState);
    }

    private void DisableIfPresent(string fullTypeName)
    {
        foreach (MonoBehaviour mb in GetComponents<MonoBehaviour>())
        {
            if (mb != null && mb.GetType().FullName == fullTypeName)
            {
                mb.enabled = false;
            }
        }
    }
}
