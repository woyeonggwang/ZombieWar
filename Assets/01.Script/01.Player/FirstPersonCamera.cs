using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// 1인칭 카메라. PlayerAgent의 자식으로 붙인 카메라에 부착합니다.
/// 좌우(yaw) 회전은 PlayerAgent의 액션(continuousActions[2])이 담당하고,
/// 여기서는 상하(pitch)만 카메라 로컬 회전으로 처리합니다.
/// -> 에이전트 본체의 forward가 학습 시점과 동일하게 유지되어 관측이 깨지지 않습니다.
/// </summary>
[RequireComponent(typeof(Camera))]
public class FirstPersonCamera : MonoBehaviour
{
    [Tooltip("상하 시야 감도")]
    public float pitchSensitivity = 0.08f;

    [Tooltip("상하 시야 제한 (도)")]
    public float pitchLimit = 75f;

    [Tooltip("시작 시 커서 잠금 (Esc로 토글)")]
    public bool lockCursor = true;

    [Tooltip("1인칭에서 자기 몸 렌더러 숨기기 (총은 그대로 보임)")]
    public bool hideOwnBody = true;

        [Tooltip("숨길 몸 루트. 비워두면 부모 오브젝트 기준")]
    public Transform bodyRoot;

private float _pitch = 0f;

    private void Start()
    {
        var cam = GetComponent<Camera>();
        cam.nearClipPlane = 0.03f;

        if (lockCursor) SetCursorLocked(true);

        if (hideOwnBody)
        {
            Transform rootT = bodyRoot != null ? bodyRoot : transform.parent;
            if (rootT != null)
            {
                foreach (var r in rootT.GetComponentsInChildren<Renderer>())
                {
                    if (r.GetComponentInParent<GunSystem>() != null) continue; // 총은 보이게 유지
                    if (r.GetComponentInParent<Gun>() != null) continue;
                    r.enabled = false;
                }
            }
        }
    }

    private void Update()
    {
        // 커서 잠금/해제는 PlayerSystem이 전담합니다. 여기선 상하 시선만 처리.
        if (Cursor.lockState != CursorLockMode.Locked) return;

        var mouse = Mouse.current;
        if (mouse == null) return;

        float dy = mouse.delta.ReadValue().y;
        _pitch = Mathf.Clamp(_pitch - dy * pitchSensitivity, -pitchLimit, pitchLimit);
        transform.localEulerAngles = new Vector3(_pitch, 0f, 0f);
    }

    private static void SetCursorLocked(bool locked)
    {
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
