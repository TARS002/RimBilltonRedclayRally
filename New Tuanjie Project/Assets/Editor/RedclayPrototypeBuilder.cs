using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// 一键生成 M1 原型场景：1 辆方块车 + 1 条红土短赛道 + 起终点计时 + 相机 + UI。
/// 菜单：红土拉力 → 构建原型场景 (1车1赛道+计时)。重复执行会覆盖旧场景与材质资产。
/// 全部用基本体拼装，零外部素材；配色即"雷姆必拓红土"氛围（可改 Waypoints 换赛道）。
public static class RedclayPrototypeBuilder
{
    // 赛道路径点 (x, z)：从起点到终点，约 600m 的绕圈赛道
    static readonly Vector2[] Waypoints =
    {
        new Vector2(0f, -40f),     // 起点
        new Vector2(0f, -8f),
        new Vector2(-20f, 22f),
        new Vector2(-16f, 60f),
        new Vector2(18f, 86f),
        new Vector2(55f, 72f),
        new Vector2(62f, 32f),
        new Vector2(36f, -2f),
        new Vector2(46f, -48f),
        new Vector2(92f, -78f),
        new Vector2(132f, -58f),
        new Vector2(127f, -12f),
        new Vector2(92f, 22f),
        new Vector2(102f, 62f),
        new Vector2(152f, 84f),
        new Vector2(186f, 62f),     // 终点
    };

    const float ConeSpacing = 8f;      // 路锥间距（米）
    const float TrackHalfWidth = 3.5f; // 半路宽：锥摆在中心线两侧
    const string ScenePath = "Assets/Scenes/Prototype.unity";

    [MenuItem("红土拉力/构建原型场景 (1车1赛道+计时)")]
    public static void Build()
    {
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsToChange()) return;

        EnsureFolder("Assets/Scenes");
        EnsureFolder("Assets/Materials");

        // ---- 材质（保存为资产，场景才能正确引用；重复构建时先删旧的）----
        Material matGround = MakeMat("Ground_RedClay", new Color(0.72f, 0.40f, 0.26f));
        Material matBody = MakeMat("Car_Body", new Color(0.85f, 0.18f, 0.10f));
        Material matCabin = MakeMat("Car_Cabin", new Color(0.15f, 0.13f, 0.14f));
        Material matWheel = MakeMat("Car_Wheel", new Color(0.12f, 0.11f, 0.11f));
        Material matConeA = MakeMat("Cone_White", new Color(0.95f, 0.93f, 0.88f));
        Material matConeB = MakeMat("Cone_Orange", new Color(1f, 0.55f, 0.15f));
        Material matPillar = MakeMat("Gate_Pillar", new Color(0.92f, 0.90f, 0.86f));
        Material matBarStart = MakeMat("Gate_BarStart", new Color(0.20f, 0.20f, 0.20f));
        Material matBarFinish = MakeMat("Gate_BarFinish", new Color(0.85f, 0.15f, 0.10f));

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- 氛围：暖黄天光 + 红土雾 + 纯色天空 ----
        Color skyColor = new Color(0.96f, 0.80f, 0.66f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 60f;
        RenderSettings.fogEndDistance = 350f;   // 雾兼做远景剔除（移动端预算）
        RenderSettings.fogColor = skyColor;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.88f, 0.80f, 0.72f);

        var sunGo = new GameObject("Sun");
        var light = sunGo.AddComponent<Light>();
        light.type = LightType.Directional;
        light.color = new Color(1f, 0.93f, 0.82f);
        light.intensity = 1.1f;
        light.shadows = LightShadows.Soft;   // 手机端上线前在 Quality 面板关阴影
        sunGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

        // ---- 地面：400x400 红土平原 ----
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground";
        ground.transform.localScale = new Vector3(40f, 1f, 40f);   // Plane 10x10 → 400x400
        ground.GetComponent<Renderer>().sharedMaterial = matGround;
        var pm = new PhysicMaterial("Ground_LowFriction")
        {
            dynamicFriction = 0.25f,   // 低摩擦让漂移交给 CarController 的 grip 管
            staticFriction = 0.2f,
            frictionCombine = PhysicMaterialCombine.Minimum,
        };
        AssetDatabase.CreateAsset(pm, "Assets/Materials/Ground_LowFriction.physicMaterial");
        ground.GetComponent<Collider>().sharedMaterial = pm;

        // ---- 比赛管理器（先建，起终点门要引用它）----
        var managerGo = new GameObject("RaceManager");
        var manager = managerGo.AddComponent<RaceManager>();

        // ---- 赛道：路锥 + 起终点门 ----
        BuildCones(matConeA, matConeB);
        Vector3 startDir = DirOf(Waypoints[0], Waypoints[1]);
        BuildGate("StartGate", Waypoints[0], startDir, manager, false, matPillar, matBarStart);
        Vector3 finishDir = DirOf(Waypoints[Waypoints.Length - 2], Waypoints[Waypoints.Length - 1]);
        BuildGate("FinishGate", Waypoints[Waypoints.Length - 1], finishDir, manager, true, matPillar, matBarFinish);

        // ---- 车：基本体拼装，出生在起点线后 12m ----
        Vector3 spawnPos = new Vector3(Waypoints[0].x, 0.3f, Waypoints[0].y) - startDir * 12f;
        Quaternion spawnRot = Quaternion.LookRotation(startDir, Vector3.up);
        GameObject car = BuildCar(spawnPos, spawnRot, matBody, matCabin, matWheel);
        manager.car = car.transform;

        // ---- 相机：追尾 ----
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = skyColor;
        cam.fieldOfView = 58f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 600f;
        camGo.AddComponent<AudioListener>();
        var chase = camGo.AddComponent<ChaseCamera>();
        chase.target = car.transform;

        // ---- UI（脚本自己生成画布）----
        var uiGo = new GameObject("RaceUI");
        var ui = uiGo.AddComponent<RaceUI>();
        ui.manager = manager;
        manager.ui = ui;

        // ---- 保存 ----
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            AssetDatabase.DeleteAsset(ScenePath);
        bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
        AssetDatabase.SaveAssets();
        if (saved)
        {
            if (!Application.isBatchMode)
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            Debug.Log("[红土拉力] 原型场景已生成并保存：" + ScenePath +
                      "\n打开该场景按 Play 试驾：W/S 油门刹车，A/D 转向，空格手刹漂移，R 复位。");
        }
        else
        {
            Debug.LogError("[红土拉力] 场景保存失败：" + ScenePath);
        }
    }

    // ---------- 车辆 ----------

    static GameObject BuildCar(Vector3 pos, Quaternion rot, Material matBody, Material matCabin, Material matWheel)
    {
        var car = new GameObject("Car");
        car.tag = "Player";
        car.transform.position = pos;
        car.transform.rotation = rot;

        var rb = car.AddComponent<Rigidbody>();
        rb.mass = 900f;
        rb.drag = 0.05f;          // 微量滑行阻力，限速交给 CarController
        rb.angularDrag = 1.2f;

        // 车身：底盘盒（自带 BoxCollider，就是全车的碰撞体）
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(car.transform, false);
        body.transform.localPosition = Vector3.zero;
        body.transform.localScale = new Vector3(1.8f, 0.5f, 3.4f);
        body.GetComponent<Renderer>().sharedMaterial = matBody;

        // 座舱
        var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cabin.name = "Cabin";
        cabin.transform.SetParent(car.transform, false);
        cabin.transform.localPosition = new Vector3(0f, 0.43f, -0.35f);
        cabin.transform.localScale = new Vector3(0.85f, 0.36f, 1.5f);
        cabin.GetComponent<Renderer>().sharedMaterial = matCabin;
        Object.DestroyImmediate(cabin.GetComponent<BoxCollider>());   // 底盘盒已够用

        car.AddComponent<CarInput>();
        var controller = car.AddComponent<CarController>();

        // 车轮：只有外观，碰撞体去掉（物理是单刚体模型）
        MakeWheel(car.transform, controller, new Vector3(-0.85f, 0.11f, 1.28f), true, matWheel, "Wheel_FL");
        MakeWheel(car.transform, controller, new Vector3(0.85f, 0.11f, 1.28f), true, matWheel, "Wheel_FR");
        MakeWheel(car.transform, controller, new Vector3(-0.85f, 0.11f, -1.28f), false, matWheel, "Wheel_RL");
        MakeWheel(car.transform, controller, new Vector3(0.85f, 0.11f, -1.28f), false, matWheel, "Wheel_RR");

        // 车尾尘烟
        var dustGo = new GameObject("Dust");
        dustGo.transform.SetParent(car.transform, false);
        dustGo.transform.localPosition = new Vector3(0f, -0.02f, -1.8f);
        dustGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 让烟往后喷
        var dust = dustGo.AddComponent<DustTrail>();
        dust.car = controller;

        car.AddComponent<TouchControls>();

        return car;
    }

    static void MakeWheel(Transform carRoot, CarController controller, Vector3 localPos, bool isFront, Material mat, string name)
    {
        var pivot = new GameObject(name);
        pivot.transform.SetParent(carRoot, false);
        pivot.transform.localPosition = localPos;
        var wheelVisual = pivot.AddComponent<WheelVisual>();
        wheelVisual.car = controller;
        wheelVisual.isFront = isFront;

        var mesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        mesh.name = "Mesh";
        mesh.transform.SetParent(pivot.transform, false);
        mesh.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);   // 圆柱轴 Y → X（轮轴方向）
        mesh.transform.localScale = new Vector3(0.72f, 0.12f, 0.72f);  // 半径 0.36、宽 0.24
        mesh.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(mesh.GetComponent<Collider>());
    }

    // ---------- 赛道 ----------

    static void BuildCones(Material matA, Material matB)
    {
        var parent = new GameObject("Cones");
        int index = 0;
        for (int i = 0; i < Waypoints.Length - 1; i++)
        {
            Vector2 a = Waypoints[i];
            Vector2 b = Waypoints[i + 1];
            Vector2 dir = (b - a).normalized;
            Vector2 perp = new Vector2(-dir.y, dir.x);
            float len = Vector2.Distance(a, b);
            int steps = Mathf.FloorToInt(len / ConeSpacing);
            for (int s = 0; s <= steps; s++)
            {
                // 起点/终点位置留给起终点门，避免锥和门叠在一起
                if (i == 0 && s == 0) continue;
                if (i == Waypoints.Length - 2 && s == steps) continue;

                Vector2 p = a + dir * (s * ConeSpacing);
                bool white = (index & 1) == 0;   // 红白相间
                MakeCone(parent.transform,
                    new Vector3(p.x + perp.x * TrackHalfWidth, 0.22f, p.y + perp.y * TrackHalfWidth),
                    white ? matA : matB);
                MakeCone(parent.transform,
                    new Vector3(p.x - perp.x * TrackHalfWidth, 0.22f, p.y - perp.y * TrackHalfWidth),
                    white ? matB : matA);
                index++;
            }
        }
    }

    static void MakeCone(Transform parent, Vector3 pos, Material mat)
    {
        var cone = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        cone.name = "Cone";
        cone.transform.SetParent(parent);
        cone.transform.position = pos;
        cone.transform.localScale = new Vector3(0.5f, 0.22f, 0.5f);   // 直径 0.5、高 0.44
        cone.GetComponent<Renderer>().sharedMaterial = mat;
        cone.GetComponent<Collider>().isTrigger = true;   // M1 只做视觉路标，不挡车
    }

    static void BuildGate(string name, Vector2 at, Vector3 dir, RaceManager manager, bool isFinish,
        Material matPillar, Material matBar)
    {
        var gate = new GameObject(name);
        gate.transform.position = new Vector3(at.x, 0f, at.y);
        gate.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

        MakeGatePart(gate.transform, "PillarL", new Vector3(-3f, 1.5f, 0f), new Vector3(0.4f, 3f, 0.4f), matPillar);
        MakeGatePart(gate.transform, "PillarR", new Vector3(3f, 1.5f, 0f), new Vector3(0.4f, 3f, 0.4f), matPillar);
        MakeGatePart(gate.transform, "Bar", new Vector3(0f, 3.1f, 0f), new Vector3(6.6f, 0.5f, 0.5f), matBar);

        // 触发判定盒：覆盖门洞，车穿过即判定
        var col = gate.AddComponent<BoxCollider>();
        col.center = new Vector3(0f, 1.5f, 0f);
        col.size = new Vector3(6.2f, 3f, 1.2f);
        col.isTrigger = true;

        var raceGate = gate.AddComponent<RaceGate>();
        raceGate.manager = manager;
        raceGate.isFinish = isFinish;
    }

    static void MakeGatePart(Transform gate, string name, Vector3 localPos, Vector3 scale, Material mat)
    {
        var part = GameObject.CreatePrimitive(PrimitiveType.Cube);
        part.name = name;
        part.transform.SetParent(gate, false);
        part.transform.localPosition = localPos;
        part.transform.localScale = scale;
        part.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // ---------- 工具 ----------

    static Vector3 DirOf(Vector2 a, Vector2 b)
    {
        return new Vector3(b.x - a.x, 0f, b.y - a.y).normalized;
    }

    static Material MakeMat(string name, Color color)
    {
        string path = "Assets/Materials/" + name + ".mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(path) != null)
            AssetDatabase.DeleteAsset(path);

        // 项目可能是内置管线或 URP，按当前管线选 Lit shader
        bool urp = GraphicsSettings.currentRenderPipeline != null;
        Shader shader = urp ? Shader.Find("Universal Render Pipeline/Lit") : Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Standard");
        var m = new Material(shader);
        if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
        if (m.HasProperty("_Color")) m.SetColor("_Color", color);
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.08f);   // 红土/哑光
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.08f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string name = System.IO.Path.GetFileName(path);
        AssetDatabase.CreateFolder(parent, name);
    }
}
