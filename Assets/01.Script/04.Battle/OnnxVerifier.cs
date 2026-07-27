using System.Text;
using UnityEngine;
using Unity.InferenceEngine;

/// <summary>
/// [검증용] .onnx 모델에 고정 입력을 직접 넣어 출력을 확인합니다.
///
/// 목적:
///   "Python은 잘 싸우는데 Unity에서 .onnx를 넣으면 못 싸운다"의 원인이
///   (A) ONNX 변환/실행이 손상됐는지
///   (B) 모델은 정상인데 ML-Agents의 액션 적용이 문제인지
///   를 가릅니다.
///
/// 방법:
///   ML-Agents를 거치지 않고 Inference Engine으로 모델을 직접 실행합니다.
///   입력은 실제 에이전트의 관측을 그대로 쓰거나, 통제된 합성 입력을 씁니다.
///
/// 판정:
///   출력이 NaN/Inf/±1 포화 -> (A) 변환·실행 손상
///   출력이 정상 범위        -> (B) 액션 적용 단계 문제
///
/// 사용법:
///   아무 오브젝트에 붙이고 인스펙터에서 Run Test 체크 (또는 재생 시 자동 1회)
/// </summary>
public class OnnxVerifier : MonoBehaviour
{
    [Header("검사할 모델")]
    public ModelAsset modelAsset;

    [Header("실행 백엔드 비교")]
    public bool testCPU = true;
    public bool testBurst = true;
    public bool testGPUCompute = true;

    [Header("입력 방식")]
    [Tooltip("켜면 씬의 실제 에이전트 관측을 사용, 끄면 합성 입력 사용")]
    public bool useRealObservation = false;

    [Tooltip("합성 입력에 쓸 시드. 같은 시드는 같은 입력을 만듭니다.")]
    public int seed = 12345;

    [Tooltip("서로 다른 입력 몇 개를 시험할지")]
    public int trials = 5;

    private void Start()
    {
        RunVerification();
    }

    [ContextMenu("Run Verification")]
    public void RunVerification()
    {
        if (modelAsset == null)
        {
            Debug.LogError("[OnnxVerifier] modelAsset이 비어 있습니다.");
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine("================================================");
        sb.AppendLine("ONNX 직접 실행 검증: " + modelAsset.name);
        sb.AppendLine("================================================");

        var model = ModelLoader.Load(modelAsset);

        sb.AppendLine("layers=" + model.layers.Count
                    + " inputs=" + model.inputs.Count
                    + " outputs=" + model.outputs.Count
                    + " constants=" + model.constants.Count);
        foreach (var i in model.inputs)
            sb.AppendLine("  IN  " + i.name + " " + i.shape);
        foreach (var o in model.outputs)
            sb.AppendLine("  OUT " + o.name + " (index " + o.index + ")");
        sb.AppendLine();

        if (testCPU) RunOnBackend(model, BackendType.CPU, "CPU", sb);
        if (testBurst) RunOnBackend(model, BackendType.CPU, "CPU(2차)", sb);
        if (testGPUCompute) RunOnBackend(model, BackendType.GPUCompute, "GPUCompute", sb);

        sb.AppendLine("================================================");
        sb.AppendLine("[판정 기준]");
        sb.AppendLine("  NaN/Inf 발생          -> ONNX 변환 또는 실행 손상");
        sb.AppendLine("  |값|이 항상 1.0 근처   -> tanh 포화. 입력 스케일 또는 가중치 문제");
        sb.AppendLine("  입력 바꿔도 출력 동일  -> 그래프가 입력을 무시함 (심각)");
        sb.AppendLine("  백엔드마다 값 다름     -> Inference Engine 실행 버그");
        sb.AppendLine("  전부 정상             -> 모델은 멀쩡. ML-Agents 액션 적용 문제");
        sb.AppendLine("================================================");

        Debug.Log(sb.ToString());
    }

    private void RunOnBackend(Model model, BackendType backend, string label, StringBuilder sb)
    {
        sb.AppendLine("--- 백엔드: " + label + " ---");

        Worker worker = null;
        try
        {
            worker = new Worker(model, backend);
        }
        catch (System.Exception e)
        {
            sb.AppendLine("  Worker 생성 실패: " + e.GetType().Name + " " + e.Message);
            return;
        }

        var rng = new System.Random(seed);

        for (int t = 0; t < trials; t++)
        {
            // 입력 준비
            float[] obs0 = new float[65];  // 레이 센서
            float[] obs1 = new float[24];  // 벡터 관측
            float[] masks = new float[2];

            if (useRealObservation && t == 0)
            {
                FillRealObservation(obs0, obs1);
            }
            else
            {
                // 레이 센서: 각 광선 5개 값 = [tag1, tag2, tag3, 미검출, 거리]
                // 원핫 + 거리 형태를 모사
                for (int r = 0; r < 13; r++)
                {
                    int hit = rng.Next(0, 4); // 0~2=태그, 3=미검출
                    for (int k = 0; k < 4; k++) obs0[r * 5 + k] = (k == hit) ? 1f : 0f;
                    obs0[r * 5 + 4] = (float)rng.NextDouble();
                }
                // 벡터 관측: 대부분 0~1, 일부 -1~1
                for (int k = 0; k < 24; k++)
                {
                    double v = rng.NextDouble();
                    obs1[k] = (k >= 2 && k <= 5) ? (float)(v * 2.0 - 1.0) : (float)v;
                }
            }

            masks[0] = 1f; masks[1] = 1f; // 두 행동 모두 허용

            using var tObs0 = new Tensor<float>(new TensorShape(1, 65), obs0);
            using var tObs1 = new Tensor<float>(new TensorShape(1, 24), obs1);
            using var tMask = new Tensor<float>(new TensorShape(1, 2), masks);
            using var tRec = new Tensor<float>(new TensorShape(1, 1, 0), new float[0]);

            try
            {
                worker.SetInput("obs_0", tObs0);
                worker.SetInput("obs_1", tObs1);
                worker.SetInput("action_masks", tMask);
                worker.SetInput("recurrent_in", tRec);
                worker.Schedule();

                sb.Append("  [시행 " + (t + 1) + "] ");
                ReportOutput(worker, model, "continuous_actions", sb);
                ReportOutput(worker, model, "deterministic_continuous_actions", sb);
                ReportOutput(worker, model, "discrete_actions", sb);
                sb.AppendLine();
            }
            catch (System.Exception e)
            {
                sb.AppendLine("  [시행 " + (t + 1) + "] 실행 실패: "
                            + e.GetType().Name + " " + e.Message);
            }
        }

        worker.Dispose();
        sb.AppendLine();
    }

    private void ReportOutput(Worker worker, Model model, string outName, StringBuilder sb)
    {
        try
        {
            var raw = worker.PeekOutput(outName) as Tensor<float>;
            if (raw == null) { sb.Append(outName + "=없음  "); return; }

            using var cpu = raw.ReadbackAndClone();
            int n = Mathf.Min(cpu.shape.length, 4);

            sb.Append(Shorten(outName)).Append("[");
            bool bad = false;
            for (int i = 0; i < n; i++)
            {
                float v = cpu[i];
                if (float.IsNaN(v) || float.IsInfinity(v)) bad = true;
                sb.Append(v.ToString("F3"));
                if (i < n - 1) sb.Append(",");
            }
            sb.Append("]");
            if (bad) sb.Append("<<NaN/Inf!>>");
            sb.Append("  ");
        }
        catch (System.Exception e)
        {
            sb.Append(Shorten(outName) + "=오류(" + e.GetType().Name + ")  ");
        }
    }

    private string Shorten(string n)
    {
        if (n.StartsWith("deterministic_continuous")) return "detCont";
        if (n.StartsWith("continuous")) return "cont";
        if (n.StartsWith("discrete")) return "disc";
        return n;
    }

    /// <summary>씬의 실제 에이전트에서 관측을 가져옵니다.</summary>
    private void FillRealObservation(float[] obs0, float[] obs1)
    {
        var agent = Object.FindFirstObjectByType<PlayerAgent>();
        if (agent == null) return;

        var vs = new Unity.MLAgents.Sensors.VectorSensor(64);
        agent.CollectObservations(vs);
        var f = typeof(Unity.MLAgents.Sensors.VectorSensor).GetField("m_Observations",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = f.GetValue(vs) as System.Collections.Generic.List<float>;
        for (int i = 0; i < Mathf.Min(24, list.Count); i++) obs1[i] = list[i];
    }
}
