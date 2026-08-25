using UnityEngine;

/// <summary>
/// 플레이어를 따라다니는 고정 각도 카메라.
///
/// 각도(Pitch/Yaw)는 항상 고정이고 위치만 대상을 따라갑니다.
/// 카메라가 대상을 "바라보게" 하지 않고 각도를 직접 지정하기 때문에,
/// 대상이 움직이거나 회전해도 화면 기울기가 전혀 흔들리지 않습니다.
///
/// 거리를 직접 넣지 않고 Height + PitchAngle 로부터 자동 계산합니다.
/// 그래서 각도를 바꿔도 대상이 항상 화면 중앙에 유지됩니다.
/// </summary>
[DisallowMultipleComponent]
public class FollowCamera : MonoBehaviour
{
    [Header("대상")]
    [Tooltip("따라갈 대상. 비워두면 씬에서 HumanBattleUnit을 자동으로 찾습니다.")]
    public Transform target;

    [Header("★ 카메라 높이")]
    [Tooltip("대상보다 몇 미터 위에 있을지. 이 값만 키우면 더 멀리서 넓게 보입니다. " +
             "각도는 그대로 유지된 채 거리만 늘어납니다.")]
    public float height = 8f;

    [Header("각도 (고정)")]
    [Tooltip("내려다보는 각도(도). 90이면 완전히 수직으로 내려다보고, " +
             "작을수록 옆에서 보는 사선이 됩니다. 55~65 정도가 적당합니다.")]
    [Range(15f, 89f)]
    public float pitchAngle = 60f;

    [Tooltip("수평 방향 각도(도). 0이면 대상의 뒤쪽(월드 -Z)에서 봅니다. " +
             "45를 주면 대각선 코너에서 보는 아이소메트릭 느낌이 됩니다.")]
    public float yawAngle = 0f;

    [Tooltip("켜면 대상이 도는 대로 카메라도 같이 돕니다(3인칭). " +
             "끄면 각도가 완전히 고정됩니다. 기본은 끔.")]
    public bool followTargetYaw = false;

    [Header("화면 중심")]
    [Tooltip("대상의 발밑이 아니라 몸통이 중앙에 오도록 올려주는 높이.")]
    public float lookAtHeight = 0.5f;

    [Tooltip("화면 중심을 앞뒤로 밀어 대상을 화면 아래/위로 치우치게 합니다. 0이면 정중앙.")]
    public float focusForwardShift = 0f;

    [Header("부드러움")]
    [Tooltip("위치 추적 속도. 0이면 즉시 따라붙습니다. 5~15 정도가 자연스럽습니다. " +
             "각도는 고정이므로 회전 보간은 없습니다.")]
    public float positionSmoothing = 12f;

    [Tooltip("켜면 첫 프레임에 보간 없이 즉시 제자리를 잡습니다.")]
    public bool snapOnStart = true;

    private bool _snapped;

    private void Start()
    {
        if (target == null) AutoFindTarget();
        if (snapOnStart) SnapToTarget();
    }

    private void AutoFindTarget()
    {
        var human = Object.FindFirstObjectByType<HumanBattleUnit>();
        if (human != null) { target = human.transform; return; }

        // 사람 플레이어가 없으면 아무 에이전트라도 잡아 화면이 비지 않게 합니다.
        var any = Object.FindFirstObjectByType<PlayerAgent>();
        if (any != null) target = any.transform;
    }

    /// <summary> 보간 없이 즉시 목표 위치로. 리스폰 직후 호출하면 좋습니다. </summary>
    public void SnapToTarget()
    {
        if (target == null) return;
        transform.SetPositionAndRotation(DesiredPosition(), DesiredRotation());
        _snapped = true;
    }

    private Quaternion DesiredRotation()
    {
        float yaw = yawAngle;
        if (followTargetYaw && target != null) yaw += target.eulerAngles.y;
        return Quaternion.Euler(pitchAngle, yaw, 0f);
    }

    /// <summary>
    /// 고정 각도에서 대상이 정확히 화면 중앙에 오는 위치를 계산합니다.
    ///
    /// 카메라가 바라보는 방향 dir 위에서 focus 지점으로부터 dist 만큼 뒤로 물러난 곳입니다.
    /// 수직 높이가 정확히 height 가 되도록 dist = height / sin(pitch) 로 잡습니다.
    /// 덕분에 각도를 바꿔도 높이는 유지되고, 높이를 바꿔도 각도는 유지됩니다.
    /// </summary>
    private Vector3 DesiredPosition()
    {
        Quaternion rot = DesiredRotation();
        Vector3 dir = rot * Vector3.forward;

        Vector3 focus = target.position + Vector3.up * lookAtHeight;
        if (!Mathf.Approximately(focusForwardShift, 0f))
        {
            Vector3 flat = new Vector3(dir.x, 0f, dir.z);
            if (flat.sqrMagnitude > 0.0001f)
                focus += flat.normalized * focusForwardShift;
        }

        float sin = Mathf.Sin(pitchAngle * Mathf.Deg2Rad);
        if (sin < 0.01f) sin = 0.01f;
        float dist = height / sin;

        return focus - dir * dist;
    }

    /// <summary>
    /// LateUpdate에서 처리합니다.
    /// 플레이어 이동이 Update/FixedUpdate에서 끝난 뒤에 카메라를 옮겨야
    /// 한 프레임 밀리는 떨림이 생기지 않습니다.
    /// </summary>
    private void LateUpdate()
    {
        if (target == null)
        {
            AutoFindTarget();
            if (target == null) return;
        }

        if (!_snapped) { SnapToTarget(); return; }

        // 회전은 항상 고정값을 그대로 대입합니다. 보간하지 않으므로 흔들리지 않습니다.
        transform.rotation = DesiredRotation();

        Vector3 want = DesiredPosition();
        transform.position = positionSmoothing <= 0f
            ? want
            : Vector3.Lerp(transform.position, want, 1f - Mathf.Exp(-positionSmoothing * Time.deltaTime));
    }
}
