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
    public bool onReload;
    public GameObject bulletObj;

    public abstract void Fire(); // ��� �ѱ⿡�� �������� ����

    public abstract void OnBulletDepleted();
}
