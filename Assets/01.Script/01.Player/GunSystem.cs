using UnityEngine;

public enum GunType
{
    Pistol,
    AssaultRifle,
    MiniGun
}
public abstract class GunSystem : MonoBehaviour
{
    public GunType type;
    public float damage = 10f;                 // RL: 총알 데미지 (총기별로 프리팹에서 설정)
    [HideInInspector] public PlayerAgent owner; // RL: 이 총을 든 에이전트 (사람 플레이어면 null)
    public float reload;
    public float shotDelay;
    private int _bullet;
    public GameObject muzzleEffect;
    public int Bullet
    {
        get => _bullet;
        set
        {
            if(_bullet !=0 && value == 0)
            {
                OnBulletDepleted();
            }
            _bullet = value;
        }
    }


    public int clipValue;
    public float debuffSpeed;
    public Transform muzzle;
    public bool canShot;

    [Header("벽 밀착 사격 방지")]
    [Tooltip("소유자 몸체에서 총구까지 벽이 가로막으면 발사를 차단")]
    public bool blockFireWhenMuzzleInWall = true;
    [Tooltip("벽 판정에 사용할 태그 (미로 벽은 레이어가 Default라 태그로 판정)")]
    public string wallTag = "Wall";
    [Tooltip("총구 주변 이 반경 안에 벽이 있으면 발사 차단 (0이면 반경 검사 안 함)")]
    public float muzzleClearRadius = 0.15f;

    /// <summary> 마지막으로 실제 발사한 시각 (RL 이동 속도 디버프 판정용) </summary>
    [System.NonSerialized] public float lastFireTime = -999f;

    /// <summary>
    /// 총구가 벽 안이거나 몸체~총구 사이가 벽으로 막혀 있으면 true.
    /// 벽에 밀착했을 때 총알이 벽 반대편에서 생성되는 문제를 막기 위한 검사.
    /// </summary>
    public bool IsMuzzleBlocked()
    {
        if (!blockFireWhenMuzzleInWall || muzzle == null) return false;

        Transform origin = (owner != null) ? owner.transform : transform;
        Vector3 start = origin.position;
        start.y = muzzle.position.y;

        Vector3 diff = muzzle.position - start;
        float dist = diff.magnitude;

        // 몸체에서 총구까지 벽이 가로막고 있으면 차단
        // 벽은 레이어가 Default이고 태그가 Wall이므로 태그로 판정한다
        if (dist > 0.001f)
        {
            var hits = Physics.RaycastAll(start, diff / dist, dist, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hits.Length; i++)
                if (hits[i].collider.CompareTag(wallTag)) return true;
        }

        // 총구가 벽 표면에 파묻혀 있는 경우 보완
        if (muzzleClearRadius > 0f)
        {
            var cols = Physics.OverlapSphere(muzzle.position, muzzleClearRadius, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < cols.Length; i++)
                if (cols[i].CompareTag(wallTag)) return true;
        }

        return false;
    }
    public bool onReload;
    public GameObject bulletObj;

    public abstract void Fire(); // ��� �ѱ⿡�� �������� ����

    public abstract void OnBulletDepleted();
}
