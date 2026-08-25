using UnityEngine;

/// <summary>
/// 총알 보정 컴포넌트 (신규 작성). Bullet.prefab에 부착합니다.
/// Bullet.cs / Gun.cs / GunSystem.cs 는 전혀 수정하지 않습니다.
///
/// ─────────────────────────────────────────────────────────
/// 해결하는 문제
/// ─────────────────────────────────────────────────────────
/// [1] 총알이 벽에 튕겨 다니다 아군/자신에게 명중
///     Bullet.OnCollisionEnter는 태그가 정확히 "Wall"일 때만 총알을 파괴합니다.
///     그런데 MazeGenerator가 CreatePrimitive로 만드는 CenterWall / BorderWall은
///     Unity 기본값인 Untagged 상태입니다.
///     그래서 이 벽들에 맞으면 파괴되지 않고 물리적으로 튕겨,
///     5초 수명이 다할 때까지 돌아다니며 아무나 맞힙니다.
///     → 태그와 무관하게, 에이전트가 아닌 것에 맞으면 즉시 소멸시킵니다.
///
/// [2] 사람 플레이어가 자기 총알에 맞음
///     Bullet의 자가피격 방어는 `shooter != null && targetHealth.owner == shooter`
///     조건입니다. 사람 플레이어의 총은 owner가 null이라 shooter도 null이 되어
///     이 방어가 통째로 무력화됩니다.
///     → 발사 순간 사수의 콜라이더와 물리 충돌 자체를 무시시킵니다.
///
/// [3] 벽 너머로 총구만 내밀고 사격
///     GunSystem.IsMuzzleBlocked()도 "Wall" 태그만 검사하므로 위 벽들을 놓칩니다.
///     게다가 사람 플레이어는 owner가 null이라 검사 시작점이 몸통이 아니라
///     총 오브젝트가 되어 사실상 무력합니다.
///     → 총알 생성 시점에 사수 몸통에서 총알까지 지형이 가로막고 있으면 소멸시킵니다.
///
/// [4] 빠른 총알의 벽 관통(터널링)
///     총알 콜라이더가 매우 작고(스케일 0.004) 프리팹이 Discrete 판정이라,
///     속도를 올리면 한 물리 스텝에 벽을 건너뛰어 그냥 통과합니다.
///     → ContinuousDynamic으로 강제합니다.
///
/// ※ 벽에 태그를 붙이는 방식은 일부러 쓰지 않았습니다.
///    RayPerceptionSensor의 DetectableTags에 "Wall"이 들어 있어서,
///    지금 태그를 붙이면 AI의 관측이 학습 당시와 달라집니다.
/// </summary>
[DisallowMultipleComponent]
public class BulletGuard : MonoBehaviour
{
    [Header("벽 충돌 시 소멸")]
    [Tooltip("에이전트가 아닌 것에 맞으면 태그와 무관하게 즉시 소멸시킵니다. 튕김 방지의 핵심입니다.")]
    public bool destroyOnNonAgentHit = true;

    [Header("벽 너머 사격 차단")]
    [Tooltip("사수 몸통과 총알 사이에 지형이 있으면 총알을 소멸시킵니다.")]
    public bool blockIfSpawnedBehindWall = true;

    [Tooltip("총알이 벽 안에서 생성됐는지 검사할 반경. 0이면 검사하지 않습니다.")]
    public float spawnOverlapRadius = 0.06f;

    [Header("물리")]
    [Tooltip("빠른 총알이 벽을 통과하지 않도록 연속 충돌 판정을 강제합니다.")]
    public bool forceContinuousCollision = true;

    private Bullet _bullet;
    private Rigidbody _rb;
    private Collider _col;
    private Transform _shooterBody;
    private bool _initialized;

    private void OnEnable()
    {
        _bullet = GetComponent<Bullet>();
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _initialized = false;

        if (forceContinuousCollision && _rb != null)
        {
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
        }
    }

    /// <summary>
    /// 첫 FixedUpdate에서 검사합니다.
    ///
    /// OnEnable에서 하면 안 됩니다.
    /// Gun.Fire()는 bulletTemp.SetActive(true)를 먼저 부르고
    /// transform.position = muzzle.position 을 그 "다음"에 대입합니다.
    /// 즉 OnEnable 시점의 총알 위치는 아직 총구가 아닙니다.
    /// FixedUpdate는 물리 시뮬레이션 직전에 돌므로,
    /// 이 시점이면 위치는 확정됐고 아직 아무것도 통과하지 않았습니다.
    /// </summary>
    private void FixedUpdate()
    {
        if (_initialized) return;
        _initialized = true;

        ResolveShooter();
        IgnoreShooterCollision();

        if (blockIfSpawnedBehindWall && IsBlockedFromShooter())
        {
            Destroy(gameObject);
        }
    }

    private void ResolveShooter()
    {
        // Bullet.shooter는 사람 플레이어일 때 null이므로 믿을 수 없습니다.
        // 대신 총구 Transform의 부모를 거슬러 올라가 사수 몸통을 찾습니다.
        // (총구 -> 총 -> 플레이어 모델 -> 루트)
        if (_bullet == null || _bullet.muzzle == null) return;

        var health = _bullet.muzzle.GetComponentInParent<AgentHealth>();
        if (health != null) { _shooterBody = health.transform; return; }

        var rb = _bullet.muzzle.GetComponentInParent<Rigidbody>();
        if (rb != null) _shooterBody = rb.transform;
    }

    private void IgnoreShooterCollision()
    {
        if (_shooterBody == null || _col == null) return;

        var cols = _shooterBody.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < cols.Length; i++)
        {
            if (cols[i] == null || cols[i] == _col) continue;
            Physics.IgnoreCollision(_col, cols[i], true);
        }
    }

    private bool IsBlockedFromShooter()
    {
        // (a) 총알이 벽 내부에서 생성된 경우
        if (spawnOverlapRadius > 0f)
        {
            var overlap = Physics.OverlapSphere(transform.position, spawnOverlapRadius, ~0,
                                                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < overlap.Length; i++)
                if (IsGeometry(overlap[i])) return true;
        }

        // (b) 사수 몸통과 총구 사이를 지형이 가로막은 경우
        //     (벽 뒤에 서서 총구만 반대편으로 내민 상황)
        if (_shooterBody == null) return false;

        Vector3 start = _shooterBody.position;
        start.y = transform.position.y;   // 바닥/천장이 끼어들지 않도록 같은 높이로

        Vector3 diff = transform.position - start;
        float dist = diff.magnitude;
        if (dist <= 0.001f) return false;

        var hits = Physics.RaycastAll(start, diff / dist, dist, ~0, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits.Length; i++)
            if (IsGeometry(hits[i].collider)) return true;

        return false;
    }

    /// <summary> 벽·장애물인가. 에이전트와 총알과 사수 자신은 제외합니다. </summary>
    private bool IsGeometry(Collider c)
    {
        if (c == null || c == _col) return false;
        if (_shooterBody != null && c.transform.IsChildOf(_shooterBody)) return false;
        if (c.GetComponentInParent<AgentHealth>() != null) return false;
        if (c.GetComponent<Bullet>() != null) return false;
        return true;
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!destroyOnNonAgentHit) return;

        // 에이전트 명중은 Bullet.cs가 데미지 처리와 함께 알아서 파괴합니다.
        // 여기서는 그 외의 모든 것(태그 없는 벽, 지형 등)을 처리합니다.
        if (collision.collider != null &&
            collision.collider.GetComponentInParent<AgentHealth>() != null) return;

        Destroy(gameObject);
    }
}
