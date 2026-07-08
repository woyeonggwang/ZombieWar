using System.Collections;
using UnityEngine;

public class MuzzleFlashLifetime : MonoBehaviour
{
    private void OnEnable()
    {
        StartCoroutine(LifeTime()); 
    }

    IEnumerator LifeTime()
    {
        yield return new WaitForSeconds(1f);
        Destroy(gameObject);
    }
}
