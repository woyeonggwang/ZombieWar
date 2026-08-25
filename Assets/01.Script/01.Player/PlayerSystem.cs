using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerSystem : MonoBehaviour
{
    public static event Action<Vector3> OnPlayerMoved;
    public float speed;
    public float rotationSpeed;
    // �� �ڵ� ������ Input Action Ŭ����
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
        _inputActions = new InputAction();  // Input �׼� �ν��Ͻ� ����
    }

    private void OnEnable()
    {
        gunMode = GunType.Pistol;
        GenerateGun();
        // �׼� �� Ȱ��ȭ
        _inputActions.Enable();
    }

    

    private void Start()
    {

    }
    private void Update()
    {
        Vector3 movement = new Vector3(directValue.x, 0, directValue.y) * speed * Time.deltaTime;
        transform.Translate(movement, Space.World); // �Ǵ� Space.Self
        Rotate();
        if (onAttack)
        {
            gun.Fire();
        }
    }

    private void Rotate()
    {
        if (playerModel == null) return;

        var cam = Camera.main;
        if (cam == null || Mouse.current == null) return;

        Ray ray = cam.ScreenPointToRay(Mouse.current.position.ReadValue());
        Plane plane = new Plane(Vector3.up, playerModel.position);

        float rayLength;
        if (!plane.Raycast(ray, out rayLength)) return;

        Vector3 hit = ray.GetPoint(rayLength);
        Vector3 look = new Vector3(hit.x, playerModel.position.y, hit.z);

        // 커서가 모델과 거의 겹치면 LookAt이 튀어 모델이 떨립니다.
        if ((look - playerModel.position).sqrMagnitude < 0.0001f) return;

        playerModel.LookAt(look);
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
        Debug.Log($"���õ� �� ���: {gunMode}");
    }
    public void OnSelectRifle(InputValue value)
    {
        Destroy(gun.gameObject);
        gunMode = GunType.AssaultRifle;
        GenerateGun();
        Debug.Log($"���õ� �� ���: {gunMode}");
    }
    public void OnSelectMinigun(InputValue value)
    {
        Destroy(gun.gameObject);
        gunMode = GunType.MiniGun;
        GenerateGun();
        Debug.Log($"���õ� �� ���: {gunMode}");
    }
    private void OnAttack(InputValue value)
    {
      
        onAttack = value.isPressed;
        //print("attack");
    }
}
