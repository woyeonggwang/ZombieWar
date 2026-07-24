using System.Collections;
using UnityEngine;

public class Bullet : MonoBehaviour
{
    public float force;
    public Transform muzzle;
    public Color[] particleColor;
    public ParticleSystem crushParticle;
    public float damage = 10f;                    // RL: 총알 데미지
    [HideInInspector] public PlayerAgent shooter; // RL: 발사한 에이전트
    private void OnEnable()
    {
        if (muzzle == null)
        {
            gameObject.SetActive(false);
        }
        else
        {
            transform.GetComponent<Rigidbody>().AddRelativeForce(muzzle.forward * force);
            Destroy(gameObject, 5f); // RL: 학습 중 총알이 쌍이지 않도록 수명 제한
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        // RL: 에이전트 피격 처리 (팀 전투용)
        AgentHealth targetHealth = collision.collider.GetComponentInParent<AgentHealth>();
        if (targetHealth != null)
        {
            // 자기 자신이 쏜 총알만 무시 (아군 피격/팀킬은 다시 활성화됨)
            if (shooter != null && targetHealth.owner == shooter)
            {
                Destroy(gameObject);
                return;
            }

            GameObject fxTemp = Instantiate(crushParticle.gameObject);
            fxTemp.GetComponent<ParticleSystemRenderer>().material.color = particleColor.Length > 1 ? particleColor[1] : Color.red;
            fxTemp.transform.position = transform.position;
            targetHealth.TakeDamage(damage, shooter);
            Destroy(gameObject);
            return;
        }
        if (collision.collider.tag == "Wall")
        {
            // RL: 벽을 맞췄을 때 사수에게 감점 통보
            if (shooter != null) shooter.NotifyWallShot();

            GameObject temp = Instantiate(crushParticle.gameObject);
            temp.GetComponent<ParticleSystemRenderer>().material.color = particleColor[0];
            temp.transform.position = transform.position;
            Destroy(gameObject);
        }
        if (collision.collider.tag == "Zombie")
        {
            GameObject temp = Instantiate(crushParticle.gameObject);
            temp.GetComponent<ParticleSystemRenderer>().material.color = particleColor[1];
            temp.transform.position = transform.position;
            Destroy(gameObject);
        }
    }

}
