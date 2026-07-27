using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;

/// <summary>
/// 정책이 실제로 내놓는 액션 값을 측정하는 진단 도구.
///
/// 목적:
///   "학습 때는 잘 싸웠는데 모델을 넣으면 이상하다"의 원인을
///   추측이 아니라 수치로 확정합니다.
///
/// 측정 항목:
///   - ContinuousActions[0..2] (moveX, moveZ, rot)의 실제 값
///   - 값의 부호가 얼마나 자주 뒤집히는지 (총구 떨림 = rot 부호 반전 빈도)
///   - DiscreteActions[0] (발사) 비율
///   - 관측 24개의 실제 범위
///
/// 해석:
///   rot이 매 결정마다 +1/-1로 튀면 -> 정책이 극단값만 출력 (포화)
///   rot이 0 근처에서만 미세 진동하면 -> 정책이 무기력 (출력 소실)
///   값 자체는 정상인데 행동이 이상하면 -> 적용 방식 문제
///
/// 사용법:
///   Arena_0에 붙이고 재생. 로그는 프로젝트 루트의 action_diag.log
/// </summary>
public class ActionDiagnostics : MonoBehaviour
{
    [Tooltip("로그 파일명 (프로젝트 루트에 생성)")]
    public string fileName = "action_diag.log";

    [Tooltip("몇 초마다 요약 통계를 기록할지")]
    public float summaryInterval = 2f;

    [Tooltip("처음 N번의 결정은 매번 상세 기록")]
    public int detailDecisions = 60;

    private string _path;
    private PlayerAgent[] _agents;
    private float _timer;
    private int _decisionCount;

    // 에이전트별 통계
    private class Stat
    {
        public float minX = 999f, maxX = -999f, sumAbsX;
        public float minZ = 999f, maxZ = -999f, sumAbsZ;
        public float minR = 999f, maxR = -999f, sumAbsR;
        public int samples;
        public int fireCount;
        public int rotSignFlips;
        public float lastRot;
        public int saturatedRot;   // |rot| > 0.95 인 횟수
        public int nearZeroRot;    // |rot| < 0.05 인 횟수
    }
    private Dictionary<PlayerAgent, Stat> _stats = new Dictionary<PlayerAgent, Stat>();

    private void Awake()
    {
        string root = Directory.GetParent(Application.dataPath).FullName;
        _path = Path.Combine(root, fileName);

        var head = new StringBuilder();
        head.AppendLine("========================================================");
        head.AppendLine("Action Diagnostics  " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        head.AppendLine("========================================================");
        head.AppendLine("측정: 정책이 내놓는 실제 액션 값");
        head.AppendLine("  cont[0]=moveX(좌우), cont[1]=moveZ(전후), cont[2]=rot(회전)");
        head.AppendLine("  disc[0]=발사");
        head.AppendLine("--------------------------------------------------------");
        File.WriteAllText(_path, head.ToString(), Encoding.UTF8);
    }

    private void Start()
    {
        _agents = Object.FindObjectsByType<PlayerAgent>(FindObjectsSortMode.None);

        W("에이전트 " + _agents.Length + "명");
        foreach (var a in _agents)
        {
            var bp = a.GetComponent<BehaviorParameters>();
            var dr = a.GetComponent<DecisionRequester>();
            W("  " + a.name
              + " | team=" + a.team
              + " | model=" + (bp.Model == null ? "NULL" : bp.Model.name)
              + " | type=" + bp.BehaviorType
              + " | device=" + bp.InferenceDevice
              + " | decisionPeriod=" + (dr == null ? "?" : dr.DecisionPeriod.ToString())
              + " | betweenDecisions=" + (dr == null ? "?" : dr.TakeActionsBetweenDecisions.ToString()));
            _stats[a] = new Stat();
        }
        W("--- 측정 시작 ---");
        W("");
    }

    private void FixedUpdate()
    {
        if (_agents == null) return;

        _decisionCount++;
        bool detail = _decisionCount <= detailDecisions;

        StringBuilder line = detail ? new StringBuilder() : null;
        if (detail) line.Append("[step ").Append(_decisionCount).Append("] ");

        foreach (var a in _agents)
        {
            if (a == null || !a.gameObject.activeInHierarchy) continue;

            // Agent가 마지막으로 받은 액션을 읽음
            var buf = GetActionBuffers(a);
            if (buf == null) continue;

            var cont = buf.Value.ContinuousActions;
            var disc = buf.Value.DiscreteActions;
            if (cont.Length < 3) continue;

            float mx = cont[0], mz = cont[1], rt = cont[2];
            int fire = disc.Length > 0 ? disc[0] : 0;

            var s = _stats[a];
            s.samples++;
            s.minX = Mathf.Min(s.minX, mx); s.maxX = Mathf.Max(s.maxX, mx); s.sumAbsX += Mathf.Abs(mx);
            s.minZ = Mathf.Min(s.minZ, mz); s.maxZ = Mathf.Max(s.maxZ, mz); s.sumAbsZ += Mathf.Abs(mz);
            s.minR = Mathf.Min(s.minR, rt); s.maxR = Mathf.Max(s.maxR, rt); s.sumAbsR += Mathf.Abs(rt);
            if (fire == 1) s.fireCount++;

            // 총구 떨림 측정: rot 부호 반전 횟수
            if (s.samples > 1 && Mathf.Sign(rt) != Mathf.Sign(s.lastRot)
                && Mathf.Abs(rt) > 0.05f && Mathf.Abs(s.lastRot) > 0.05f)
                s.rotSignFlips++;
            s.lastRot = rt;

            if (Mathf.Abs(rt) > 0.95f) s.saturatedRot++;
            if (Mathf.Abs(rt) < 0.05f) s.nearZeroRot++;

            if (detail)
            {
                line.Append(Short(a.name)).Append("(")
                    .Append(mx.ToString("F2")).Append(",")
                    .Append(mz.ToString("F2")).Append(",")
                    .Append(rt.ToString("F2")).Append(",f")
                    .Append(fire).Append(") ");
            }
        }

        if (detail) W(line.ToString());

        _timer += Time.fixedDeltaTime;
        if (_timer >= summaryInterval)
        {
            _timer = 0f;
            WriteSummary();
        }
    }

    /// <summary>
    /// Agent가 마지막으로 받은 ActionBuffers를 리플렉션으로 읽습니다.
    /// (기존 스크립트를 수정하지 않기 위함)
    /// </summary>
    private ActionBuffers? GetActionBuffers(Agent a)
    {
        var t = typeof(Agent);
        var bf = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

        var f = t.GetField("m_ActuatorManager", bf);
        if (f == null) return null;
        var am = f.GetValue(a);
        if (am == null) return null;

        var pm = am.GetType().GetProperty("StoredActions",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        if (pm == null) return null;

        object v = pm.GetValue(am);
        if (v == null) return null;
        return (ActionBuffers)v;
    }

    private void WriteSummary()
    {
        W("");
        W("=== 요약 (t=" + Time.timeSinceLevelLoad.ToString("F1") + "초) ===");
        foreach (var a in _agents)
        {
            if (a == null) continue;
            Stat s;
            if (!_stats.TryGetValue(a, out s) || s.samples == 0) continue;

            W(Short(a.name)
              + " | n=" + s.samples
              + " | moveX[" + s.minX.ToString("F2") + "~" + s.maxX.ToString("F2")
                  + "] avg|x|=" + (s.sumAbsX / s.samples).ToString("F2")
              + " | moveZ[" + s.minZ.ToString("F2") + "~" + s.maxZ.ToString("F2")
                  + "] avg|z|=" + (s.sumAbsZ / s.samples).ToString("F2")
              + " | rot[" + s.minR.ToString("F2") + "~" + s.maxR.ToString("F2")
                  + "] avg|r|=" + (s.sumAbsR / s.samples).ToString("F2"));
            W("     발사율=" + (100f * s.fireCount / s.samples).ToString("F0") + "%"
              + " | rot부호반전=" + s.rotSignFlips + "회 (" + (100f * s.rotSignFlips / s.samples).ToString("F0") + "%)"
              + " | rot포화(|r|>0.95)=" + (100f * s.saturatedRot / s.samples).ToString("F0") + "%"
              + " | rot거의0(|r|<0.05)=" + (100f * s.nearZeroRot / s.samples).ToString("F0") + "%");
        }
        W("");
        W("[해석]");
        W("  rot부호반전 > 30%  -> 총구 떨림 확정. 정책 출력이 진동 중.");
        W("  rot포화 > 70%      -> 정책이 극단값만 출력. 관측이 학습분포 밖일 가능성.");
        W("  rot거의0 > 70%     -> 정책 무기력. 모델 로딩/관측 문제.");
        W("  발사율 > 80%       -> 무차별 난사. 조준 판단이 작동하지 않음.");
        W("");
    }

    private string Short(string n)
    {
        if (n.Contains("Blue")) return "B" + n.Substring(n.Length - 1);
        if (n.Contains("Red")) return "R" + n.Substring(n.Length - 1);
        return n;
    }

    private void W(string line)
    {
        try
        {
            using (var fs = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite))
            using (var sw = new StreamWriter(fs, Encoding.UTF8))
            {
                sw.WriteLine(line);
                sw.Flush();
                fs.Flush(true);
            }
        }
        catch { }
    }

    private void OnDisable() { W("--- 측정 종료 ---"); }
}
