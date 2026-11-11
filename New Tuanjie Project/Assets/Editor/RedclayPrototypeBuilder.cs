using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// 一键生成 M1 原型场景（A 路线）：
/// - 单条非环形拉力赛段：长直道 / 长缓弯 / S 弯 / 直角弯 / 爬坡 / 飞坡 / 颠簸段
/// - 路面为 Catmull-Rom 样条生成的砂石网格（8m 宽），带 MeshCollider 与 SurfaceTag(Gravel)
/// - 800x800 沙地平（SurfaceTag(Sand)，更滑）+ 四周隐形边界墙
/// - 一辆四轮拉力车（4×WheelCollider 悬挂），转速→扭矩曲线，四驱
/// 菜单：红土拉力 → 构建原型场景。重复执行会覆盖旧场景 / 材质 / 网格资产。
public static class RedclayPrototypeBuilder
{
    // 中心线控制点（x, y, z）。y = 路面高度，用来做上下坡与飞坡。
    // 段落注释对应设计意图，调赛道直接改这里。
    static readonly Vector3[] ControlPoints =
    {
        new Vector3(-40f, 0.10f, -240f),   // 起点
        new Vector3(60f, 0.10f, -238f),    // 长直道
        new Vector3(130f, 0.25f, -230f),   // 长直道尾
        new Vector3(180f, 1.00f, -205f),   // 长缓弯入口
        new Vector3(195f, 1.60f, -165f),   // 长缓弯出口
        new Vector3(188f, 2.00f, -145f),   // S 弯 1
        new Vector3(170f, 2.30f, -128f),   // S 弯 2
        new Vector3(188f, 2.60f, -110f),   // S 弯 3
        new Vector3(180f, 2.90f, -92f),    // S 弯出口
        new Vector3(140f, 3.30f, -80f),    // 过渡直道
        new Vector3(105f, 3.80f, -78f),    // 直角弯入口（约 90° 向西）
        new Vector3(100f, 4.20f, -45f),    // 直角弯出口（转南）
        new Vector3(70f, 5.60f, -20f),     // 爬坡
        new Vector3(25f, 8.50f, -5f),      // 爬坡
        new Vector3(0f, 11.50f, 5f),       // 飞坡坡顶（速度够就腾空）
        new Vector3(-20f, 3.80f, 12f),     // 飞坡落地（陡降）
        new Vector3(-65f, 2.90f, 20f),     // 落地缓冲
        new Vector3(-115f, 2.70f, 28f),    // 颠簸段入口
        new Vector3(-165f, 2.50f, 34f),    // 颠簸段
        new Vector3(-215f, 2.30f, 40f),    // 颠簸段出口
        new Vector3(-285f, 2.10f, 46f),    // 冲刺直道
        new Vector3(-350f, 2.00f, 50f),    // 终点
    };

    const float RoadHalfWidth = 4f;      // 路面半宽（总宽 8m）
    const float SampleStep = 2f;         // 样条采样间距（米）
    const int BumpFromSeg = 17;          // 颠簸段：控制点段号 [17, 19)
    const int BumpToSegExclusive = 19;
    const float GroundExtent = 400f;     // 沙地平半宽（800x800）
    const string ScenePath = "Assets/Scenes/Prototype.unity";
    const string TrackMeshPath = "Assets/Meshes/TrackSurface.asset";

    [MenuItem("红土拉力/构建原型场景 (1车1赛道+计时)")]
    public static void Build()
    {
        // 注意：本方法会新建场景并覆盖 Prototype.unity。
        // 团结引擎 2022.3 没有 EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsToChange，
        // 构建前请自行保存正在编辑的其他场景。
        EnsureFolder("Assets/Scenes");
        EnsureFolder("Assets/Materials");
        EnsureFolder("Assets/Meshes");

        // ---- 材质 & 物理材质（存成资产）----
        Material matSand = MakeMat("Ground_Sand", new Color(0.80f, 0.70f, 0.55f));
        Material matGravel = MakeMat("Track_Gravel", new Color(0.55f, 0.50f, 0.42f));
        Material matBody = MakeMat("Car_Body", new Color(0.85f, 0.18f, 0.10f));
        Material matCabin = MakeMat("Car_Cabin", new Color(0.15f, 0.13f, 0.14f));
        Material matWheel = MakeMat("Car_Wheel", new Color(0.12f, 0.11f, 0.11f));
        Material matStakeA = MakeMat("Stake_White", new Color(0.95f, 0.93f, 0.88f));
        Material matStakeB = MakeMat("Stake_Orange", new Color(1f, 0.55f, 0.15f));
        Material matPillar = MakeMat("Gate_Pillar", new Color(0.92f, 0.90f, 0.86f));
        Material matBarStart = MakeMat("Gate_BarStart", new Color(0.20f, 0.20f, 0.20f));
        Material matBarFinish = MakeMat("Gate_BarFinish", new Color(0.85f, 0.15f, 0.10f));

        PhysicMaterial pmGravel = MakePhysicMat("Gravel", 0.55f, 0.60f);   // 砂石
        PhysicMaterial pmSand = MakePhysicMat("Sand", 0.30f, 0.35f);      // 沙地（更滑）

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // ---- 氛围：暖黄天光 + 红土雾 + 纯色天空 ----
        Color skyColor = new Color(0.96f, 0.80f, 0.66f);
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Linear;
        RenderSettings.fogStartDistance = 80f;
        RenderSettings.fogEndDistance = 550f;
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

        // ---- 中心线采样 + 颠簸 ----
        var points = new List<Vector3>();
        var segs = new List<int>();
        BuildCenterline(points, segs);
        ApplyBumps(points, segs);

        // ---- 沙地地面（赛道外的沙路）----
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.name = "Ground_Sand";
        ground.transform.position = new Vector3(0f, -0.1f, 0f);
        ground.transform.localScale = new Vector3(GroundExtent * 2f / 10f, 1f, GroundExtent * 2f / 10f);
        ground.GetComponent<Renderer>().sharedMaterial = matSand;
        ground.GetComponent<Collider>().sharedMaterial = pmSand;
        ground.AddComponent<SurfaceTag>().kind = SurfaceKind.Sand;

        // ---- 砂石赛道（样条网格）----
        BuildTrackMesh(points, matGravel, pmGravel);

        // ---- 地图边界隐形墙 ----
        BuildWalls();

        // ---- 路边护栏桩 ----
        BuildStakes(points, matStakeA, matStakeB);

        // ---- 比赛管理器（门要引用它）----
        var managerGo = new GameObject("RaceManager");
        var manager = managerGo.AddComponent<RaceManager>();

        // ---- 起终点门 ----
        Vector3 startDir = (points[1] - points[0]).normalized;
        BuildGate("StartGate", points[0], startDir, manager, false, matPillar, matBarStart);
        Vector3 finishDir = (points[points.Count - 1] - points[points.Count - 2]).normalized;
        BuildGate("FinishGate", points[points.Count - 1], finishDir, manager, true, matPillar, matBarFinish);

        // ---- 车：出生在起点门前 12m 的沙地上，开上赛道穿门开始计时 ----
        Vector3 spawnPos = points[0] - startDir * 12f;
        spawnPos.y = points[0].y + 0.9f;
        Quaternion spawnRot = Quaternion.LookRotation(startDir, Vector3.up);
        GameObject car = BuildCar(spawnPos, spawnRot, matBody, matCabin, matWheel);
        manager.car = car.transform;

        // ---- 相机 ----
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        var cam = camGo.AddComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = skyColor;
        cam.fieldOfView = 58f;
        cam.nearClipPlane = 0.3f;
        cam.farClipPlane = 900f;
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
                      "\n赛道：" + (points.Count * SampleStep) + "m 左右，含长直道/S弯/直角弯/爬坡/飞坡/颠簸段。" +
                      "\n按 Play 试驾：W/S 油门刹车，A/D 转向，空格手刹漂移，R 复位。");
        }
        else
        {
            Debug.LogError("[红土拉力] 场景保存失败：" + ScenePath);
        }
    }

    // ---------- 中心线与路面 ----------

    static void BuildCenterline(List<Vector3> points, List<int> segs)
    {
        points.Clear();
        segs.Clear();
        int n = ControlPoints.Length;
        for (int i = 0; i < n - 1; i++)
        {
            Vector3 p0 = ControlPoints[Mathf.Max(i - 1, 0)];
            Vector3 p1 = ControlPoints[i];
            Vector3 p2 = ControlPoints[i + 1];
            Vector3 p3 = ControlPoints[Mathf.Min(i + 2, n - 1)];
            float len = Vector3.Distance(p1, p2);
            int steps = Mathf.Max(2, Mathf.CeilToInt(len / SampleStep));
            for (int s = 0; s < steps; s++)
            {
                points.Add(CatmullRom(p0, p1, p2, p3, s / (float)steps));
                segs.Add(i);
            }
        }
        points.Add(ControlPoints[n - 1]);
        segs.Add(n - 2);
    }

    static Vector3 CatmullRom(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float t2 = t * t;
        float t3 = t2 * t;
        return 0.5f * ((2f * p1) + (-p0 + p2) * t
            + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2
            + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
    }

    static void ApplyBumps(List<Vector3> points, List<int> segs)
    {
        for (int i = 0; i < points.Count; i++)
        {
            if (segs[i] < BumpFromSeg || segs[i] >= BumpToSegExclusive) continue;
            Vector3 p = points[i];
            p.y += (Mathf.PerlinNoise(i * SampleStep * 0.55f, 7.3f) - 0.5f) * 0.2f;   // ±10cm 连续颠簸
            points[i] = p;
        }
    }

    static void BuildTrackMesh(List<Vector3> points, Material mat, PhysicMaterial pm)
    {
        int count = points.Count;
        var verts = new Vector3[count * 2];
        var tris = new int[(count - 1) * 6];

        for (int i = 0; i < count; i++)
        {
            Vector3 tangent;
            if (i == 0) tangent = (points[1] - points[0]).normalized;
            else if (i == count - 1) tangent = (points[count - 1] - points[count - 2]).normalized;
            else tangent = (points[i + 1] - points[i - 1]).normalized;

            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            verts[i * 2] = points[i] - right * RoadHalfWidth;
            verts[i * 2 + 1] = points[i] + right * RoadHalfWidth;

            if (i < count - 1)
            {
                int v = i * 2;
                int vn = (i + 1) * 2;
                int t = i * 6;
                tris[t] = v; tris[t + 1] = vn; tris[t + 2] = v + 1;
                tris[t + 3] = v + 1; tris[t + 4] = vn; tris[t + 5] = vn + 1;
            }
        }

        var mesh = new Mesh { name = "TrackSurface" };
        mesh.vertices = verts;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();

        if (AssetDatabase.LoadAssetAtPath<Mesh>(TrackMeshPath) != null)
            AssetDatabase.DeleteAsset(TrackMeshPath);
        AssetDatabase.CreateAsset(mesh, TrackMeshPath);

        var go = new GameObject("TrackSurface");
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = mat;
        var collider = go.AddComponent<MeshCollider>();
        collider.sharedMesh = mesh;
        collider.sharedMaterial = pm;
        go.AddComponent<SurfaceTag>().kind = SurfaceKind.Gravel;
    }

    static void BuildWalls()
    {
        var parent = new GameObject("BoundaryWalls");
        MakeWall(parent.transform, "N", new Vector3(0f, 4f, GroundExtent), new Vector3(GroundExtent * 2f, 8f, 2f));
        MakeWall(parent.transform, "S", new Vector3(0f, 4f, -GroundExtent), new Vector3(GroundExtent * 2f, 8f, 2f));
        MakeWall(parent.transform, "E", new Vector3(GroundExtent, 4f, 0f), new Vector3(2f, 8f, GroundExtent * 2f));
        MakeWall(parent.transform, "W", new Vector3(-GroundExtent, 4f, 0f), new Vector3(2f, 8f, GroundExtent * 2f));
    }

    static void MakeWall(Transform parent, string name, Vector3 pos, Vector3 size)
    {
        var go = new GameObject("Wall_" + name);
        go.transform.SetParent(parent, false);
        go.transform.position = pos;
        var box = go.AddComponent<BoxCollider>();
        box.size = size;
    }

    static void BuildStakes(List<Vector3> points, Material matA, Material matB)
    {
        var parent = new GameObject("TrackStakes");
        int index = 0;
        for (int i = 2; i < points.Count - 2; i += 6)   // 约每 12m 一根
        {
            Vector3 tangent = (points[i + 1] - points[i - 1]).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            MakeStake(parent.transform, points[i] - right * (RoadHalfWidth + 0.8f), (index & 1) == 0 ? matA : matB);
            MakeStake(parent.transform, points[i] + right * (RoadHalfWidth + 0.8f), (index & 1) == 0 ? matB : matA);
            index++;
        }
    }

    static void MakeStake(Transform parent, Vector3 pos, Material mat)
    {
        var stake = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        stake.name = "Stake";
        stake.transform.SetParent(parent);
        stake.transform.position = new Vector3(pos.x, pos.y + 0.5f, pos.z);
        stake.transform.localScale = new Vector3(0.1f, 0.5f, 0.1f);
        stake.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(stake.GetComponent<Collider>());   // 纯视觉，避免撞桩卡住
    }

    // ---------- 车辆 ----------

    static GameObject BuildCar(Vector3 pos, Quaternion rot, Material matBody, Material matCabin, Material matWheel)
    {
        var car = new GameObject("Car");
        car.tag = "Player";
        car.transform.position = pos;
        car.transform.rotation = rot;

        var rb = car.AddComponent<Rigidbody>();
        rb.mass = 1200f;
        rb.drag = 0.02f;          // 空气阻力由 RallyCar 按 v² 施加，这里只留微量
        rb.angularDrag = 0.8f;

        // 车身（Cube 自带 BoxCollider，就是车身碰撞体）
        var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
        body.name = "Body";
        body.transform.SetParent(car.transform, false);
        body.transform.localPosition = new Vector3(0f, 0.55f, 0f);
        body.transform.localScale = new Vector3(1.7f, 0.5f, 3.6f);
        body.GetComponent<Renderer>().sharedMaterial = matBody;

        var cabin = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cabin.name = "Cabin";
        cabin.transform.SetParent(car.transform, false);
        cabin.transform.localPosition = new Vector3(0f, 0.95f, -0.35f);
        cabin.transform.localScale = new Vector3(0.8f, 0.35f, 1.5f);
        cabin.GetComponent<Renderer>().sharedMaterial = matCabin;
        Object.DestroyImmediate(cabin.GetComponent<BoxCollider>());

        car.AddComponent<CarInput>();

        // 4 个 WheelCollider（顺序：前左、前右、后左、后右）
        WheelCollider wFL = MakeWheelCollider(car.transform, "Wheel_FL", new Vector3(-0.78f, 0.45f, 1.3f), matWheel);
        WheelCollider wFR = MakeWheelCollider(car.transform, "Wheel_FR", new Vector3(0.78f, 0.45f, 1.3f), matWheel);
        WheelCollider wRL = MakeWheelCollider(car.transform, "Wheel_RL", new Vector3(-0.78f, 0.45f, -1.3f), matWheel);
        WheelCollider wRR = MakeWheelCollider(car.transform, "Wheel_RR", new Vector3(0.78f, 0.45f, -1.3f), matWheel);

        var rally = car.AddComponent<RallyCar>();
        rally.wheels = new[] { wFL, wFR, wRL, wRR };

        // 车尾尘烟
        var dustGo = new GameObject("Dust");
        dustGo.transform.SetParent(car.transform, false);
        dustGo.transform.localPosition = new Vector3(0f, 0.05f, -1.9f);
        dustGo.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 往后喷
        var dust = dustGo.AddComponent<DustTrail>();
        dust.car = rally;

        car.AddComponent<TouchControls>();

        return car;
    }

    static WheelCollider MakeWheelCollider(Transform car, string name, Vector3 localPos, Material wheelMat)
    {
        var wheelGo = new GameObject(name);
        wheelGo.transform.SetParent(car, false);
        wheelGo.transform.localPosition = localPos;

        var wheel = wheelGo.AddComponent<WheelCollider>();
        wheel.radius = 0.36f;
        wheel.mass = 25f;
        wheel.suspensionDistance = 0.25f;
        JointSpring spring = wheel.suspensionSpring;
        spring.spring = 35000f;
        spring.damper = 4500f;
        spring.targetPosition = 0.35f;   // 静止时约 35% 行程
        wheel.suspensionSpring = spring;

        // 视觉轮子：独立对象，由 WheelPose 每帧用 GetWorldPose 同步
        var visual = new GameObject(name + "_Visual");
        visual.transform.SetParent(car, false);
        visual.transform.localPosition = localPos;
        var pose = visual.AddComponent<WheelPose>();
        pose.wheel = wheel;

        var mesh = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        mesh.name = "Mesh";
        mesh.transform.SetParent(visual.transform, false);
        mesh.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);   // 圆柱轴 Y → X
        mesh.transform.localScale = new Vector3(0.72f, 0.12f, 0.72f);  // 半径 0.36、宽 0.24
        mesh.GetComponent<Renderer>().sharedMaterial = wheelMat;
        Object.DestroyImmediate(mesh.GetComponent<Collider>());

        return wheel;
    }

    // ---------- 起终点门 ----------

    static void BuildGate(string name, Vector3 pos, Vector3 dir, RaceManager manager, bool isFinish,
        Material matPillar, Material matBar)
    {
        var gate = new GameObject(name);
        gate.transform.position = pos;
        gate.transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

        MakeGatePart(gate.transform, "PillarL", new Vector3(-3f, 1.5f, 0f), new Vector3(0.4f, 3f, 0.4f), matPillar);
        MakeGatePart(gate.transform, "PillarR", new Vector3(3f, 1.5f, 0f), new Vector3(0.4f, 3f, 0.4f), matPillar);
        MakeGatePart(gate.transform, "Bar", new Vector3(0f, 3.1f, 0f), new Vector3(6.6f, 0.5f, 0.5f), matBar);

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
        if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.08f);
        if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.08f);
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static PhysicMaterial MakePhysicMat(string name, float dynamicFriction, float staticFriction)
    {
        string path = "Assets/Materials/" + name + ".physicMaterial";
        if (AssetDatabase.LoadAssetAtPath<PhysicMaterial>(path) != null)
            AssetDatabase.DeleteAsset(path);
        var pm = new PhysicMaterial(name)
        {
            dynamicFriction = dynamicFriction,
            staticFriction = staticFriction,
            frictionCombine = PhysicMaterialCombine.Minimum,
            bounceCombine = PhysicMaterialCombine.Minimum,
        };
        AssetDatabase.CreateAsset(pm, path);
        return pm;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
        string name = System.IO.Path.GetFileName(path);
        AssetDatabase.CreateFolder(parent, name);
    }
}
