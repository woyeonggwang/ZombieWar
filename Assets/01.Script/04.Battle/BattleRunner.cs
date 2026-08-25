using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Policies;
using Unity.MLAgents.Sensors;

/// <summary>
/// 학습된 모델을 재학습 없이 그대로 돌리기 위한 전투 씬 컨트롤러 (신규 작성).
///
/// 기존 스크립트(PlayerAgent, TeamBattleManager, AgentHealth, Gun 등)는
/// 전혀 수정하지 않고, 이 스크립트가 환경을 "학습 당시와 동일하게" 맞춰 줍니다.
///
/// ─────────────────────────────────────────────────────────
/// 해결하는 문제들 (전체 점검으로 발견한 것)
/// ─────────────────────────────────────────────────────────
///
/// [1] 레이 센서 태그 순서가 팀 대칭이 아님  ★ 가장 중요
///     모든 에이전트의 DetectableTags가 [BlueAgent, RedAgent, Wall]로 고정입니다.
///     즉 관측 슬롯1은 항상 "BlueAgent 감지"입니다.
///     학습 시에는 self_play로 TeamId=0과 TeamId=1이 각각 별개의 정책
///     스냅샷을 썼기 때문에, 각자 자기 관점으로 해석하면 됐습니다.
///     그러나 추론 시에는 양 팀이 같은 .onnx 하나를 쓰므로,
///     한쪽 팀은 아군과 적을 뒤바꿔 인식합니다.
///     → "적 옆에서 아군을 쏘는" 증상의 직접 원인.
///     해결: Red 팀의 태그 순서를 [RedAgent, BlueAgent, Wall]로 뒤집어
///           양 팀 모두 "슬롯1=아군, 슬롯2=적"이 되게 만듭니다.
///
/// [2] aliveRatio 관측이 항상 0
///     TeamBattleManager를 껐더니 _blueAlive/_redAlive가 갱신되지 않았습니다.
///     해결: 매 프레임 리플렉션으로 실제 생존자 수를 써넣습니다.
///
/// [3] 에피소드 경계가 없음
///     MaxStep=0이고 EndEpisode()도 호출되지 않아
///     라운드가 바뀌어도 하나의 긴 에피소드가 무한히 이어졌습니다.
///     해결: 리스폰 시 EndEpisode()를 호출합니다.
///
/// [4] 라운드 길이 불일치
///     학습은 10000 FixedUpdate(=200초), 추론은 120초였습니다.
///     해결: maxEnvironmentSteps에서 자동 계산합니다.
///
/// [5] 스폰 배치 방식 불일치
///     학습은 separateTeamsBySide=False(완전 무작위),
///     추론은 True(대각선 분리)였습니다.
///     해결: 매니저 설정을 그대로 따라갑니다.
///
/// [6] 관측 정규화 파라미터 미동기화
///     TeamBattleManager.Setup()이 cohesionRadius를 에이전트에 덮어쓰는데
///     (이 값은 관측 23번 mateDist의 정규화 분모입니다),
///     BattleArenaController는 Setup()을 부르지 않아 누락됐습니다.
///     해결: 관측에 영향을 주는 값들을 동일하게 복사합니다.
/// </summary>
public class BattleRunner : MonoBehaviour
{
    [Header("필수 참조")]
    [Tooltip("관측 제공용. enabled=false로 꺼둔 채 데이터 소스로만 씁니다.")]
    public TeamBattleManager observationSource;

    public MazeGenerator mazeGenerator;

    [Header("진영")]
    public List<PlayerAgent> blueAgents = new List<PlayerAgent>();
    public List<PlayerAgent> redAgents = new List<PlayerAgent>();
    public List<Transform> blueSpawns = new List<Transform>();
    public List<Transform> redSpawns = new List<Transform>();

    [Header("모델")]
    [Tooltip("양 팀에 적용할 학습 모델. 비우면 각 에이전트에 지정된 것을 씁니다.")]
    public Unity.InferenceEngine.ModelAsset battleModel;

    [Header("팀 대칭 보정 ★")]
    [Tooltip("켜면 Red 팀의 레이 센서 태그 순서를 뒤집어 양 팀 관측을 대칭으로 만듭니다. " +
             "이것이 '아군을 쏘는' 문제의 핵심 수정입니다. " +
             "만약 모델이 Red 관점으로 학습됐다면 flipRedInstead를 꺼서 Blue를 뒤집으세요.")]
    public bool makeTeamSymmetric = true;

    [Tooltip("체크: Red를 뒤집음(모델이 Blue 관점일 때). 해제: Blue를 뒤집음(모델이 Red 관점일 때). " +
             "어느 쪽이 맞는지 모르면 둘 다 시험해 보세요. 한쪽은 반드시 정상 동작합니다.")]
    public bool flipRedInstead = true;

    [Header("라운드")]
    [Tooltip("0이면 매니저의 maxEnvironmentSteps에서 자동 계산합니다.")]
    public float roundTimeLimit = 0f;

    public float restartDelay = 2f;
    public bool logResults = true;

    [Header("사람 플레이어")]
    [Tooltip("전투에 참가시킬 사람 플레이어. Player 오브젝트의 HumanBattleUnit을 넣으세요. " +
             "속할 팀은 HumanBattleUnit 쪽에서 선택합니다. 비워두면 AI끼리만 싸웁니다.")]
    public HumanBattleUnit humanPlayer;

    [Header("인원 구성")]
    [Tooltip("참가시킬 Blue AI 수. -1이면 blueAgents 목록 전체. " +
             "목록보다 적게 적으면 나머지 AI는 비활성화됩니다. " +
             "예) 사람이 Blue이고 3대3을 원하면 2")]
    public int blueAICount = -1;

    [Tooltip("참가시킬 Red AI 수. -1이면 redAgents 목록 전체.")]
    public int redAICount = -1;

    [Header("런타임 정보")]
    [SerializeField] private int _roundNumber;
    [SerializeField] private int _blueWins;
    [SerializeField] private int _redWins;
    [SerializeField] private int _draws;
    [SerializeField] private float _roundElapsed;

    public int RoundNumber { get { return _roundNumber; } }
    public int BlueWins { get { return _blueWins; } }
    public int RedWins { get { return _redWins; } }
    public int Draws { get { return _draws; } }
    public bool RoundActive { get { return _roundActive; } }

    /// <summary> 라운드 종료 이벤트 (승리 팀, 무승부면 null). UI 연결용. </summary>
    public event System.Action<AgentTeam?> OnRoundFinished;

    private readonly List<BattleUnit> _all = new List<BattleUnit>();
    private readonly List<BattleUnit> _blue = new List<BattleUnit>();
    private readonly List<BattleUnit> _red = new List<BattleUnit>();

    private bool _roundActive;
    private FieldInfo _fiBlueAlive, _fiRedAlive;

    // ── 초기화 ────────────────────────────────────────────

    private void Awake()
    {
        // Awake에서 처리하는 이유:
        // ML-Agents의 Agent.OnEnable()이 Start()보다 먼저 돌면서
        // Academy를 초기화하고 첫 AgentStep을 실행합니다.
        // manager 연결과 태그 보정은 그 전에 끝나야 합니다.
        if (observationSource == null)
            observationSource = GetComponent<TeamBattleManager>();

        if (observationSource != null)
        {
            observationSource.enabled = false;  // 학습 전용 로직(그룹 보상) 차단
            observationSource.mazeGenerator = mazeGenerator;
            // (신규) 사람 플레이어 초기화 (태그 / AgentHealth / 표식 PlayerAgent 연결)
            if (humanPlayer != null) humanPlayer.EnsureSetup();

            // (신규) 인원 구성 적용
            TrimRoster(blueAgents, blueAICount);
            TrimRoster(redAgents, redAICount);

            // (신규) 사람 플레이어를 AI의 적/아군 탐색 목록에 포함시킵니다.
            //        이게 없으면 AI가 플레이어에게 총을 쓰지 않습니다.
            //        원본 blueAgents/redAgents에는 넣지 않습니다.
            //        (SetupTeam이 사람을 AI로 오인해 Bind하지 않도록)
            var mgrBlue = new List<PlayerAgent>(blueAgents);
            var mgrRed = new List<PlayerAgent>(redAgents);
            if (humanPlayer != null && humanPlayer.MarkerAgent != null)
            {
                if (humanPlayer.team == AgentTeam.Blue) mgrBlue.Add(humanPlayer.MarkerAgent);
                else mgrRed.Add(humanPlayer.MarkerAgent);
            }
            observationSource.blueAgents = mgrBlue;
            observationSource.redAgents = mgrRed;
            observationSource.blueSpawns = blueSpawns;
            observationSource.redSpawns = redSpawns;
            if (mazeGenerator != null)
                observationSource.arenaSize = mazeGenerator.WorldSize;

            var t = typeof(TeamBattleManager);
            var bf = BindingFlags.NonPublic | BindingFlags.Instance;
            _fiBlueAlive = t.GetField("_blueAlive", bf);
            _fiRedAlive = t.GetField("_redAlive", bf);

            // [4] 라운드 길이를 학습과 동일하게
            if (roundTimeLimit <= 0f)
                roundTimeLimit = observationSource.maxEnvironmentSteps * Time.fixedDeltaTime;
        }

        SetupTeam(blueAgents, _blue, AgentTeam.Blue);
        SetupTeam(redAgents, _red, AgentTeam.Red);

        // (신규) 사람 플레이어를 팀 리스트에 등록
        if (humanPlayer != null)
        {
            if (humanPlayer.team == AgentTeam.Blue) _blue.Add(humanPlayer);
            else _red.Add(humanPlayer);
            _all.Add(humanPlayer);
        }

        // (신규) 최종 인원수에 맞춰 스폰 지점 확보
        EnsureSpawnCapacity();
    }

    private void SetupTeam(List<PlayerAgent> agents, List<BattleUnit> target, AgentTeam side)
    {
        foreach (var a in agents)
        {
            if (a == null) continue;

            // [2] manager 연결 - 관측 8개가 정상적으로 나오게 하는 핵심
            a.manager = observationSource;

            // [6] 관측 정규화에 쓰이는 값 동기화
            //     (TeamBattleManager.Setup()이 학습 때 하던 일과 동일)
            if (observationSource != null)
            {
                a.cohesionRadius = observationSource.cohesionRadius;
                a.cohesionMinDistance = observationSource.cohesionMinDistance;
                a.aimAngleThreshold = observationSource.aimAngleThreshold;
                a.noEnemyTimeLimit = observationSource.noEnemyTimeLimit;
                a.centerPenaltyMaxDistance = observationSource.centerRewardMaxDistance;
            }

            var unit = a.GetComponent<BattleUnit>();
            if (unit == null) unit = a.gameObject.AddComponent<BattleUnit>();
            unit.Bind(a, battleModel);

            target.Add(unit);
            _all.Add(unit);
        }
    }

    private void Start()
    {
        // [1] 팀 대칭 보정 - 태그 순서 뒤집기
        //     Initialize()에서 gameObject.tag가 팀별로 지정된 뒤에 실행해야 하므로
        //     Awake가 아닌 Start에서 처리합니다.
        if (makeTeamSymmetric) ApplyTeamSymmetry();

        if (_blue.Count == 0 || _red.Count == 0)
        {
            Debug.LogError("[BattleRunner] 양 진영에 최소 1명씩 필요합니다. " +
                           "(Blue=" + _blue.Count + ", Red=" + _red.Count + ")");
            enabled = false;
            return;
        }

        foreach (var u in _all) u.OnDied += HandleDied;

        StartRound();
    }

    /// <summary>
    /// [1] 한쪽 팀의 레이 센서 태그 순서를 뒤집어 관측을 팀 대칭으로 만듭니다.
    ///
    /// 원리:
    ///   기본 상태는 양 팀 모두 [BlueAgent, RedAgent, Wall]입니다.
    ///   Blue 입장에서는 [아군, 적, 벽]이지만
    ///   Red 입장에서는 [적, 아군, 벽]이 되어 의미가 반대입니다.
    ///
    ///   Red의 순서를 [RedAgent, BlueAgent, Wall]로 바꾸면
    ///   Red도 [아군, 적, 벽]이 되어 양 팀이 같은 의미 구조를 갖습니다.
    ///   그러면 모델 하나로 양 팀이 올바르게 동작합니다.
    ///
    /// 주의:
    ///   센서는 OnEnable 시점에 만들어지므로, 태그를 바꾼 뒤
    ///   센서를 강제로 재생성해야 반영됩니다.
    /// </summary>
    private void ApplyTeamSymmetry()
    {
        var flipTargets = flipRedInstead ? _red : _blue;
        int flipped = 0;

        foreach (var u in flipTargets)
        {
            var sensors = u.GetComponents<RayPerceptionSensorComponent3D>();
            foreach (var s in sensors)
            {
                var tags = s.DetectableTags;
                if (tags == null || tags.Count < 2) continue;

                // 앞의 두 태그(BlueAgent, RedAgent)를 서로 교환
                int iBlue = tags.IndexOf("BlueAgent");
                int iRed = tags.IndexOf("RedAgent");
                if (iBlue < 0 || iRed < 0) continue;

                string tmp = tags[iBlue];
                tags[iBlue] = tags[iRed];
                tags[iRed] = tmp;
                flipped++;
            }
        }

        if (logResults)
        {
            string who = flipRedInstead ? "Red" : "Blue";
            Debug.Log("[BattleRunner] 팀 대칭 보정: " + who + " 팀 센서 " + flipped +
                      "개의 태그 순서를 뒤집었습니다. " +
                      "이제 양 팀 모두 [아군, 적, 벽] 순서로 관측합니다.");
        }
    }

    // ── 라운드 진행 ────────────────────────────────────────

    public void StartRound()
    {
        _roundNumber++;
        _roundElapsed = 0f;

        // [5] 학습과 동일하게 매 에피소드 미로 재생성
        if (mazeGenerator != null && observationSource != null
            && observationSource.regenerateMazePerEpisode)
        {
            mazeGenerator.Generate();
        }
        else if (mazeGenerator != null)
        {
            mazeGenerator.Generate();
        }

        UpdateSpawnPositions();

        for (int i = 0; i < _blue.Count; i++)
            _blue[i].Respawn(SpawnOf(blueSpawns, i));
        for (int i = 0; i < _red.Count; i++)
            _red[i].Respawn(SpawnOf(redSpawns, i));

        _roundActive = true;

        if (logResults)
            Debug.Log("[Battle] 라운드 " + _roundNumber + " 시작 (Blue " +
                      _blue.Count + " vs Red " + _red.Count + ")");
    }

    private Transform SpawnOf(List<Transform> list, int i)
    {
        if (list == null || list.Count == 0) return null;
        return list[i % list.Count];
    }

    /// <summary>
    /// [5] 스폰 위치 갱신. 학습 때와 동일한 규칙을 씁니다.
    /// separateTeamsBySide는 매니저 설정을 그대로 따릅니다.
    /// </summary>
    private void UpdateSpawnPositions()
    {
        if (mazeGenerator == null) return;

        int w = mazeGenerator.Width;
        int d = mazeGenerator.Depth;
        float y = transform.position.y;

        bool separate = observationSource != null && observationSource.separateTeamsBySide;
        int minDist = observationSource != null ? observationSource.minSpawnCellDistance : 3;
        int clearR = observationSource != null ? observationSource.spawnClearRadius : 0;

        int need = blueSpawns.Count + redSpawns.Count;
        var picked = new List<Vector2Int>(need);
        int guard = 0, maxTries = Mathf.Max(200, need * 60);

        while (picked.Count < need && guard++ < maxTries)
        {
            Vector2Int c;
            if (separate)
            {
                bool blueSide = picked.Count < blueSpawns.Count;
                int hw = Mathf.Max(1, w / 2), hd = Mathf.Max(1, d / 2);
                c = blueSide
                    ? new Vector2Int(Random.Range(0, hw), Random.Range(0, hd))
                    : new Vector2Int(Random.Range(w - hw, w), Random.Range(d - hd, d));
            }
            else
            {
                c = new Vector2Int(Random.Range(0, w), Random.Range(0, d));
            }

            bool ok = true;
            for (int i = 0; i < picked.Count; i++)
                if (Mathf.Abs(picked[i].x - c.x) + Mathf.Abs(picked[i].y - c.y) < minDist)
                { ok = false; break; }

            if (ok) picked.Add(c);
        }
        while (picked.Count < need)
            picked.Add(new Vector2Int(Random.Range(0, w), Random.Range(0, d)));

        Vector3 center = GetArenaCenter();
        int idx = 0;
        for (int i = 0; i < blueSpawns.Count; i++) Place(blueSpawns[i], picked[idx++], y, clearR, center);
        for (int i = 0; i < redSpawns.Count; i++) Place(redSpawns[i], picked[idx++], y, clearR, center);
    }

    private void Place(Transform sp, Vector2Int cell, float y, int clearRadius, Vector3 center)
    {
        if (sp == null) return;

        if (clearRadius > 0)
            mazeGenerator.ClearAreaAroundCell(cell.x, cell.y, clearRadius);

        Vector3 p = mazeGenerator.GetCellWorldPosition(cell.x, cell.y);
        sp.position = new Vector3(p.x, y, p.z);

        Vector3 toCenter = center - sp.position;
        toCenter.y = 0f;
        sp.rotation = toCenter.sqrMagnitude > 0.01f
            ? Quaternion.LookRotation(toCenter.normalized, Vector3.up)
            : Quaternion.identity;
    }

    private void Update()
    {
        SyncAliveCounts();   // [2]

        if (!_roundActive) return;

        _roundElapsed += Time.deltaTime;
        if (roundTimeLimit > 0f && _roundElapsed >= roundTimeLimit)
            FinishRound(null);
    }

    /// <summary>
    /// [2] TeamBattleManager._blueAlive / _redAlive 를 실제 생존자 수로 갱신.
    /// GetAliveRatio()가 이 필드를 읽으므로, 관측 15번(aliveRatio)이 정상화됩니다.
    /// </summary>
    private void SyncAliveCounts()
    {
        if (observationSource == null) return;
        if (_fiBlueAlive == null || _fiRedAlive == null) return;

        _fiBlueAlive.SetValue(observationSource, CountAlive(_blue));
        _fiRedAlive.SetValue(observationSource, CountAlive(_red));
    }

    private int CountAlive(List<BattleUnit> team)
    {
        int n = 0;
        foreach (var u in team) if (u.IsAlive) n++;
        return n;
    }

    private void HandleDied(BattleUnit victim, BattleUnit killer)
    {
        if (!_roundActive) return;

        if (logResults)
            Debug.Log("[Battle] " + victim.name + " 사망 (by " +
                      (killer != null ? killer.name : "환경") + ")");

        int b = CountAlive(_blue), r = CountAlive(_red);
        if (b > 0 && r > 0) return;

        AgentTeam? winner = null;
        if (b > 0) winner = AgentTeam.Blue;
        else if (r > 0) winner = AgentTeam.Red;

        FinishRound(winner);
    }

    private void FinishRound(AgentTeam? winner)
    {
        if (!_roundActive) return;
        _roundActive = false;

        if (winner == AgentTeam.Blue) _blueWins++;
        else if (winner == AgentTeam.Red) _redWins++;
        else _draws++;

        foreach (var u in _all) u.StopMotion();

        if (logResults)
            Debug.Log("[Battle] 라운드 " + _roundNumber + " 종료 - " +
                      (winner.HasValue ? winner.Value + " 승리" : "무승부") +
                      " | 누적 Blue " + _blueWins + " : Red " + _redWins + " (무 " + _draws + ")");

        if (OnRoundFinished != null) OnRoundFinished(winner);

        StartCoroutine(RestartAfterDelay());
    }

    private IEnumerator RestartAfterDelay()
    {
        if (restartDelay > 0f) yield return new WaitForSeconds(restartDelay);
        StartRound();
    }

    public void ForceRestart()
    {
        StopAllCoroutines();
        _roundActive = false;
        StartRound();
    }

    public Vector3 GetArenaCenter()
    {
        if (mazeGenerator == null) return transform.position;
        Vector3 c0 = mazeGenerator.GetCellWorldPosition(0, 0);
        Vector3 c1 = mazeGenerator.GetCellWorldPosition(
            mazeGenerator.Width - 1, mazeGenerator.Depth - 1);
        return (c0 + c1) * 0.5f;
    }

    /// <summary>
    /// (신규) 인원 구성 적용.
    /// count가 0 이상이면 그 수만큼만 남기고 나머지 AI는 비활성화합니다.
    /// -1이면 목록 전체를 그대로 씁니다.
    ///
    ///   예) 사람이 Blue일 때 3대3  -> blueAICount=2, redAICount=3
    ///        사람이 Blue일 때 1대3  -> blueAICount=0, redAICount=3
    ///        사람이 Blue일 때 4대3  -> blueAICount=3, redAICount=3
    /// </summary>
    private void TrimRoster(List<PlayerAgent> list, int count)
    {
        if (list == null || count < 0) return;
        if (count > list.Count) count = list.Count;

        for (int i = list.Count - 1; i >= count; i--)
        {
            var a = list[i];
            if (a != null) a.gameObject.SetActive(false);
            list.RemoveAt(i);
        }
    }

    /// <summary>
    /// (신규) 팀 인원수만큼 스폰 지점을 확보합니다.
    /// 사람 플레이어가 추가되면 기존 스폰 수로는 모자라 두 유닛이 같은 칸에 겹칩니다.
    /// 리스트 인스턴스를 그대로 쓰므로 매니저 쪽 참조도 같이 갱신됩니다.
    /// </summary>
    private void EnsureSpawnCapacity()
    {
        EnsureSpawnList(blueSpawns, _blue.Count, "BlueSpawn_auto_");
        EnsureSpawnList(redSpawns, _red.Count, "RedSpawn_auto_");
    }

    private void EnsureSpawnList(List<Transform> list, int need, string prefix)
    {
        if (list == null) return;
        while (list.Count < need)
        {
            var go = new GameObject(prefix + list.Count);
            go.transform.SetParent(transform, false);
            list.Add(go.transform);
        }
    }

    private void OnDestroy()
    {
        foreach (var u in _all) if (u != null) u.OnDied -= HandleDied;
    }
}
