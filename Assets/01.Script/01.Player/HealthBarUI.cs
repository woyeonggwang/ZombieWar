using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 화면 좌측 하단 플레이어 체력바.
/// fill 이미지의 fillAmount로 줄어듭니다 (Image.type = Filled 여야 동작합니다).
/// </summary>
public class HealthBarUI : MonoBehaviour
{
    public AgentHealth target;
    public Image fill;

    [Tooltip("체력이 줄 때 따라가는 속도(비율/초). 0이면 즉시 반영")]
    public float smoothSpeed = 1.5f;

    private float _shown = 1f;

    private void Start()
    {
        _shown = Ratio();
        if (fill != null) fill.fillAmount = _shown;
    }

    private float Ratio()
    {
        if (target == null || target.maxHp <= 0f) return 1f;
        return Mathf.Clamp01(target.CurrentHp / target.maxHp);
    }

    private void Update()
    {
        if (fill == null) return;

        float t = Ratio();
        _shown = smoothSpeed > 0f
            ? Mathf.MoveTowards(_shown, t, smoothSpeed * Time.deltaTime)
            : t;
        fill.fillAmount = _shown;
    }
}
