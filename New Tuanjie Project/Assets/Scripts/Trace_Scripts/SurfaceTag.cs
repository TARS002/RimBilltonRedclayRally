using UnityEngine;

/// 路面类型标记：挂在路面/地面的 Collider 上，
/// RallyCar 吃到地面时按它切换轮胎摩擦刚度。
public enum SurfaceKind
{
    Asphalt,  // 柏油（预留）
    Gravel,   // 砂石赛道
    Sand,     // 赛道外沙地（最滑）
}

public class SurfaceTag : MonoBehaviour
{
    public SurfaceKind kind = SurfaceKind.Gravel;
}
