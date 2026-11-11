using System.Collections.Generic;
using UnityEngine;

/// 四轮拉力车：WheelCollider 负责悬挂与轮胎滑移，本脚本负责：
/// 1) 发动机转速 → 扭矩曲线 → 轮上扭矩（按真实两参数换挡图谱的自动挡：R / N / 1-6）
/// 2) 按脚下路面（SurfaceTag）切换轮胎摩擦刚度；前后轮刚度不同 —— 前轮侧向更抓、后轮略滑
public class RallyCar : MonoBehaviour
{
    [Header("轮子（顺序：前左、前右、后左、后右）")]
    public WheelCollider[] wheels = new WheelCollider[4];

    [Header("动力总成")]
    public float efficiency = 0.85f;
    public float idleRpm = 1200f;        // 怠速（GR 雅力士约 1200）
    public float maxRpm = 8600f;         // 红线（约 8600）
    public float maxSpeed = 46f;         // 保护限速 m/s
    public float frontDrive = 0.40f;     // 前轴动力占比（其余给后轴）
    public AnimationCurve torqueCurve;   // 转速 → 发动机扭矩(Nm)

    [Header("变速箱（两参数换挡图谱：油门 × 转速）")]
    public float[] gearRatios = { 3.538f, 2.238f, 1.535f, 1.162f, 1.081f, 0.902f }; // 真实 GR Yaris 6MT
    public float finalDrive = 4.06f;      // 主减速比
    public float reverseRatio = 3.831f;   // 倒挡比
    public float shiftUpRpm = 7900f;          // 满油门升挡（红线前）
    public float lightThrottleUpRpm = 3000f;  // 轻油门升挡
    public float shiftDownRpm = 1500f;        // 轻油门降挡
    public float fullThrottleDownRpm = 3600f; // 满油门降挡
    public float shiftTime = 0.28f;           // 单次换挡时长（断油→接合）
    public float shiftLockTime = 0.45f;       // 换挡后冷却，防来回跳挡
    public float kickdownRpm = 4200f;         // 深油门且低于该转速 → 强制降挡
    public float engineBrakeTorque = 900f;    // 发动机制动：最大轮上反拖扭矩（低挡更明显）

    [Header("操控")]
    public float steerAngleMax = 38f;      // 前轮最大转角
    public float steerSpeedFalloff = 70f;  // 到达该速度时转角约减半
    public float steerResponse = 4.5f;     // 转向跟随速度（满行程/秒，越大越跟手）
    public float throttleResponse = 6f;    // 油门响应（满行程/秒）
    public float brakeTorque = 3200f;
    public float handbrakeTorque = 3600f;
    public float handbrakeGripScale = 0.5f;

    [Header("空气")]
    public float airDrag = 0.7f;         // 合并系数：0.5*ρ*Cd*A
    public float downforce = 420f;

    public bool IsGrounded { get; private set; }
    public float Speed { get; private set; }
    public float SlipAmount { get; private set; }
    public bool IsDrifting => IsGrounded && SlipAmount > 0.35f;
    public float EngineRpm { get; private set; }
    public int CurrentGear => currentGear;   // -1 = R，0 = N，1..6

    Rigidbody rb;
    CarInput input;
    float currentSteer;
    float smoothedThrottle;
    int currentGear = 1;
    float shiftTimer;      // 换挡进行中的剩余时间（断油→接合）
    float shiftLock;       // 换挡后冷却计时（防 hunting）
    float stopTimer;       // 停车计时（防低速抖动时反复切 N）
    readonly Dictionary<Collider, SurfaceKind> surfaceCache = new Dictionary<Collider, SurfaceKind>();

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        input = GetComponent<CarInput>();
        if (torqueCurve == null || torqueCurve.length == 0) torqueCurve = DefaultTorqueCurve();
        MigrateOldDefaults();

        // 重心低且略靠前：防翻 + 车尾更活
        rb.centerOfMass = new Vector3(0f, -0.30f, 0.15f);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        InitFrictionCurves();

        // 运行时自动挂发动机音效（无需在场景手动添加组件；音效为程序合成，零外部资产）
        if (GetComponent<EngineAudio>() == null)
        {
            var engineAudio = gameObject.AddComponent<EngineAudio>();
            engineAudio.car = this;
        }
    }

    /// 兼容旧场景：把早期默认值升级为真实 GR Yaris 规格（场景里存的是旧数值）
    void MigrateOldDefaults()
    {
        if (Mathf.Approximately(idleRpm, 900f) && Mathf.Approximately(maxRpm, 8000f))
        {
            idleRpm = 1200f;
            maxRpm = 8600f;
        }
        if (gearRatios != null && gearRatios.Length == 6 && Mathf.Approximately(gearRatios[0], 3.60f))
        {
            gearRatios = new[] { 3.538f, 2.238f, 1.535f, 1.162f, 1.081f, 0.902f };
            finalDrive = 4.06f;
            reverseRatio = 3.831f;
        }
        if (Mathf.Approximately(shiftUpRpm, 6400f) || Mathf.Approximately(shiftUpRpm, 7200f)) shiftUpRpm = 7900f;
        if (Mathf.Approximately(shiftDownRpm, 2100f) || Mathf.Approximately(shiftDownRpm, 2400f)) shiftDownRpm = 1500f;
        if (rb != null && rb.mass > 1000f && rb.mass < 1100f) rb.mass = 1280f;   // 真实整备质量
        if (torqueCurve != null && torqueCurve.length >= 6 && Mathf.Approximately(torqueCurve.Evaluate(4000f), 435f))
            torqueCurve = DefaultTorqueCurve();   // 旧扭矩曲线 → 真实 400Nm 平台
    }

    static AnimationCurve DefaultTorqueCurve()
    {
        // GR Yaris：400 Nm @ 3250-4600 rpm，红线前逐渐衰减
        return new AnimationCurve(
            new Keyframe(1200f, 220f),
            new Keyframe(2000f, 330f),
            new Keyframe(3250f, 400f),
            new Keyframe(4600f, 400f),
            new Keyframe(6000f, 355f),
            new Keyframe(7500f, 285f),
            new Keyframe(8600f, 205f));
    }

    void InitFrictionCurves()
    {
        if (wheels == null) return;
        for (int i = 0; i < wheels.Length; i++)
        {
            WheelCollider w = wheels[i];
            if (w == null) continue;
            bool front = i < 2;
            w.forwardFriction = new WheelFrictionCurve
            {
                extremumSlip = 0.35f,
                extremumValue = 1f,
                asymptoteSlip = 0.7f,
                asymptoteValue = 0.7f,
                stiffness = front ? 1.0f : 1.05f,   // 后轮纵向略高：驱动轮更容易挠地
            };
            w.sidewaysFriction = new WheelFrictionCurve
            {
                extremumSlip = 0.30f,
                extremumValue = 1f,
                asymptoteSlip = 0.55f,
                asymptoteValue = 0.7f,
                stiffness = front ? 1.15f : 1.0f,   // 前轮侧向更抓、后轮略滑 → 转向过度
            };
        }
    }

    void FixedUpdate()
    {
        if (input == null || wheels == null || wheels.Length < 4 || wheels[0] == null) return;

        float dt = Time.fixedDeltaTime;
        bool handbrake = input.Handbrake;
        Speed = rb.velocity.magnitude;
        float forwardSpeed = Vector3.Dot(rb.velocity, transform.forward);

        // 油门平滑（避免瞬间满扭，手感更线性）
        smoothedThrottle = Mathf.MoveTowards(smoothedThrottle, input.Throttle, throttleResponse * dt);
        float throttle01 = Mathf.Clamp01(Mathf.Abs(smoothedThrottle));

        // 转向：平滑跟随 + 随速度衰减（低速大角度、高速沉稳）
        float steerScale = 1f / (1f + Speed / steerSpeedFalloff);
        float steerTarget = input.Steer * steerAngleMax * steerScale;
        currentSteer = Mathf.MoveTowards(currentSteer, steerTarget, steerAngleMax * steerResponse * dt);
        wheels[0].steerAngle = currentSteer;
        wheels[1].steerAngle = currentSteer;

        // 逐个轮：接地、路面摩擦、滑移统计
        IsGrounded = false;
        float slipSum = 0f;
        int grounded = 0;
        for (int i = 0; i < 4; i++)
        {
            WheelCollider w = wheels[i];
            SurfaceKind surface = SurfaceKind.Sand;
            if (w.GetGroundHit(out WheelHit hit))
            {
                IsGrounded = true;
                grounded++;
                surface = ResolveSurface(hit.collider);
                slipSum += Mathf.Abs(hit.forwardSlip) + Mathf.Abs(hit.sidewaysSlip);
            }
            ApplyFriction(i, w, surface, handbrake);
        }
        SlipAmount = grounded > 0 ? slipSum / grounded : 0f;

        // ---- 变速箱：先按上一帧转速决定是否换挡 ----
        float wheelRpm = (Mathf.Abs(wheels[2].rpm) + Mathf.Abs(wheels[3].rpm)) * 0.5f;
        UpdateGearbox(smoothedThrottle, throttle01, forwardSpeed, dt);

        // 转速：换挡期间模拟离合滑差（平滑靠向新挡目标，不瞬跳）
        float targetRpm = ComputeEngineRpm(wheelRpm, smoothedThrottle, currentGear);
        bool shifting = shiftTimer > 0f;
        if (shifting)
        {
            EngineRpm = Mathf.MoveTowards(EngineRpm, targetRpm, 14000f * dt);
            shiftTimer -= dt;
        }
        else
        {
            EngineRpm = targetRpm;
        }

        float ratio = GearRatio(currentGear);   // N = 0
        float wheelTorque = torqueCurve.Evaluate(Mathf.Clamp(EngineRpm, 800f, maxRpm)) * ratio * efficiency;

        // 换挡过渡：扭矩按正弦曲线 1 → 0 → 1，模拟断油再平滑接合
        float torqueScale = 1f;
        if (shifting)
        {
            float k = Mathf.Clamp01((shiftTime - shiftTimer) / Mathf.Max(0.01f, shiftTime));
            torqueScale = 1f - Mathf.Sin(k * Mathf.PI);
        }

        bool overLimit = forwardSpeed > maxSpeed;
        bool braking = smoothedThrottle < 0f && forwardSpeed > 1f;   // 前进中踩 S = 刹车
        bool reversing = currentGear < 0;
        bool engineBraking = Mathf.Abs(smoothedThrottle) < 0.05f && forwardSpeed > 1f
                             && currentGear >= 1 && !shifting && !handbrake;

        // 发动机制动 = 发动机被车轮反拖的负扭矩（低挡强、高挡弱、转速越高越强）
        float engineBrake = 0f;
        if (engineBraking && !braking)
        {
            float gearFactor = Mathf.Clamp01(1.25f - 0.15f * currentGear);   // 1挡≈1.10，6挡≈0.35
            float rpmFactor = Mathf.Clamp01(EngineRpm / maxRpm);
            engineBrake = engineBrakeTorque * gearFactor * (0.4f + 0.6f * rpmFactor);
        }

        for (int i = 0; i < 4; i++)
        {
            WheelCollider w = wheels[i];
            float share = i < 2 ? frontDrive * 0.5f : (1f - frontDrive) * 0.5f;

            float motor = 0f;
            if (!braking && !overLimit && ratio > 0f)
                motor = smoothedThrottle * wheelTorque * share * torqueScale;
            motor -= engineBrake * share;   // 负扭矩 = 发动机制动（通过传动系反拖，不抱死）
            if (reversing && forwardSpeed < -10f) motor = 0f;   // 倒车限速

            float brake = 0f;
            if (braking) brake = brakeTorque;
            if (handbrake && i >= 2) brake = handbrakeTorque;

            w.motorTorque = motor;
            w.brakeTorque = brake;
        }

        // 空气阻力（v²）+ 下压力
        if (Speed > 0.1f)
            rb.AddForce(-rb.velocity / Speed * (airDrag * Speed * Speed), ForceMode.Force);
        rb.AddForce(-transform.up * (downforce * Mathf.Clamp01(Speed / 20f)), ForceMode.Force);
    }

    float GearRatio(int gear)
    {
        if (gear > 0 && gear <= gearRatios.Length) return gearRatios[gear - 1] * finalDrive;
        if (gear < 0) return reverseRatio * finalDrive;
        return 0f;   // N
    }

    float ComputeEngineRpm(float wheelRpm, float throttle, int gear)
    {
        if (gear == 0)
        {
            // 空挡：转速跟随油门空转（踩油门能拉到高转，声音随之变高）
            float spin = idleRpm + Mathf.Max(0f, throttle) * (maxRpm - idleRpm) * 0.85f;
            return Mathf.Clamp(spin, idleRpm, maxRpm);
        }
        return Mathf.Clamp(Mathf.Max(idleRpm, wheelRpm * GearRatio(gear)), idleRpm, maxRpm);
    }

    /// 两参数换挡图谱：油门开度 × 转速。轻油门早升挡，大油门延迟升挡；深踩油门可 kickdown。
    void UpdateGearbox(float throttle, float throttle01, float forwardSpeed, float dt)
    {
        if (shiftLock > 0f) shiftLock -= dt;

        float absFwd = Mathf.Abs(forwardSpeed);
        bool stopped = absFwd < 0.4f;
        bool noInput = Mathf.Abs(throttle) < 0.05f;
        stopTimer = (stopped && noInput) ? stopTimer + dt : 0f;

        float upRpm = Mathf.Lerp(lightThrottleUpRpm, shiftUpRpm, throttle01);
        float downRpm = Mathf.Lerp(shiftDownRpm, fullThrottleDownRpm, throttle01);

        if (currentGear >= 1)
        {
            // Kickdown：深踩油门且转速偏低 → 强制降挡
            if (shiftLock <= 0f && throttle01 > 0.8f && EngineRpm < kickdownRpm && currentGear > 1)
            {
                currentGear--;
                BeginShift(0.18f);
                return;
            }

            if (shiftLock <= 0f)
            {
                if (EngineRpm > upRpm && currentGear < gearRatios.Length)
                {
                    currentGear++;
                    BeginShift(shiftTime);
                }
                else if (EngineRpm < downRpm && currentGear > 1)
                {
                    currentGear--;
                    BeginShift(shiftTime);
                }
                else if (stopped && throttle < -0.2f)
                {
                    currentGear = -1;   // 停稳后踩 S → 倒挡
                    BeginShift(0.12f);
                }
                else if (stopTimer > 0.8f)
                {
                    currentGear = 0;    // 停车 0.8s 无输入 → 空挡
                    shiftLock = 0.1f;
                }
            }
        }
        else if (currentGear == -1)
        {
            if (throttle > 0.2f && stopped && shiftLock <= 0f)
            {
                currentGear = 1;
                BeginShift(0.06f);   // 起步几乎无延迟
            }
            else if (stopTimer > 0.8f && shiftLock <= 0f)
            {
                currentGear = 0;
                shiftLock = 0.1f;
            }
        }
        else // N
        {
            if (throttle > 0.2f && shiftLock <= 0f)
            {
                currentGear = 1;
                BeginShift(0.06f);   // 起步几乎无延迟
            }
            else if (throttle < -0.2f && stopped && shiftLock <= 0f)
            {
                currentGear = -1;
                BeginShift(0.10f);
            }
        }
    }

    void BeginShift(float duration)
    {
        shiftTimer = duration;
        shiftLock = duration + shiftLockTime;
    }

    SurfaceKind ResolveSurface(Collider ground)
    {
        if (ground == null) return SurfaceKind.Sand;
        if (surfaceCache.TryGetValue(ground, out SurfaceKind kind)) return kind;
        SurfaceTag tag = ground.GetComponent<SurfaceTag>();
        kind = tag != null ? tag.kind : SurfaceKind.Sand;
        surfaceCache[ground] = kind;
        return kind;
    }

    void ApplyFriction(int index, WheelCollider w, SurfaceKind surface, bool handbrake)
    {
        float surfaceScale;
        switch (surface)
        {
            case SurfaceKind.Asphalt: surfaceScale = 1.3f; break;
            case SurfaceKind.Gravel: surfaceScale = 1.0f; break;
            default: surfaceScale = 0.62f; break;   // Sand 最滑
        }

        bool front = index < 2;
        float forwardBase = front ? 1.0f : 1.05f;
        float sideBase = front ? 1.15f : 1.0f;
        float hb = (handbrake && !front) ? handbrakeGripScale : 1f;   // 手刹锁后轮 → 后轮摩擦骤降、可甩尾

        WheelFrictionCurve f = w.forwardFriction;
        f.stiffness = forwardBase * surfaceScale * hb;
        w.forwardFriction = f;

        WheelFrictionCurve s = w.sidewaysFriction;
        s.stiffness = sideBase * surfaceScale * hb;
        w.sidewaysFriction = s;
    }
}
