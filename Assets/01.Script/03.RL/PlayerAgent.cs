using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;
using UnityEngine;
using UnityEngine.InputSystem;

public enum AgentTeam
{
    Blue = 0,
    Red = 1
}

/// <summary>
/// 미로 팀 전투용 ML-Agents 에이전트.
/// 기존 PlayerSystem의 이동/총기 로직을 RL 액션 기반으로 재구성한 버전입니다.
/// 관측(Vector Obs) 13개: 위치(2) + 전방(2) + 속도(2) + HP(1) + 탄약(1) + 재장전(1) + 총기종류 원핫(3) + 생존아군비율(1)
/// 액션: 연속 3개(이동X, 이동Z, 회전) + 이산 1브랜치(발사 여부 0/1)
/// 벽/적/아군 인식은 RayPerceptionSensor3D 컴포넌트가 담당합니다.
/// </summary>
public class PlayerAgent : Agent
{
    [Header("Team")]
    public AgentTeam team;

    [Header("Movement (PlayerSystem 값과 동일하게 맞추는 것을 권장)")]
    public float moveSpeed = 5f;
    public float rotationSpeed = 200f;

    [Header("Weapon")]
    public Transform gunPos;
    public GunSystem[] gunPrefabs;      // Pistol / AssaultRifle / MiniGun 프리팹
    public bool randomGunOnReset = true; // 에피소드마다 무작위 총기 지급 (모든 총기 학습)
    public GunType startGun = GunType.Pistol;

    [Header("Rewards")]
    public float hitEnemyReward = 0.1f;
    public float hitTeammatePenalty = -0.2f;
    public float fireCostPenalty = -0.0005f; // 무분별한 난사 방지

    [HideInInspector] public GunSystem gun;
    [HideInInspector] public TeamBattleManager manager;
    [HideInInspector] public AgentHealth health;

    private Rigidbody _rb;

    public override void Initialize()
    {
        _rb = GetComponent<Rigidbody>();
        _rb.freezeRotation = true;

        health = GetComponent<AgentHealth>();
        health.owner = this;

        // RayPerceptionSensor의 태그 감지를 위해 팀별 태그 지정
        gameObject.tag = team == AgentTeam.Blue ? "BlueAgent" : "RedAgent";

        TintTeamColor();
    }

    /// <summary> 팀 색상으로 몸체 렌더러를 물들여 구분 </summary>
    private void TintTeamColor()
    {
        Color c = team == AgentTeam.Blue ? new Color(0.2f, 0.4f, 1f) : new Color(1f, 0.25f, 0.2f);
        foreach (Renderer r in GetComponentsInChildren<Renderer>())
        {
            if (r.GetComponentInParent<GunSystem>() != null) continue; // 총은 제외
            r.material.color = c;
        }
    }

    /// <summary> 에피소드 시작 시 매니저가 호출. 위치/체력은 매니저가 처리하고 여기선 총기만. </summary>
    public void ResetAgentState()
    {
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;

        GunType type = randomGunOnReset
            ? (GunType)Random.Range(0, System.Enum.GetValues(typeof(GunType)).Length)
            : startGun;
        EquipGun(type);
    }

    /// <summary> PlayerSystem.GenerateGun과 동일한 방식으로 총기 장착 </summary>
    public void EquipGun(GunType type)
    {
        if (gun != null)
        {
            Destroy(gun.gameObject);
            gun = null;
        }

        for (int i = 0; i < gunPrefabs.Length; i++)
        {
            if (gunPrefabs[i] != null && gunPrefabs[i].type == type)
            {
                GameObject gunTemp = Instantiate(gunPrefabs[i].gameObject);
                gunTemp.transform.parent = gunPos;
                gunTemp.transform.localPosition = Vector3.zero;
                gunTemp.transform.localScale = new Vector3(0.2f, 0.4f, 0.2f);
                gunTemp.transform.localEulerAngles = Vector3.zero;
                gun = gunTemp.GetComponent<GunSystem>();
                gun.owner = this;
                break;
            }
        }
    }

    public override void CollectObservations(VectorSensor sensor)
    {
        float arena = manager != null ? manager.arenaSize : 20f;

        // 위치 (미로 크기로 정규화)
        sensor.AddObservation(transform.position.x / arena);
        sensor.AddObservation(transform.position.z / arena);

        // 바라보는 방향
        sensor.AddObservation(transform.forward.x);
        sensor.AddObservation(transform.forward.z);

        // 속도
        Vector3 v = _rb.linearVelocity;
        sensor.AddObservation(v.x / Mathf.Max(0.01f, moveSpeed));
        sensor.AddObservation(v.z / Mathf.Max(0.01f, moveSpeed));

        // 체력 비율
        sensor.AddObservation(health.CurrentHp / health.maxHp);

        // 탄약 비율 / 재장전 여부
        sensor.AddObservation(gun != null ? (float)gun.Bullet / Mathf.Max(1, gun.clipValue) : 0f);
        sensor.AddObservation(gun != null && gun.onReload ? 1f : 0f);

        // 총기 종류 원핫 (Pistol / AssaultRifle / MiniGun)
        for (int i = 0; i < 3; i++)
        {
            sensor.AddObservation(gun != null && (int)gun.type == i ? 1f : 0f);
        }

        // 생존 아군 비율
        sensor.AddObservation(manager != null ? manager.GetAliveRatio(team) : 1f);
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // --- 이동/회전 (연속) ---
        float moveX = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float moveZ = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float rot = Mathf.Clamp(actions.ContinuousActions[2], -1f, 1f);

        transform.Rotate(0f, rot * rotationSpeed * Time.fixedDeltaTime, 0f);

        Vector3 move = new Vector3(moveX, 0f, moveZ);
        if (move.sqrMagnitude > 1f) move.Normalize();
        _rb.MovePosition(_rb.position + move * moveSpeed * Time.fixedDeltaTime);

        // --- 발사 (이산) ---
        if (actions.DiscreteActions[0] == 1 && gun != null)
        {
            gun.Fire();
            AddReward(fireCostPenalty);
        }
    }

    /// <summary> 노트북에서 수동 테스트용: WASD 이동, Q/E 회전, Space 발사 </summary>
    public override void Heuristic(in ActionBuffers actionsOut)
    {
        var kb = Keyboard.current;
        if (kb == null) return;

        var c = actionsOut.ContinuousActions;
        c[0] = (kb.dKey.isPressed ? 1f : 0f) - (kb.aKey.isPressed ? 1f : 0f);
        c[1] = (kb.wKey.isPressed ? 1f : 0f) - (kb.sKey.isPressed ? 1f : 0f);
        c[2] = (kb.eKey.isPressed ? 1f : 0f) - (kb.qKey.isPressed ? 1f : 0f);

        var d = actionsOut.DiscreteActions;
        d[0] = kb.spaceKey.isPressed ? 1 : 0;
    }

    /// <summary> AgentHealth가 호출: 내 총알이 누군가를 맞췄을 때 </summary>
    public void NotifyDealtDamage(PlayerAgent victim)
    {
        if (victim == null) return;
        AddReward(victim.team == team ? hitTeammatePenalty : hitEnemyReward);
    }
}
