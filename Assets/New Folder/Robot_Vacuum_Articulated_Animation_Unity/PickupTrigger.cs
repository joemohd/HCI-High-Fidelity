using UnityEngine;

public class PickupTrigger : MonoBehaviour
{
    public RobotVacuumArm arm;

    void OnTriggerEnter(Collider other)
    {
        if (!arm) arm = GetComponentInParent<RobotVacuumArm>();
        if (!arm) return;

        GameObject candidate = other.attachedRigidbody ? other.attachedRigidbody.gameObject : other.gameObject;
        if (candidate.CompareTag(arm.pickupTag))
            arm.ReachAndPick(candidate);
    }
}
