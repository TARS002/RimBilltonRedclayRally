using UnityEngine;

/// 车轮视觉：随车速滚动，前轮随转向偏转。
/// 轮子只是外观（碰撞体已去掉），不参与物理。
public class WheelVisual : MonoBehaviour
{
    public CarController car;
    public bool isFront;
    public float radius = 0.36f;

    float spin;

    void LateUpdate()
    {
        if (car == null) return;
        spin += car.ForwardSpeed / radius * Time.deltaTime * Mathf.Rad2Deg;
        float steerDeg = isFront ? car.CurrentSteer * 32f : 0f;
        transform.localRotation = Quaternion.Euler(0f, steerDeg, 0f)
                                * Quaternion.AngleAxis(spin, Vector3.right);
    }
}
