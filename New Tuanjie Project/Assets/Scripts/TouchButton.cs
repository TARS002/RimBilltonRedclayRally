using UnityEngine;
using UnityEngine.EventSystems;

public enum TouchButtonKind { Left, Right, Gas, Brake }

/// 屏幕按钮的按下/抬起回调，把状态记到 TouchControls。
/// （EventSystem 会把抬起事件发回最初按下的按钮，手指滑出也不会卡键）
public class TouchButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    public TouchControls host;
    public TouchButtonKind kind;

    public void OnPointerDown(PointerEventData eventData)
    {
        if (host != null) host.Set(kind, true);
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (host != null) host.Set(kind, false);
    }
}
