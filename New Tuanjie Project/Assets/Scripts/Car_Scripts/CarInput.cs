using UnityEngine;

/// 汇总所有输入来源（键盘 + 屏幕虚拟按键），供 CarController 读取。
/// 触屏按钮来自 TouchControls，键盘支持 WASD 与方向键。
public class CarInput : MonoBehaviour
{
    public float Throttle { get; private set; }   // -1 刹车/倒车，+1 油门
    public float Steer { get; private set; }      // -1 左转，+1 右转
    public bool Handbrake { get; private set; }   // 手刹（漂移用）

    void Update()
    {
        float throttle = 0f;
        float steer = 0f;

        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) throttle += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) throttle -= 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) steer -= 1f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) steer += 1f;

        // 触屏/鼠标点按虚拟按键
        if (TouchControls.Instance != null)
        {
            throttle += TouchControls.Instance.Throttle;
            steer += TouchControls.Instance.Steer;
        }

        Throttle = Mathf.Clamp(throttle, -1f, 1f);
        Steer = Mathf.Clamp(steer, -1f, 1f);
        Handbrake = Input.GetKey(KeyCode.Space);
    }
}
