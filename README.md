# EcoBot: Arm Pickup & Navigation Guide

Quick reference for implementing the vacuum robot's navigation AI.

---

## 1. What Items Does the Robot Pick Up?
* **Layer:** Any object in **`Pickupable`**.
* **Requirements:** The object must have a **Collider** (and optionally a Rigidbody).
* **Detection:** The robot automatically detects items within a **~1m radius** around its front.

If layer is Vacuumable, the item is vacuumed. If avoidable, the item is avoided
---

## 2. Where Does It Put Them Down?
* **Target Drop Point:** Defined by the **`testDropPoint`** Transform on the `RobotArmController` component (e.g., an onboard bin/hopper or a trash disposal station).
* **Behavior:**
  * **If `testDropPoint` is assigned:** The arm picks up the object, swings over to the drop point, places it down, and releases it.
  * **If `testDropPoint` is empty:** The arm picks up the object and holds it in its upright carry position.
* **Anti-loop protection:** Items placed at the drop point are ignored for 3 seconds so the robot won't immediately pick them back up.

---

## 3. Essential Info for the Movement / Navigation AI

### Stop While Picking Up
* **`arm.IsBusy` (`bool`)**: When `true`, the arm is actively reaching, lifting, or placing. **The navigation AI must stop the vacuum's movement** while this is true so the arm doesn't miss the target or tilt during motion.
* **`arm.HeldObject` (`GameObject`)**: `null` if the robot is empty; references the grabbed item if currently holding one.

### Stopping Distance & Approach
* **Stopping Distance:** The robot should stop with the target object roughly **0.5m – 0.9m in front of its bumper** (centered ahead) to ensure the arm can comfortably reach it.
* **Reach Check:** You can call `arm.CanReach(target.position)` before stopping to confirm the item is in reach.
* **AI Decision & Guidance Hook:** `arm.ShouldPickUpWithoutGuidance(target)` evaluates whether to pick up (`true`) or call `arm.AskUserForGuidance(target)` (`false`).
