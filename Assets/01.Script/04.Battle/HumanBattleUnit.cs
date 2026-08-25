using UnityEngine;

/// <summary>
/// 사람 플레이어를 전투에 참가시키는 컴포넌트 (신규 작성).
/// Player 오브젝트에 붙입니다.
///
/// ─────────────────────────────────────────────────────────
/// 어떻게 AI가 사람 플레이어를 적으로 인식하는가
/// ─────────────────────────────────────────────────────────
/// PlayerAgent는 적을 오직 manager.blueAgents / manager.redAgents
/// (둘 다 List&lt;PlayerAgent&gt;)에서만 찾습니다.
/// 태그만 붙여서는 레이 센서에만 잡히고, 조준·발사 판단에는 전혀 반영되지 않습니다.
/// 즉 AI가 플레이어에게 총을 쏘지 않습니다.
///
/// 그런데 그 탐색 코드가 각 PlayerAgent에서 실제로 읽는 것은 세 가지뿐입니다.
///   - e.gameObject.activeInHierarchy
///   - e.health (AgentHealth)
///   - e.transform.position
///
/// 따라서 Player 오브젝트에 "비활성화된" PlayerAgent 컴포넌트를 표식으로 달아두고
/// 그것을 manager의 팀 목록에 넣으면, 조작에는 전혀 간섭하지 않으면서
/// AI가 사람 플레이어를 정상적인 적으로 취급합니다.
/// PlayerAgent.cs는 한 줄도 수정하지 않습니다.
///
/// ※ 표식 PlayerAgent는 반드시 비활성(enabled=false) 상태여야 합니다.
///    활성화되면 OnActionReceived가 Rigidbody 속도를 덮어써서
///    사람의 조작을 매 결정마다 지워버립니다.
/// </summary>
[DisallowMultipleComponent]
public class HumanBattleUnit : BattleUnit
{
    [Header("팀 (자유롭게 변경 가능)")]
    [Tooltip("플레이어가 속할 팀. 태그와 표식 PlayerAgent의 team이 여기에 맞춰 자동 설정됩니다.")]
    public AgentTeam team = AgentTeam.Blue;

    [Header("사망 시 처리")]
    [Tooltip("사망 시 끌 조작 스크립트. 비워두면 PlayerSystem / PlayerInput을 자동으로 찾습니다.")]
    public MonoBehaviour[] controlScripts;

    [Tooltip("사망 시 숨길 몸체. 비워두면 자식 중 'Player'를 자동으로 찾습니다. " +
             "카메라가 다른 자식에 있으므로 루트를 끄지 않고 몸체만 숨깁니다.")]
    public GameObject modelRoot;

    [Tooltip("사망 시 시체가 총알을 막지 않도록 콜라이더를 끕니다.")]
    public bool disableColliderOnDeath = true;

    private PlayerAgent _marker;
    private Collider _col;
    private bool _setupDone;

    /// <summary> AI 팀 목록에 넣을 표식용 PlayerAgent (항상 비활성) </summary>
    public PlayerAgent MarkerAgent { get { return _marker; } }

    private void Awake()
    {
        EnsureSetup();
    }

    /// <summary>
    /// 초기화. BattleRunner.Awake에서도 한 번 더 호출되므로 몇 번 불려도 안전합니다.
    /// </summary>
    public void EnsureSetup()
    {
        if (_setupDone) return;
        _setupDone = true;

        gameObject.tag = (team == AgentTeam.Blue) ? "BlueAgent" : "RedAgent";

        var health = GetComponent<AgentHealth>();
        if (health == null)
        {
            Debug.LogError("[HumanBattleUnit] " + name + " 에 AgentHealth가 없습니다. " +
                           "Tools/ML Debug/플레이어 전투 셋업 을 실행하세요.");
            return;
        }

        _marker = GetComponent<PlayerAgent>();
        if (_marker == null)
        {
            Debug.LogError("[HumanBattleUnit] " + name + " 에 표식용 PlayerAgent가 없습니다. " +
                           "Tools/ML Debug/플레이어 전투 셋업 을 실행하세요. " +
                           "(런타임에 AddComponent하면 ML-Agents가 즉시 초기화되어 위험하므로 " +
                           " 에디터에서 미리 비활성 상태로 붙여야 합니다)");
            return;
        }

        // 절대 활성화하지 않습니다. 활성화되면 조작을 덮어씁니다.
        _marker.enabled = false;
        _marker.team = team;
        _marker.health = health;   // AI의 적 탐색이 e.health.IsDead 를 읽습니다
        health.owner = _marker;    // 피격 시 공격자 보상 처리 경로

        if (controlScripts == null || controlScripts.Length == 0)
            controlScripts = AutoFindControlScripts();

        if (modelRoot == null)
        {
            var t = transform.Find("Player");
            if (t != null) modelRoot = t.gameObject;
        }

        _col = GetComponent<Collider>();

        BindHuman(team, _marker);
    }

    /// <summary> PlayerSystem / PlayerInput 을 이름으로 찾습니다(하드 의존 회피). </summary>
    private MonoBehaviour[] AutoFindControlScripts()
    {
        var all = GetComponents<MonoBehaviour>();
        var list = new System.Collections.Generic.List<MonoBehaviour>();
        foreach (var m in all)
        {
            if (m == null || m == this) continue;
            string n = m.GetType().Name;
            if (n == "PlayerSystem" || n == "PlayerInput") list.Add(m);
        }
        return list.ToArray();
    }

    // ── 사망 / 리스폰 ──────────────────────────────────────

    protected override void ApplyDeathState()
    {
        // 루트를 끄지 않습니다. 카메라가 자식(Cube)에 붙어 있어
        // 꺼버리면 화면이 사라집니다. 관전 상태로 남깁니다.
        SetControlEnabled(false);
        StopMotion();

        if (modelRoot != null) modelRoot.SetActive(false);
        if (disableColliderOnDeath && _col != null) _col.enabled = false;
    }

    protected override void ApplyRespawnState()
    {
        if (modelRoot != null) modelRoot.SetActive(true);
        if (_col != null) _col.enabled = true;
        SetControlEnabled(true);
    }

    private void SetControlEnabled(bool on)
    {
        if (controlScripts == null) return;
        foreach (var m in controlScripts)
            if (m != null) m.enabled = on;
    }

    /// <summary>
    /// 인스펙터에서 팀을 바꿨을 때 즉시 태그가 따라오도록 합니다(에디터 편의).
    /// </summary>
    private void OnValidate()
    {
        if (Application.isPlaying) return;
        string want = (team == AgentTeam.Blue) ? "BlueAgent" : "RedAgent";
        if (gameObject.tag != want)
        {
            try { gameObject.tag = want; } catch (UnityEngine.UnityException) { }
        }
        var pa = GetComponent<PlayerAgent>();
        if (pa != null)
        {
            pa.team = team;
            pa.enabled = false;
        }
    }
}
