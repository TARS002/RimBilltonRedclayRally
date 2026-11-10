using UnityEngine;

/// 起点线 / 终点线的触发判定：车穿过门时通知 RaceManager。
public class RaceGate : MonoBehaviour
{
    public RaceManager manager;
    public bool isFinish;   // false = 起点线，true = 终点线

    void OnTriggerEnter(Collider other)
    {
        Rigidbody rb = other.attachedRigidbody;
        if (rb != null && rb.CompareTag("Player") && manager != null)
            manager.OnGateCrossed(isFinish);
    }
}
