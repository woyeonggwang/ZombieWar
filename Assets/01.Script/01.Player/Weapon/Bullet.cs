using System.Collections;
using UnityEngine;

public class Bullet : MonoBehaviour
{
    public float force;
    public Transform muzzle;
    public Color[] particleColor;
    public ParticleSystem crushParticle;
    private void OnEnable()
    {
        if (muzzle == null)
        {
            gameObject.SetActive(false);
        }
        else
        {
            transform.GetComponent<Rigidbody>().AddRelativeForce(muzzle.forward * force);
        }
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (collision.collider.tag == "Wall")
        {
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
