using UnityEngine;

public class VacuumPickupExample : MonoBehaviour
{
    public RobotVacuumArm arm;
    public GameObject target;

    // Call this from a UI button, gameplay event, or your own trigger.
    public void PickTarget()
    {
        if (arm && target) arm.ReachAndPick(target);
    }

    public void DropTarget()
    {
        if (arm) arm.Drop();
    }
}
