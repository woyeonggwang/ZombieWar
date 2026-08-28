using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 에이전트 머리 위 체력바 (World Space Canvas에 부착).
/// - 항상 플레이어 카메라를 정면으로 바라봅니다 (빌보드)
/// - 카메라와 대상 사이에 벽이 있으면 숨겨서 벽 너머로 비치지 않게 합니다
/// - 죽었거나 너무 멀면 숨깁니다
/// </summary>
[RequireComponent(typeof(Canvas))]
public class WorldHealthBar : MonoBehaviour
{
    public AgentHealth target;
    public Image fill;

    [Tooltip("따라다닐 대상 트랜스폼")]
    public Transform anchor;

    [Tooltip("대상 기준 머리 위 높이 (월드 단위)")]
    public float heightOffset = 1.1f;

    [Tooltip("이 거리보다 멀면 숨김")]
    public float maxDistance = 40f;

    [Tooltip("시야를 막는 것으로 취급할 레이어")]
    public LayerMask occluders = ~0;

    private Canvas _canvas;
    private Camera _cam;

    private void Awake()
    {
        _canvas = GetComponent<Canvas>();
    }

    private void LateUpdate()
    {
        if (anchor == null || target == null) { Show(false); return; }

        if (_cam == null || !_cam.isActiveAndEnabled) _cam = Camera.main;
        if (_cam == null) { Show(false); return; }

        Vector3 head = anchor.position + Vector3.up * heightOffset;
        transform.position = head;

        // 빌보드: 카메라를 정면으로 바라보게 회전
        Vector3 away = head - _cam.transform.position;
        if (away.sqrMagnitude > 0.0001f)
            transform.rotation = Quaternion.LookRotation(away, Vector3.up);

        if (target.IsDead) { Show(false); return; }

        float dist = away.magnitude;
        if (dist > maxDistance) { Show(false); return; }

        // 벽에 가리면 숨김
        bool blocked = false;
        if (dist > 0.01f)
        {
            RaycastHit hit;
            if (Physics.Raycast(_cam.transform.position, away / dist, out hit, dist, occluders, QueryTriggerInteraction.Ignore))
            {
                Transform t = hit.transform;
                blocked = !(t == anchor || t.IsChildOf(anchor));
            }
        }
        Show(!blocked);

        if (fill != null && target.maxHp > 0f)
            fill.fillAmount = Mathf.Clamp01(target.CurrentHp / target.maxHp);
    }

    private void Show(bool on)
    {
        if (_canvas != null && _canvas.enabled != on) _canvas.enabled = on;
    }
}
