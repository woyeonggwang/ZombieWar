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

    public abstract void Fire(); // 모든 총기에서 공통으로 구현

    public abstract void OnBulletDepleted();
}
