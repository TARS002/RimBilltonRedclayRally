using UnityEngine;

/// 车轮视觉：用 WheelCollider.GetWorldPose 同步位置与旋转（含悬挂伸缩、转向、滚动）。
public class WheelPose : MonoBehaviour
{
    public WheelCollider wheel;

    void LateUpdate()
    {
        if (wheel == null) return;
        wheel.GetWorldPose(out Vector3 pos, out Quaternion rot);
        transform.SetPositionAndRotation(pos, rot);
    }
}
