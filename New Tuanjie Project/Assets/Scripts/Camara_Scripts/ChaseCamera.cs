using UnityEngine;

/// 追尾相机：位置与注视点都用 SmoothDamp 滞后跟随 + 速度越快 FOV 越大。
/// 手感的另一半在相机上：更近、更快的跟随带来拉力速度感。
public class ChaseCamera : MonoBehaviour
{
    public Transform target;
    public float distance = 6.5f;        // 车后距离
    public float height = 2.3f;          // 相机高度
    public float posSmoothTime = 0.13f;  // 位置跟随阻尼
    public float lookSmoothTime = 0.09f; // 注视点阻尼
    public float baseFov = 62f;
    public float maxFovBoost = 18f;      // 高速时的视野拉伸
    public float fovSpeedRange = 30f;    // 达到最大 FOV 拉伸的速度

    Rigidbody targetRb;
    Vector3 posVel;
    Vector3 lookVel;
    Vector3 lookPos;

    void Start()
    {
        if (target == null) return;
        targetRb = target.GetComponent<Rigidbody>();
        lookPos = target.position + target.forward * 2.5f + Vector3.up * 1.1f;
        transform.position = target.position - target.forward * distance + Vector3.up * height;
        transform.LookAt(lookPos);
    }

    void LateUpdate()
    {
        if (target == null) return;

        Vector3 desiredPos = target.position - target.forward * distance + Vector3.up * height;
        transform.position = Vector3.SmoothDamp(transform.position, desiredPos, ref posVel, posSmoothTime);

        Vector3 desiredLook = target.position + target.forward * 2.5f + Vector3.up * 1.1f;
        lookPos = Vector3.SmoothDamp(lookPos, desiredLook, ref lookVel, lookSmoothTime);
        transform.LookAt(lookPos);

        float speed = targetRb != null ? targetRb.velocity.magnitude : 0f;
        Camera cam = GetComponent<Camera>();
        if (cam != null)
            cam.fieldOfView = Mathf.Lerp(baseFov, baseFov + maxFovBoost, Mathf.Clamp01(speed / fovSpeedRange));
    }
}
