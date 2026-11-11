using System.Collections.Generic;
using UnityEngine;

/// 四轮拉力车：WheelCollider 负责悬挂与轮胎滑移，本脚本负责两件事：
/// 1) 发动机转速 → 扭矩曲线 → 轮上扭矩（单挡，四驱按 frontDrive 分配前后）
/// 2) 按脚下路面（SurfaceTag）切换轮胎摩擦刚度：柏油 > 砂石 > 沙地，手刹时后轮再降
public class RallyCar : MonoBehaviour
{
    [Header("轮子（顺序：前左、前右、后左、后右）")]
    public WheelCollider[] wheels = new WheelCollider[4];

    [Header("动力总成")]
    public float totalRatio = 4.2f;      // 单挡总传动比（含主减）
    public float efficiency = 0.85f;     // 传动效率
    public float idleRpm = 900f;
    public float maxRpm = 7800f;
    public float maxSpeed = 45f;         // 保护限速 m/s
    public float frontDrive = 0.42f;     // 前轴动力占比（其余给后轴）
    public AnimationCurve torqueCurve;   // 转速 → 发动机扭矩(Nm)

    [Header("操控")]
    public float steerAngleMax = 32f;
    public float steerSpeedFalloff = 45f;   // 到达该速度时前轮转角减半
    public float brakeTorque = 2000f;
    public float handbrakeTorque = 3000f;
    public float handbrakeGripScale = 0.55f;

    [Header("空气")]
    public float airDrag = 0.6f;         // 合并系数：0.5*ρ*Cd*A
    public float downforce = 350f;

    public bool IsGrounded { get; private set; }
    public float Speed { get; private set; }
    public float SlipAmount { get; private set; }   // 四轮滑移绝对值平均
    public bool IsDrifting => IsGrounded && SlipAmount > 0.35f;
    public float EngineRpm { get; private set; }

    Rigidbody rb;
    CarInput input;
    readonly Dictionary<Collider, SurfaceKind> surfaceCache = new Dictionary<Collider, SurfaceKind>();

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        input = GetComponent<CarInput>();
        if (torqueCurve == null || torqueCurve.length == 0) torqueCurve = DefaultTorqueCurve();
        rb.centerOfMass = new Vector3(0f, -0.35f, 0.1f);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        InitFrictionCurves();
    }

    static AnimationCurve DefaultTorqueCurve()
    {
        return new AnimationCurve(
            new Keyframe(1000f, 170f),
            new Keyframe(2500f, 280f),
            new Keyframe(4500f, 330f),
            new Keyframe(6000f, 300f),
            new Keyframe(7000f, 240f),
            new Keyframe(7800f, 160f));
    }

    void InitFrictionCurves()
    {
        if (wheels == null) return;
        foreach (WheelCollider w in wheels)
        {
            if (w == null) continue;
            w.forwardFriction = new WheelFrictionCurve
            {
                extremumSlip = 0.4f,
                extremumValue = 1f,
                asymptoteSlip = 0.8f,
                asymptoteValue = 0.75f,
                stiffness = 1f,
            };
            w.sideFriction = new WheelFrictionCurve
            {
                extremumSlip = 0.35f,
                extremumValue = 1f,
                asymptoteSlip = 0.6f,
                asymptoteValue = 0.75f,
                stiffness = 1f,
            };
        }
    }

    void FixedUpdate()
    {
        if (input == null || wheels == null || wheels.Length < 4 || wheels[0] == null) return;

        float throttle = input.Throttle;
        bool handbrake = input.Handbrake;
        Speed = rb.velocity.magnitude;
        float forwardSpeed = Vector3.Dot(rb.velocity, transform.forward);

        // 前轮转向：速度越快转角越小
        float steerScale = 1f / (1f + Speed / steerSpeedFalloff);
        float steer = input.Steer * steerAngleMax * steerScale;
        wheels[0].steerAngle = steer;
        wheels[1].steerAngle = steer;

        // 逐个轮子：接地判断、路面摩擦、滑移统计
        IsGrounded = false;
        float slipSum = 0f;
        int grounded = 0;
        for (int i = 0; i < 4; i++)
        {
            WheelCollider w = wheels[i];
            SurfaceKind surface = SurfaceKind.Sand;
            bool onGround = w.GetGroundHit(out WheelHit hit);
            if (onGround)
            {
                IsGrounded = true;
                grounded++;
                surface = ResolveSurface(hit.collider);
                slipSum += Mathf.Abs(hit.forwardSlip) + Mathf.Abs(hit.sidewaysSlip);
            }
            ApplyFriction(w, surface, handbrake && i >= 2);
        }
        SlipAmount = grounded > 0 ? slipSum / grounded : 0f;

        // 发动机转速：由后轮转速反推（单挡；换挡留到以后）
        float rearRpm = (Mathf.Abs(wheels[2].rpm) + Mathf.Abs(wheels[3].rpm)) * 0.5f;
        EngineRpm = Mathf.Max(idleRpm, rearRpm * totalRatio);

        // 轮上总扭矩 → 按四驱比例分给每个轮
        float wheelTorque = torqueCurve.Evaluate(Mathf.Min(EngineRpm, maxRpm)) * totalRatio * efficiency;
        bool overLimit = forwardSpeed > maxSpeed;
        bool braking = throttle < 0f && forwardSpeed > 1f;
        bool reversing = throttle < 0f && forwardSpeed <= 1f;

        for (int i = 0; i < 4; i++)
        {
            WheelCollider w = wheels[i];
            float share = i < 2 ? frontDrive * 0.5f : (1f - frontDrive) * 0.5f;

            float motor = 0f;
            if (!braking && !overLimit) motor = throttle * wheelTorque * share;
            if (reversing && forwardSpeed < -8f) motor = 0f;   // 倒车限速

            float brake = 0f;
            if (braking) brake = brakeTorque;
            if (handbrake && i >= 2) brake = handbrakeTorque;

            w.motorTorque = motor;
            w.brakeTorque = brake;
        }

        // 空气阻力（与速度平方成正比）+ 下压力
        if (Speed > 0.1f)
            rb.AddForce(-rb.velocity / Speed * (airDrag * Speed * Speed), ForceMode.Force);
        rb.AddForce(-transform.up * (downforce * Mathf.Clamp01(Speed / 20f)), ForceMode.Force);
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

    void ApplyFriction(WheelCollider w, SurfaceKind surface, bool rearHandbrake)
    {
        float scale;
        switch (surface)
        {
            case SurfaceKind.Asphalt: scale = 1.3f; break;
            case SurfaceKind.Gravel: scale = 1.0f; break;
            default: scale = 0.62f; break;   // Sand 最滑
        }
        if (rearHandbrake) scale *= handbrakeGripScale;

        WheelFrictionCurve forward = w.forwardFriction;
        forward.stiffness = scale;
        w.forwardFriction = forward;

        WheelFrictionCurve side = w.sideFriction;
        side.stiffness = scale;
        w.sideFriction = side;
    }
}
