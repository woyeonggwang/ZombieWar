using UnityEngine;

public class Bullet : MonoBehaviour
{
    [Header("발사")]
    [Tooltip("총알 속도(m/s). 이 값이 0보다 크면 이 속도로 직접 발사합니다.")]
    public float speed = 40f;

    [Tooltip("[구버전 호환] speed가 0 이하일 때만 사용. 속도 = force * fixedDeltaTime / mass 로 환산됩니다.")]
    public float force = 2000f;

    [Tooltip("총구가 위아래로 기울어도 항상 수평으로 발사합니다.")]
    public bool horizontalOnly = true;

    [Tooltip("총알 수명(초). 벽에 맞지 않아도 이 시간이 지나면 사라집니다.")]
    public float lifeTime = 5f;

    [Header("연출")]
    public Transform muzzle;
    public Color[] particleColor;
    public ParticleSystem crushParticle;

    [Header("전투")]
    public float damage = 10f;                    // RL: 총알 데미지
    [HideInInspector] public PlayerAgent shooter; // RL: 발사한 에이전트 (사람 플레이어면 null)

    private void OnEnable()
    {
        if (muzzle == null)
        {
            // Gun.Fire()가 muzzle을 대입하기 전에 활성화된 상태.
            // 일단 꺼두면 대입 후 SetActive(true)에서 다시 들어옵니다.
            gameObject.SetActive(false);
            return;
        }

        var rb = GetComponent<Rigidbody>();
        if (rb == null)
        {
            Destroy(gameObject);
            return;
        }

        // ── 발사 방향 ────────────────────────────────────────
        // 이전 구현은 rb.AddRelativeForce(muzzle.forward * force) 였습니다.
        // AddRelativeForce는 "총알의 로컬 좌표계"로 힘을 주는데,
        // 넣는 벡터 muzzle.forward는 "월드 방향"이라 좌표계가 섞여 있었습니다.
        // 총알 회전이 단위행렬일 때만 우연히 맞고, 조금이라도 돌아가면
        // 엉뚱한 방향으로 날아갑니다.
        // → 월드 방향을 명시적으로 계산하고 속도를 직접 지정합니다.
        Vector3 dir = muzzle.forward;
        if (horizontalOnly) dir.y = 0f;

        if (dir.sqrMagnitude < 1e-6f) dir = Vector3.forward;
        dir.Normalize();

        // 총알 자체도 진행 방향으로 정렬해 둡니다(연출 및 콜라이더 정렬).
        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);

        float v = speed;
        if (v <= 0f)
        {
            float mass = rb.mass > 0f ? rb.mass : 1f;
            v = force * Time.fixedDeltaTime / mass;
        }

        rb.linearVelocity = dir * v;
        rb.angularVelocity = Vector3.zero;

        Destroy(gameObject, lifeTime);
    }

    private void OnCollisionEnter(Collision collision)
    {
        AgentHealth targetHealth = collision.collider.GetComponentInParent<AgentHealth>();
        if (targetHealth != null)
        {
            // 자기 자신은 맞히지 않습니다.
            // shooter가 null인 경우(사람 플레이어)에도 방어되도록
            // 총구 소유자를 함께 확인합니다.
            if (IsSelf(targetHealth))
            {
                Destroy(gameObject);
                return;
            }

            SpawnHitEffect(particleColor.Length > 1 ? particleColor[1] : Color.red);
            targetHealth.TakeDamage(damage, shooter);
            Destroy(gameObject);
            return;
        }

        if (collision.collider.CompareTag("Zombie"))
        {
            SpawnHitEffect(particleColor.Length > 1 ? particleColor[1] : Color.red);
            Destroy(gameObject);
            return;
        }

        // 벽/지형.
        // 주의: MazeGenerator가 CreatePrimitive로 만드는 CenterWall / BorderWall은
        // 태그가 Untagged입니다. 태그로만 판정하면 그 벽들에서 총알이 튕겨
        // 돌아다니다 아무나 맞히게 됩니다.
        // 그래서 "에이전트가 아닌 것"은 전부 지형으로 보고 소멸시킵니다.
        if (collision.collider.CompareTag("Wall") && shooter != null)
        {
            shooter.NotifyWallShot();
        }

        SpawnHitEffect(particleColor.Length > 0 ? particleColor[0] : Color.white);
        Destroy(gameObject);
    }

    /// <summary> 이 총알이 쏜 사람 자신에게 맞은 것인지 </summary>
    private bool IsSelf(AgentHealth target)
    {
        if (shooter != null && target.owner == shooter) return true;

        // 사람 플레이어는 총기 owner가 null이라 shooter도 null입니다.
        // 총구의 부모를 거슬러 올라가 사수를 직접 확인합니다.
        if (muzzle != null)
        {
            var muzzleOwner = muzzle.GetComponentInParent<AgentHealth>();
            if (muzzleOwner != null && muzzleOwner == target) return true;
        }
        return false;
    }

    private void SpawnHitEffect(Color c)
    {
        if (crushParticle == null) return;

        GameObject fx = Instantiate(crushParticle.gameObject);
        var r = fx.GetComponent<ParticleSystemRenderer>();
        if (r != null) r.material.color = c;
        fx.transform.position = transform.position;
    }
}
