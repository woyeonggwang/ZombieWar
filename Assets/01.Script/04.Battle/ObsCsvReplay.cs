using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using Unity.InferenceEngine;

/// <summary>
/// [진단용] ObsActionRecorder가 기록한 CSV(주로 Python 추론 모드의 obs_PY.csv)를 읽어,
/// 그때의 관측을 .onnx 모델에 그대로 다시 먹이고, Python이 냈던 액션과 비교합니다.
///
/// 왜 필요한가:
///   Python 모드와 ONNX 모드를 각각 실행해서 관측을 인덱스별로 비교하는 방식은
///   1스텝만 지나도 상태가 갈라지기 때문에 성립하지 않습니다(미로 시드도 다름).
///   같은 입력에 대한 같은 출력만이 정책 동일성을 증명합니다.
///
/// 판정:
///   - detCont(결정론 출력)가 Python 액션과 거의 일치  -> 정책·변환·관측 조립 모두 정상.
///                                                       원인은 액션 적용 이후 단계(시간 스케일, 결정 주기, 물리 등)
///   - 계통적으로 다름                                 -> .onnx 가 .pt 정책과 다름(변환/체크포인트/샘플링 문제)
///   - cont는 다르고 detCont는 일치                    -> 확률적 샘플링 vs 결정론 출력 차이. DeterministicInference 설정 문제
///
/// 사용법:
///   아무 오브젝트에 붙이고 modelAsset 지정 -> 우클릭 Context Menu의 "Run Replay" 실행.
///   재생 중이 아니어도 동작합니다.
/// </summary>
public class ObsCsvReplay : MonoBehaviour
{
    [Header("입력")]
    public ModelAsset modelAsset;

    [Tooltip("프로젝트 루트 기준 상대 경로 또는 절대 경로")]
    public string csvFileName = "obs_PY.csv";

    [Header("실행")]
    public BackendType backend = BackendType.CPU;

    [Tooltip("비교할 최대 행 수")]
    public int maxRows = 500;

    [Tooltip("몇 행마다 하나씩 샘플링할지 (1 = 전부)")]
    public int stride = 1;

    [Tooltip("로그에 원본/재생 값을 나란히 찍을 샘플 행 수")]
    public int printSamples = 12;

    [Header("모델 입력 이름")]
    public string actionMaskInputName = "action_masks";
    public int actionMaskLength = 2;
    public string recurrentInputName = "recurrent_in";

    [ContextMenu("Run Replay")]
    public void RunReplay()
    {
        if (modelAsset == null) { Debug.LogError("[ObsCsvReplay] modelAsset이 비어 있습니다."); return; }

        string path = ResolvePath(csvFileName);
        if (!File.Exists(path)) { Debug.LogError("[ObsCsvReplay] CSV를 찾을 수 없습니다: " + path); return; }

        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) { Debug.LogError("[ObsCsvReplay] CSV가 비어 있습니다."); return; }

        // ---- 헤더 파싱 ----
        var sensorLens = new List<int>();
        var sensorNames = new List<string>();
        int headerRowIdx = -1;
        string[] cols = null;

        for (int i = 0; i < lines.Length; i++)
        {
            string ln = lines[i];
            if (ln.StartsWith("# sensors=", StringComparison.Ordinal))
            {
                foreach (var part in ln.Substring("# sensors=".Length).Split('|'))
                {
                    var seg = part.Split(':');
                    if (seg.Length >= 3 && int.TryParse(seg[seg.Length - 1], out int len))
                    {
                        sensorNames.Add(seg[0]);
                        sensorLens.Add(len);
                    }
                }
            }
            else if (!ln.StartsWith("#", StringComparison.Ordinal))
            {
                headerRowIdx = i;
                cols = ln.Split(',');
                break;
            }
        }

        if (headerRowIdx < 0 || cols == null) { Debug.LogError("[ObsCsvReplay] 컬럼 헤더를 찾지 못했습니다."); return; }
        if (sensorLens.Count == 0) { Debug.LogError("[ObsCsvReplay] '# sensors=' 헤더가 없습니다. ObsActionRecorder로 만든 CSV인지 확인하세요."); return; }

        var contIdx = new List<int>();
        var discIdx = new List<int>();
        var obsIdx = new List<int>();
        for (int c = 0; c < cols.Length; c++)
        {
            string n = cols[c].Trim();
            if (n.Length >= 2 && n[0] == 'c' && char.IsDigit(n[1])) contIdx.Add(c);
            else if (n.Length >= 2 && n[0] == 'd' && char.IsDigit(n[1])) discIdx.Add(c);
            else if (n.Length >= 2 && n[0] == 'o' && char.IsDigit(n[1])) obsIdx.Add(c);
        }

        int obsTotal = 0;
        foreach (var l in sensorLens) obsTotal += l;
        if (obsTotal != obsIdx.Count)
        {
            Debug.LogError($"[ObsCsvReplay] 센서 길이 합({obsTotal})과 관측 컬럼 수({obsIdx.Count})가 다릅니다.");
            return;
        }

        // ---- 모델 준비 ----
        var model = ModelLoader.Load(modelAsset);
        var modelInputNames = new HashSet<string>();
        foreach (var inp in model.inputs) modelInputNames.Add(inp.name);

        Worker worker;
        try { worker = new Worker(model, backend); }
        catch (Exception e) { Debug.LogError("[ObsCsvReplay] Worker 생성 실패: " + e.Message); return; }

        var sb = new StringBuilder();
        sb.AppendLine("================ ObsCsvReplay ================");
        sb.AppendLine("csv   : " + path);
        sb.AppendLine("model : " + modelAsset.name + "  backend=" + backend);
        sb.Append("센서  : ");
        for (int s = 0; s < sensorLens.Count; s++)
            sb.Append($"obs_{s}({sensorNames[s]}, len={sensorLens[s]})  ");
        sb.AppendLine();
        sb.Append("모델 입력: ");
        foreach (var inp in model.inputs) sb.Append(inp.name).Append(" ");
        sb.AppendLine();
        sb.AppendLine();

        int nCont = contIdx.Count;
        var diffAbsSumCont = new double[nCont];
        var diffAbsSumDet = new double[nCont];
        var maxDiffDet = new double[nCont];
        var pySum = new double[nCont];
        var pyAbsSum = new double[nCont];
        var detSum = new double[nCont];
        var detAbsSum = new double[nCont];
        int discMismatch = 0, discCompared = 0;
        int compared = 0, printed = 0;

        var maskArr = new float[Mathf.Max(1, actionMaskLength)];
        for (int i = 0; i < maskArr.Length; i++) maskArr[i] = 1f;

        for (int r = headerRowIdx + 1; r < lines.Length && compared < maxRows; r++)
        {
            if (((r - headerRowIdx - 1) % Mathf.Max(1, stride)) != 0) continue;
            var f = lines[r].Split(',');
            if (f.Length < cols.Length) continue;

            // 관측 -> 센서별 분리
            var tensors = new List<Tensor<float>>();
            try
            {
                int cursor = 0;
                for (int s = 0; s < sensorLens.Count; s++)
                {
                    var arr = new float[sensorLens[s]];
                    for (int k = 0; k < sensorLens[s]; k++)
                        arr[k] = ParseF(f[obsIdx[cursor + k]]);
                    cursor += sensorLens[s];
                    tensors.Add(new Tensor<float>(new TensorShape(1, sensorLens[s]), arr));
                }

                for (int s = 0; s < tensors.Count; s++)
                {
                    string inName = "obs_" + s;
                    if (modelInputNames.Contains(inName)) worker.SetInput(inName, tensors[s]);
                }

                Tensor<float> maskT = null, recT = null;
                if (modelInputNames.Contains(actionMaskInputName))
                {
                    maskT = new Tensor<float>(new TensorShape(1, maskArr.Length), maskArr);
                    worker.SetInput(actionMaskInputName, maskT);
                }
                if (modelInputNames.Contains(recurrentInputName))
                {
                    recT = new Tensor<float>(new TensorShape(1, 1, 0), new float[0]);
                    worker.SetInput(recurrentInputName, recT);
                }

                worker.Schedule();

                var cont = Read(worker, "continuous_actions");
                var det = Read(worker, "deterministic_continuous_actions");
                var disc = Read(worker, "discrete_actions");
                var detDisc = Read(worker, "deterministic_discrete_actions");

                for (int i = 0; i < nCont; i++)
                {
                    float py = ParseF(f[contIdx[i]]);
                    pySum[i] += py; pyAbsSum[i] += Mathf.Abs(py);
                    if (cont != null && i < cont.Length) diffAbsSumCont[i] += Mathf.Abs(py - cont[i]);
                    if (det != null && i < det.Length)
                    {
                        double d = Mathf.Abs(py - det[i]);
                        diffAbsSumDet[i] += d;
                        if (d > maxDiffDet[i]) maxDiffDet[i] = d;
                        detSum[i] += det[i]; detAbsSum[i] += Mathf.Abs(det[i]);
                    }
                }

                if (discIdx.Count > 0)
                {
                    var use = detDisc ?? disc;
                    if (use != null)
                    {
                        for (int i = 0; i < discIdx.Count && i < use.Length; i++)
                        {
                            int py = (int)ParseF(f[discIdx[i]]);
                            int on = Mathf.RoundToInt(use[i]);
                            discCompared++;
                            if (py != on) discMismatch++;
                        }
                    }
                }

                if (printed < printSamples)
                {
                    sb.Append($"row {r - headerRowIdx,-5} PY[");
                    for (int i = 0; i < nCont; i++) sb.Append(ParseF(f[contIdx[i]]).ToString("F3", CultureInfo.InvariantCulture)).Append(i < nCont - 1 ? "," : "");
                    sb.Append("]  det[");
                    if (det != null) for (int i = 0; i < nCont && i < det.Length; i++) sb.Append(det[i].ToString("F3", CultureInfo.InvariantCulture)).Append(i < nCont - 1 ? "," : "");
                    sb.Append("]  cont[");
                    if (cont != null) for (int i = 0; i < nCont && i < cont.Length; i++) sb.Append(cont[i].ToString("F3", CultureInfo.InvariantCulture)).Append(i < nCont - 1 ? "," : "");
                    sb.Append("]");
                    if (discIdx.Count > 0)
                    {
                        sb.Append("  discPY=").Append(f[discIdx[0]]);
                        var use = detDisc ?? disc;
                        if (use != null && use.Length > 0) sb.Append(" discONNX=").Append(Mathf.RoundToInt(use[0]));
                    }
                    sb.AppendLine();
                    printed++;
                }

                if (maskT != null) maskT.Dispose();
                if (recT != null) recT.Dispose();
                compared++;
            }
            catch (Exception e)
            {
                sb.AppendLine($"row {r}: 실행 실패 {e.GetType().Name} {e.Message}");
            }
            finally
            {
                foreach (var t in tensors) t.Dispose();
            }
        }

        worker.Dispose();

        sb.AppendLine();
        sb.AppendLine($"비교 행 수 = {compared}");
        if (compared > 0)
        {
            sb.AppendLine("dim | PY평균   PY절대평균 | ONNX(det)평균 절대평균 | |PY-det|평균  최대 | |PY-cont|평균");
            for (int i = 0; i < nCont; i++)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "c{0}  | {1,8:F4} {2,9:F4} | {3,12:F4} {4,9:F4} | {5,11:F4} {6,6:F4} | {7,12:F4}",
                    i, pySum[i] / compared, pyAbsSum[i] / compared,
                    detSum[i] / compared, detAbsSum[i] / compared,
                    diffAbsSumDet[i] / compared, maxDiffDet[i],
                    diffAbsSumCont[i] / compared));
            }
            if (discCompared > 0)
                sb.AppendLine($"이산 액션 불일치: {discMismatch}/{discCompared} ({(100.0 * discMismatch / discCompared):F1}%)");
        }

        sb.AppendLine();
        sb.AppendLine("[판정]");
        sb.AppendLine("  |PY-det|평균이 0.01 미만  -> 정책·ONNX변환·관측조립 전부 동일. 원인은 그 이후 단계");
        sb.AppendLine("  |PY-det|평균이 크다       -> .onnx 가 .pt 와 다른 정책. 변환/체크포인트/샘플링 확인");
        sb.AppendLine("==============================================");

        string outPath = ResolvePath("replay_compare.txt");
        try { File.WriteAllText(outPath, sb.ToString(), Encoding.UTF8); } catch { }
        Debug.Log(sb.ToString() + "\n저장: " + outPath);
    }

    private static float[] Read(Worker worker, string name)
    {
        try
        {
            var raw = worker.PeekOutput(name) as Tensor<float>;
            if (raw == null) return null;
            using var cpu = raw.ReadbackAndClone();
            var arr = new float[cpu.shape.length];
            for (int i = 0; i < arr.Length; i++) arr[i] = cpu[i];
            return arr;
        }
        catch { return null; }
    }

    private static float ParseF(string s)
    {
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out float v) ? v : 0f;
    }

    private static string ResolvePath(string fileName)
    {
        if (Path.IsPathRooted(fileName)) return fileName;
        return Path.GetFullPath(Path.Combine(Application.dataPath, "..", fileName));
    }
}
