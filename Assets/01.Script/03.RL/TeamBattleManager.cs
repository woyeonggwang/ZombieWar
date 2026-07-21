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

    [Header("Agents (3 vs 3)")]
    public List<PlayerAgent> blueAgents = new List<PlayerAgent>();
    public List<PlayerAgent> redAgents = new List<PlayerAgent>();

    [Header("Spawn Points")]
    public List<Transform> blueSpawns = new List<Transform>();
    public List<Transform> redSpawns = new List<Transform>();

    [Header("Rewards")]
    public float winReward = 1f;
    public float loseReward = -1f;
    public float killReward = 1f;
    public float teamKillPenalty = -1f;
    public float deathPenalty = -0.5f;

    private SimpleMultiAgentGroup _blueGroup;
    private SimpleMultiAgentGroup _redGroup;
    private int _stepCount;
    private int _blueAlive;
    private int _redAlive;
    private bool _mazeBuilt;

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
        var hp = agent.GetComponent<AgentHealth>();
        hp.OnDeath += HandleDeath;
    }

    private void FixedUpdate()
    {
        _stepCount++;

        // 시간 초과 -> 무승부 (보상 없이 에피소드 중단)
        if (maxEnvironmentSteps > 0 && _stepCount >= maxEnvironmentSteps)
        {
            _blueGroup.GroupEpisodeInterrupted();
            _redGroup.GroupEpisodeInterrupted();
            ResetScene();
            return;
        }

        // 빠른 승부를 유도하는 미세한 시간 패널티
        float timePenalty = -0.5f / Mathf.Max(1, maxEnvironmentSteps);
        _blueGroup.AddGroupReward(timePenalty);
        _redGroup.AddGroupReward(timePenalty);
    }

    private void HandleDeath(AgentHealth victim, PlayerAgent killer)
    {
        PlayerAgent victimAgent = victim.owner;
        if (victimAgent == null) return;

        victimAgent.AddReward(deathPenalty);
        if (killer != null)
        {
            killer.AddReward(killer.team != victimAgent.team ? killReward : teamKillPenalty);
        }

        victimAgent.gameObject.SetActive(false);

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

            _blueGroup.EndGroupEpisode();
            _redGroup.EndGroupEpisode();
            ResetScene();
        }
    }

    public void ResetScene()
    {
        _stepCount = 0;

        // 미로 생성/재생성 + 스폰 위치 갱신
        if (mazeGenerator != null)
        {
            if (regenerateMazePerEpisode || !_mazeBuilt)
            {
                mazeGenerator.Generate();
                _mazeBuilt = true;
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
    private void UpdateSpawnPositions()
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
}
