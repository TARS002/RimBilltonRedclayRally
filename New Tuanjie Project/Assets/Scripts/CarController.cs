using UnityEngine;

/// 街机风格车辆物理：单刚体、不使用轮子碰撞体。
/// 手感四件套 = maxSpeed / accel / turnSpeed / grip，全部可在 Inspector 直接调。
/// 漂移原理：把速度分解到车身局部坐标，转向时按 grip 比例消除"侧向分量"，
/// grip 越小车尾甩得越开（手刹时用手刹 grip，所以按空格能漂）。
[RequireComponent(typeof(Rigidbody))]
public class CarController : MonoBehaviour
{
    [Header("动力")]
    public float maxSpeed = 30f;          // m/s，约 108 km/h
    public float reverseSpeed = 8f;      // 倒车限速
    public float accel = 20f;            // 加速度 m/s²
    public float brakeAccel = 32f;       // 刹车减速度

    [Header("转向与抓地")]
    public float turnSpeed = 2.3f;       // 最大转向角速度 rad/s
    public float steerAuthSpeed = 2.5f;  // 低于该速度转向力下降（起步转不动）
    public float speedDamp = 0.015f;      // 高速转向衰减（速度越快方向越沉稳）
    public float grip = 6f;              // 每秒消除侧滑的比例，越小越滑
    public float handbrakeGrip = 1.2f;   // 按手刹时的抓地（低 = 大漂移）
    public float downforce = 8f;         // 贴地下压力

    public bool IsGrounded { get; private set; }
    public float ForwardSpeed { get; private set; }   // 车头方向速度（m/s）
    public float LateralSpeed { get; private set; }   // 侧滑速度（漂移判定用）
    public float CurrentSteer { get; private set; }   // 供车轮视觉用
    public bool IsDrifting => IsGrounded && Mathf.Abs(LateralSpeed) > 4f;

    Rigidbody rb;
    CarInput input;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        input = GetComponent<CarInput>();
        // 压低重心防翻车；插值让运动在渲染上更顺滑
        rb.centerOfMass = new Vector3(0f, -0.35f, 0.1f);
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    void FixedUpdate()
    {
        float dt = Time.fixedDeltaTime;
        GroundCheck();

        CurrentSteer = input != null ? input.Steer : 0f;
        float throttle = input != null ? input.Throttle : 0f;
        bool handbrake = input != null && input.Handbrake;

        // 速度分解到车身局部坐标：z = 纵向，x = 侧向
        Vector3 localVel = transform.InverseTransformDirection(rb.velocity);
        ForwardSpeed = localVel.z;
        LateralSpeed = localVel.x;
        float speed = localVel.magnitude;

        if (IsGrounded)
        {
            // 纵向动力：油门 / 刹车 / 倒车
            if (throttle > 0f)
                rb.AddForce(transform.forward * (accel * throttle), ForceMode.Acceleration);
            else if (throttle < 0f)
            {
                if (ForwardSpeed > 0.5f)
                    rb.AddForce(-transform.forward * brakeAccel, ForceMode.Acceleration);
                else
                    rb.AddForce(transform.forward * (accel * 0.5f * throttle), ForceMode.Acceleration);
            }
            if (handbrake)
                rb.AddForce(-transform.forward * (brakeAccel * 0.35f), ForceMode.Acceleration);

            // 转向：速度趋零时无效、高速衰减、倒车方向相反
            float steerAuth = Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / steerAuthSpeed)
                              / (1f + speed * speedDamp);
            float dir = ForwardSpeed >= 0f ? 1f : -1f;
            float yawDeg = CurrentSteer * turnSpeed * steerAuth * dir * dt * Mathf.Rad2Deg;
            rb.MoveRotation(rb.rotation * Quaternion.Euler(0f, yawDeg, 0f));

            // 抓地：按比例消除侧向速度；限速（正/倒车分开）
            float g = handbrake ? handbrakeGrip : grip;
            localVel.x *= 1f - Mathf.Clamp01(g * dt);
            localVel.z = Mathf.Clamp(localVel.z, -reverseSpeed, maxSpeed);
            rb.velocity = transform.TransformDirection(localVel);

            // 贴地下压力，过弯不飘起
            rb.AddForce(-transform.up * (downforce + speed * 0.2f), ForceMode.Acceleration);
        }
    }

    void GroundCheck()
    {
        // 从车身上方一点向下探测；忽略触发器，避免踩到记分门误判为腾空
        Vector3 origin = transform.position + Vector3.up * 0.5f;
        IsGrounded = Physics.Raycast(origin, Vector3.down, 1.2f, ~0, QueryTriggerInteraction.Ignore);
    }
}
