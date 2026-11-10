using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// 计时 / 提示 / 成绩 UI：Awake 时用代码搭好整块画布，
/// 场景里不用手动摆任何 UI；发布到小游戏（H5/触屏）时直接可用。
public class RaceUI : MonoBehaviour
{
    public RaceManager manager;

    Text timerText;
    Text messageText;
    Text bestText;
    float messageTimer = -1f;   // -1 = 消息常驻（结算界面）

    void Awake()
    {
        BuildUI();
        if (manager == null) manager = FindObjectOfType<RaceManager>();
        RefreshBest();
    }

    void Update()
    {
        if (manager != null)
            timerText.text = FormatTime(manager.Running ? manager.CurrentTime : 0f);

        if (messageTimer >= 0f)
        {
            messageTimer -= Time.deltaTime;
            if (messageTimer < 0f) messageText.text = "";
        }
    }

    public void ShowMessage(string msg, float duration)
    {
        messageText.text = msg;
        messageTimer = duration;
    }

    public void ShowFinish(float time, bool record, float best)
    {
        string sub = record ? "NEW RECORD!" : "BEST  " + FormatTime(best);
        messageText.text = "FINISH  " + FormatTime(time) + "\n" + sub
                         + "\nPress R or tap RESET to retry";
        messageTimer = -1f;
        RefreshBest();
    }

    public void RefreshBest()
    {
        float best = PlayerPrefs.GetFloat(RaceManager.BestKey, -1f);
        bestText.text = best < 0f ? "BEST  --:--.--" : "BEST  " + FormatTime(best);
    }

    public static string FormatTime(float t)
    {
        if (t <= 0f) return "00:00.00";
        int m = Mathf.FloorToInt(t) / 60;
        float s = t - m * 60;
        return string.Format("{0:00}:{1:00.00}", m, s);
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10;   // 盖在触屏按键(5)之上

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);   // 横屏基准
        scaler.matchWidthOrHeight = 0.5f;

        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.transform.SetParent(transform, false);
        }

        Font font = GetFont();

        timerText = MakeText(canvasGo.transform, "TimerText", font, 58, FontStyle.Bold, Color.white,
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -26f), new Vector2(800f, 90f));
        bestText = MakeText(canvasGo.transform, "BestText", font, 26, FontStyle.Bold, new Color(1f, 1f, 1f, 0.85f),
            new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0f, -116f), new Vector2(800f, 44f));
        messageText = MakeText(canvasGo.transform, "MessageText", font, 60, FontStyle.Bold, new Color(1f, 0.92f, 0.65f),
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0f, 60f), new Vector2(1700f, 420f));
        MakeText(canvasGo.transform, "HintText", font, 24, FontStyle.Normal, new Color(1f, 1f, 1f, 0.7f),
            new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0f, 258f), new Vector2(1700f, 40f))
            .text = "W gas · S brake · A/D steer · SPACE handbrake · R reset";

        BuildResetButton(canvasGo.transform, font);
    }

    void BuildResetButton(Transform parent, Font font)
    {
        var btnGo = new GameObject("ResetButton", typeof(Image), typeof(Button));
        btnGo.transform.SetParent(parent, false);
        var img = btnGo.GetComponent<Image>();
        img.color = new Color(0f, 0f, 0f, 0.45f);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(1f, 1f);
        rt.pivot = new Vector2(1f, 1f);
        rt.anchoredPosition = new Vector2(-20f, -20f);
        rt.sizeDelta = new Vector2(190f, 56f);

        Text label = MakeText(btnGo.transform, "Label", font, 28, FontStyle.Bold, Color.white,
            new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(180f, 50f));
        label.text = "RESET  [R]";

        var button = btnGo.GetComponent<Button>();
        button.targetGraphic = img;
        button.onClick.AddListener(() => { if (manager != null) manager.Respawn(); });
    }

    static Text MakeText(Transform parent, string name, Font font, int size, FontStyle style, Color color,
        Vector2 anchor, Vector2 pivot, Vector2 pos, Vector2 sizeDelta)
    {
        var go = new GameObject(name, typeof(Text));
        go.transform.SetParent(parent, false);
        var text = go.GetComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.fontStyle = style;
        text.color = color;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;   // 文字不挡点击
        var rt = text.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = sizeDelta;
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
        outline.effectDistance = new Vector2(2f, -2f);
        return text;
    }

    public static Font GetFont()
    {
        try { return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
        try { return Resources.GetBuiltinResource<Font>("Arial.ttf"); } catch { }
        return null;
    }
}
