using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// 屏幕虚拟按键（发布到手机小游戏时直接可用；PC 上鼠标也能按）：
/// 左下两个转向键，右下油门 / 刹车。画布在 Awake 里代码生成。
public class TouchControls : MonoBehaviour
{
    public static TouchControls Instance { get; private set; }

    public float Steer { get; private set; }     // -1 左，+1 右
    public float Throttle { get; private set; }  // -1 刹车/倒车，+1 油门

    bool left, right, gas, brake;

    void Awake()
    {
        Instance = this;
        BuildUI();
    }

    public void Set(TouchButtonKind kind, bool down)
    {
        switch (kind)
        {
            case TouchButtonKind.Left: left = down; break;
            case TouchButtonKind.Right: right = down; break;
            case TouchButtonKind.Gas: gas = down; break;
            case TouchButtonKind.Brake: brake = down; break;
        }
        Steer = (right ? 1 : 0) - (left ? 1 : 0);
        Throttle = (gas ? 1 : 0) - (brake ? 1 : 0);
    }

    void BuildUI()
    {
        var canvasGo = new GameObject("TouchCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        canvasGo.transform.SetParent(transform, false);
        var canvas = canvasGo.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 5;

        var scaler = canvasGo.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;

        if (EventSystem.current == null)
        {
            var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            es.transform.SetParent(transform, false);
        }

        Font font = RaceUI.GetFont();
        const float s = 200f;
        const float gap = 30f;

        // 左下：转向；右下：油门 / 刹车
        MakeButton(canvasGo.transform, "<", TouchButtonKind.Left,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f, 40f), s, font);
        MakeButton(canvasGo.transform, ">", TouchButtonKind.Right,
            new Vector2(0f, 0f), new Vector2(0f, 0f), new Vector2(40f + s + gap, 40f), s, font);
        MakeButton(canvasGo.transform, "v", TouchButtonKind.Brake,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f, 40f), s, font);
        MakeButton(canvasGo.transform, "^", TouchButtonKind.Gas,
            new Vector2(1f, 0f), new Vector2(1f, 0f), new Vector2(-40f - s - gap, 40f), s, font);
    }

    void MakeButton(Transform parent, string label, TouchButtonKind kind,
        Vector2 anchor, Vector2 pivot, Vector2 pos, float size, Font font)
    {
        var go = new GameObject(kind.ToString() + "Button", typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = new Color(1f, 1f, 1f, 0.2f);
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = anchor;
        rt.pivot = pivot;
        rt.anchoredPosition = pos;
        rt.sizeDelta = new Vector2(size, size);

        var tb = go.AddComponent<TouchButton>();
        tb.host = this;
        tb.kind = kind;

        var labelGo = new GameObject("Label", typeof(Text));
        labelGo.transform.SetParent(go.transform, false);
        var text = labelGo.GetComponent<Text>();
        text.font = font;
        text.text = label;
        text.fontSize = Mathf.RoundToInt(size * 0.5f);
        text.fontStyle = FontStyle.Bold;
        text.color = Color.white;
        text.alignment = TextAnchor.MiddleCenter;
        text.raycastTarget = false;
        var labelRt = text.rectTransform;
        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.sizeDelta = Vector2.zero;
    }
}
