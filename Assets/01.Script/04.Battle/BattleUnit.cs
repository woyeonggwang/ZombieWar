using System;
using UnityEngine;
using Unity.MLAgents.Policies;

/// <summary>
/// 전투 씬에서 유닛 하나를 감싸는 컴포넌트.
///
/// 기존 PlayerAgent / AgentHealth는 전혀 수정하지 않고,
/// 이 컴포넌트가 리스폰과 사망 이벤트를 담당합니다.
///
/// AI 유닛은 Bind(), 사람 플레이어는 BindHuman()으로 초기화합니다.
/// 사람 플레이어용 세부 동작은 HumanBattleUnit이 상속해서 재정의합니다.
/// </summary>
[DisallowMultipleComponent]
public class BattleUnit : MonoBehaviour
{
    protected PlayerAgent _agent;
    protected AgentHealth _health;
    protected Rigidbody _rb;
    protected BehaviorParameters _bp;
    protected bool _bound;

    /// <summary> 사람 플레이어인가. true면 ML-Agents 관련 호출을 전부 건너뜁니다. </summary>
    protected bool _isHuman;
    protected AgentTeam _humanTeam = AgentTeam.Blue;

    /// <summary> 사망 이벤트 (죽은 유닛, 죽인 유닛 또는 null) </summary>
    public event Action<BattleUnit, BattleUnit> OnDied;

    public PlayerAgent Agent { get { return _agent; } }
    public AgentHealth Health { get { return _health; } }
    public bool IsHuman { get { return _isHuman; } }
    public bool IsBound { get { return _bound; } }

    public AgentTeam Team
    {
        get
        {
            if (_isHuman) return _humanTeam;
            return _agent != null ? _agent.team : AgentTeam.Blue;
        }
    }

    public virtual bool IsAlive
    {
        get { return _health != null && !_health.IsDead && gameObject.activeSelf; }
    }

    // ── 초기화 ────────────────────────────────────────────

    /// <summary>
    /// AI 유닛 초기화. BattleRunner가 호출합니다.
    /// </summary>
    public void Bind(PlayerAgent agent, Unity.InferenceEngine.ModelAsset model)
    {
        if (_bound) return;
        _bound = true;

        _agent = agent;
        _health = GetComponent<AgentHealth>();
        _rb = GetComponent<Rigidbody>();
        _bp = GetComponent<BehaviorParameters>();

        if (_bp != null)
        {
            if (model != null) _bp.Model = model;

            if (_bp.Model == null)
            {
                Debug.LogWarning("[" + name + "] 모델이 없어 Heuristic(키보드)으로 동작합니다.");
            }
            else
            {
                _bp.BehaviorType = BehaviorType.InferenceOnly;

                // InferenceDevice를 Burst(CPU)로 강제합니다.
                // ComputeShader(GPU)로 두면 현재 ML-Agents 버전이
                // Inference Engine의 GPU 텐서에 직접 인덱싱을 시도하다가
                // "Tensor data cannot be read from" 예외로 액션이 전달되지 않습니다.
                _bp.InferenceDevice = InferenceDevice.Burst;
            }
        }

        if (_health != null) _health.OnDeath += HandleDeath;
    }

    /// <summary>
    /// 사람 플레이어 초기화.
    ///
    /// markerAgent는 "비활성화된" PlayerAgent 컴포넌트입니다.
    /// AI들의 적 탐색 코드가 List&lt;PlayerAgent&gt;만 순회하고
    /// 그 안에서 transform.position / health / activeInHierarchy 만 읽기 때문에,
    /// 비활성 컴포넌트를 표식으로 넣어 두면 조작에 전혀 간섭하지 않으면서도
    /// AI가 사람 플레이어를 정상적으로 적으로 인식합니다.
    /// </summary>
    public void BindHuman(AgentTeam team, PlayerAgent markerAgent)
    {
        if (_bound) return;
        _bound = true;

        _isHuman = true;
        _humanTeam = team;
        _agent = markerAgent;
        _health = GetComponent<AgentHealth>();
        _rb = GetComponent<Rigidbody>();

        if (_health != null) _health.OnDeath += HandleDeath;
    }

    protected virtual void OnDestroy()
    {
        if (_health != null) _health.OnDeath -= HandleDeath;
    }

    // ── 사망 / 리스폰 ──────────────────────────────────────

    private void HandleDeath(AgentHealth victim, PlayerAgent killer)
    {
        BattleUnit killerUnit = null;
        if (killer != null) killerUnit = killer.GetComponent<BattleUnit>();

        ApplyDeathState();

        if (OnDied != null) OnDied(this, killerUnit);
    }

    /// <summary>
    /// 사망 시 표현. AI는 오브젝트를 꺼버립니다.
    /// 사람 플레이어는 카메라가 자식에 있어 끄면 화면이 사라지므로
    /// HumanBattleUnit이 이 동작을 재정의합니다.
    /// </summary>
    protected virtual void ApplyDeathState()
    {
        gameObject.SetActive(false);
    }

    /// <summary>
    /// 라운드 시작 시 호출. 지정 위치로 부활시키고 상태를 초기화합니다.
    /// </summary>
    public void Respawn(Transform spawn)
    {
        gameObject.SetActive(true);

        StopMotion();

        if (spawn != null)
            transform.SetPositionAndRotation(spawn.position, spawn.rotation);

        if (_health != null) _health.ResetHealth();

        if (!_isHuman && _agent != null)
        {
            // 무기 재지급 + 내부 타이머 초기화.
            _agent.ResetAgentState();

            // 학습 때와 동일한 에피소드 경계를 만듭니다.
            _agent.EndEpisode();
        }

        ApplyRespawnState();
    }

    /// <summary> 리스폰 시 추가 처리. 사람 플레이어가 입력/모델을 되살릴 때 씁니다. </summary>
    protected virtual void ApplyRespawnState() { }

    /// <summary> 물리 속도를 0으로. 라운드 종료 및 리스폰 시 사용. </summary>
    public void StopMotion()
    {
        if (_rb == null) return;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }
}
