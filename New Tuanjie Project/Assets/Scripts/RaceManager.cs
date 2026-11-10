using UnityEngine;

/// 比赛流程：穿起点开始计时 → 穿终点结算并记录最佳成绩 → R 键 / RESET 按钮回起点。
/// 翻车、卡死、飞出地图时自动复位（拉力赛不能让玩家干瞪眼）。
public class RaceManager : MonoBehaviour
{
    public const string BestKey = "PrototypeBest";   // 最佳成绩的存档键

    public Transform car;
    public RaceUI ui;
    public float flipResetTime = 3f;   // 翻车/卡死多久后自动复位
    public float fallResetY = -5f;     // 掉到该高度以下判定出界

    public bool Running { get; private set; }
    public float CurrentTime { get; private set; }

    Vector3 spawnPos;
    Quaternion spawnRot;
    Rigidbody carRb;
    float flipTimer;

    void Start()
    {
        if (car == null) return;
        spawnPos = car.position;
        spawnRot = car.rotation;
        carRb = car.GetComponent<Rigidbody>();
        if (ui != null) ui.ShowMessage("READY", 1.6f);
    }

    void Update()
    {
        if (Running) CurrentTime += Time.deltaTime;

        if (Input.GetKeyDown(KeyCode.R)) Respawn();

        // 翻车(车顶朝下且速度≈0)、掉出地面 → 一段时间后自动复活
        if (car != null && carRb != null)
        {
            bool stuck = car.position.y < fallResetY
                      || (car.up.y < 0.3f && carRb.velocity.magnitude < 1.5f);
            flipTimer = stuck ? flipTimer + Time.deltaTime : 0f;
            if (flipTimer >= flipResetTime)
            {
                flipTimer = 0f;
                Respawn();
            }
        }
    }

    public void OnGateCrossed(bool isFinish)
    {
        if (!isFinish)
        {
            // 穿过起点线才开始计时（拉力赛规则）
            if (!Running)
            {
                Running = true;
                CurrentTime = 0f;
                if (ui != null) ui.ShowMessage("GO!", 1f);
            }
        }
        else if (Running)
        {
            Running = false;
            float best = PlayerPrefs.GetFloat(BestKey, -1f);
            bool record = best < 0f || CurrentTime < best;
            if (record) PlayerPrefs.SetFloat(BestKey, CurrentTime);
            if (ui != null) ui.ShowFinish(CurrentTime, record, record ? CurrentTime : best);
        }
    }

    /// 回到起点重跑（不清最佳成绩）
    public void Respawn()
    {
        if (car == null) return;
        if (carRb == null) carRb = car.GetComponent<Rigidbody>();
        carRb.velocity = Vector3.zero;
        carRb.angularVelocity = Vector3.zero;
        car.position = spawnPos;
        car.rotation = spawnRot;
        Running = false;
        CurrentTime = 0f;
        if (ui != null)
        {
            ui.ShowMessage("READY", 1.2f);
            ui.RefreshBest();
        }
    }
}
