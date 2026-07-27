using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// 전투 씬 크래시 원인 진단용 스크립트.
///
/// 목적:
///   Unity가 튕기면 Console 내용이 전부 사라져 원인을 알 수 없습니다.
///   이 스크립트는 매 프레임 상태를 파일에 즉시 flush 하므로
///   크래시가 나도 직전까지의 기록이 디스크에 남습니다.
///
/// 사용법:
///   Arena_0(또는 아무 오브젝트)에 붙이고 재생하면 됩니다.
///   로그는 프로젝트 폴더 바로 아래 battle_diag.log 로 생성됩니다.
///   (Assets 폴더 밖이라 Unity가 임포트하지 않아 안전합니다)
///
/// 안전장치:
///   총알 수가 bulletHardLimit을 넘으면 즉시 일시정지(timeScale=0)하고
///   원인 스냅샷을 기록합니다. 이걸로 크래시 자체를 막고 증거를 남깁니다.
///
/// 기존 스크립트는 전혀 수정하지 않습니다. 읽기만 합니다.
/// </summary>
public class BattleDiagnostics : MonoBehaviour
{
    [Header("기록 설정")]
    [Tooltip("몇 초마다 상태를 기록할지. 0.1 = 초당 10회")]
    public float logInterval = 0.1f;

    [Tooltip("로그 파일명. 프로젝트 폴더(Assets의 상위)에 생성됩니다.")]
    public string fileName = "battle_diag.log";

    [Header("안전장치")]
    [Tooltip("총알이 이 수를 넘으면 즉시 일시정지하고 스냅샷을 남깁니다. 0이면 비활성.")]
    public int bulletHardLimit = 400;

    [Tooltip("한계 도달 시 재생을 멈출지 여부. 끄면 기록만 하고 계속 진행합니다.")]
    public bool pauseOnLimit = true;

    [Header("추적 대상")]
    [Tooltip("체크 시 각 에이전트의 발사 횟수를 개별 추적합니다.")]
    public bool trackPerAgentFireCount = true;

    private string _path;
    private float _timer;
    private int _frameCount;
    private bool _halted;

    // 에이전트별 이전 탄약 수 - 탄약이 줄면 발사한 것으로 간주
    private readonly Dictionary<PlayerAgent, int> _prevAmmo = new Dictionary<PlayerAgent, int>();
    private readonly Dictionary<PlayerAgent, int> _fireCount = new Dictionary<PlayerAgent, int>();

    private PlayerAgent[] _agents;

    private void Awake()
    {
        // Assets 폴더 밖에 기록 (Unity가 임포트하지 않도록)
        string projectRoot = Directory.GetParent(Application.dataPath).FullName;
        _path = Path.Combine(projectRoot, fileName);

        var head = new StringBuilder();
        head.AppendLine("================================================");
        head.AppendLine("Battle Diagnostics  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        head.AppendLine("Unity " + Application.unityVersion);
        head.AppendLine("Scene: " + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name);
        head.AppendLine("fixedDeltaTime = " + Time.fixedDeltaTime
                        + "  (초당 " + (1f / Time.fixedDeltaTime).ToString("F0") + "회 FixedUpdate)");
        head.AppendLine("================================================");
        head.AppendLine("time | frame | bullets | muzzleFx | totalGO | monoMB | 비고");
        head.AppendLine("------------------------------------------------");

        // 새로 쓰기 (이전 실행 기록은 덮어씀)
        File.WriteAllText(_path, head.ToString(), Encoding.UTF8);

        Debug.Log("[Diag] 로그 파일: " + _path);
    }

    private void Start()
    {
        _agents = Object.FindObjectsByType<PlayerAgent>(FindObjectsSortMode.None);
        Write("에이전트 발견: " + _agents.Length + "명");

        foreach (var a in _agents)
        {
            var bp = a.GetComponent<Unity.MLAgents.Policies.BehaviorParameters>();
            string modelName = (bp != null && bp.Model != null) ? bp.Model.name : "NULL";
            string btype = bp != null ? bp.BehaviorType.ToString() : "?";

            Write("  " + a.name
                  + " | team=" + a.team
                  + " | model=" + modelName
                  + " | type=" + btype
                  + " | gunPrefabs=" + (a.gunPrefabs == null ? "NULL" : a.gunPrefabs.Length.ToString())
                  + " | gunPos=" + (a.gunPos == null ? "NULL" : a.gunPos.name)
                  + " | manager=" + (a.manager == null ? "NULL" : a.manager.name));

            _prevAmmo[a] = -1;
            _fireCount[a] = 0;
        }

        Write("--- 기록 시작 ---");
    }

    private void FixedUpdate()
    {
        if (_halted) return;

        _frameCount++;

        if (trackPerAgentFireCount) TrackFiring();

        // 총알 수는 매 FixedUpdate 확인 (폭증을 놓치지 않기 위해)
        int bullets = CountBullets();

        if (bulletHardLimit > 0 && bullets >= bulletHardLimit)
        {
            HaltAndDump(bullets);
            return;
        }
    }

    private void Update()
    {
        if (_halted) return;

        _timer += Time.unscaledDeltaTime;
        if (_timer < logInterval) return;
        _timer = 0f;

        WriteSnapshot("");
    }

    /// <summary>
    /// 탄약 감소를 감지해 발사 횟수를 셉니다.
    /// Gun.Fire()를 직접 후킹하지 않고 관측만 하므로 기존 코드에 영향이 없습니다.
    /// </summary>
    private void TrackFiring()
    {
        foreach (var a in _agents)
        {
            if (a == null) continue;

            var gun = a.gun;
            if (gun == null) continue;

            int now = gun.Bullet;
            int prev;
            if (!_prevAmmo.TryGetValue(a, out prev)) prev = -1;

            // 탄약이 줄었으면 그만큼 발사한 것
            if (prev >= 0 && now < prev)
            {
                _fireCount[a] = _fireCount[a] + (prev - now);
            }
            _prevAmmo[a] = now;
        }
    }

    private int CountBullets()
    {
        var arr = Object.FindObjectsByType<Bullet>(FindObjectsSortMode.None);
        return arr.Length;
    }

    private int CountMuzzleFx()
    {
        var arr = Object.FindObjectsByType<MuzzleFlashLifetime>(FindObjectsSortMode.None);
        return arr.Length;
    }

    private void WriteSnapshot(string note)
    {
        int bullets = CountBullets();
        int fx = CountMuzzleFx();
        int totalGO = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).Length;
        float monoMB = System.GC.GetTotalMemory(false) / (1024f * 1024f);

        var sb = new StringBuilder();
        sb.Append(Time.timeSinceLevelLoad.ToString("F2")).Append(" | ");
        sb.Append(_frameCount).Append(" | ");
        sb.Append(bullets).Append(" | ");
        sb.Append(fx).Append(" | ");
        sb.Append(totalGO).Append(" | ");
        sb.Append(monoMB.ToString("F1")).Append(" | ");

        if (trackPerAgentFireCount)
        {
            foreach (var a in _agents)
            {
                if (a == null) continue;
                int fc;
                _fireCount.TryGetValue(a, out fc);
                sb.Append(ShortName(a.name)).Append("=").Append(fc).Append(" ");
            }
        }

        if (!string.IsNullOrEmpty(note)) sb.Append(" ").Append(note);

        Write(sb.ToString());
    }

    private string ShortName(string n)
    {
        // "Agent_Blue_0" -> "B0"
        if (n.Contains("Blue")) return "B" + n.Substring(n.Length - 1);
        if (n.Contains("Red")) return "R" + n.Substring(n.Length - 1);
        return n;
    }

    /// <summary>
    /// 총알이 한계를 넘으면 즉시 멈추고 상세 스냅샷을 남깁니다.
    /// 이것이 작동하면 "총알 폭증"이 크래시 원인으로 확정됩니다.
    /// </summary>
    private void HaltAndDump(int bullets)
    {
        _halted = true;

        Write("");
        Write("################################################");
        Write("### 총알 한계 도달! 크래시 방지를 위해 중단 ###");
        Write("################################################");
        Write("총알 수 = " + bullets + " (한계 " + bulletHardLimit + ")");
        Write("경과 시간 = " + Time.timeSinceLevelLoad.ToString("F2") + "초");
        Write("FixedUpdate 프레임 = " + _frameCount);
        Write("");
        Write("--- 에이전트별 상태 ---");

        foreach (var a in _agents)
        {
            if (a == null) continue;

            var gun = a.gun;
            int fc;
            _fireCount.TryGetValue(a, out fc);

            var sb = new StringBuilder();
            sb.Append(a.name);
            sb.Append(" | 발사수=").Append(fc);
            sb.Append(" | active=").Append(a.gameObject.activeInHierarchy);

            if (gun == null)
            {
                sb.Append(" | gun=NULL");
            }
            else
            {
                sb.Append(" | gun=").Append(gun.name);
                sb.Append(" | 탄약=").Append(gun.Bullet).Append("/").Append(gun.clipValue);
                sb.Append(" | onReload=").Append(gun.onReload);
                sb.Append(" | canShot=").Append(gun.canShot);
                sb.Append(" | gunEnabled=").Append(gun.isActiveAndEnabled);
                sb.Append(" | lastFire=").Append(gun.lastFireTime.ToString("F2"));
            }

            Write(sb.ToString());
        }

        Write("");
        Write("--- 해석 가이드 ---");
        Write("canShot=True 인데 발사수가 매우 크면 -> ShotDelay 코루틴이 안 돌아 연사 제한 실패");
        Write("gunEnabled=False 이면 -> 비활성 오브젝트라 StartCoroutine 실패가 원인");
        Write("발사수가 정상(수십)인데 총알만 많으면 -> Bullet 파괴가 안 되는 것이 원인");
        Write("################################################");

        if (pauseOnLimit)
        {
            Time.timeScale = 0f;
            Debug.LogError("[Diag] 총알 " + bullets + "개 도달. 재생을 멈췄습니다. " +
                           "battle_diag.log 를 확인하세요.");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPaused = true;
#endif
        }
    }

    /// <summary>
    /// 파일에 즉시 기록하고 flush 합니다.
    /// 크래시가 나도 이 시점까지의 내용은 디스크에 남습니다.
    /// </summary>
    private void Write(string line)
    {
        try
        {
            using (var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (var sw = new StreamWriter(fs, Encoding.UTF8))
            {
                sw.WriteLine(line);
                sw.Flush();
                fs.Flush(true); // OS 버퍼까지 강제로 비움 - 크래시 대비 핵심
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning("[Diag] 기록 실패: " + e.Message);
        }
    }

    private void OnApplicationQuit()
    {
        Write("--- 정상 종료 ---");
    }

    private void OnDisable()
    {
        Write("--- OnDisable (재생 중지 또는 크래시) ---");
    }
}
