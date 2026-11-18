# EcoBot (`HCI-High-Fidelity`) — Comprehensive Unity Project Report

## 1. Executive Summary & Tech Stack

**EcoBot (`HCI-High-Fidelity`)** is a Unity project designed to prototype and demonstrate **Human-Robot Interaction (HRI)** and **autonomous indoor cleaning** across two contrasting fidelity levels:

1. **High-Fidelity Environment ([`HighFidelityScene.unity`](Assets/Scenes/HighFidelityScene.unity))**: A furnished open-plan apartment/kitchen built with `LowPolyInteriorsFree`, featuring a rigged mobile manipulator robot (`Robot_Vacuum_With_Arm_Rigged`), NavMesh-based autonomous navigation, analytic 2-link Inverse Kinematics (IK) arm control, dynamic obstacle yielding, avoidable hazard detouring, an autonomous wandering cat (`Kitty_001`), and interactive HRI UI/drag-and-drop systems.
2. **Low-Fidelity Whitebox Prototype ([`SampleScene.unity`](Assets/Scenes/SampleScene.unity))**: A grayscale/whitebox restaurant service simulation generated at runtime via [`AutoSceneSetup.cs`](Assets/Scripts/AutoSceneSetup.cs) (and via an editor menu in [`SceneBuilder.cs`](Assets/Editor/SceneBuilder.cs)), demonstrating a scripted host-summoned restaurant cleaning scenario with human-in-the-loop lost-and-found confirmation.

### Engine & Key Packages
* **Unity Version**: `6000.6.5f1` (`3ff58d469c8a`) ([`ProjectVersion.txt`](ProjectSettings/ProjectVersion.txt))
* **Render Pipeline**: Universal Render Pipeline (`com.unity.render-pipelines.universal` `17.6.0`) + Post-Processing (`3.5.4`)
* **Navigation**: Unity AI Navigation (`com.unity.ai.navigation` `2.0.15`)
* **Input & UI**: Input System (`com.unity.inputsystem` `1.20.0`), uGUI (`2.6.0`), with dual support for Legacy Input Manager and the new Input System
* **Build Scenes** ([`EditorBuildSettings.asset`](ProjectSettings/EditorBuildSettings.asset)):
  1. [`Assets/Scenes/HighFidelityScene.unity`](Assets/Scenes/HighFidelityScene.unity)
  2. [`Assets/Scenes/SampleScene.unity`](Assets/Scenes/SampleScene.unity)

---

## 2. Project Evolution (Git History & Current Working State)

| Commit / State | Author | Summary of Work |
| :--- | :--- | :--- |
| `b704c9f` | — | Initial repository check-in. |
| `7fa9efd` | Joe Mohd | Added initial `EcoBot` architecture including [`RobotArmController.cs`](Assets/EcoBot/Scripts/RobotArmController.cs). |
| `9000387` | Joe Mohd | Added [`RobotCleanerAI.cs`](Assets/EcoBot/Scripts/RobotCleanerAI.cs), updated `RobotArmController.cs`, configured NavMesh (`NavMesh-NavMesh Surface.asset`), `TagManager.asset`, and `SampleScene.unity`. |
| `f8ae59e` | Youssef Mohammed Saeed Wahba Elkomy | Updated `README.md` pickup item criteria (detection radius and layer-based classification). |
| `90004f2` | Nourhan_Zahran | Iterated on `RobotArmController.cs` and `RobotCleanerAI.cs`. |
| `66c882c` | Joe Mohd | Split scenes into `HighFidelityScene.unity` and `SampleScene.unity`, updated `EditorBuildSettings.asset`, and added the low-fidelity restaurant prototype scripts (`AutoSceneSetup.cs`, `EcoBotController.cs`, `RestaurantScenarioManager.cs`, `CleanableItem.cs`, `CameraFlyController.cs`, `SceneSwitcherUI.cs`, and `Assets/Editor/SceneBuilder.cs`). |
| `7ab5a6d` (`HEAD`) | Nourhan_Zahran | Added 3 HRI/interaction features: [`DraggableItem.cs`](Assets/Scripts/DraggableItem.cs), [`GuidancePromptUI.cs`](Assets/Scripts/GuidancePromptUI.cs), and `AwaitingGuidance` / `NotifyObjectMoved` integration in `RobotCleanerAI.cs`. |
| **Working Tree (Uncommitted)** | Local | • Expanded [`RobotCleanerAI.cs`](Assets/EcoBot/Scripts/RobotCleanerAI.cs) with **Wandering (Robot Vacuum Mode)**, **Avoidable Detouring** (`DetouringAroundAvoidable`), **Litter vs. Drop-off routing** (`trashCanLocation`), **Renderer-based target scanning**, and **`VacuumNearby()`**.<br>• Added [`AvoidableAlertUI.cs`](Assets/Scripts/AvoidableAlertUI.cs), [`CameraSwitcherUI.cs`](Assets/Scripts/CameraSwitcherUI.cs), and [`CatWanderAI.cs`](Assets/Scripts/CatWanderAI.cs) (paired with `Assets/ithappy/Animals_FREE/`).<br>• Removed `Assets/LowPolyFoodLite/`. |

---

## 3. Tags, Layers & Classification System

Defined in [`ProjectSettings/TagManager.asset`](ProjectSettings/TagManager.asset):

| Type | Name | Purpose in EcoBot |
| :--- | :--- | :--- |
| **Layer 6** | `Obstacles` | General physical obstacles for dynamic yielding. |
| **Layer 7** | `Pickupables` | Solid items picked up by the articulated arm, dragged by users (`DraggableItem`), and delivered to a drop-off point. |
| **Layer 8** | `Vacuumables` | Floor debris/crumbs driven over and destroyed by the vacuum base. |
| **Layer 9** | `Avoidables` | Floor hazards (e.g., `WaterSpill_KitchenFridge`, `WaterSpill_KitchenIsland`) that trigger a turn-around detour and user warning banner. |
| **Tag** | `Pickupable` | Used by `RobotArmController` proximity/auto-pickup mode. |
| **Tag** | `Unsure` | Marks `Pickupables` items that require human confirmation via `GuidancePromptUI` before the robot picks them up. |
| **Tag** | `Litter` | Routes `Pickupables` items to `trashCanLocation` instead of the default `dropOffLocation`. |

---

## 4. Core Robotics & AI Architecture (`Assets/EcoBot/Scripts/`)

### 4.1 Articulated Arm & Inverse Kinematics — [`RobotArmController.cs`](Assets/EcoBot/Scripts/RobotArmController.cs)
Controls the rigged 4-DOF arm + parallel-jaw gripper (`Arm_Yaw`, `Arm_Shoulder`, `Arm_Elbow`, `Arm_Wrist`, `Finger_Left`, `Finger_Right`, `GripPoint`) on `Robot_Vacuum_With_Arm_Rigged`:

* **Analytic Inverse Kinematics (`Solve`)**:
  * Solves yaw angle accounting for lateral `GripPoint` offset (`gripLateral`).
  * Solves 2-link planar IK (`shoulder` + `elbow` with elbow-up configuration) to position the wrist.
  * Counter-rotates `wrist` (`-foreDelta`) so the gripper stays pointed straight down.
* **Pick & Place Sequences**:
  * `PickRoutine`: Opens fingers → moves to hover pose (`hoverHeight = 0.35f`) → lowers linearly (`MoveLinear`) → closes fingers to object width (`TravelFor`) → parents object to `GripPoint` (`Attach`) → lifts → places at `dropPoint` (`PlaceRoutine`) or returns to upright rest pose.
  * **Anti-loop cooldown**: Detached objects are ignored for `rePickCooldown` (`3s`) so they are not immediately re-grabbed.
* **Proximity Sensor & Guidance Hooks**:
  * Spawns a child `Pickup_Proximity_Sensor` (`SphereCollider` + `RobotArmProximitySensor` relay) and also runs `Physics.OverlapSphereNonAlloc` when `autoPickupByTag` is enabled (disabled when `RobotCleanerAI` takes over).
  * `ShouldPickUpWithoutGuidance(target)` checks `approvedByUser` and the `"Unsure"` tag; `AskUserForGuidance(target)` invokes `onGuidanceNeeded` (`UnityEvent<GameObject>`).

### 4.2 Autonomous Navigation & Cleaning AI — [`RobotCleanerAI.cs`](Assets/EcoBot/Scripts/RobotCleanerAI.cs)
A `NavMeshAgent`-driven state machine (`CleanerState`) coordinating room scanning, pathfinding, obstacle handling, arm control, and HRI:

* **State Machine (`CleanerState`)**:
  `Idle`, `Scanning`, `MovingToPickupable`, `AligningForPickup`, `PickingUp`, `MovingToDropOff`, `DroppingOff`, `MovingToVacuumable`, `Vacuuming`, `Completed`, `Yielding`, `AwaitingGuidance`, `Wandering`, `DetouringAroundAvoidable`.
* **Target Discovery (`AddNewTargets` & `TickLiveRescan`)**:
  * Scans both `Collider`s and `Renderer`s (ensuring raw FBX `Vacuumables` without colliders are still detected) every `rescanInterval` (`1.5s`).
  * Supports **Wandering Mode** (`wanderMode = true`): roams within `wanderRadius` (`8m`), enqueues `Pickupables` (`pickupDetectionRadius = 2.5m`) and `Vacuumables` (`vacuumDetectionRadius = 2.5m`) as it encounters them, and passively vacuums nearby `Vacuumables` (`VacuumNearby`).
* **Pickup & Drop-Off Routing**:
  * Approaches `Pickupables` to `localPickupApproachDistance` (`0.72` local units scaled by `WorldScale`), aligns rotation, fine-tunes distance, and checks `arm.ShouldPickUpWithoutGuidance`.
  * If `"Unsure"`, enters `AwaitingGuidance` and yields until `ProvideGuidance(target, approve)` resolves the item.
  * Routes `"Litter"`-tagged objects to `trashCanLocation` and all other `Pickupables` to `dropOffLocation` (`GetDropOffFor`).
* **Obstacle Yielding vs. Avoidable Detouring**:
  * Uses a forward `Physics.OverlapCapsuleNonAlloc` sensor along `RouteDirection()`.
  * **Standard Obstacles**: Enters `Yielding`, stops, faces the blocker, waits `clearPathDelay` (`0.5s`), and requests human help after `blockedHelpAfterSeconds` (`6s`).
  * **Avoidables (`Avoidables` layer)**: Enters `DetouringAroundAvoidable` (`BeginAvoidableDetour`) to sample a NavMesh detour away from the hazard without freezing, and invokes `onAvoidableEncountered` (also triggered within `avoidableReverseRadius = 2.5m` during wandering).

---

## 5. Human-Robot Interaction (HRI), UI & Scene Scripts (`Assets/Scripts/`)

### 5.1 Runtime Auto-Spawned UI & Interaction Systems
All five of the following components use `[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]` with `DontDestroyOnLoad` (or auto-attachment) so they work seamlessly across scene switches without manual scene wiring:

| Script | Responsibility |
| :--- | :--- |
| [`GuidancePromptUI.cs`](Assets/Scripts/GuidancePromptUI.cs) | Subscribes to `RobotArmController.onGuidanceNeeded`. Displays a centered modal (`"EcoBot — Confirmation Required"`) with **Pick up** and **Leave** buttons, forwarding the user's decision to `RobotCleanerAI.ProvideGuidance(target, approve)`. |
| [`AvoidableAlertUI.cs`](Assets/Scripts/AvoidableAlertUI.cs) | Subscribes to `RobotCleanerAI.onAvoidableEncountered`. Shows a non-blocking top-center warning banner (`"EcoBot — Avoidable Hazard Detected"`) that fades in (`0.3s`), holds (`5.0s`), and fades out (`0.4s`). |
| [`DraggableItem.cs`](Assets/Scripts/DraggableItem.cs) | Auto-attaches to all `Pickupables`-layer colliders on play. Lets the user click and drag objects along the floor plane (supporting both Legacy Input and Input System), and calls `RobotCleanerAI.NotifyObjectMoved(gameObject)` on release so the robot re-evaluates previously skipped or delivered items. |
| [`SceneSwitcherUI.cs`](Assets/Scripts/SceneSwitcherUI.cs) | Renders a persistent top-right button toggling between `HighFidelityScene` and `SampleScene`. |
| [`CameraSwitcherUI.cs`](Assets/Scripts/CameraSwitcherUI.cs) | Renders a top-right button below `SceneSwitcherUI` toggling between the scene's `MainCamera` (with `CameraFlyController`) and the camera mounted on `RobotCleanerAI` (`Switch to Robot Cam` / `Switch to Main Cam`), managing `AudioListener`s cleanly. |

### 5.2 Dynamic Environment Actor
* [`CatWanderAI.cs`](Assets/Scripts/CatWanderAI.cs): Controls `Kitty_001` (`Assets/ithappy/Animals_FREE/`) via `NavMeshAgent`, disabling the asset pack's `MovePlayerInput`, `CreatureMover`, and `CharacterController`. Alternates between idle pauses, walking (`0.9 m/s`), and running (`2.2 m/s`, `25%` chance) while driving the animator's `Vert` and `State` blend parameters—creating a moving, living obstacle in `HighFidelityScene`.

### 5.3 Low-Fidelity Restaurant Prototype (`SampleScene` & Editor Builder)
* [`AutoSceneSetup.cs`](Assets/Scripts/AutoSceneSetup.cs): Runs at `[DefaultExecutionOrder(-100)]` to procedurally construct a grayscale whitebox restaurant (`Restaurant_Environment`, dining zone, table, chairs, host stand, dock station, capsule actors for Host & Guests, primitive `EcoBot`, and 3 `CleanableItem`s).
* [`SceneBuilder.cs`](Assets/Editor/SceneBuilder.cs): Editor menu tool (`EcoBot/Build Restaurant Low-Poly Scene`) that builds and saves a static whitebox restaurant scene with URP Lit materials in `Assets/Materials/`.
* [`RestaurantScenarioManager.cs`](Assets/Scripts/RestaurantScenarioManager.cs): Orchestrates the 5-phase restaurant demo via `OnGUI` HUD: guests finishing meal → guests exiting → host summoning EcoBot → cleaning sequence → docking. Includes an `OnGUI` Human-in-the-Loop confirmation popup (`Hand over to Host Lost & Found` / `Leave on table untouched`).
* [`EcoBotController.cs`](Assets/Scripts/EcoBotController.cs): Coroutine-based controller for the low-fidelity robot (undock, 360° room scan, approach items, wait for host guidance, animate simple arm pivot, return to dock).
* [`CleanableItem.cs`](Assets/Scripts/CleanableItem.cs): Metadata (`ItemCategory`: `TrashNapkin`, `RecyclableCan`, `FoodCrumbSpill`, `ForgottenPersonalItem`) and shrink-out `CleanUp()` coroutine.
* [`CameraFlyController.cs`](Assets/Scripts/CameraFlyController.cs): WASD/Arrow panning and right-mouse-button look rotation for the main camera.

---

## 6. Third-Party & Imported Asset Packs

* **`Assets/New Folder/Robot_Vacuum_With_Arm_Unity/` & `Robot_Vacuum_Articulated_Animation_Unity/`**: Source `.obj`/`.mtl` meshes, reference articulated arm scripts (`RobotVacuumArm.cs`, `PickupTrigger.cs`, `Pickupable.cs`, `VacuumPickupExample.cs`), and the `Robot_Vacuum_With_Arm_Rigged.prefab` used by `EcoBot`.
* **`Assets/LowPolyInteriorsFree/`**: Modular apartment/kitchen meshes, materials, and demo scenes powering `HighFidelityScene.unity` (includes `LPI_Pipeline.cs`, `LPI_Setup.cs`).
* **`Assets/ithappy/Animals_FREE/`**: `Kitty_001` mesh, animations, and prefab used with `CatWanderAI.cs`.
* **`Assets/Simple Foods/`**: Food prop assets.
* **`Assets/WoodlandCastle/`**: Environment prop pack (unused in the two main build scenes).
* *(Deleted in working tree)* **`Assets/LowPolyFoodLite/`**: Previously present, currently staged as deleted in `git status`.
