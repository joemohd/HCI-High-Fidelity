using UnityEngine;

[DefaultExecutionOrder(-100)]
public class AutoSceneSetup : MonoBehaviour
{
    void Awake()
    {
        // Check if environment is already built
        if (GameObject.Find("Restaurant_Environment") != null) return;

        BuildUltraLowFidelityRestaurant();
    }

    private void BuildUltraLowFidelityRestaurant()
    {
        // Shaders & Neutral / Grayscale Minimalist Palette (Whitebox / Wireframe style)
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");

        Material matFloor = new Material(shader) { color = new Color(0.92f, 0.92f, 0.92f) }; // Light grey floor
        Material matCarpet = new Material(shader) { color = new Color(0.82f, 0.82f, 0.82f) }; // Slightly darker grey zone
        Material matWall = new Material(shader) { color = new Color(0.96f, 0.96f, 0.96f) }; // Off-white walls
        Material matFurniture = new Material(shader) { color = new Color(0.75f, 0.75f, 0.75f) }; // Mid grey furniture
        Material matBot = new Material(shader) { color = new Color(0.25f, 0.25f, 0.25f) }; // Dark grey robot cube
        Material matBotAccent = new Material(shader) { color = new Color(0.45f, 0.45f, 0.45f) }; // Face indicator
        Material matHuman = new Material(shader) { color = new Color(0.6f, 0.6f, 0.65f) }; // Slate capsule
        Material matItem = new Material(shader) { color = new Color(0.35f, 0.35f, 0.35f) }; // Small grey props

        // 1. Camera Setup - Clean high-angle isometric overview
        Camera mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.transform.position = new Vector3(-8.5f, 10.5f, -8.5f);
            mainCam.transform.LookAt(new Vector3(0, 0.8f, 0));
            if (mainCam.GetComponent<CameraFlyController>() == null)
            {
                mainCam.gameObject.AddComponent<CameraFlyController>();
            }
        }

        // Directional Light - Soft even lighting
        Light light = FindAnyObjectByType<Light>();
        if (light == null)
        {
            GameObject lObj = new GameObject("Directional Light");
            light = lObj.AddComponent<Light>();
            light.type = LightType.Directional;
        }
        light.transform.rotation = Quaternion.Euler(55f, -30f, 0);
        light.color = Color.white;
        light.intensity = 1.0f;

        // 2. Environment Root
        GameObject envRoot = new GameObject("Restaurant_Environment");

        // Main floor (Tile)
        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor_Main";
        floor.transform.parent = envRoot.transform;
        floor.transform.position = new Vector3(0, -0.05f, 0);
        floor.transform.localScale = new Vector3(20f, 0.1f, 16f);
        floor.GetComponent<Renderer>().material = matFloor;

        // Dining zone floor marker (Carpet)
        GameObject carpet = GameObject.CreatePrimitive(PrimitiveType.Cube);
        carpet.name = "Floor_DiningZone";
        carpet.transform.parent = envRoot.transform;
        carpet.transform.position = new Vector3(0, 0.005f, 0);
        carpet.transform.localScale = new Vector3(8f, 0.01f, 8f);
        carpet.GetComponent<Renderer>().material = matCarpet;

        // Simple minimal boundary walls
        CreateWall(envRoot.transform, matWall, new Vector3(0, 1.0f, 8f), new Vector3(20f, 2f, 0.2f)); // North
        CreateWall(envRoot.transform, matWall, new Vector3(-10f, 1.0f, 0), new Vector3(0.2f, 2f, 16f)); // West
        CreateWall(envRoot.transform, matWall, new Vector3(10f, 1.0f, 0), new Vector3(0.2f, 2f, 16f)); // East
        CreateWall(envRoot.transform, matWall, new Vector3(-5.5f, 1.0f, -8f), new Vector3(9f, 2f, 0.2f)); // South Left
        CreateWall(envRoot.transform, matWall, new Vector3(5.5f, 1.0f, -8f), new Vector3(9f, 2f, 0.2f)); // South Right

        // 3. Ultra-simple Furniture (Plain Cubes)
        GameObject furnitureRoot = new GameObject("Furniture");
        furnitureRoot.transform.parent = envRoot.transform;

        // Dining Table: Single minimalist rectangular prism
        GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = "Table";
        table.transform.parent = furnitureRoot.transform;
        table.transform.position = new Vector3(0, 0.45f, 0);
        table.transform.localScale = new Vector3(2.4f, 0.9f, 1.4f);
        table.GetComponent<Renderer>().material = matFurniture;

        // Chair 1 & Chair 2: Minimalist cubes
        GameObject chair1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chair1.name = "Chair_West";
        chair1.transform.parent = furnitureRoot.transform;
        chair1.transform.position = new Vector3(-1.6f, 0.3f, 0);
        chair1.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        chair1.GetComponent<Renderer>().material = matFurniture;

        GameObject chair2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chair2.name = "Chair_East";
        chair2.transform.parent = furnitureRoot.transform;
        chair2.transform.position = new Vector3(1.6f, 0.3f, 0);
        chair2.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        chair2.GetComponent<Renderer>().material = matFurniture;

        // Host Stand: Minimalist upright prism
        GameObject hostStand = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hostStand.name = "Host_Stand";
        hostStand.transform.parent = furnitureRoot.transform;
        hostStand.transform.position = new Vector3(-3.0f, 0.55f, -5.5f);
        hostStand.transform.localScale = new Vector3(1.0f, 1.1f, 0.6f);
        hostStand.GetComponent<Renderer>().material = matFurniture;

        // 4. Ultra-simple Robot Model (Single Rounded Cylinder / Block with small front pointer)
        GameObject dock = GameObject.CreatePrimitive(PrimitiveType.Cube);
        dock.name = "Dock_Station";
        dock.transform.parent = envRoot.transform;
        dock.transform.position = new Vector3(7.5f, 0.1f, 6.0f);
        dock.transform.localScale = new Vector3(1.2f, 0.2f, 1.2f);
        dock.GetComponent<Renderer>().material = matFurniture;

        GameObject botObj = new GameObject("EcoBot");
        botObj.transform.position = new Vector3(7.5f, 0.2f, 4.5f);
        EcoBotController ecobotCtrl = botObj.AddComponent<EcoBotController>();
        ecobotCtrl.chargingStation = dock.transform;

        // Robot Chassis: Simple cylinder disc
        GameObject botBody = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        botBody.name = "Bot_Body";
        botBody.transform.parent = botObj.transform;
        botBody.transform.localPosition = new Vector3(0, 0.15f, 0);
        botBody.transform.localScale = new Vector3(0.85f, 0.15f, 0.85f);
        botBody.GetComponent<Renderer>().material = matBot;
        ecobotCtrl.robotBody = botBody.transform;

        // Front directional notch
        GameObject botFrontNotch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        botFrontNotch.name = "Bot_DirectionMarker";
        botFrontNotch.transform.parent = botBody.transform;
        botFrontNotch.transform.localPosition = new Vector3(0, 0.5f, 0.35f);
        botFrontNotch.transform.localScale = new Vector3(0.2f, 0.2f, 0.25f);
        botFrontNotch.GetComponent<Renderer>().material = matBotAccent;

        // Minimalistic small pickup arm
        GameObject armPivot = new GameObject("Arm_Pivot");
        armPivot.transform.parent = botObj.transform;
        armPivot.transform.localPosition = new Vector3(0.3f, 0.3f, 0.2f);
        ecobotCtrl.roboticArm = armPivot.transform;

        GameObject armMesh = GameObject.CreatePrimitive(PrimitiveType.Cube);
        armMesh.name = "Arm_Pointer";
        armMesh.transform.parent = armPivot.transform;
        armMesh.transform.localPosition = new Vector3(0, -0.1f, 0.15f);
        armMesh.transform.localScale = new Vector3(0.08f, 0.25f, 0.08f);
        armMesh.GetComponent<Renderer>().material = matBotAccent;

        // 5. Ultra-low Fidelity Characters (Single White/Grey Capsule per person)
        GameObject actorsRoot = new GameObject("Actors");
        actorsRoot.transform.parent = envRoot.transform;

        // Host: Single capsule
        GameObject host = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        host.name = "Host";
        host.transform.parent = actorsRoot.transform;
        host.transform.position = new Vector3(-3.0f, 0.85f, -4.6f);
        host.transform.localScale = new Vector3(0.5f, 0.85f, 0.5f);
        host.GetComponent<Renderer>().material = matHuman;

        // Guest 1 & Guest 2: Single capsules
        GameObject guest1 = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        guest1.name = "Guest_1";
        guest1.transform.parent = actorsRoot.transform;
        guest1.transform.position = new Vector3(-1.6f, 0.85f, 0);
        guest1.transform.localScale = new Vector3(0.5f, 0.85f, 0.5f);
        guest1.GetComponent<Renderer>().material = matHuman;

        GameObject guest2 = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        guest2.name = "Guest_2";
        guest2.transform.parent = actorsRoot.transform;
        guest2.transform.position = new Vector3(1.6f, 0.85f, 0);
        guest2.transform.localScale = new Vector3(0.5f, 0.85f, 0.5f);
        guest2.GetComponent<Renderer>().material = matHuman;

        // 6. Cleanable Objects (Low-poly grey cubes/cylinders)
        GameObject itemsRoot = new GameObject("CleanableItems");
        itemsRoot.transform.parent = envRoot.transform;
        var itemList = new System.Collections.Generic.List<CleanableItem>();

        // Napkin: small flat cube
        GameObject napkin = GameObject.CreatePrimitive(PrimitiveType.Cube);
        napkin.name = "Item_Napkin";
        napkin.transform.parent = itemsRoot.transform;
        napkin.transform.position = new Vector3(-0.8f, 0.03f, -0.9f);
        napkin.transform.localScale = new Vector3(0.2f, 0.04f, 0.2f);
        napkin.GetComponent<Renderer>().material = matItem;
        var nItem = napkin.AddComponent<CleanableItem>();
        nItem.itemName = "Paper Napkin (Trash)";
        nItem.category = ItemCategory.TrashNapkin;
        nItem.cleaningActionDescription = "Vacuuming floor";
        itemList.Add(nItem);

        // Can: small cylinder
        GameObject can = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        can.name = "Item_Can";
        can.transform.parent = itemsRoot.transform;
        can.transform.position = new Vector3(0.8f, 0.1f, -0.7f);
        can.transform.localScale = new Vector3(0.15f, 0.1f, 0.15f);
        can.GetComponent<Renderer>().material = matItem;
        var cItem = can.AddComponent<CleanableItem>();
        cItem.itemName = "Soda Can (Recyclable)";
        cItem.category = ItemCategory.RecyclableCan;
        cItem.cleaningActionDescription = "Arm pickup & sorting to recycling";
        itemList.Add(cItem);

        // Forgotten Item: small flat bar (requires confirmation)
        GameObject uncertainItem = GameObject.CreatePrimitive(PrimitiveType.Cube);
        uncertainItem.name = "Item_UncertainPersonalItem";
        uncertainItem.transform.parent = itemsRoot.transform;
        uncertainItem.transform.position = new Vector3(0.1f, 0.93f, 0.2f);
        uncertainItem.transform.localScale = new Vector3(0.12f, 0.03f, 0.2f);
        uncertainItem.GetComponent<Renderer>().material = matBot;
        var uItem = uncertainItem.AddComponent<CleanableItem>();
        uItem.itemName = "Forgotten Guest Belonging (Uncertain)";
        uItem.category = ItemCategory.ForgottenPersonalItem;
        uItem.requiresHumanConfirmation = true;
        uItem.cleaningActionDescription = "Pending host decision";
        itemList.Add(uItem);

        // 7. Waypoints
        GameObject wpRoot = new GameObject("Waypoints");
        wpRoot.transform.parent = envRoot.transform;

        GameObject roomEntry = new GameObject("WP_DiningEntry");
        roomEntry.transform.parent = wpRoot.transform;
        roomEntry.transform.position = new Vector3(0, 0, -3.0f);

        GameObject exitDoor = new GameObject("WP_ExitDoor");
        exitDoor.transform.parent = wpRoot.transform;
        exitDoor.transform.position = new Vector3(0, 0, -10.0f);

        // 8. Scenario Manager
        RestaurantScenarioManager mgr = FindAnyObjectByType<RestaurantScenarioManager>();
        if (mgr == null)
        {
            GameObject mgrObj = new GameObject("RestaurantScenarioManager");
            mgr = mgrObj.AddComponent<RestaurantScenarioManager>();
        }
        mgr.ecobot = ecobotCtrl;
        mgr.hostCharacter = host.transform;
        mgr.guestCharacters = new System.Collections.Generic.List<Transform> { guest1.transform, guest2.transform };
        mgr.roomEntryWaypoint = roomEntry.transform;
        mgr.exitDoorWaypoint = exitDoor.transform;
        mgr.itemsToClean = itemList;
    }

    private static void CreateWall(Transform parent, Material mat, Vector3 pos, Vector3 scale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "Wall";
        wall.transform.parent = parent;
        wall.transform.position = pos;
        wall.transform.localScale = scale;
        wall.GetComponent<Renderer>().material = mat;
    }
}
