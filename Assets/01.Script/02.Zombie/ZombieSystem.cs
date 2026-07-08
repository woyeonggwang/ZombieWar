using UnityEngine;
using UnityEngine.AI;


public enum ZombieType
{
    Grunt, //일반 좀비
    Brute, //덩치 큰 좀비
    Boss   //보스 좀비
}

public abstract class ZombieSystem : MonoBehaviour
{
    public float moveSpeed;
    public float atk;
    public Transform targetPos;
    public NavMeshAgent navAgent;
    private bool _onDamage;
    public bool OnDamage {
        get => _onDamage;
        set
        {
            if (!_onDamage && value)
            {
                GetDamage();
            }
            _onDamage = value;
        }
    }
    public int originHP;
    private int _hp;
    public int HP
    {
        get => _hp;
        set
        {
            if (_hp != 0 && value == 0)
            {
                Death();
            }
            _hp = value;
        }
    }


    public abstract void Death();
    public abstract void GetDamage();
}
