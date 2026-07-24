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
    [Tooltip("[TeamBattleManager가 시작 시 덮어쓸] 적 명중 보상. 값은 매니저에서 조정하세요")]
    public float hitEnemyReward = 0.1f;
    [Tooltip("[TeamBattleManager가 시작 시 덮어쓸] 아군 명중 감점. 값은 매니저에서 조정하세요")]
    public float hitTeammatePenalty = -0.2f;
    [Tooltip("[TeamBattleManager가 시작 시 덮어쓸] 벽 사격 감점. 값은 매니저에서 조정하세요")]
    public float wallShotPenalty = -0.01f;
    [Tooltip("[TeamBattleManager가 시작 시 덮어쓸] 살아있을 때 중앙 접근 유도 보상 계수")]
    public float centerSeekReward = 0.002f;
            private float _prevCenterDist = -1f;
    [Tooltip("[TeamBattleManager가 시작 시 덮어씀] 중앙에서 멀수록 주는 감점(음수)")]
    public float centerDistancePenalty = -0.01f;
    [Tooltip("[TeamBattleManager가 시작 시 덮어씀] 감점이 최대가 되는 거리. 0 이하면 런타임에 arenaSize 절반으로 자동 계산")]
    public float centerPenaltyMaxDistance = 0f;

    [Header("[매니저가 시작 시 덮어씀] 조준/팀/체력/무기")]
    public float aimAlignReward = 0.004f;
    public float aimAngleThreshold = 20f;
    public float cohesionReward = 0.002f;
    public float cohesionRadius = 18f;
    public float cohesionMinDistance = 4f;
    public float lowHpThreshold = 0.3f;
    public float retreatReward = 0.004f;
    public float recklessPenalty = -0.004f;
    public float weaponChoiceReward = 0.003f;
    public float pistolRange = 8f;
    public int minigunEnemyCount = 2;
    private float _prevNearestEnemyDist = -1f;

    [Header("이동 방향별 속도 배율")]
    [Tooltip("좌우 스트레이프 속도 배율 (전진 대비)")]
    [Range(0.1f, 1f)] public float strafeSpeedMultiplier = 0.75f;
    [Tooltip("후진 속도 배율 (전진 대비). 낮을수록 백무빙이 불리해짐")]
    [Range(0.1f, 1f)] public float backwardSpeedMultiplier = 0.5f;

    [Header("[매니저가 시작 시 덮어씀] 교전 거리 / 엄폐")]
    public float tooCloseDistance = 6f;
    public float tooClosePenalty = -0.002f;
    public float longRangeDistance = 14f;
    public float longRangeHitBonus = 0.05f;
    public float exposurePenalty = -0.0015f;

    [Header("[매니저가 시작 시 덮어씀] 사격 중 이동 제한")]
    public float firingMoveWindow = 0.25f;
    [Range(0.1f, 1f)] public float firingForwardMultiplier = 0.4f;
    [Range(0.1f, 1f)] public float firingLateralMultiplier = 0.7f;
    public bool useGunDebuffSpeed = true;

    [Header("[매니저가 시작 시 덮어씀] 적 탐색 실패")]
    public float noEnemyTimeLimit = 8f;
    public float noEnemyPenalty = -0.001f;
    public bool noEnemyPenaltyScaling = true;
    public float noEnemyPenaltyMaxScale = 3f;
    private float _noEnemyTimer = 0f; // 적을 마지막으로 본 뒤 경과 시간

    [Header("[매니저가 시작 시 덮어씀] 교전 회피")]
    public float holdFireTimeLimit = 1f;
    public float holdFirePenalty = -0.005f;
    public bool holdFireIgnoreReload = true;
    private float _holdFireTimer = 0f;   // 조준선에 적이 있는데 안 쏜 시간
    private bool _firedThisDecision = false; // 이번 결정에서 발사했는가
    [Tooltip("[TeamBattleManager가 시작 시 덮어씀] 정체 판정 시간(초)")]
    public float stallTimeLimit = 3f;
    [Tooltip("[TeamBattleManager가 시작 시 덮어씀] 정체 판정 기준 이동거리")]
    public float stallMoveThreshold = 2f;
    [Tooltip("[TeamBattleManager가 시작 시 덮어씀] 정체 시 매 결정마다 주는 감점")]
    public float stallPenalty = -0.02f;
    private Vector3 _stallAnchorPos;   // 정체 판정 기준 위치
    private float _stallTimer = 0f;    // 기준 위치에 머문 시간
    public float fireCostPenalty = -0.0005f; // 무분별한 난사 방지
    [Tooltip("상대 베이스에 가까워질 때마다 주는 유도 보상 계수. 0이면 비활성")]
    public float baseSeekReward = 0.001f;
    private float _prevEnemyBaseDist = -1f;    [Tooltip("미로 경계를 벗어났을 때 감점량 (사망과 함께 적용)")]
    public float outOfBoundsPenalty = -1f;
    [Tooltip("경계 이탈 판정 여유 거리. 미로 밖으로 이만큼 더 나가면 이탈로 간주")]
    public float boundsMargin = 2f;

    [Header("추가 규칙 보상")]
        [Tooltip("[TeamBattleManager가 시작 시 덮어씀] 자기 베이스 캠핑 판정 시간(초)")]
    public float campTimeLimit = 3f;
        [Tooltip("[TeamBattleManager가 시작 시 덮어씀] 자기 베이스 캠핑 시 매 결정마다 주는 감점")]
    public float campPenalty = -0.01f;
        [Tooltip("[TeamBattleManager가 시작 시 덮어씀] 자기 베이스에 적이 들어와 있을 때 매 결정마다 주는 감점")]
    public float baseIntrudedPenalty = -0.02f;
        [Tooltip("[TeamBattleManager가 시작 시 덮어씀] 베이스 판정 반경(월드 단위)")]
    public float baseRadius = 8f;
    [Tooltip("발사했으나 아무도 못 맞췄을 때(허공/벽) 추가 감점")]
    public float missPenalty = -0.01f;
    private float _campTimer = 0f;
    private bool _dealtDamageThisShot = false;
    [Tooltip("시야에서 적을 새로 발견했을 때 주는 보상")]
    public float spotEnemyReward = 0.5f;
    [Tooltip("적 발견 판정 시야각(도, 전방 기준 좌우 절반각)")]
    public float spotFovHalfAngle = 45f;
    [Tooltip("적 발견 판정 최대 거리")]
    public float spotRange = 30f;
    private bool _wasSpottingEnemy = false;
    private bool _spotRewardGiven = false; // 에피소드당 1회 제한




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
                _prevEnemyBaseDist = -1f; // 리스폰 순간 잘못된 보상 방지
                _prevCenterDist = -1f;    // 중앙 접근 보상도 동일하게 초기화
        _stallAnchorPos = transform.position; // 정체 판정 기준을 리스폰 위치로 초기화
        _stallTimer = 0f;
        _campTimer = 0f;
        _wasSpottingEnemy = false;
        _spotRewardGiven = false;


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
        sensor.AddObservation(transform.localPosition.x / arena); // 아레나 로컬 좌표 (멀티 아레나 필수)
        sensor.AddObservation(transform.localPosition.z / arena);

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
        // 사격 중 여부 (이동 속도가 깎이는 상태임을 인지시켜 '쏠지 움직일지' 판단 가능하게)
        sensor.AddObservation(gun != null && (Time.time - gun.lastFireTime) <= firingMoveWindow ? 1f : 0f);
        // 적을 못 본 시간 (0=방금 봄, 1=제한시간 도달). 감점이 임박했음을 인지시킴
        sensor.AddObservation(Mathf.Clamp01(_noEnemyTimer / Mathf.Max(0.01f, noEnemyTimeLimit)));
        // 총구가 벽에 막혀 발사 불가한 상태인가 (벽에서 떨어지는 행동을 학습하게 함)
        sensor.AddObservation(gun != null && gun.IsMuzzleBlocked() ? 1f : 0f);

        // 총기 종류 원핫 (Pistol / AssaultRifle / MiniGun)
        for (int i = 0; i < 3; i++)
        {
            sensor.AddObservation(gun != null && (int)gun.type == i ? 1f : 0f);
        }

        // 생존 아군 비율
        sensor.AddObservation(manager != null ? manager.GetAliveRatio(team) : 1f);

        // 아레나 중앙 정보 (중앙 보상/감점을 학습 가능하게 만드는 핵심 관측)
        // 방향 2개 + 거리 1개 = 3개
        if (manager != null)
        {
            Vector3 toCenter = manager.GetArenaCenter() - transform.position;
            toCenter.y = 0f;
            float distToCenter = toCenter.magnitude;

            // 중앙을 향하는 단위 방향 (에이전트 로컬 기준으로 변환해 회전에 불변)
            Vector3 dirLocal = distToCenter > 0.01f
                ? transform.InverseTransformDirection(toCenter / distToCenter)
                : Vector3.zero;
            sensor.AddObservation(dirLocal.x);
            sensor.AddObservation(dirLocal.z);

            // 중앙까지의 정규화 거리 (0=중앙, 1=최대거리)
            float maxD = manager.GetCenterMaxDistance();
            sensor.AddObservation(maxD > 0f ? Mathf.Clamp01(distToCenter / maxD) : 0f);

            // --- 가장 가까운 적 정보 (방향2 + 거리1 + 보이는가1 = 4개) ---
            var enemyList = (team == AgentTeam.Blue) ? manager.redAgents : manager.blueAgents;
            PlayerAgent nearestE = null;
            float nearestD = float.MaxValue;
            if (enemyList != null)
            {
                for (int i = 0; i < enemyList.Count; i++)
                {
                    var e = enemyList[i];
                    if (e == null || !e.gameObject.activeInHierarchy) continue;
                    if (e.health != null && e.health.IsDead) continue;
                    float d = Vector3.Distance(transform.position, e.transform.position);
                    if (d < nearestD) { nearestD = d; nearestE = e; }
                }
            }

            if (nearestE != null)
            {
                Vector3 toE = nearestE.transform.position - transform.position;
                toE.y = 0f;
                Vector3 eDirLocal = transform.InverseTransformDirection(toE.normalized);
                sensor.AddObservation(eDirLocal.x);
                sensor.AddObservation(eDirLocal.z);
                sensor.AddObservation(Mathf.Clamp01(nearestD / Mathf.Max(1f, spotRange)));

                bool visible = nearestD <= spotRange
                    && Vector3.Angle(transform.forward, toE) <= spotFovHalfAngle
                    && !Physics.Raycast(transform.position + Vector3.up, toE.normalized, nearestD);
                sensor.AddObservation(visible ? 1f : 0f);
            }
            else
            {
                sensor.AddObservation(0f);
                sensor.AddObservation(0f);
                sensor.AddObservation(1f);
                sensor.AddObservation(0f);
            }

            // --- 가장 가까운 아군 거리 (1개) ---
            var mateList = (team == AgentTeam.Blue) ? manager.blueAgents : manager.redAgents;
            float nearestM = float.MaxValue;
            if (mateList != null)
            {
                for (int i = 0; i < mateList.Count; i++)
                {
                    var t2 = mateList[i];
                    if (t2 == null || t2 == this || !t2.gameObject.activeInHierarchy) continue;
                    if (t2.health != null && t2.health.IsDead) continue;
                    float d = Vector3.Distance(transform.position, t2.transform.position);
                    if (d < nearestM) nearestM = d;
                }
            }
            sensor.AddObservation(nearestM < float.MaxValue
                ? Mathf.Clamp01(nearestM / Mathf.Max(1f, cohesionRadius)) : 1f);
        }
        else
        {
            // manager가 없을 때도 관측 개수를 동일하게 유지 (중앙3 + 적4 + 아군1 = 8)
            for (int i = 0; i < 8; i++) sensor.AddObservation(0f);
        }
    }

    public override void OnActionReceived(ActionBuffers actions)
    {
        // --- 이동/회전 (연속) ---
        float moveX = Mathf.Clamp(actions.ContinuousActions[0], -1f, 1f);
        float moveZ = Mathf.Clamp(actions.ContinuousActions[1], -1f, 1f);
        float rot = Mathf.Clamp(actions.ContinuousActions[2], -1f, 1f);

        // 회전은 항상 액션으로 독립 제어 -> 조준을 유지한 채 옆/뒤로 이동(스트레이프) 가능
        transform.Rotate(0f, rot * rotationSpeed * Time.fixedDeltaTime, 0f);

        // 이동은 '로컬 기준': moveZ=전후, moveX=좌우 스트레이프
        Vector3 move = new Vector3(moveX, 0f, moveZ);
        if (move.sqrMagnitude > 1f) move.Normalize();

        // 방향별 속도 배율: 전진이 가장 빠르고 후진이 가장 느림
        // -> 무작정 백무빙은 불리하지만, 엄폐/후퇴를 위한 선택지는 남음
        float dirMul = 1f;
        if (move.sqrMagnitude > 0.0001f)
        {
            Vector3 n = move.normalized;
            if (n.z >= 0f)
                dirMul = Mathf.Lerp(strafeSpeedMultiplier, 1f, n.z);
            else
                dirMul = Mathf.Lerp(strafeSpeedMultiplier, backwardSpeedMultiplier, -n.z);
        }

        // 사격 중이면 이동 속도 감소: 전진을 가장 크게 깎아 '쏘면서 돌진'을 억제.
        // 후진/스트레이프는 덜 깎아 사격하며 엄폐물로 빠지는 움직임은 가능하게 둠.
        if (gun != null && (Time.time - gun.lastFireTime) <= firingMoveWindow)
        {
            float fwd = Mathf.Clamp01(move.z); // 전진 성분만 강하게 제한
            float fireMul = Mathf.Lerp(firingLateralMultiplier, firingForwardMultiplier, fwd);

            // 총기별 debuffSpeed 반영 (미니건 -0.5 등, 음수값이므로 더함)
            if (useGunDebuffSpeed) fireMul = Mathf.Clamp(fireMul + gun.debuffSpeed, 0.1f, 1f);

            dirMul *= fireMul;
        }

        Vector3 vel = transform.TransformDirection(move) * moveSpeed * dirMul;
        vel.y = _rb.linearVelocity.y; // 중력 유지
        _rb.linearVelocity = vel;

        // 중앙 접근 유도 보상: 아레나 중앙에 가까워진 만큼 소량 보상 (한곳 고착 방지)
        if (manager != null && health != null && !health.IsDead && !Mathf.Approximately(centerSeekReward, 0f))
        {
            float centerDist = Vector3.Distance(_rb.position, manager.GetArenaCenter());
            if (_prevCenterDist >= 0f)
            {
                AddReward((_prevCenterDist - centerDist) * centerSeekReward);
            }
            _prevCenterDist = centerDist;
        }

        // 전술 보상 (조준/체력/무기) + 팀 응집
        ApplyTacticalRewards();
        ApplyCohesionReward();

        // 중앙 거리 감점: 중앙에서 멀리 떨어져 있을수록 매 결정마다 감점.
        // 정중앙=0, 최대거리 이상=centerDistancePenalty 전액 (선형 보간).
        if (manager != null && health != null && !health.IsDead && !Mathf.Approximately(centerDistancePenalty, 0f))
        {
            float maxD = centerPenaltyMaxDistance > 0f ? centerPenaltyMaxDistance : manager.GetCenterMaxDistance();
            if (maxD > 0f)
            {
                float d2 = Vector3.Distance(_rb.position, manager.GetArenaCenter());
                float t2 = Mathf.Clamp01(d2 / maxD); // 0=중앙, 1=최대거리
                AddReward(centerDistancePenalty * t2);
            }
        }

        // 정체 페널티: 기준 위치에서 stallTimeLimit 초 동안
        // stallMoveThreshold 미만으로만 움직였다면 매 결정마다 감점.
        // 기준 거리를 벗어나면 그 지점을 새 기준으로 삼고 타이머 초기화.
        if (health != null && !health.IsDead && !Mathf.Approximately(stallPenalty, 0f))
        {
            float movedDist = Vector3.Distance(_rb.position, _stallAnchorPos);
            if (movedDist >= stallMoveThreshold)
            {
                _stallAnchorPos = _rb.position; // 충분히 이동함: 기준 갱신
                _stallTimer = 0f;
        _prevNearestEnemyDist = -1f; // 적 거리 기준도 초기화
        _noEnemyTimer = 0f;          // 적 탐색 타이머 초기화
        _holdFireTimer = 0f;         // 교전 회피 타이머 초기화
        _firedThisDecision = false;
            }
            else
            {
                _stallTimer += Time.fixedDeltaTime;
                if (_stallTimer >= stallTimeLimit)
                {
                    AddReward(stallPenalty);
                }
            }
        }

        // 적 발견 보상: 시야(전방 FOV) 안에 적이 새로 들어오면 1회 +spotEnemyReward
        if (manager != null && health != null && !health.IsDead)
        {
            var enemies2 = team == AgentTeam.Blue ? manager.redAgents : manager.blueAgents;
            bool spotting = false;
            if (enemies2 != null)
            {
                for (int i = 0; i < enemies2.Count; i++)
                {
                    var e = enemies2[i];
                    if (e == null || !e.gameObject.activeInHierarchy) continue;
                    Vector3 to = e.transform.position - transform.position;
                    if (to.magnitude > spotRange) continue;
                    float ang = Vector3.Angle(transform.forward, to);
                    if (ang > spotFovHalfAngle) continue;
                    // 시야각/거리 통과: 벽에 가리지 않았는지 레이캐스트로 확인
                    RaycastHit hi;
                    if (Physics.Raycast(transform.position, to.normalized, out hi, spotRange))
                    {
                        string enemyTag = team == AgentTeam.Blue ? "RedAgent" : "BlueAgent";
                        if (hi.collider.CompareTag(enemyTag)) { spotting = true; break; }
                    }
                }
            }
            if (spotting && !_wasSpottingEnemy && !_spotRewardGiven)
            {
                AddReward(spotEnemyReward); // 에피소드당 1회만 지급
                _spotRewardGiven = true;
            }
            _wasSpottingEnemy = spotting;
        }

        // 미로 경계 이탈 감지: 벗어나면 감점 후 즉사 처리
        if (manager != null && health != null && !health.IsDead)
        {
            var mz = manager.mazeGenerator;
            float pad = mz != null ? mz.BorderPadding : 0f;
            float mx = mz != null ? mz.MaxX : manager.arenaSize;
            float mzd = mz != null ? mz.MaxZ : manager.arenaSize;
            Vector3 origin = mz != null ? mz.transform.position : (transform.parent != null ? transform.parent.position : Vector3.zero);
            float lx = _rb.position.x - origin.x;
            float lz = _rb.position.z - origin.z;
            float killLine = pad + 1f; // 테두리 벽 바로 바깥에서 즉시 사망 (boundsMargin 미사용)
            bool outside = lx < -killLine || lx > mx + killLine
                        || lz < -killLine || lz > mzd + killLine;
            if (outside)
            {
                AddReward(outOfBoundsPenalty);
                health.TakeDamage(health.maxHp, null); // attacker=null -> 킬 보상 없이 사망
            }
        }



        // --- 자기 베이스 캠핑 / 적 침입 감점 ---
        // 두 감점이 모두 0이면(랜덤 스폰 사용 시 권장) 계산 자체를 건너뜀
        bool baseRulesActive = !Mathf.Approximately(campPenalty, 0f) || !Mathf.Approximately(baseIntrudedPenalty, 0f);
        if (baseRulesActive && manager != null && health != null && !health.IsDead)
        {
            Vector3 ownBase = manager.GetOwnBaseCenter(team);
            float distToOwn = Vector3.Distance(_rb.position, ownBase);

            // (1) 자기 베이스에 오래 머물면 감점
            if (distToOwn <= baseRadius)
            {
                _campTimer += Time.fixedDeltaTime;
                if (_campTimer >= campTimeLimit)
                {
                    AddReward(campPenalty);
                }
            }
            else
            {
                _campTimer = 0f;
            }

            // (2) 자기 베이스에 적이 들어와 있으면 감점
            var enemies = team == AgentTeam.Blue ? manager.redAgents : manager.blueAgents;
            if (enemies != null)
            {
                for (int i = 0; i < enemies.Count; i++)
                {
                    var e = enemies[i];
                    if (e == null || !e.gameObject.activeInHierarchy) continue;
                    if (Vector3.Distance(e.transform.position, ownBase) <= baseRadius)
                    {
                        AddReward(baseIntrudedPenalty);
                        break; // 침입당함: 한 번만 감점
                    }
                }
            }
        }

        // --- 발사 (이산) ---
        _firedThisDecision = actions.DiscreteActions[0] == 1; // 발사 의도 기록
        if (_firedThisDecision && gun != null)
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
    /// <summary> AgentHealth가 호출: 내 총알이 누군가를 맞췄을 때 </summary>
    public void NotifyDealtDamage(PlayerAgent victim)
    {
        if (victim == null) return;
        if (victim.team == team)
        {
            // 아군 오사: 감점 (매니저 인스펙터에서 조정)
            AddReward(hitTeammatePenalty);
            return;
        }

        AddReward(hitEnemyReward);

        // 원거리 명중 보너스: 멀리서 맞출수록 추가 보상 (원거리 총기의 가치 부여)
        if (!Mathf.Approximately(longRangeHitBonus, 0f))
        {
            float d = Vector3.Distance(transform.position, victim.transform.position);
            if (d >= longRangeDistance) AddReward(longRangeHitBonus);
        }
    }

    /// <summary>
    /// 조준 정렬 / 팀 응집 / 체력 기반 행동 / 무기 선택 보상을 한 번에 계산.
    /// OnActionReceived에서 매 결정마다 호출됨.
    /// </summary>
    private void ApplyTacticalRewards()
    {
        if (manager == null || health == null || health.IsDead) return;

        Vector3 myPos = transform.position;
        var enemies = (team == AgentTeam.Blue) ? manager.redAgents : manager.blueAgents;
        if (enemies == null) return;

        PlayerAgent nearest = null;
        float nearestDist = float.MaxValue;
        int visibleEnemies = 0;

        for (int i = 0; i < enemies.Count; i++)
        {
            var e = enemies[i];
            if (e == null || !e.gameObject.activeInHierarchy) continue;
            if (e.health != null && e.health.IsDead) continue;

            Vector3 to = e.transform.position - myPos;
            to.y = 0f;
            float dist = to.magnitude;

            if (dist < nearestDist) { nearestDist = dist; nearest = e; }

            // 시야 판정: spotRange / spotFovHalfAngle 재사용, 벽에 가리면 제외
            if (dist <= spotRange && Vector3.Angle(transform.forward, to) <= spotFovHalfAngle)
            {
                if (!Physics.Raycast(myPos + Vector3.up, to.normalized, dist))
                    visibleEnemies++;
            }
        }

        // 적 탐색 실패 타이머: 시야에 적이 있으면 리셋, 없으면 누적
        ApplyNoEnemyPenalty(visibleEnemies);

        if (nearest == null) { _prevNearestEnemyDist = -1f; return; }

        Vector3 toNearest = nearest.transform.position - myPos;
        toNearest.y = 0f;
        float angleToEnemy = Vector3.Angle(transform.forward, toNearest);

        // (2) 조준 정렬: 정면에 가까울수록 큰 보상
        bool enemyInSights = visibleEnemies > 0 && angleToEnemy <= aimAngleThreshold;

        // 교전 회피 감점: 조준선에 적이 있는데 쏘지 않으면 감점
        if (!Mathf.Approximately(holdFirePenalty, 0f))
        {
            bool reloading = holdFireIgnoreReload && gun != null && gun.onReload;
            bool muzzleBlocked = gun != null && gun.IsMuzzleBlocked(); // 벽에 막혀 쏠 수 없음
            if (enemyInSights && !_firedThisDecision && !reloading && !muzzleBlocked)
            {
                _holdFireTimer += Time.fixedDeltaTime;
                if (_holdFireTimer >= holdFireTimeLimit) AddReward(holdFirePenalty);
            }
            else _holdFireTimer = 0f;
        }

        if (!Mathf.Approximately(aimAlignReward, 0f) && enemyInSights)
        {
            float align = 1f - (angleToEnemy / Mathf.Max(0.01f, aimAngleThreshold));
            AddReward(aimAlignReward * align);
        }

        // (6) 체력 기반: 저체력이면 후퇴 가점 / 접근 감점
        if (_prevNearestEnemyDist >= 0f && health.maxHp > 0)
        {
            float delta = nearestDist - _prevNearestEnemyDist; // +면 멀어짐
            if ((health.CurrentHp / health.maxHp) <= lowHpThreshold)
            {
                if (delta > 0f) AddReward(retreatReward * Mathf.Clamp01(delta));
                else            AddReward(recklessPenalty * Mathf.Clamp01(-delta));
            }
        }
        _prevNearestEnemyDist = nearestDist;

        // 무모한 근접 억제: 적에게 너무 붙으면 감점 (원거리 총기의 가치 보존)
        if (!Mathf.Approximately(tooClosePenalty, 0f) && nearestDist < tooCloseDistance)
        {
            float closeness = 1f - (nearestDist / Mathf.Max(0.01f, tooCloseDistance));
            AddReward(tooClosePenalty * closeness);
        }

        // 엄폐 유도: 적 시야에 노출된 채로 있으면 감점
        // (내가 적을 보고 있으면 적도 나를 본다 -> 서로 보이는 상태가 노출)
        if (!Mathf.Approximately(exposurePenalty, 0f) && visibleEnemies > 0)
        {
            AddReward(exposurePenalty * visibleEnemies);
        }

        // (7) 상황별 무기 선택
        if (!Mathf.Approximately(weaponChoiceReward, 0f) && visibleEnemies > 0 && gun != null)
        {
            GunType best;
            if (visibleEnemies >= minigunEnemyCount) best = GunType.MiniGun;
            else if (nearestDist <= pistolRange)      best = GunType.Pistol;
            else                                     best = GunType.AssaultRifle;

            if (gun.type == best) AddReward(weaponChoiceReward);
        }
    }

    /// <summary>
    /// 적 탐색 실패 감점: 일정 시간 동안 적을 한 번도 보지 못하면 감점.
    /// 적이 시야에 들어오면 타이머가 리셋되어 감점이 멈춤.
    /// </summary>
    private void ApplyNoEnemyPenalty(int visibleEnemies)
    {
        if (health == null || health.IsDead) return;

        if (visibleEnemies > 0)
        {
            _noEnemyTimer = 0f; // 적 발견 -> 리셋
            return;
        }

        if (Mathf.Approximately(noEnemyPenalty, 0f)) return;

        _noEnemyTimer += Time.fixedDeltaTime;
        if (_noEnemyTimer < noEnemyTimeLimit) return;

        float penalty = noEnemyPenalty;
        if (noEnemyPenaltyScaling)
        {
            // 제한 시간을 넘긴 정도에 비례해 감점이 커짐 (상한 적용)
            float over = (_noEnemyTimer - noEnemyTimeLimit) / Mathf.Max(0.01f, noEnemyTimeLimit);
            penalty *= Mathf.Clamp(1f + over, 1f, Mathf.Max(1f, noEnemyPenaltyMaxScale));
        }
        AddReward(penalty);
    }

    /// <summary>
    /// (5) 팀 응집 보상: 아군과 적정 거리를 유지하면 가점.
    /// 너무 멀면(각개격파) 없고, 너무 가까우면(전멸 위험) 없음.
    /// </summary>
    private void ApplyCohesionReward()
    {
        if (manager == null || health == null || health.IsDead) return;
        if (Mathf.Approximately(cohesionReward, 0f)) return;

        var mates = (team == AgentTeam.Blue) ? manager.blueAgents : manager.redAgents;
        if (mates == null) return;

        float nearestMate = float.MaxValue;
        for (int i = 0; i < mates.Count; i++)
        {
            var t = mates[i];
            if (t == null || t == this || !t.gameObject.activeInHierarchy) continue;
            if (t.health != null && t.health.IsDead) continue;

            float d = Vector3.Distance(transform.position, t.transform.position);
            if (d < nearestMate) nearestMate = d;
        }

        // 너무 멀면(각개격파) 없고, 너무 가까우면(전멸 위험) 없음
        if (nearestMate < float.MaxValue &&
            nearestMate >= cohesionMinDistance && nearestMate <= cohesionRadius)
        {
            AddReward(cohesionReward);
        }
    }

    /// <summary> Bullet이 호출: 내 총알이 벽에 맞았을 때 감점 </summary>
    public void NotifyWallShot()
    {
        AddReward(wallShotPenalty);
    }
}
