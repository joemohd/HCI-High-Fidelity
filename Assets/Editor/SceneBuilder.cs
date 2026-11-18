using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.Collections.Generic;

public static class SceneBuilder
{
    [MenuItem("EcoBot/Build Restaurant Low-Poly Scene")]
    public static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Lighting & Camera
        GameObject lightObj = new GameObject("Directional Light");
        Light dirLight = lightObj.AddComponent<Light>();
        dirLight.type = LightType.Directional;
        dirLight.color = Color.white;
        dirLight.intensity = 1.0f;
        dirLight.shadows = LightShadows.None; // Flat architectural look
        lightObj.transform.rotation = Quaternion.Euler(55f, -30f, 0);

        GameObject camObj = new GameObject("Main Camera");
        Camera cam = camObj.AddComponent<Camera>();
        camObj.tag = "MainCamera";
        camObj.transform.position = new Vector3(-8.5f, 10.5f, -8.5f);
        camObj.transform.LookAt(new Vector3(0, 0.8f, 0));
        camObj.AddComponent<AudioListener>();
        camObj.AddComponent<CameraFlyController>();

        // Monochromatic / Architectural Whitebox Materials
        Shader litShader = Shader.Find("Universal Render Pipeline/Lit");
        if (litShader == null) litShader = Shader.Find("Standard");

        Material matFloor = CreateMaterial("Mat_Floor_Neutral", litShader, new Color(0.92f, 0.92f, 0.92f));
        Material matZone = CreateMaterial("Mat_Carpet_Neutral", litShader, new Color(0.84f, 0.84f, 0.84f));
        Material matWall = CreateMaterial("Mat_Wall_Neutral", litShader, new Color(0.96f, 0.96f, 0.96f));
        Material matBlock = CreateMaterial("Mat_Block_Neutral", litShader, new Color(0.75f, 0.75f, 0.75f));
        Material matBot = CreateMaterial("Mat_Bot_Neutral", litShader, new Color(0.28f, 0.28f, 0.28f));
        Material matBotAccent = CreateMaterial("Mat_Bot_Accent", litShader, new Color(0.48f, 0.48f, 0.48f));
        Material matHuman = CreateMaterial("Mat_Human_Neutral", litShader, new Color(0.6f, 0.6f, 0.65f));
        Material matProp = CreateMaterial("Mat_Prop_Neutral", litShader, new Color(0.4f, 0.4f, 0.4f));

        // Environment
        GameObject envRoot = new GameObject("Restaurant_Environment");

        GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Floor_Main";
        floor.transform.parent = envRoot.transform;
        floor.transform.position = new Vector3(0, -0.05f, 0);
        floor.transform.localScale = new Vector3(20f, 0.1f, 16f);
        floor.GetComponent<Renderer>().material = matFloor;

        GameObject diningArea = GameObject.CreatePrimitive(PrimitiveType.Cube);
        diningArea.name = "Floor_DiningZone";
        diningArea.transform.parent = envRoot.transform;
        diningArea.transform.position = new Vector3(0, 0.005f, 0);
        diningArea.transform.localScale = new Vector3(8f, 0.01f, 8f);
        diningArea.GetComponent<Renderer>().material = matZone;

        CreateWall(envRoot.transform, matWall, new Vector3(0, 1.0f, 8f), new Vector3(20f, 2f, 0.2f));
        CreateWall(envRoot.transform, matWall, new Vector3(-10f, 1.0f, 0), new Vector3(0.2f, 2f, 16f));
        CreateWall(envRoot.transform, matWall, new Vector3(10f, 1.0f, 0), new Vector3(0.2f, 2f, 16f));
        CreateWall(envRoot.transform, matWall, new Vector3(-5.5f, 1.0f, -8f), new Vector3(9f, 2f, 0.2f));
        CreateWall(envRoot.transform, matWall, new Vector3(5.5f, 1.0f, -8f), new Vector3(9f, 2f, 0.2f));

        // Furniture: Minimalist Cubes
        GameObject furnitureRoot = new GameObject("Furniture");
        furnitureRoot.transform.parent = envRoot.transform;

        GameObject table = GameObject.CreatePrimitive(PrimitiveType.Cube);
        table.name = "Table";
        table.transform.parent = furnitureRoot.transform;
        table.transform.position = new Vector3(0, 0.45f, 0);
        table.transform.localScale = new Vector3(2.4f, 0.9f, 1.4f);
        table.GetComponent<Renderer>().material = matBlock;

        GameObject chair1 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chair1.name = "Chair_1";
        chair1.transform.parent = furnitureRoot.transform;
        chair1.transform.position = new Vector3(-1.6f, 0.3f, 0);
        chair1.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        chair1.GetComponent<Renderer>().material = matBlock;

        GameObject chair2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        chair2.name = "Chair_2";
        chair2.transform.parent = furnitureRoot.transform;
        chair2.transform.position = new Vector3(1.6f, 0.3f, 0);
        chair2.transform.localScale = new Vector3(0.6f, 0.6f, 0.6f);
        chair2.GetComponent<Renderer>().material = matBlock;

        GameObject hostStand = GameObject.CreatePrimitive(PrimitiveType.Cube);
        hostStand.name = "Host_Stand";
        hostStand.transform.parent = furnitureRoot.transform;
        hostStand.transform.position = new Vector3(-3.0f, 0.55f, -5.5f);
        hostStand.transform.localScale = new Vector3(1.0f, 1.1f, 0.6f);
        hostStand.GetComponent<Renderer>().material = matBlock;

        // Ultra-low Fidelity Robot (Single Cylinder with Front Notch)
        GameObject dock = GameObject.CreatePrimitive(PrimitiveType.Cube);
        dock.name = "Dock_Station";
        dock.transform.parent = envRoot.transform;
        dock.transform.position = new Vector3(7.5f, 0.1f, 6.0f);
        dock.transform.localScale = new Vector3(1.2f, 0.2f, 1.2f);
        dock.GetComponent<Renderer>().material = matBlock;

        GameObject botObj = new GameObject("EcoBot");
        botObj.transform.position = new Vector3(7.5f, 0.2f, 4.5f);
        EcoBotController ecobotCtrl = botObj.AddComponent<EcoBotController>();
        ecobotCtrl.chargingStation = dock.transform;

        GameObject botBody = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        botBody.name = "Bot_Body";
        botBody.transform.parent = botObj.transform;
        botBody.transform.localPosition = new Vector3(0, 0.15f, 0);
        botBody.transform.localScale = new Vector3(0.85f, 0.15f, 0.85f);
        botBody.GetComponent<Renderer>().material = matBot;
        ecobotCtrl.robotBody = botBody.transform;

        GameObject botFrontNotch = GameObject.CreatePrimitive(PrimitiveType.Cube);
        botFrontNotch.name = "Bot_DirectionMarker";
        botFrontNotch.transform.parent = botBody.transform;
        botFrontNotch.transform.localPosition = new Vector3(0, 0.5f, 0.35f);
        botFrontNotch.transform.localScale = new Vector3(0.2f, 0.2f, 0.25f);
        botFrontNotch.GetComponent<Renderer>().material = matBotAccent;

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

        // Ultra-low Fidelity Characters (Single Neutral Capsule per Human)
        GameObject actorsRoot = new GameObject("Actors");
        actorsRoot.transform.parent = envRoot.transform;

        GameObject host = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        host.name = "Host";
        host.transform.parent = actorsRoot.transform;
        host.transform.position = new Vector3(-3.0f, 0.85f, -4.6f);
        host.transform.localScale = new Vector3(0.5f, 0.85f, 0.5f);
        host.GetComponent<Renderer>().material = matHuman;

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

        // Cleanable Items (Minimalist cubes & cylinders)
        GameObject itemsRoot = new GameObject("CleanableItems");
        itemsRoot.transform.parent = envRoot.transform;
        List<CleanableItem> itemList = new List<CleanableItem>();

        // Napkin
        GameObject napkin = GameObject.CreatePrimitive(PrimitiveType.Cube);
        napkin.name = "Item_Napkin";
        napkin.transform.parent = itemsRoot.transform;
        napkin.transform.position = new Vector3(-0.8f, 0.03f, -0.9f);
        napkin.transform.localScale = new Vector3(0.2f, 0.04f, 0.2f);
        napkin.GetComponent<Renderer>().material = matProp;
        var nItem = napkin.AddComponent<CleanableItem>();
        nItem.itemName = "Napkin (Trash)";
        nItem.category = ItemCategory.TrashNapkin;
        nItem.cleaningActionDescription = "Vacuuming floor";
        itemList.Add(nItem);

        // Can
        GameObject can = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        can.name = "Item_Can";
        can.transform.parent = itemsRoot.transform;
        can.transform.position = new Vector3(0.8f, 0.1f, -0.7f);
        can.transform.localScale = new Vector3(0.15f, 0.1f, 0.15f);
        can.GetComponent<Renderer>().material = matProp;
        var cItem = can.AddComponent<CleanableItem>();
        cItem.itemName = "Drink Can (Recyclable)";
        cItem.category = ItemCategory.RecyclableCan;
        cItem.cleaningActionDescription = "Picking up & sorting";
        itemList.Add(cItem);

        // Uncertain Object
        GameObject uncertainItem = GameObject.CreatePrimitive(PrimitiveType.Cube);
        uncertainItem.name = "Item_UncertainPersonalItem";
        uncertainItem.transform.parent = itemsRoot.transform;
        uncertainItem.transform.position = new Vector3(0.1f, 0.93f, 0.2f);
        uncertainItem.transform.localScale = new Vector3(0.12f, 0.03f, 0.2f);
        uncertainItem.GetComponent<Renderer>().material = matBot;
        var uItem = uncertainItem.AddComponent<CleanableItem>();
        uItem.itemName = "Forgotten Personal Belonging (Uncertain)";
        uItem.category = ItemCategory.ForgottenPersonalItem;
        uItem.requiresHumanConfirmation = true;
        uItem.cleaningActionDescription = "Pending host decision";
        itemList.Add(uItem);

        // Waypoints & Manager
        GameObject wpRoot = new GameObject("Waypoints");
        wpRoot.transform.parent = envRoot.transform;

        GameObject roomEntry = new GameObject("WP_DiningEntry");
        roomEntry.transform.parent = wpRoot.transform;
        roomEntry.transform.position = new Vector3(0, 0, -3.0f);

        GameObject exitDoor = new GameObject("WP_ExitDoor");
        exitDoor.transform.parent = wpRoot.transform;
        exitDoor.transform.position = new Vector3(0, 0, -10.0f);

        GameObject mgrObj = new GameObject("RestaurantScenarioManager");
        var mgr = mgrObj.AddComponent<RestaurantScenarioManager>();
        mgr.ecobot = ecobotCtrl;
        mgr.hostCharacter = host.transform;
        mgr.guestCharacters = new List<Transform> { guest1.transform, guest2.transform };
        mgr.roomEntryWaypoint = roomEntry.transform;
        mgr.exitDoorWaypoint = exitDoor.transform;
        mgr.itemsToClean = itemList;

        string scenePath = "Assets/Scenes/Restaurant_EcoBot_Scene.unity";
        EditorSceneManager.SaveScene(scene, scenePath);
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

    private static Material CreateMaterial(string name, Shader shader, Color color)
    {
        string path = $"Assets/Materials/{name}.mat";
        Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(shader);
            mat.color = color;
            AssetDatabase.CreateAsset(mat, path);
        }
        else
        {
            mat.shader = shader;
            mat.color = color;
        }
        return mat;
    }
}
