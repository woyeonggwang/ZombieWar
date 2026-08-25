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
        if (!isActiveAndEnabled) return; // 비활성 상태에서 재장전 코루틴 방지

        if (!onReload)
        {
            StartCoroutine(ReloadDelay());
        }
    }

    public override void Fire()
    {
        if (!isActiveAndEnabled) return; // 비활성(사망) 상태에서 코루틴 시작 방지

        // 벽에 총구를 대고 쏘면 총알이 벽 반대편에서 생성되므로 발사 차단
        if (IsMuzzleBlocked()) return;

        if (!onReload)
        {
            
                if (canShot)
                {
                    try
                    {
                        StartCoroutine(ShotDelay());
                    }
                    catch
                    {

                    }
                    lastFireTime = Time.time; // RL: 사격 중 이동 디버프 판정용
                    GameObject efxTemp = Instantiate(muzzleEffect);
                    efxTemp.transform.parent = muzzle;
                    efxTemp.transform.localPosition = Vector3.zero;
                    efxTemp.transform.localEulerAngles = Vector3.zero; 
                    efxTemp.transform.localScale = new Vector3(5, 5, 5);
                    // 총알은 "위치와 회전을 먼저 맞춘 뒤"에 활성화해야 합니다.
                    // 예전에는 SetActive(true) 이후에 position을 대입해서,
                    // Bullet.OnEnable이 도는 시점의 총알 위치가 아직 총구가 아니었습니다.
                    GameObject bulletTemp = Instantiate(bulletObj);
                    Bullet bulletComp = bulletTemp.GetComponent<Bullet>();
                    bulletComp.muzzle = muzzle;
                    bulletComp.shooter = owner;   // RL: 발사자 정보 전달
                    bulletComp.damage = damage;   // RL: 총기별 데미지 전달

                    bulletTemp.transform.SetPositionAndRotation(muzzle.position, muzzle.rotation);
                    bulletTemp.transform.localScale = new Vector3(0.004f, 0.004f, 0.004f);

                    bulletTemp.SetActive(true);
                }
            try
            {
            }
            catch
            {

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
        //print(Bullet);
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
