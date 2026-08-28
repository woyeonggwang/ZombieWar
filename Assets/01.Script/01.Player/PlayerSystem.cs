using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerSystem : MonoBehaviour
{
    public static event Action<Vector3> OnPlayerMoved;
    public float speed;
    public float rotationSpeed;
    // �� �ڵ� ������ Input Action Ŭ����
        [Header("1인칭 조작")]
    [Tooltip("마우스 좌우 감도 (도/픽셀)")]
    public float mouseSensitivity = 0.12f;
    [Tooltip("커서가 잠겨 있을 때만 시점을 돌림 (Esc로 토글)")]
    public bool requireCursorLock = true;    [Tooltip("상하 시선 감도")]
    public float pitchSensitivity = 0.08f;
    [Tooltip("상하 시선 제한 (도)")]
    public float pitchLimit = 75f;
    [Tooltip("1인칭 카메라. 상하 시선은 이 트랜스폼에만 적용됩니다")]
    public Transform cameraPivot;
    [Tooltip("1인칭에서 자기 몸 숨기기 (들고 있는 총은 그대로 보임)")]
    public bool hideOwnBody = true;

    private Rigidbody _rb;
    private bool _wantCursorLock = true;
    private float _pitch = 0f;

    /// <summary> 이동을 Rigidbody 속도로 처리해 벽을 통과하지 않게 합니다. </summary>
    private void FixedUpdate()
    {
        if (_rb == null) return;

        Vector3 local = new Vector3(directValue.x, 0f, directValue.y);
        if (local.sqrMagnitude > 1f) local.Normalize();

        Vector3 dir = playerModel != null ? playerModel.TransformDirection(local) : local;
        dir.y = 0f;

        Vector3 v = dir * speed;
        v.y = _rb.linearVelocity.y; // 중력 성분 유지
        _rb.linearVelocity = v;
    }

    /// <summary> 상하 시선: 카메라에만 적용해 몸체 forward가 흔들리지 않게 합니다. </summary>
    private void LookPitch()
    {
        if (cameraPivot == null) return;
        if (Mouse.current == null) return;
        if (Cursor.lockState != CursorLockMode.Locked) return;

        float dy = Mouse.current.delta.ReadValue().y;
        _pitch = Mathf.Clamp(_pitch - dy * pitchSensitivity, -pitchLimit, pitchLimit);
        cameraPivot.localEulerAngles = new Vector3(_pitch, 0f, 0f);
    }

    /// <summary>
    /// 커서를 화면 중앙에 고정하고 숨깁니다.
    /// 무언가가 잠금을 풀면 매 프레임 되돌려 마우스가 게임 화면을 벗어나지 않게 합니다.
    /// Esc로 해제, 화면 클릭으로 재잠금.
    /// </summary>
    private void UpdateCursorLock()
    {
        var kb = Keyboard.current;
        if (kb != null && kb.escapeKey.wasPressedThisFrame)
        {
            _wantCursorLock = false;
            ApplyCursorLock(false);
            return;
        }

        if (!_wantCursorLock)
        {
            var m = Mouse.current;
            if (m != null && m.leftButton.wasPressedThisFrame)
            {
                _wantCursorLock = true;
                ApplyCursorLock(true);
            }
            return;
        }

        if (Cursor.lockState != CursorLockMode.Locked || Cursor.visible)
            ApplyCursorLock(true);
    }

    private static void ApplyCursorLock(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }

        
    



private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus && _wantCursorLock) ApplyCursorLock(true);
    }


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
        _inputActions = new InputAction();

        _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
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
        ApplyCursorLock(true);

        if (cameraPivot != null)
        {
            var cam = cameraPivot.GetComponent<Camera>();
            if (cam != null) cam.nearClipPlane = 0.03f;
        }

        if (hideOwnBody && playerModel != null)
        {
            foreach (var r in playerModel.GetComponentsInChildren<Renderer>())
            {
                if (r.GetComponentInParent<GunSystem>() != null) continue; // 들고 있는 총은 보이게 유지
                r.enabled = false;
            }
        }
    }
    private void Update()
    {
        UpdateCursorLock();
        Rotate();
        LookPitch();

        if (onAttack && gun != null)
        {
            gun.Fire();
        }
    }

    /// <summary>
    /// 1인칭 마우스룩: 좌우(yaw)만 몸체(playerModel)에 적용합니다.
    /// 상하(pitch)는 FirstPersonCamera가 카메라에만 적용합니다.
    /// </summary>
    private void Rotate()
    {
        if (playerModel == null) return;
        if (Mouse.current == null) return;
        if (requireCursorLock && Cursor.lockState != CursorLockMode.Locked) return;

        float dx = Mouse.current.delta.ReadValue().x;
        if (Mathf.Approximately(dx, 0f)) return;

        playerModel.Rotate(0f, dx * mouseSensitivity, 0f, Space.World);
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
