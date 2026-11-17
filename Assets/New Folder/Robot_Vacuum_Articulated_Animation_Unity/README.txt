ROBOT VACUUM – ARTICULATED ANIMATED ARM (UNITY)

This package is a custom procedural Unity asset based on the latest reference:
upright two-link arm, visible circular hinge joints, wrist, and two-finger gripper.

FILES
- Robot_Vacuum_Body.obj / .mtl : vacuum chassis
- RobotVacuumArm.cs : creates the arm and animates it using 2-link inverse kinematics
- PickupTrigger.cs : detects a Pickupable object touching the gripper
- Pickupable.cs : helper component for collectible objects
- VacuumPickupExample.cs : sample public methods for gameplay/UI events

SETUP
1. Extract this folder into your Unity project's Assets folder.
2. Drag Robot_Vacuum_Body.obj into the scene.
3. Add RobotVacuumArm to the vacuum body root.
4. Add a Rigidbody to the vacuum root; enable Is Kinematic.
5. Add a Collider to the vacuum root if your gameplay needs body collisions.
6. For each object to collect:
   - Add a Collider (Is Trigger OFF).
   - Add a Rigidbody.
   - Add Pickupable. Set its tag to Pickupable (create the tag if Unity asks).
7. Press Play. The articulated arm is built automatically.
8. Call arm.ReachAndPick(object) to animate a pickup, or arm.Drop() to release.

PICKUP VIA TRIGGER
The gripper has a trigger collider and a kinematic Rigidbody. When an object
tagged Pickupable enters it, PickupTrigger calls ReachAndPick automatically.
For predictable gameplay, you can instead call ReachAndPick from your own
detection script when the object is within reach.

ANIMATION
- The arm has separate shoulder, elbow, and wrist pivots.
- A two-link inverse-kinematics solver moves the gripper toward a world-space
  target instead of relying on a fixed joint angle.
- Pickup sequence: approach above object, close fingers, attach object, lift.
- Drop sequence: detach object, reopen fingers, return arm to upright rest pose.

CUSTOMIZATION
- Change shoulderLocalPosition, upperLength, forearmLength to fit the scale
  of your scene.
- Change approachHeight, liftHeight, moveDuration, openGap, closedGap.
- The arm operates in the vacuum's local XY plane. Place the object in front
  of the vacuum in that plane. For a different arm plane, rotate the vacuum root.

NOTES
- No Animator Controller, external downloads, packages, or plugins required.
- The articulated arm meshes are generated from Unity primitives at runtime;
  they are separate named GameObjects with unscaled joint pivots.
- This is a procedural articulated model and animation system, not a baked
  FBX animation clip. You can control it through C# scripts.
