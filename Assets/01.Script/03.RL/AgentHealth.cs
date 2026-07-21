using System;
using UnityEngine;

/// <summary>
/// RL 에이전트의 체력 관리 컴포넌트.
/// Bullet이 이 컴포넌트를 감지하면 데미지를 적용합니다.
/// </summary>
public class AgentHealth : MonoBehaviour
{
    public float maxHp = 100f;

    public float CurrentHp { get; private set; }
    public bool IsDead { get; private set; }

    [HideInInspector] public PlayerAgent owner;

    /// <summary> (희생자, 킬러) 형태로 사망을 알림. 킬러는 null일 수 있음. </summary>
    public event Action<AgentHealth, PlayerAgent> OnDeath;

    private void Awake()
    {
        ResetHealth();
    }

    public void ResetHealth()
    {
        CurrentHp = maxHp;
        IsDead = false;
    }

    public void TakeDamage(float amount, PlayerAgent attacker)
    {
        if (IsDead) return;

        CurrentHp -= amount;

        // 공격자에게 피격 보상 알림 (아군/적군 구분은 PlayerAgent가 처리)
        if (attacker != null && owner != null)
        {
            attacker.NotifyDealtDamage(owner);
        }

        if (CurrentHp <= 0f)
        {
            CurrentHp = 0f;
            IsDead = true;
            OnDeath?.Invoke(this, attacker);
        }
    }
}
