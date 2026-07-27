using System;
using UnityEngine;
using Unity.MLAgents.Policies;

/// <summary>
/// 전투 씬에서 에이전트 하나를 감싸는 컴포넌트 (신규 작성).
///
/// 기존 PlayerAgent / AgentHealth는 전혀 수정하지 않고,
/// 이 컴포넌트가 리스폰과 사망 이벤트를 담당합니다.
///
/// 사람 플레이어를 추가할 때는 이 클래스와 같은 인터페이스를 가진
/// HumanUnit을 만들어 BattleRunner의 리스트에 넣으면 됩니다.
/// (BattleRunner는 BattleUnit 타입만 알면 되므로,
///  HumanUnit이 BattleUnit을 상속하고 Respawn/StopMotion만
///  오버라이드하는 방식이 가장 간단합니다.)
/// </summary>
[DisallowMultipleComponent]
public class BattleUnit : MonoBehaviour
{
    private PlayerAgent _agent;
    private AgentHealth _health;
    private Rigidbody _rb;
    private BehaviorParameters _bp;
    private bool _bound;

    /// <summary> 사망 이벤트 (죽은 유닛, 죽인 유닛 또는 null) </summary>
    public event Action<BattleUnit, BattleUnit> OnDied;

    public PlayerAgent Agent { get { return _agent; } }
    public AgentTeam Team { get { return _agent != null ? _agent.team : AgentTeam.Blue; } }
    public bool IsAlive
    {
        get { return _health != null && !_health.IsDead && gameObject.activeSelf; }
    }

    /// <summary>
    /// BattleRunner가 호출. 참조를 캐싱하고 추론 모드를 설정합니다.
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

    private void OnDestroy()
    {
        if (_health != null) _health.OnDeath -= HandleDeath;
    }

    private void HandleDeath(AgentHealth victim, PlayerAgent killer)
    {
        BattleUnit killerUnit = null;
        if (killer != null) killerUnit = killer.GetComponent<BattleUnit>();

        gameObject.SetActive(false);

        if (OnDied != null) OnDied(this, killerUnit);
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

        _health.ResetHealth();

        // 무기 재지급 + 내부 타이머 초기화.
        // 학습용 함수지만 "리스폰 시 상태를 깨끗이 한다"는 목적이 동일하므로 재사용합니다.
        _agent.ResetAgentState();

        // 학습 때와 동일한 에피소드 경계를 만듭니다.
        // 학습 시 TeamBattleManager가 maxEnvironmentSteps마다 에피소드를 끊었고,
        // 정책은 항상 제한된 에피소드 안에서 행동하도록 학습되었습니다.
        // 이걸 호출하지 않으면 하나의 긴 에피소드가 무한히 이어져
        // 내부 스텝 카운터가 학습 분포를 벗어납니다.
        _agent.EndEpisode();
    }

    /// <summary> 물리 속도를 0으로. 라운드 종료 및 리스폰 시 사용. </summary>
    public void StopMotion()
    {
        if (_rb == null) return;
        _rb.linearVelocity = Vector3.zero;
        _rb.angularVelocity = Vector3.zero;
    }
}
