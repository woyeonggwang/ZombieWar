using System.Collections;
using UnityEngine;

public class Gun : GunSystem
{

    private void OnEnable()
    {
        Bullet = clipValue;
        onReload = false;
        canShot = true;
    }

    public override void OnBulletDepleted()
    {
        if (!onReload)
        {
            StartCoroutine(ReloadDelay());
        }
    }

    public override void Fire()
    {
        if (!onReload)
        {
            if (canShot)
            {
                StartCoroutine(ShotDelay());
                GameObject efxTemp = Instantiate(muzzleEffect);
                efxTemp.transform.parent = muzzle;
                efxTemp.transform.localPosition = Vector3.zero;
                efxTemp.transform.localEulerAngles = Vector3.zero; 
                efxTemp.transform.localScale = new Vector3(5, 5, 5);
                GameObject bulletTemp = Instantiate(bulletObj);
                bulletTemp.GetComponent<Bullet>().muzzle = muzzle;
                bulletTemp.SetActive(true);
                //bulletTemp.transform.parent = muzzle;
                bulletTemp.transform.position = muzzle.position;
                //bulletTemp.transform.eulerAngles =muzzle.eulerAngles;
                bulletTemp.transform.localScale = new Vector3(0.004f, 0.004f, 0.004f);
            }
        }
      
    }

    IEnumerator ShotDelay()
    {
        canShot = false;
        if (Bullet == 0)
        {
            Bullet = clipValue;
        }
        else
        {
            Bullet--;
        }
        print(Bullet);
        yield return new WaitForSeconds(shotDelay);
        canShot = true;
    }

    IEnumerator ReloadDelay()
    {
        onReload = true;
        yield return new WaitForSeconds(reload);
        onReload = false;
    }
}
