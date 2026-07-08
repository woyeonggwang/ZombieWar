using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerSystem : MonoBehaviour
{
    public static event Action<Vector3> OnPlayerMoved;
    public float speed;
    public float rotationSpeed;
    // ② 자동 생성된 Input Action 클래스
    private InputAction _inputActions;
    private Vector2 directValue;
    public Transform playerModel;
    public Transform gunPos;
    public GameObject gunParticle;
    public GunSystem gun;
    public GunSystem[] gunPrefabs;
    public GunType gunMode;
    private bool onAttack;
    private void Awake()
    {
        _inputActions = new InputAction();  // Input 액션 인스턴스 생성
    }

    private void OnEnable()
    {
        gunMode = GunType.Pistol;
        GenerateGun();
        // 액션 맵 활성화
        _inputActions.Enable();
    }

    

    private void Start()
    {

    }
    private void Update()
    {
        Vector3 movement = new Vector3(directValue.x, 0, directValue.y) * speed * Time.deltaTime;
        transform.Translate(movement, Space.World); // 또는 Space.Self
        Rotate();
        if (onAttack)
        {
            gun.Fire();
        }
    }

    private void Rotate()
    {
        Ray ray = Camera.main.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane plane = new Plane(Vector3.up, Vector3.zero);
        float rayLength;
        if (plane.Raycast(ray, out rayLength))
        {
            Vector3 lookDir = ray.GetPoint(rayLength); // 마우스의 월드 좌표
            playerModel.LookAt(new Vector3(lookDir.x, playerModel.position.y, lookDir.z));
        }
    }
    private void GenerateGun()
    {
        for (int i = 0; i < gunPrefabs.Length; i++)
        {
            if (gunPrefabs[i].type == gunMode)
            {
                GameObject gunTemp = Instantiate(gunPrefabs[i].gameObject);
                gunTemp.transform.parent = gunPos;
                gunTemp.transform.localPosition = Vector3.zero;
                gunTemp.transform.localScale = new Vector3(0.2f, 0.4f, 0.2f);
                gunTemp.transform.localEulerAngles = Vector3.zero;
                gun = gunTemp.GetComponent<GunSystem>();
            }
        }
    }
    private void OnMove(InputValue value)
    {
        Vector2 input = value.Get<Vector2>();
        directValue = input;
        if (input.x != 0 || input.y != 0)
        {
            OnPlayerMoved?.Invoke(transform.position);
        }
        //Debug.Log($"SEND_MESSAGE : {input}");
    }

    public void OnSelectPistol(InputValue value)
    {
        Destroy(gun.gameObject);
        gunMode = GunType.Pistol;
        GenerateGun();
        Debug.Log($"선택된 총 모드: {gunMode}");
    }
    public void OnSelectRifle(InputValue value)
    {
        Destroy(gun.gameObject);
        gunMode = GunType.AssaultRifle;
        GenerateGun();
        Debug.Log($"선택된 총 모드: {gunMode}");
    }
    public void OnSelectMinigun(InputValue value)
    {
        Destroy(gun.gameObject);
        gunMode = GunType.MiniGun;
        GenerateGun();
        Debug.Log($"선택된 총 모드: {gunMode}");
    }
    private void OnAttack(InputValue value)
    {
      
        onAttack = value.isPressed;
        //print("attack");
    }
}
