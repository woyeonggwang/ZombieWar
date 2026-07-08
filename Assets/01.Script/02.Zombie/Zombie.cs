using UnityEngine;
using UnityEngine.AI;

public class Zombie : ZombieSystem
{
    private void OnEnable()
    {
        if (targetPos == null)
        {
            gameObject.SetActive(false);
        }
        else
        {
            navAgent = transform.GetComponent<NavMeshAgent>();

        }
    }
    private void Update()
    {
        navAgent.SetDestination(targetPos.position);
    }
    public override void Death()
    {
        FindObjectOfType<ZombieSpawner>().ReturnToPool(gameObject);
        gameObject.SetActive(false);
    }
    public override void GetDamage()
    {
        
    }
    //private void OnDestroy()
    //{
    //    // 이벤트 해제 (메모리 누수 방지)
    //    PlayerSystem.OnPlayerMoved -= UpdateDestination;
    //}

    //private void UpdateDestination(Vector3 playerPosition)
    //{
    //    navAgent.SetDestination(playerPosition);
    //}
}
