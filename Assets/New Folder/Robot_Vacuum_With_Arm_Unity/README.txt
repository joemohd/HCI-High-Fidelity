ROBOT VACUUM WITH PICKUP ARM – UNITY ASSET

Files:
- Robot_Vacuum_With_Arm.obj : 3D model, with named mesh groups
- Robot_Vacuum_With_Arm.mtl : material definitions
- README.txt : this guide

Import:
1. Extract this folder into your Unity project's Assets/Models folder.
2. Unity imports OBJ and MTL natively. If materials do not appear automatically,
   create materials using the included names in the MTL file and assign them.
3. Select the model and adjust its scale/rotation as needed.

Model notes:
- Built as an original, stylized hard-surface 3D asset based on the supplied reference.
- Includes circular vacuum body, wheels, front sensor, lidar puck, arm mount,
  shoulder/elbow/wrist joints, two arm links, and a two-finger gripper.
- Meshes are separated into named OBJ groups for easier selection/material assignment.
- This is a static mesh assembly. The joints are modeled as separate visible parts,
  but are not rigged. To animate, separate the named groups into GameObjects and
  set pivots/parenting at the shoulder, elbow, and wrist joints.
- No external downloads, plugins, or textures required.
