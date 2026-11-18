using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace EcoBot
{
    /// <summary>
    /// Autonomous navigation and cleaning controller for EcoBot.
    /// Scans the room for objects on the "Pickupables" and "Vacuumables" layers:
    /// - Vacuumables: drives directly over them to vacuum them up.
    /// - Pickupables: stops at a reachable distance in front, faces the item, picks it up with RobotArmController,
    ///   carries it to the drop-off location, places it down, and repeats until the room is clean.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public class RobotCleanerAI : MonoBehaviour
    {
        public enum CleanerState
        {
            Idle,
            Scanning,
            MovingToPickupable,
            AligningForPickup,
            PickingUp,
            MovingToDropOff,
            DroppingOff,
            MovingToVacuumable,
            Vacuuming,
            Completed
        }

        [Header("References")]
        [Tooltip("Reference to the RobotArmController on this robot (auto-filled if empty).")]
        public RobotArmController arm;

        [Tooltip("Reference to the NavMeshAgent on this robot (auto-filled if empty).")]
        public NavMeshAgent agent;

        [Tooltip("Where the robot brings Pickupable items. If empty, inherits testDropPoint from RobotArmController.")]
        public Transform dropOffLocation;

        [Header("Layer Configuration")]
        [Tooltip("Layer mask for objects the arm should pick up and carry to DropOffLocation.")]
        public LayerMask pickupablesMask;

        [Tooltip("Layer mask for small debris/dust the robot should drive directly over to vacuum.")]
        public LayerMask vacuumablesMask;

        [Header("Navigation & Approach Tuning (Robot-Local Units)")]
        [Tooltip("Desired stopping distance in front of a Pickupable item in robot-local units (0.7 local = ~0.175m world at 0.25x scale).")]
        [Range(0.45f, 1.0f)]
        public float localPickupApproachDistance = 0.72f;

        [Tooltip("Distance in robot-local units from a Vacuumable item at which it gets sucked up.")]
        public float localVacuumCollectRadius = 0.6f;

        [Tooltip("How fast (degrees/sec) the robot rotates in place to center the target before reaching with the arm.")]
        public float alignmentTurnSpeed = 240f;

        [Tooltip("Brief pause (seconds) when vacuuming over a Vacuumable item.")]
        public float vacuumPauseDuration = 0.25f;

        [Tooltip("Start scanning and cleaning automatically when Play begins.")]
        public bool autoStartOnPlay = true;

        [Header("Runtime Status (Read-Only)")]
        [SerializeField] private CleanerState currentState = CleanerState.Idle;
        [SerializeField] private GameObject currentTarget;
        [SerializeField] private List<GameObject> remainingTargets = new List<GameObject>();

        private readonly HashSet<GameObject> deliveredObjects = new HashSet<GameObject>();

        public CleanerState State => currentState;
        public GameObject CurrentTarget => currentTarget;

        float WorldScale => Mathf.Max(0.05f, Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z)));
        float WorldApproachDistance => localPickupApproachDistance * WorldScale;
        float WorldVacuumRadius => localVacuumCollectRadius * WorldScale;

        void Reset()
        {
            AutoAssignReferencesAndLayers();
        }

        void Awake()
        {
            AutoAssignReferencesAndLayers();
        }

        void AutoAssignReferencesAndLayers()
        {
            if (!arm) arm = GetComponent<RobotArmController>();
            if (!agent) agent = GetComponent<NavMeshAgent>();

            if (pickupablesMask.value == 0)
            {
                int pLayer = LayerMask.NameToLayer("Pickupables");
                if (pLayer != -1) pickupablesMask = 1 << pLayer;
            }

            if (vacuumablesMask.value == 0)
            {
                int vLayer = LayerMask.NameToLayer("Vacuumables");
                if (vLayer != -1) vacuumablesMask = 1 << vLayer;
            }
        }

        void Start()
        {
            if (arm)
            {
                // Disable automatic proximity/test triggers so RobotCleanerAI controls when the arm picks & drops
                arm.autoPickupByTag = false;
                arm.runTestOnStart = false;

                // Inherit testDropPoint from the arm if dropOffLocation isn't set,
                // and clear arm.testDropPoint so PickUp() holds the item upright until we drive to dropOffLocation.
                if (!dropOffLocation && arm.testDropPoint)
                {
                    dropOffLocation = arm.testDropPoint;
                }
                arm.testDropPoint = null;
            }

            // Ensure agent is snapped onto the NavMesh at start
            if (agent && !agent.isOnNavMesh)
            {
                if (NavMesh.SamplePosition(transform.position, out NavMeshHit hit, 2.0f, NavMesh.AllAreas))
                {
                    agent.Warp(hit.position);
                }
            }

            if (autoStartOnPlay)
            {
                StartCleaning();
            }
        }

        /// <summary>
        /// Scans the entire scene for objects on Pickupables and Vacuumables layers and begins cleaning.
        /// </summary>
        [ContextMenu("Start Cleaning")]
        public void StartCleaning()
        {
            StopAllCoroutines();
            ScanRoom();
            SelectAndMoveToNextTarget();
        }

        /// <summary>
        /// Finds all active GameObjects with colliders on the Pickupables or Vacuumables layers.
        /// </summary>
        public void ScanRoom()
        {
            currentState = CleanerState.Scanning;
            remainingTargets.Clear();

            int combinedMask = pickupablesMask.value | vacuumablesMask.value;
            Collider[] allColliders = FindObjectsByType<Collider>();
            HashSet<GameObject> uniqueObjects = new HashSet<GameObject>();

            foreach (Collider col in allColliders)
            {
                if (!col || !col.gameObject.activeInHierarchy) continue;
                if (col.transform.IsChildOf(transform)) continue;

                GameObject rootCandidate = ResolveTargetRoot(col, combinedMask);
                if (!rootCandidate || deliveredObjects.Contains(rootCandidate)) continue;

                if (uniqueObjects.Add(rootCandidate))
                {
                    remainingTargets.Add(rootCandidate);
                }
            }

            Debug.Log($"[RobotCleanerAI] Room scan complete. Found {remainingTargets.Count} target(s).");
        }

        GameObject ResolveTargetRoot(Collider col, int layerMask)
        {
            if (col.attachedRigidbody && IsInLayerMask(col.attachedRigidbody.gameObject.layer, layerMask))
            {
                return col.attachedRigidbody.gameObject;
            }

            Transform t = col.transform;
            GameObject match = null;
            while (t != null)
            {
                if (IsInLayerMask(t.gameObject.layer, layerMask))
                    match = t.gameObject;
                t = t.parent;
            }
            return match;
        }

        static bool IsInLayerMask(int layer, int layerMask) => (layerMask & (1 << layer)) != 0;

        void SelectAndMoveToNextTarget()
        {
            remainingTargets.RemoveAll(item => item == null || !item.activeInHierarchy || deliveredObjects.Contains(item));

            if (remainingTargets.Count == 0)
            {
                currentTarget = null;
                currentState = CleanerState.Completed;
                if (agent && agent.isOnNavMesh) agent.isStopped = true;
                Debug.Log("[RobotCleanerAI] All Pickupables and Vacuumables have been cleaned!");
                return;
            }

            // Pick the closest target to the robot's current position
            float bestDistSqr = float.MaxValue;
            int bestIndex = 0;
            for (int i = 0; i < remainingTargets.Count; i++)
            {
                float d = (remainingTargets[i].transform.position - transform.position).sqrMagnitude;
                if (d < bestDistSqr)
                {
                    bestDistSqr = d;
                    bestIndex = i;
                }
            }

            currentTarget = remainingTargets[bestIndex];
            remainingTargets.RemoveAt(bestIndex);

            bool isPickupable = IsInLayerMask(currentTarget.layer, pickupablesMask.value);

            if (isPickupable)
            {
                Vector3 approachPoint = GetTargetApproachPosition(currentTarget.transform, WorldApproachDistance);
                agent.stoppingDistance = 0.02f;
                agent.isStopped = false;
                agent.SetDestination(approachPoint);
                currentState = CleanerState.MovingToPickupable;
                Debug.Log($"[RobotCleanerAI] Moving to Pickupable: {currentTarget.name}");
            }
            else
            {
                Vector3 targetPoint = SampleNavMesh(currentTarget.transform.position, 2.0f);
                agent.stoppingDistance = 0.02f;
                agent.isStopped = false;
                agent.SetDestination(targetPoint);
                currentState = CleanerState.MovingToVacuumable;
                Debug.Log($"[RobotCleanerAI] Moving to Vacuumable: {currentTarget.name}");
            }
        }

        void Update()
        {
            // Never move while the arm is actively reaching/lifting/placing
            if (arm && arm.IsBusy)
            {
                if (agent && agent.isOnNavMesh && !agent.isStopped)
                    agent.isStopped = true;
                return;
            }

            switch (currentState)
            {
                case CleanerState.MovingToPickupable:
                    if (!currentTarget || !currentTarget.activeInHierarchy)
                    {
                        SelectAndMoveToNextTarget();
                        return;
                    }

                    if (HasArrived())
                    {
                        agent.isStopped = true;
                        StartCoroutine(PickupSequenceRoutine());
                    }
                    break;

                case CleanerState.MovingToDropOff:
                    if (HasArrived())
                    {
                        agent.isStopped = true;
                        StartCoroutine(DropOffSequenceRoutine());
                    }
                    break;

                case CleanerState.MovingToVacuumable:
                    if (!currentTarget || !currentTarget.activeInHierarchy)
                    {
                        SelectAndMoveToNextTarget();
                        return;
                    }

                    float horizontalDist = Vector2.Distance(
                        new Vector2(transform.position.x, transform.position.z),
                        new Vector2(currentTarget.transform.position.x, currentTarget.transform.position.z));

                    if (horizontalDist <= WorldVacuumRadius || HasArrived())
                    {
                        agent.isStopped = true;
                        StartCoroutine(VacuumSequenceRoutine());
                    }
                    break;
            }
        }

        bool HasArrived()
        {
            if (!agent || !agent.isOnNavMesh || agent.pathPending) return false;
            return agent.remainingDistance <= agent.stoppingDistance + 0.05f;
        }

        IEnumerator PickupSequenceRoutine()
        {
            currentState = CleanerState.AligningForPickup;

            if (currentTarget)
            {
                yield return RotateTowardsPointRoutine(currentTarget.transform.position);

                // Fine-tune distance if slightly too far or too close for the scaled arm
                Vector3 flatToTarget = currentTarget.transform.position - transform.position;
                flatToTarget.y = 0f;
                float currentFlatDist = flatToTarget.magnitude;
                float idealDist = WorldApproachDistance;
                if (Mathf.Abs(currentFlatDist - idealDist) > 0.03f && flatToTarget.sqrMagnitude > 0.0001f)
                {
                    Vector3 adjustedPos = currentTarget.transform.position - flatToTarget.normalized * idealDist;
                    adjustedPos.y = transform.position.y;
                    if (NavMesh.SamplePosition(adjustedPos, out NavMeshHit hit, 0.3f, NavMesh.AllAreas))
                    {
                        float moveSpeed = agent ? agent.speed : 0.5f;
                        while (Vector3.Distance(transform.position, hit.position) > 0.01f)
                        {
                            transform.position = Vector3.MoveTowards(transform.position, hit.position, moveSpeed * Time.deltaTime);
                            yield return null;
                        }
                    }
                }

                yield return RotateTowardsPointRoutine(currentTarget.transform.position);
            }

            currentState = CleanerState.PickingUp;
            if (arm && currentTarget)
            {
                bool started = arm.PickUp(currentTarget, dropPoint: null);
                if (started)
                {
                    yield return new WaitUntil(() => !arm.IsBusy);
                }
            }

            if (arm && arm.HeldObject != null && dropOffLocation != null)
            {
                Vector3 dropApproach = GetDropOffApproachPosition(dropOffLocation);
                agent.stoppingDistance = 0.05f;
                agent.isStopped = false;
                agent.SetDestination(dropApproach);
                currentState = CleanerState.MovingToDropOff;
                Debug.Log($"[RobotCleanerAI] Carrying {arm.HeldObject.name} to drop-off ({dropOffLocation.name}).");
            }
            else
            {
                SelectAndMoveToNextTarget();
            }
        }

        IEnumerator DropOffSequenceRoutine()
        {
            currentState = CleanerState.DroppingOff;

            if (dropOffLocation)
            {
                yield return RotateTowardsPointRoutine(dropOffLocation.position);
            }

            if (arm && arm.HeldObject != null)
            {
                GameObject droppedObj = arm.HeldObject;
                deliveredObjects.Add(droppedObj);

                // Place the item on the floor directly in front of the robot within the arm's reach
                Vector3 placeSpot = transform.position + transform.forward * WorldApproachDistance;
                placeSpot.y = transform.position.y;

                arm.PlaceAt(placeSpot);
                yield return new WaitUntil(() => !arm.IsBusy);
            }

            SelectAndMoveToNextTarget();
        }

        IEnumerator VacuumSequenceRoutine()
        {
            currentState = CleanerState.Vacuuming;

            if (vacuumPauseDuration > 0f)
                yield return new WaitForSeconds(vacuumPauseDuration);

            if (currentTarget)
            {
                Destroy(currentTarget);
                currentTarget = null;
            }

            SelectAndMoveToNextTarget();
        }

        IEnumerator RotateTowardsPointRoutine(Vector3 worldPoint)
        {
            Vector3 flatDir = worldPoint - transform.position;
            flatDir.y = 0f;
            if (flatDir.sqrMagnitude < 0.0001f) yield break;

            Quaternion targetRotation = Quaternion.LookRotation(flatDir.normalized, Vector3.up);
            while (Quaternion.Angle(transform.rotation, targetRotation) > 1.5f)
            {
                transform.rotation = Quaternion.RotateTowards(
                    transform.rotation,
                    targetRotation,
                    alignmentTurnSpeed * Time.deltaTime);
                yield return null;
            }
            transform.rotation = targetRotation;
        }

        Vector3 GetTargetApproachPosition(Transform target, float stopDistance)
        {
            Vector3 targetPos = target.position;
            Vector3 dirToRobot = transform.position - targetPos;
            dirToRobot.y = 0f;
            if (dirToRobot.sqrMagnitude < 0.001f)
                dirToRobot = -transform.forward;
            else
                dirToRobot.Normalize();

            Vector3 desiredStopPos = targetPos + dirToRobot * stopDistance;
            desiredStopPos.y = transform.position.y;
            return SampleNavMesh(desiredStopPos, 1.5f);
        }

        Vector3 GetDropOffApproachPosition(Transform dropTarget)
        {
            Vector3 dropPos = dropTarget.position;
            Collider dropCol = dropTarget.GetComponentInChildren<Collider>();
            if (dropCol)
            {
                // Find the closest point on the outside of the drop-off station collider towards the robot
                Vector3 surfacePoint = dropCol.ClosestPoint(transform.position);
                surfacePoint.y = transform.position.y;
                Vector3 dirOut = transform.position - dropCol.bounds.center;
                dirOut.y = 0f;
                if (dirOut.sqrMagnitude < 0.001f) dirOut = Vector3.forward;
                dirOut.Normalize();

                Vector3 desired = surfacePoint + dirOut * (WorldApproachDistance + 0.1f);
                return SampleNavMesh(desired, 2.0f);
            }

            return GetTargetApproachPosition(dropTarget, WorldApproachDistance);
        }

        Vector3 SampleNavMesh(Vector3 point, float maxDistance)
        {
            if (NavMesh.SamplePosition(point, out NavMeshHit hit, maxDistance, NavMesh.AllAreas))
                return hit.position;
            return point;
        }
    }
}
