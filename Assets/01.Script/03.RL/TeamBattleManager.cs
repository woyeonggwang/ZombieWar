using System.Collections.Generic;
using Unity.MLAgents;
using UnityEngine;

/// <summary>
/// 3대3 팀 전투 환경 컨트롤러.
/// MA-POCA용 SimpleMultiAgentGroup으로 팀 단위 보상을 관리하고,
/// 에피소드 시작/종료, 미로 재생성, 에이전트 리스폰을 담당합니다.
/// </summary>
public class TeamBattleManager : MonoBehaviour
{
    [Header("Environment")]
    [Tooltip("한 에피소드 최대 스텝 수 (FixedUpdate 기준). 초과 시 무승부 처리")]
    public int maxEnvironmentSteps = 5000;
    [Tooltip("관측 정규화용 크기. mazeGenerator가 연결되면 자동 계산됨")]
    public float arenaSize = 40f;

    [Header("Maze")]
    [Tooltip("연결 시 에피소드 시작마다 미로를 새로 생성하고 스폰 위치를 미로 코너에 맞춤")]
    public MazeGenerator mazeGenerator;
    [Tooltip("에피소드마다 미로 재생성 여부. 끄면 처음 한 번만 생성")]
        public bool regenerateMazePerEpisode = true;

    [Header("Spawn Randomization")]
    [Tooltip("체크 시 에피소드마다 미로 안의 임의 셀에 스폰. 끄면 기존처럼 고정 코너 스폰")]
    public bool randomizeSpawns = true;
    [Tooltip("체크 시 두 팀을 대각선으로 나눠 서로 반대편 영역에 스폰. 끄면 맵 전체에 완전 무작위")]
    public bool separateTeamsBySide = false;
    [Tooltip("스폰 지점끼리 최소 몇 칸 떨어뜨릴지. 너무 크면 배치 실패 후 무작위로 대체됨")]
        public int minSpawnCellDistance = 3;
    [Tooltip("스폰 지점 주변 몇 칸의 벽을 제거할지. 0이면 벽을 건드리지 않음(미로 보존). 1이면 3x3 제거")]
    public int spawnClearRadius = 0;

    [Header("Agents (3 vs 3)")]
    public List<PlayerAgent> blueAgents = new List<PlayerAgent>();
    public List<PlayerAgent> redAgents = new List<PlayerAgent>();

    [Header("Spawn Points")]
    public List<Transform> blueSpawns = new List<Transform>();
    public List<Transform> redSpawns = new List<Transform>();

    [Header("Rewards")]
    public float winReward = 1f;
    public float loseReward = -1f;
    [Tooltip("시간 초과 무승부 시 양 팀 모두에게 주는 그룹 감점 (음수). 소극적 플레이를 억제")]
    public float drawPenalty = -1f;
    [Tooltip("에피소드 전체에 걸쳐 누적될 시간 감점 총량 (음수). 매 스텝 이 값을 maxEnvironmentSteps로 나눠 지급")]
    public float timePenaltyTotal = -0.5f;
    public float killReward = 1f;
    public float teamKillPenalty = -1f;
    public float deathPenalty = -0.5f;

    private SimpleMultiAgentGroup _blueGroup;
    private SimpleMultiAgentGroup _redGroup;
    private int _stepCount;
    private int _blueAlive;
    private int _redAlive;
    private bool _mazeBuilt;
    [Header("Rewards - 개별 에이전트 보상 (매니저에서 일괄 지정, 모든 에이전트에 자동 적용)")]
    [Tooltip("적을 새로 시야에서 발견했을 때 주는 보상 (에피소드당 1회)")]
    public float spotEnemyReward = 0.5f;
    [Tooltip("적을 명중시켰을 때 주는 보상")]
    public float hitEnemyReward = 0.1f;
    [Tooltip("아군을 명중시켰을 때 주는 감점 (음수로 입력)")]
    public float hitTeammatePenalty = -0.2f;
    [Tooltip("벙이나 허공을 쌀을 때 주는 감점 (음수로 입력)")]
    public float wallShotPenalty = -0.01f;

    [Header("Rewards - 중앙 근접 보상 (한곳에만 머무르는 것 방지)")]
    [Tooltip("아레나 정중앙에서 죽었을 때 받는 최대 점수. 거리가 멀어질수록 0으로 선형 감소")]
    public float centerDeathMaxReward = 2f;
    [Tooltip("이 거리 이상 떨어진 곳에서 죽으면 중앙 보상 0. 0 이하면 arenaSize의 절반을 자동 사용")]
    public float centerRewardMaxDistance = 0f;
    [Tooltip("살아있는 동안 중앙에 가까워질 때마다 주는 유도 보상 계수. 0이면 비활성")]
            public float centerSeekReward = 0.002f;
    [Tooltip("중앙에서 멀수록 매 결정마다 주는 감점(음수). 정중앙=0, 최대거리=이 값 전액. 0이면 비활성")]
    public float centerDistancePenalty = -0.01f;

    [Header("Rewards - 조준 정렬")]
    [Tooltip("적을 조준선에 정렬했을 때 매 결정마다 주는 보상. 정면에 가까울수록 큼")]
    public float aimAlignReward = 0.004f;
    [Tooltip("이 각도(도) 안에 적이 들어와야 조준 보상 지급")]
    public float aimAngleThreshold = 20f;

    [Header("Rewards - 팀 응집")]
    [Tooltip("아군과 적정 거리를 유지할 때 매 결정마다 주는 보상. 0이면 비활성")]
    public float cohesionReward = 0.002f;
    [Tooltip("이 거리 안에 아군이 있으면 응집 보상 지급")]
    public float cohesionRadius = 18f;
    [Tooltip("이 거리보다 가까우면 과밀로 보고 보상 없음 (뭉쳐서 전멸 방지)")]
    public float cohesionMinDistance = 4f;

    [Header("Rewards - 체력 기반 행동")]
    [Tooltip("이 비율 이하면 저체력으로 판정 (0.3 = HP 30%)")]
    public float lowHpThreshold = 0.3f;
    [Tooltip("저체력일 때 적에게서 멀어지면 주는 보상 (후퇴 유도)")]
    public float retreatReward = 0.004f;
    [Tooltip("저체력인데 적에게 접근하면 주는 감점 (무모한 돌진 억제)")]
    public float recklessPenalty = -0.004f;

    [Header("Rewards - 무기 선택")]
    [Tooltip("상황에 맞는 무기를 들고 있을 때 매 결정마다 주는 보상")]
    public float weaponChoiceReward = 0.003f;
    [Tooltip("이 거리보다 가까우면 권총이 적합")]
    public float pistolRange = 8f;
    [Tooltip("이 인원 이상 적이 보이면 미니건이 적합")]
    public int minigunEnemyCount = 2;

    [Header("Rewards - 교전 거리 / 엄폐")]
    [Tooltip("이 거리보다 가까이 적에게 붙으면 매 결정마다 감점 (무모한 돌진 억제)")]
    public float tooCloseDistance = 6f;
    [Tooltip("적에게 너무 붙었을 때 주는 감점 (음수)")]
    public float tooClosePenalty = -0.002f;
    [Tooltip("이 거리 이상에서 적을 명중시키면 추가 보상 (원거리 교전 유도)")]
    public float longRangeDistance = 14f;
    [Tooltip("원거리 명중 시 추가로 주는 보상")]
    public float longRangeHitBonus = 0.05f;
    [Tooltip("적 시야에 노출된 채로 있으면 매 결정마다 감점 (엄폐 유도). 0이면 비활성")]
    public float exposurePenalty = -0.0015f;

    [Header("사격 중 이동 제한")]
    [Tooltip("사격 후 이 시간(초) 동안 '사격 중'으로 보고 이동 속도를 줄임")]
    public float firingMoveWindow = 0.25f;
    [Tooltip("사격 중 전진 속도 배율. 낮을수록 쏘면서 돌진하기 어려워짐")]
    [Range(0.1f, 1f)] public float firingForwardMultiplier = 0.4f;
    [Tooltip("사격 중 후진/스트레이프 속도 배율. 전진보다 덜 깎아 후퇴/회피는 가능하게")]
    [Range(0.1f, 1f)] public float firingLateralMultiplier = 0.7f;
    [Tooltip("체크 시 총기별 debuffSpeed(미니건 -0.5 등)를 추가로 반영")]
    public bool useGunDebuffSpeed = true;

    [Header("Rewards - 적 탐색 실패")]
    [Tooltip("이 시간(초) 동안 적을 한 번도 보지 못하면 감점 시작. 적을 보면 타이머 초기화")]
    public float noEnemyTimeLimit = 8f;
    [Tooltip("적을 못 찾는 동안 매 결정마다 주는 감점 (음수)")]
    public float noEnemyPenalty = -0.001f;
    [Tooltip("체크 시 시간이 길어질수록 감점이 커짐 (탐색을 계속 미루면 손해가 가속)")]
    public bool noEnemyPenaltyScaling = true;
    [Tooltip("감점 배율 상한. 예: 3이면 최대 noEnemyPenalty의 3배까지 커짐")]
    public float noEnemyPenaltyMaxScale = 3f;

    [Header("Rewards - 발사 / 교전 회피")]
    [Tooltip("발사 1회당 감점(탄약 낭비 억제). 너무 크면 아예 안 쏘게 되니 주의")]
    public float fireCostPenalty = -0.0005f;
    [Tooltip("조준 각도 안에 적이 보이는데 이 시간(초) 이상 쏘지 않으면 감점 시작")]
    public float holdFireTimeLimit = 1f;
    [Tooltip("적을 보고도 쏘지 않을 때 매 결정마다 주는 감점 (음수)")]
    public float holdFirePenalty = -0.005f;
    [Tooltip("체크 시 재장전 중에는 미사격 감점을 면제")]
    public bool holdFireIgnoreReload = true;

    [Header("Rewards - 정체 페널티 (제자리에 머무는 것 방지)")]
    [Tooltip("이 시간(초) 동안 아래 이동거리 미만으로 움직이면 정체로 판정")]
    public float stallTimeLimit = 3f;
    [Tooltip("정체 판정 기준 이동거리(월드 단위). 이 거리보다 적게 움직이면 정체로 간주")]
    public float stallMoveThreshold = 2f;
    [Tooltip("정체 판정 시 매 결정마다 주는 감점 (음수로 입력)")]
        public float stallPenalty = -0.02f;

    [Header("Rewards - 베이스 관련 (랜덤 스폰 사용 시 0 권장)")]
    [Tooltip("자기 베이스 근처에 오래 머물면 매 결정마다 주는 감점. 랜덤 스폰이면 기준점이 매 에피소드 바뀌므로 0 권장")]
    public float campPenalty = 0f;
    [Tooltip("자기 베이스 반경 안에 적이 있을 때 매 결정마다 주는 감점. 랜덤 스폰이면 0 권장")]
    public float baseIntrudedPenalty = 0f;
    [Tooltip("자기 베이스에 이 시간(초) 이상 머물면 campPenalty 적용 시작")]
    public float campTimeLimit = 3f;
    [Tooltip("베이스 판정 반경(월드 단위)")]
    public float baseRadius = 8f;



    private void Start()
    {
        _blueGroup = new SimpleMultiAgentGroup();
        _redGroup = new SimpleMultiAgentGroup();

        foreach (var a in blueAgents) Setup(a);
        foreach (var a in redAgents) Setup(a);

        ResetScene();
    }

    private void Setup(PlayerAgent agent)
    {
        agent.manager = this;
        // 매니저 인스펙터의 보상 값을 모든 에이전트에 일괄 적용 (에이전트마다 개별 설정 불필요)
        agent.spotEnemyReward = spotEnemyReward;
        agent.hitEnemyReward = hitEnemyReward;
        agent.hitTeammatePenalty = hitTeammatePenalty;
        agent.wallShotPenalty = wallShotPenalty;
                        agent.centerSeekReward = centerSeekReward;
        agent.centerDistancePenalty = centerDistancePenalty;
        agent.aimAlignReward = aimAlignReward;
        agent.aimAngleThreshold = aimAngleThreshold;
        agent.cohesionReward = cohesionReward;
        agent.cohesionRadius = cohesionRadius;
        agent.cohesionMinDistance = cohesionMinDistance;
        agent.lowHpThreshold = lowHpThreshold;
        agent.retreatReward = retreatReward;
        agent.recklessPenalty = recklessPenalty;
        agent.weaponChoiceReward = weaponChoiceReward;
        agent.pistolRange = pistolRange;
        agent.minigunEnemyCount = minigunEnemyCount;
        agent.tooCloseDistance = tooCloseDistance;
        agent.tooClosePenalty = tooClosePenalty;
        agent.longRangeDistance = longRangeDistance;
        agent.longRangeHitBonus = longRangeHitBonus;
        agent.exposurePenalty = exposurePenalty;
        agent.firingMoveWindow = firingMoveWindow;
        agent.firingForwardMultiplier = firingForwardMultiplier;
        agent.firingLateralMultiplier = firingLateralMultiplier;
        agent.useGunDebuffSpeed = useGunDebuffSpeed;
        agent.noEnemyTimeLimit = noEnemyTimeLimit;
        agent.noEnemyPenalty = noEnemyPenalty;
        agent.noEnemyPenaltyScaling = noEnemyPenaltyScaling;
        agent.noEnemyPenaltyMaxScale = noEnemyPenaltyMaxScale;
        agent.fireCostPenalty = fireCostPenalty;
        agent.holdFireTimeLimit = holdFireTimeLimit;
        agent.holdFirePenalty = holdFirePenalty;
        agent.holdFireIgnoreReload = holdFireIgnoreReload;
        // 최대 거리는 런타임에 매니저가 계산하므로 여기서는 전달하지 않음 (0 = 자동)
        agent.centerPenaltyMaxDistance = centerRewardMaxDistance;
        agent.stallTimeLimit = stallTimeLimit;
        agent.stallMoveThreshold = stallMoveThreshold;
                agent.stallPenalty = stallPenalty;
        agent.campPenalty = campPenalty;
        agent.baseIntrudedPenalty = baseIntrudedPenalty;
        agent.campTimeLimit = campTimeLimit;
        agent.baseRadius = baseRadius;
        var hp = agent.GetComponent<AgentHealth>();
        hp.OnDeath += HandleDeath;
    }

    void FixedUpdate()
    {
        _stepCount++;

        // 시간 초과 -> 무승부: 양 팀 모두 감점 후 에피소드 종료
        if (maxEnvironmentSteps > 0 && _stepCount >= maxEnvironmentSteps)
        {
            // 소극적으로 버티기만 하는 전략을 억제하기 위해 양 팀에 감점
            _blueGroup.AddGroupReward(drawPenalty);
            _redGroup.AddGroupReward(drawPenalty);

            // Interrupted는 부트스트랩을 유지하므로 무승부에 적합
            _blueGroup.GroupEpisodeInterrupted();
            _redGroup.GroupEpisodeInterrupted();
            ResetScene();
            return;
        }

        // 빠른 승부를 유도하는 미세한 시간 패널티 (총량을 스텝 수로 분할)
        float timePenalty = timePenaltyTotal / Mathf.Max(1, maxEnvironmentSteps);
        _blueGroup.AddGroupReward(timePenalty);
        _redGroup.AddGroupReward(timePenalty);
    }

    private void HandleDeath(AgentHealth victim, PlayerAgent killer)
    {
        PlayerAgent victimAgent = victim.owner;
        if (victimAgent == null) return;

        victimAgent.AddReward(deathPenalty);

        // 중앙 근접 사망 보상: 아레나 중앙에 가까울수록 centerDeathMaxReward에 가까운 점수 지급
        // 예) 최대 2점 설정 + 중간 거리에서 사망 -> 1점
        victimAgent.AddReward(GetCenterProximityReward(victimAgent.transform.position));

        if (killer != null)
        {
            killer.AddReward(killer.team != victimAgent.team ? killReward : teamKillPenalty);
        }

        victimAgent.gameObject.SetActive(false);
        Debug.Log(victimAgent.team == AgentTeam.Blue ? "파랑 사망" : "주황 사망");

        if (victimAgent.team == AgentTeam.Blue) _blueAlive--;
        else _redAlive--;

        if (_blueAlive <= 0 || _redAlive <= 0)
        {
            bool blueWin = _redAlive <= 0 && _blueAlive > 0;
            bool redWin = _blueAlive <= 0 && _redAlive > 0;

            if (blueWin)
            {
                _blueGroup.AddGroupReward(winReward);
                _redGroup.AddGroupReward(loseReward);
            }
            else if (redWin)
            {
                _redGroup.AddGroupReward(winReward);
                _blueGroup.AddGroupReward(loseReward);
            }
            else
            {
                // 양 팀 동시 전멸 -> 무승부 처리
                _blueGroup.AddGroupReward(drawPenalty);
                _redGroup.AddGroupReward(drawPenalty);
            }

            _blueGroup.EndGroupEpisode();
            _redGroup.EndGroupEpisode();
            ResetScene();
        }
    }

    /// <summary>
    /// 중앙 보상/감점 판정에 쓰는 최대 거리.
    /// centerRewardMaxDistance가 0 이하면 미로 중앙에서 모서리까지의 실제 대각선 거리를 사용.
    /// (arenaSize*0.5를 쓰면 모서리 구역에서 값이 포화되어 구배가 사라짐)
    /// </summary>
    public float GetCenterMaxDistance()
    {
        if (centerRewardMaxDistance > 0f) return centerRewardMaxDistance;
        if (mazeGenerator == null) return arenaSize * 0.5f;

        // 중앙 -> 모서리 셀 거리 = 대각선의 절반
        float halfX = mazeGenerator.MaxX * 0.5f;
        float halfZ = mazeGenerator.MaxZ * 0.5f;
        return Mathf.Sqrt(halfX * halfX + halfZ * halfZ);
    }

    /// <summary>
    /// 아레나 중앙까지의 거리를 점수로 remap.
    /// 거리 0 -> centerDeathMaxReward, 거리 >= maxDist -> 0. 그 사이는 선형 보간.
    /// </summary>
    public float GetCenterProximityReward(Vector3 worldPos)
    {
        if (Mathf.Approximately(centerDeathMaxReward, 0f)) return 0f;

        float maxDist = GetCenterMaxDistance();
        if (maxDist <= 0f) return 0f;

        float dist = Vector3.Distance(worldPos, GetArenaCenter());
        float t = Mathf.Clamp01(1f - (dist / maxDist)); // 1=정중앙, 0=먼 거리
        return centerDeathMaxReward * t;
    }

    public void ResetScene()
    {
        _stepCount = 0;

        // 미로 생성/재생성 + 스폰 위치 갱신
        // 중요: 스폰 지점 주변 벽을 뚫는 처리가 있으므로, 재생성을 끄면
        // 뚫린 벽이 에피소드마다 누적되어 미로가 사라집니다.
        if (mazeGenerator != null)
        {
            if (regenerateMazePerEpisode || !_mazeBuilt)
            {
                mazeGenerator.Generate(); // 매 에피소드 새 미로 -> 뚫린 벽도 함께 복구
                _mazeBuilt = true;
            }
            else if (randomizeSpawns)
            {
                // 재생성을 껐는데 랜덤 스폰을 쓰면 벽이 계속 깎여나가므로 강제로 재생성
                mazeGenerator.Generate();
            }

            arenaSize = mazeGenerator.WorldSize;
            UpdateSpawnPositions();
        }

        ResetTeam(blueAgents, blueSpawns, _blueGroup);
        ResetTeam(redAgents, redSpawns, _redGroup);

        _blueAlive = blueAgents.Count;
        _redAlive = redAgents.Count;
    }

    /// <summary> 스폰 포인트를 미로 양쪽 코너 셀 중심으로 이동 </summary>
    /// <summary> 스폰 포인트를 미로 안에 배치. randomizeSpawns가 켜지면 매 에피소드 무작위 셀. </summary>
    private void UpdateSpawnPositions()
    {
        if (!randomizeSpawns)
        {
            UpdateSpawnPositionsFixed();
            return;
        }

        int w = mazeGenerator.Width;
        int d = mazeGenerator.Depth;
        float y = transform.position.y;
        int need = blueSpawns.Count + redSpawns.Count;

        // 서로 최소 거리를 지키는 셀들을 뽑는다 (실패해도 무작위로 채움)
        List<Vector2Int> picked = new List<Vector2Int>();
        int guard = 0;
        int maxTries = Mathf.Max(200, need * 60);

        while (picked.Count < need && guard++ < maxTries)
        {
            Vector2Int c = PickCellForIndex(picked.Count, w, d);

            bool ok = true;
            for (int i = 0; i < picked.Count; i++)
            {
                int dx = Mathf.Abs(picked[i].x - c.x);
                int dz = Mathf.Abs(picked[i].y - c.y);
                if (dx + dz < minSpawnCellDistance) { ok = false; break; }
            }
            if (ok) picked.Add(c);
        }

        // 최소 거리 조건을 못 채웠으면 남은 자리는 그냥 무작위로 채움
        while (picked.Count < need)
        {
            picked.Add(PickCellForIndex(picked.Count, w, d));
        }

        // 앞쪽은 블루, 뒤쪽은 레드에 배정
        int idx = 0;
        for (int i = 0; i < blueSpawns.Count; i++, idx++)
        {
            if (blueSpawns[i] == null) continue;
            ApplySpawn(blueSpawns[i], picked[idx], y);
        }
        for (int i = 0; i < redSpawns.Count; i++, idx++)
        {
            if (redSpawns[i] == null) continue;
            ApplySpawn(redSpawns[i], picked[idx], y);
        }
    }

    /// <summary> 스폰 인덱스에 맞는 후보 셀 하나를 뽑는다. 팀 분리 옵션 반영. </summary>
    private Vector2Int PickCellForIndex(int index, int w, int d)
    {
        if (!separateTeamsBySide)
        {
            return new Vector2Int(Random.Range(0, w), Random.Range(0, d));
        }

        // 앞쪽 blueSpawns.Count 개는 아래 절반, 나머지는 위 절반
        bool isBlue = index < blueSpawns.Count;
        int half = Mathf.Max(1, d / 2);
        int zMin = isBlue ? 0 : half;
        int zMax = isBlue ? half : d;
        return new Vector2Int(Random.Range(0, w), Random.Range(zMin, Mathf.Max(zMin + 1, zMax)));
    }

    /// <summary> 셀 좌표를 월드 위치로 바꿔 스폰 트랜스폼에 적용. 회전은 아레나 중앙을 보게 설정. </summary>
    private void ApplySpawn(Transform spawn, Vector2Int cell, float y)
    {
        // 랜덤 스폰 지점이 벽에 갇히지 않도록 주변 벽 제거
        mazeGenerator.ClearAreaAroundCell(cell.x, cell.y, 1);

        Vector3 p = mazeGenerator.GetCellWorldPosition(cell.x, cell.y);
        spawn.position = new Vector3(p.x, y, p.z);

        // 스폰 직후 중앙을 바라보게 해서 중앙 인지를 돕는다
        Vector3 toCenter = GetArenaCenter() - spawn.position;
        toCenter.y = 0f;
        spawn.rotation = toCenter.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(toCenter.normalized, Vector3.up)
            : Quaternion.identity;
    }

    /// <summary> 기존 고정 코너 스폰 (randomizeSpawns를 끄면 사용) </summary>
    private void UpdateSpawnPositionsFixed()
    {
        int w = mazeGenerator.Width;
        int d = mazeGenerator.Depth;
        float y = transform.position.y;

        Vector2Int[] blueCells = new Vector2Int[] { new Vector2Int(0, 0), new Vector2Int(1, 0), new Vector2Int(0, 1) };
        Vector2Int[] redCells = new Vector2Int[] { new Vector2Int(w - 1, d - 1), new Vector2Int(w - 2, d - 1), new Vector2Int(w - 1, d - 2) };

        for (int i = 0; i < blueSpawns.Count && i < blueCells.Length; i++)
        {
            Vector3 p = mazeGenerator.GetCellWorldPosition(blueCells[i].x, blueCells[i].y);
            blueSpawns[i].position = new Vector3(p.x, y, p.z);
            blueSpawns[i].rotation = Quaternion.Euler(0f, 45f, 0f);
        }
        for (int i = 0; i < redSpawns.Count && i < redCells.Length; i++)
        {
            Vector3 p = mazeGenerator.GetCellWorldPosition(redCells[i].x, redCells[i].y);
            redSpawns[i].position = new Vector3(p.x, y, p.z);
            redSpawns[i].rotation = Quaternion.Euler(0f, 225f, 0f);
        }
    }

    private void ResetTeam(List<PlayerAgent> agents, List<Transform> spawns, SimpleMultiAgentGroup group)
    {
        for (int i = 0; i < agents.Count; i++)
        {
            PlayerAgent agent = agents[i];
            if (agent == null) continue;

            agent.gameObject.SetActive(true);

            if (spawns.Count > 0)
            {
                Transform sp = spawns[i % spawns.Count];
                agent.transform.position = sp.position;
                agent.transform.rotation = sp.rotation;
            }

            agent.GetComponent<AgentHealth>().ResetHealth();
            agent.ResetAgentState();

            // 비활성화로 그룹에서 빠졌던 에이전트 재등록 (중복 등록은 내부에서 무시됨)
            group.RegisterAgent(agent);
        }
    }

    /// <summary> 관측용: 해당 팀의 생존 비율 </summary>
    public float GetAliveRatio(AgentTeam team)
    {
        if (team == AgentTeam.Blue)
        {
            return blueAgents.Count > 0 ? (float)_blueAlive / blueAgents.Count : 0f;
        }
        return redAgents.Count > 0 ? (float)_redAlive / redAgents.Count : 0f;
    }

    /// <summary> 지정한 팀의 '상대' 베이스(스폰 평균) 월드 좌표를 반환. 없으면 자기 위치 반환. </summary>
    public Vector3 GetEnemyBaseCenter(AgentTeam team)
    {
        List<Transform> enemySpawns = team == AgentTeam.Blue ? redSpawns : blueSpawns;
        if (enemySpawns == null || enemySpawns.Count == 0) return transform.position;
        Vector3 sum = Vector3.zero;
        int n = 0;
        for (int i = 0; i < enemySpawns.Count; i++)
        {
            if (enemySpawns[i] == null) continue;
            sum += enemySpawns[i].position;
            n++;
        }
        return n > 0 ? sum / n : transform.position;
    }

    /// <summary> 지정한 팀의 '자기' 베이스(스폰 평균) 월드 좌표를 반환. </summary>
    public Vector3 GetOwnBaseCenter(AgentTeam team)
    {
        List<Transform> ownSpawns = team == AgentTeam.Blue ? blueSpawns : redSpawns;
        if (ownSpawns == null || ownSpawns.Count == 0) return transform.position;
        Vector3 sum = Vector3.zero;
        int n = 0;
        for (int i = 0; i < ownSpawns.Count; i++)
        {
            if (ownSpawns[i] == null) continue;
            sum += ownSpawns[i].position;
            n++;
        }
        return n > 0 ? sum / n : transform.position;
    }

    /// <summary> 이 아레나(미로 영역)의 중심 월드 좌표를 반환. 양 팀 공통 목표점. </summary>
    public Vector3 GetArenaCenter()
    {
        if (mazeGenerator == null) return transform.position;
        // 미로 영역 로컬 중심 (셀 좌표 기준 가운데)
        Vector3 c0 = mazeGenerator.GetCellWorldPosition(0, 0);
        Vector3 c1 = mazeGenerator.GetCellWorldPosition(mazeGenerator.Width - 1, mazeGenerator.Depth - 1);
        return (c0 + c1) * 0.5f;
    }



}
