using UnityEngine;

/// <summary>
/// 총알이 실제로 도달할 지점을 반투명 구체로 표시합니다.
///
/// Bullet.cs와 동일한 규칙으로 경로를 계산합니다.
///  - 시작점: 현재 총의 muzzle 위치
///  - 방향  : muzzle.forward 를 수평으로 눕힌 것 (Bullet.horizontalOnly = true)
/// 따라서 화면 중앙 조준점과 달리 실제 탄착점과 어긋나지 않습니다.
/// </summary>
public class AimSphereIndicator : MonoBehaviour
{
    [Header("참조")]
    public PlayerSystem player;

    [Header("설정")]
    [Tooltip("아무것도 맞지 않을 때 표시할 최대 사거리")]
    public float maxDistance = 60f;

    [Tooltip("거리 1m당 구체 지름. 멀수록 커져서 화면상 크기가 일정하게 보입니다")]
    public float sizePerMeter = 0.022f;
    public float minSize = 0.1f;
    public float maxSize = 0.8f;

    [Tooltip("표면에서 살짝 띄워 Z-파이팅을 막습니다")]
    public float surfaceOffset = 0.03f;

    private Renderer _rend;
    private Transform _shooterRoot;

    private void Awake()
    {
        _rend = GetComponent<Renderer>();
        if (player != null) _shooterRoot = player.transform;
    }

    private void SetVisible(bool on)
    {
        if (_rend != null && _rend.enabled != on) _rend.enabled = on;
    }

    private bool IsSelf(Transform t)
    {
        if (_shooterRoot == null) return false;
        return t == _shooterRoot || t.IsChildOf(_shooterRoot);
    }

    private void LateUpdate()
    {
        if (player == null) { SetVisible(false); return; }
        if (_shooterRoot == null) _shooterRoot = player.transform;

        var gun = player.gun;
        if (gun == null || gun.muzzle == null) { SetVisible(false); return; }

        Transform mz = gun.muzzle;

        // Bullet.cs와 동일: 수평 성분만 사용
        Vector3 dir = mz.forward;
        dir.y = 0f;
        if (dir.sqrMagnitude < 1e-6f) { SetVisible(false); return; }
        dir.Normalize();

        Vector3 origin = mz.position;
        Vector3 point = origin + dir * maxDistance;

        // 자기 자신은 무시하고 가장 가까운 충돌 지점을 찾습니다
        var hits = Physics.RaycastAll(origin, dir, maxDistance, ~0, QueryTriggerInteraction.Ignore);
        float best = float.MaxValue;
        for (int i = 0; i < hits.Length; i++)
        {
            if (IsSelf(hits[i].transform)) continue;
            if (hits[i].distance < best)
            {
                best = hits[i].distance;
                point = hits[i].point - dir * surfaceOffset;
            }
        }

        transform.position = point;

        float d = Vector3.Distance(origin, point);
        float s = Mathf.Clamp(d * sizePerMeter, minSize, maxSize);
        transform.localScale = Vector3.one * s;

        SetVisible(true);
    }
}
