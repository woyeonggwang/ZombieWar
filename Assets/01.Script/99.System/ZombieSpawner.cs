using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class ZombieSpawner : MonoBehaviour
{
    public GameObject zombieObj;
    public Transform player; // 플레이어 트랜스폼
    public Queue<GameObject> pool = new Queue<GameObject>();

    private int poolSize = 20;
    private int maxAttempts = 50;

    void Start()
    {
        for (int i = 0; i < poolSize; i++)
        {
            GameObject obj = Instantiate(zombieObj);
            obj.SetActive(false);
            pool.Enqueue(obj);
        }

        StartCoroutine(SpawnPlay());
    }

    void Spawn()
    {
        
        print(pool.Count);
        if (pool.Count == 0) return;

        Vector3 pos = GetValidSpawnPosition();
        if (pos == Vector3.negativeInfinity) return;

        GameObject zombie = pool.Dequeue();
        zombie.GetComponent<Zombie>().targetPos = player;
        zombie.transform.position = pos;
        zombie.SetActive(true);
    }
    IEnumerator SpawnPlay()
    {
        while (true)
        {
            Spawn();
            yield return new WaitForSeconds(1f);
        }
    }
    Vector3 GetValidSpawnPosition()
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            int x = Random.Range(0, 20);
            int z = Random.Range(0, 20);

            int px = Mathf.RoundToInt(player.position.x);
            int pz = Mathf.RoundToInt(player.position.z);

            if (Mathf.Abs(x - px) >= 4 && Mathf.Abs(z - pz) >= 4)
            {
                return new Vector3(x, 0, z);
            }
        }

        return Vector3.negativeInfinity;
    }

    // 죽은 몬스터를 다시 큐에 넣는 메서드
    public void ReturnToPool(GameObject zombie)
    {
        zombie.SetActive(false);
        pool.Enqueue(zombie);
    }
}
