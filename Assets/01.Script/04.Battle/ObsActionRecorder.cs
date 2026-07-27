using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEngine;
using Unity.MLAgents;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Sensors;

/// <summary>
/// [진단용] ML-Agents가 실제로 정책에 넘기는 관측(89개)과 그 결과 액션을 CSV로 기록합니다.
///
/// 기존 학습 스크립트는 전혀 수정하지 않습니다.
/// Academy가 FixedUpdate에서 스텝을 돌리므로 WaitForFixedUpdate 코루틴으로
/// 물리 스텝 직후에 붙고, Agent 내부 sensors 리스트를 리플렉션으로 읽습니다.
/// (Academy.AgentAct 이벤트와 ObservationWriter.SetTarget은 internal이라 직접 못 씁니다)
///
/// 사용법:
///   1) 학습 씬(SampleScene)에 빈 GameObject를 만들고 이 스크립트를 붙입니다.
///   2) 그냥 재생하면 모드가 자동 판별됩니다.
///        Python(mlagents-learn --resume --inference) 연결됨 -> obs_PY.csv
///        서버 없이 .onnx 로 실행                          -> obs_ONNX.csv
///   3) 프로젝트 루트(D:\Unity\ZombieWar)에 csv와 _summary.txt가 생깁니다.
/// </summary>
public class ObsActionRecorder : MonoBehaviour
{
    [Header("대상 에이전트 선택")]
    [Tooltip("기록할 에이전트 수. 하이어라키 경로 오름차순으로 앞에서부터 고릅니다(두 모드에서 동일 대상 보장).")]
    public int maxAgents = 3;

    [Tooltip("비어 있지 않으면 하이어라키 경로에 이 문자열이 포함된 에이전트만 대상. 예: Arena_00")]
    public string pathFilter = "";

    [Header("기록량")]
    [Tooltip("에이전트 1명당 기록할 최대 행 수")]
    public int maxRowsPerAgent = 3000;

    [Tooltip("결정 주기(DecisionRequester)에 맞춰 기록. 끄면 매 Academy 스텝 기록")]
    public bool onlyOnDecisionSteps = true;

    [Tooltip("몇 행마다 디스크로 flush 할지")]
    public int flushEvery = 50;

    [Header("파일")]
    [Tooltip("비워두면 모드에 따라 obs_PY.csv / obs_ONNX.csv 로 자동 결정")]
    public string fileNameOverride = "";

    [Header("시간 스케일 강제 (ONNX 단독 실행 전용)")]
    [Tooltip("0보다 크면 재생 시 Time.timeScale을 이 값으로 고정합니다. 학습 시 timeScale(보통 20)과 맞춰 비교할 때 사용합니다. 0이면 건드리지 않습니다.")]
    public float forceTimeScale = 0f;

    // ---------------- 내부 ----------------

    private class Target
    {
        public Agent agent;
        public string path;
        public int decisionPeriod = 1;
        public int rows;

        public double[] sum;
        public float[] min;
        public float[] max;
        public int statCount;

        public double[] actSum;
        public double[] actAbsSum;
        public float[] actMin;
        public float[] actMax;
        public int[] actSat;
        public Dictionary<int, int> discHist = new Dictionary<int, int>();
    }

    private readonly List<Target> m_Targets = new List<Target>();
    private StreamWriter m_Writer;
    private string m_FilePath;
    private string m_Mode = "UNKNOWN";
    private int m_PendingFlush;
    private int m_ObsLen = -1;
    private string m_SensorHeader = "";
    private int m_LastAcadStep = -1;
    private bool m_Running;

    private static readonly BindingFlags kAny =
        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    private FieldInfo m_SensorsField;
    private MethodInfo m_GetActionsMethod;

    private object m_ObsWriter;          // ObservationWriter 인스턴스
    private MethodInfo m_SetTargetMethod; // internal SetTarget(IList<float>, ObservationSpec, int)

    private void Start()
    {
        if (!Academy.IsInitialized)
        {
            Debug.LogError("[ObsActionRecorder] Academy가 초기화되지 않았습니다. 에이전트가 있는 씬인지 확인하세요.");
            enabled = false;
            return;
        }

        m_Mode = Academy.Instance.IsCommunicatorOn ? "PY" : "ONNX";

        if (forceTimeScale > 0f)
        {
            Time.timeScale = forceTimeScale;
            Debug.Log("[ObsActionRecorder] Time.timeScale을 " + forceTimeScale + " 로 강제했습니다.");
        }

        if (!SetupReflection()) { enabled = false; return; }

        CollectTargets();
        if (m_Targets.Count == 0)
        {
            Debug.LogError("[ObsActionRecorder] 대상 Agent를 찾지 못했습니다. pathFilter를 확인하세요.");
            enabled = false;
            return;
        }

        OpenFile();

        m_Running = true;
        StartCoroutine(CaptureLoop());

        Debug.Log($"[ObsActionRecorder] 모드={m_Mode}  대상={m_Targets.Count}명  파일={m_FilePath}\n" +
                  $"  timeScale={Time.timeScale}  fixedDeltaTime={Time.fixedDeltaTime}");
    }

    private bool SetupReflection()
    {
        var agentType = typeof(Agent);
        m_SensorsField = agentType.GetField("sensors", kAny) ?? agentType.GetField("m_Sensors", kAny);
        if (m_SensorsField == null)
        {
            Debug.LogError("[ObsActionRecorder] Agent.sensors 필드를 찾지 못했습니다. ML-Agents 버전 확인 필요.");
            return false;
        }

        m_GetActionsMethod = agentType.GetMethod("GetStoredActionBuffers", kAny, null, Type.EmptyTypes, null);

        var writerType = typeof(ObservationWriter);
        m_ObsWriter = Activator.CreateInstance(writerType, true);

        foreach (var mi in writerType.GetMethods(kAny | BindingFlags.Static))
        {
            if (mi.Name != "SetTarget") continue;
            var ps = mi.GetParameters();
            if (ps.Length == 3 && typeof(IList<float>).IsAssignableFrom(ps[0].ParameterType))
            {
                m_SetTargetMethod = mi;
                break;
            }
        }
        if (m_SetTargetMethod == null)
        {
            Debug.LogError("[ObsActionRecorder] ObservationWriter.SetTarget(IList<float>,...) 을 찾지 못했습니다.");
            return false;
        }
        return true;
    }

    private void CollectTargets()
    {
        var all = UnityEngine.Object.FindObjectsByType<Agent>(FindObjectsSortMode.None);

        var withPath = new List<KeyValuePair<string, Agent>>();
        foreach (var a in all)
        {
            string p = HierarchyPath(a.transform);
            if (!string.IsNullOrEmpty(pathFilter) && p.IndexOf(pathFilter, StringComparison.Ordinal) < 0)
                continue;
            withPath.Add(new KeyValuePair<string, Agent>(p, a));
        }
        withPath.Sort((x, y) => string.CompareOrdinal(x.Key, y.Key));

        int n = Mathf.Min(maxAgents, withPath.Count);
        for (int i = 0; i < n; i++)
        {
            var t = new Target { agent = withPath[i].Value, path = withPath[i].Key };
            var dr = t.agent.GetComponent<DecisionRequester>();
            if (dr != null) t.decisionPeriod = Mathf.Max(1, dr.DecisionPeriod);
            m_Targets.Add(t);
        }
    }

    private static string HierarchyPath(Transform t)
    {
        var sb = new StringBuilder(t.name);
        var p = t.parent;
        while (p != null)
        {
            sb.Insert(0, p.name + "/");
            p = p.parent;
        }
        return sb.ToString();
    }

    private void OpenFile()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string name = string.IsNullOrEmpty(fileNameOverride) ? $"obs_{m_Mode}.csv" : fileNameOverride;
        m_FilePath = Path.Combine(root, name);
        m_Writer = new StreamWriter(m_FilePath, false, Encoding.UTF8) { AutoFlush = false };
    }

    // ---------------- 캡처 ----------------

    /// <summary>
    /// Academy는 FixedUpdate에서 EnvironmentStep을 돌립니다.
    /// WaitForFixedUpdate는 그 프레임의 모든 FixedUpdate 이후에 재개되므로
    /// 이 시점의 센서 버퍼와 저장된 액션이 방금 결정에 쓰인 값입니다.
    /// </summary>
    private IEnumerator CaptureLoop()
    {
        var wait = new WaitForFixedUpdate();
        while (m_Running)
        {
            yield return wait;
            Capture();
        }
    }

    private void Capture()
    {
        if (m_Writer == null || !Academy.IsInitialized) return;

        int acadStep = Academy.Instance.StepCount;
        if (acadStep == m_LastAcadStep) return;   // 스텝이 진행되지 않았으면 중복 기록 방지
        m_LastAcadStep = acadStep;

        for (int i = 0; i < m_Targets.Count; i++)
        {
            var t = m_Targets[i];
            if (t.agent == null || t.rows >= maxRowsPerAgent) continue;
            if (onlyOnDecisionSteps && (acadStep % t.decisionPeriod) != 0) continue;

            var obs = ReadObservations(t.agent);
            if (obs == null || obs.Length == 0) continue;

            var act = ReadActions(t.agent);

            if (m_ObsLen < 0)
            {
                m_ObsLen = obs.Length;
                WriteHeader(t.agent, obs.Length, act);
            }
            if (obs.Length != m_ObsLen) continue;

            AccumulateStats(t, obs, act);
            WriteRow(t, i, acadStep, obs, act);
            t.rows++;
        }

        if (m_PendingFlush >= flushEvery)
        {
            m_Writer.Flush();
            m_PendingFlush = 0;
        }
    }

    /// <summary>Agent 내부 sensors 리스트(정렬 완료 상태)를 그대로 읽어 평탄화합니다.</summary>
    private float[] ReadObservations(Agent agent)
    {
        var sensors = m_SensorsField.GetValue(agent) as List<ISensor>;
        if (sensors == null || sensors.Count == 0) return null;

        int total = 0;
        for (int s = 0; s < sensors.Count; s++) total += SpecLength(sensors[s]);
        if (total <= 0) return null;

        var buf = new float[total];
        var args = new object[3];
        int offset = 0;

        for (int s = 0; s < sensors.Count; s++)
        {
            var sensor = sensors[s];
            int len = SpecLength(sensor);
            if (len <= 0) continue;
            try
            {
                args[0] = buf;
                args[1] = sensor.GetObservationSpec();
                args[2] = offset;
                m_SetTargetMethod.Invoke(m_ObsWriter, args);
                sensor.Write((ObservationWriter)m_ObsWriter);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ObsActionRecorder] 센서 {sensor.GetName()} 읽기 실패: {e.GetType().Name} {e.Message}");
            }
            offset += len;
        }
        return buf;
    }

    private static int SpecLength(ISensor sensor)
    {
        var shape = sensor.GetObservationSpec().Shape;
        int len = 1;
        for (int i = 0; i < shape.Length; i++) len *= shape[i];
        return len;
    }

    private ActionBuffers ReadActions(Agent agent)
    {
        if (m_GetActionsMethod == null) return ActionBuffers.Empty;
        try { return (ActionBuffers)m_GetActionsMethod.Invoke(agent, null); }
        catch { return ActionBuffers.Empty; }
    }

    // ---------------- 출력 ----------------

    private void WriteHeader(Agent agent, int obsLen, ActionBuffers act)
    {
        var sensors = m_SensorsField.GetValue(agent) as List<ISensor>;
        var sb = new StringBuilder();
        sb.Append("# mode=").Append(m_Mode);
        sb.Append(" timeScale=").Append(Time.timeScale.ToString(CultureInfo.InvariantCulture));
        sb.Append(" fixedDeltaTime=").Append(Time.fixedDeltaTime.ToString(CultureInfo.InvariantCulture));
        sb.Append(" obsLen=").Append(obsLen);
        sb.AppendLine();

        var hdr = new StringBuilder();
        int off = 0;
        for (int s = 0; s < sensors.Count; s++)
        {
            int len = SpecLength(sensors[s]);
            if (s > 0) hdr.Append("|");
            hdr.Append(sensors[s].GetName()).Append(":").Append(off).Append("-").Append(off + len - 1).Append(":").Append(len);
            off += len;
        }
        m_SensorHeader = hdr.ToString();
        sb.Append("# sensors=").Append(m_SensorHeader).AppendLine();

        sb.Append("# agents=");
        for (int i = 0; i < m_Targets.Count; i++)
        {
            if (i > 0) sb.Append("|");
            sb.Append(i).Append("=").Append(m_Targets[i].path);
        }
        sb.AppendLine();

        sb.Append("mode,agentIdx,acadStep,agentStep,episodes");
        for (int c = 0; c < act.ContinuousActions.Length; c++) sb.Append(",c").Append(c);
        for (int d = 0; d < act.DiscreteActions.Length; d++) sb.Append(",d").Append(d);
        for (int o = 0; o < obsLen; o++) sb.Append(",o").Append(o);
        m_Writer.WriteLine(sb.ToString());

        Debug.Log($"[ObsActionRecorder] 센서 구성: {m_SensorHeader}\n" +
                  $"  총 관측 길이 = {obsLen} (기대값 89)\n" +
                  $"  연속 액션 {act.ContinuousActions.Length}개, 이산 액션 {act.DiscreteActions.Length}개");
    }

    private void WriteRow(Target t, int idx, int acadStep, float[] obs, ActionBuffers act)
    {
        var sb = new StringBuilder(1024);
        sb.Append(m_Mode).Append(',').Append(idx).Append(',').Append(acadStep).Append(',')
          .Append(t.agent.StepCount).Append(',').Append(t.agent.CompletedEpisodes);

        for (int c = 0; c < act.ContinuousActions.Length; c++)
            sb.Append(',').Append(act.ContinuousActions[c].ToString("G7", CultureInfo.InvariantCulture));
        for (int d = 0; d < act.DiscreteActions.Length; d++)
            sb.Append(',').Append(act.DiscreteActions[d]);
        for (int o = 0; o < obs.Length; o++)
            sb.Append(',').Append(obs[o].ToString("G7", CultureInfo.InvariantCulture));

        m_Writer.WriteLine(sb.ToString());
        m_PendingFlush++;
    }

    // ---------------- 통계 ----------------

    private void AccumulateStats(Target t, float[] obs, ActionBuffers act)
    {
        if (t.sum == null)
        {
            t.sum = new double[obs.Length];
            t.min = new float[obs.Length];
            t.max = new float[obs.Length];
            for (int i = 0; i < obs.Length; i++) { t.min[i] = float.MaxValue; t.max[i] = float.MinValue; }

            int ca = act.ContinuousActions.Length;
            t.actSum = new double[ca];
            t.actAbsSum = new double[ca];
            t.actMin = new float[ca];
            t.actMax = new float[ca];
            t.actSat = new int[ca];
            for (int i = 0; i < ca; i++) { t.actMin[i] = float.MaxValue; t.actMax[i] = float.MinValue; }
        }

        for (int i = 0; i < obs.Length; i++)
        {
            float v = obs[i];
            t.sum[i] += v;
            if (v < t.min[i]) t.min[i] = v;
            if (v > t.max[i]) t.max[i] = v;
        }
        t.statCount++;

        for (int i = 0; i < act.ContinuousActions.Length && i < t.actSum.Length; i++)
        {
            float v = act.ContinuousActions[i];
            t.actSum[i] += v;
            t.actAbsSum[i] += Mathf.Abs(v);
            if (v < t.actMin[i]) t.actMin[i] = v;
            if (v > t.actMax[i]) t.actMax[i] = v;
            if (Mathf.Abs(v) >= 0.95f) t.actSat[i]++;
        }

        for (int d = 0; d < act.DiscreteActions.Length; d++)
        {
            int key = d * 1000 + act.DiscreteActions[d];
            t.discHist.TryGetValue(key, out int c);
            t.discHist[key] = c + 1;
        }
    }

    private void WriteSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine("================ ObsActionRecorder 요약 ================");
        sb.AppendLine($"mode={m_Mode}  obsLen={m_ObsLen}");
        sb.AppendLine($"sensors={m_SensorHeader}");
        sb.AppendLine($"timeScale={Time.timeScale}  fixedDeltaTime={Time.fixedDeltaTime}");
        sb.AppendLine();

        foreach (var t in m_Targets)
        {
            if (t.statCount == 0 || t.sum == null) continue;
            sb.AppendLine($"--- agent [{t.path}]  rows={t.rows}  decisionPeriod={t.decisionPeriod} ---");

            sb.AppendLine("[연속 액션]  dim  mean      meanAbs   min       max       포화율(|v|>=0.95)");
            for (int i = 0; i < t.actSum.Length; i++)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "             c{0}   {1,8:F4}  {2,8:F4}  {3,8:F4}  {4,8:F4}  {5,6:P1}",
                    i, t.actSum[i] / t.statCount, t.actAbsSum[i] / t.statCount,
                    t.actMin[i], t.actMax[i], (float)t.actSat[i] / t.statCount));
            }

            sb.AppendLine("[이산 액션] branch:value = count");
            foreach (var kv in t.discHist)
                sb.AppendLine($"             d{kv.Key / 1000}:{kv.Key % 1000} = {kv.Value}");

            sb.AppendLine("[관측] idx  mean      min       max");
            for (int i = 0; i < m_ObsLen; i++)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "       o{0,-3}  {1,8:F4}  {2,8:F4}  {3,8:F4}{4}",
                    i, t.sum[i] / t.statCount, t.min[i], t.max[i],
                    (Mathf.Approximately(t.min[i], t.max[i]) ? "   <-- 상수(전 구간 동일)" : "")));
            }
            sb.AppendLine();
        }
        sb.AppendLine("=======================================================");

        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string path = Path.Combine(root, $"obs_{m_Mode}_summary.txt");
        try { File.WriteAllText(path, sb.ToString(), Encoding.UTF8); }
        catch (Exception e) { Debug.LogWarning("[ObsActionRecorder] 요약 저장 실패: " + e.Message); }

        Debug.Log($"[ObsActionRecorder] 요약 저장: {path}\n{sb}");
    }

    // ---------------- 정리 ----------------

    private void Close()
    {
        m_Running = false;
        if (m_Writer != null)
        {
            try { m_Writer.Flush(); m_Writer.Close(); } catch { }
            m_Writer = null;

            if (m_ObsLen > 0) WriteSummary();
            Debug.Log($"[ObsActionRecorder] 기록 종료: {m_FilePath}");
        }
    }

    private void OnDisable() { Close(); }
    private void OnApplicationQuit() { Close(); }
}
