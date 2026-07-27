#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Unity.MLAgents.Policies;

/// <summary>
/// [진단용 Editor 확장] ONNX 추론 성능 저하 디버깅을 위한 일괄 설정/점검 메뉴.
///
/// 기존 학습 스크립트는 건드리지 않습니다. 씬에 있는 BehaviorParameters 값만 바꿉니다.
///
/// 왜 필요한가:
///   - Python 추론 테스트(테스트 A)에는 BehaviorType이 Default 여야 합니다.
///     InferenceOnly면 Python이 붙어 있어도 로컬 .onnx를 써버려서
///     PY 데이터가 아니라 ONNX 데이터가 기록됩니다.
///   - ONNX 단독 테스트(테스트 B)에는 InferenceOnly 여야 합니다.
///   - 에이전트가 96명이라 손으로 바꾸는 건 비현실적입니다.
/// </summary>
public static class MLDebugMenu
{
    private const string kMenu = "Tools/ML Debug/";

    [MenuItem(kMenu + "1. BehaviorType 전체 -> Default  (테스트 A: Python 추론)", false, 10)]
    private static void SetDefault() { SetBehaviorType(BehaviorType.Default); }

    [MenuItem(kMenu + "2. BehaviorType 전체 -> InferenceOnly  (테스트 B: ONNX 단독)", false, 11)]
    private static void SetInferenceOnly() { SetBehaviorType(BehaviorType.InferenceOnly); }

    private static void SetBehaviorType(BehaviorType type)
    {
        var all = Object.FindObjectsByType<BehaviorParameters>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        int n = 0;
        foreach (var bp in all)
        {
            Undo.RecordObject(bp, "Set BehaviorType");
            bp.BehaviorType = type;
            EditorUtility.SetDirty(bp);
            n++;
        }

        EditorSceneManager.MarkAllScenesDirty();
        Debug.Log($"[MLDebug] BehaviorType을 {type}로 변경했습니다: {n}개\n" +
                  $"  ※ 씬을 저장하십시오 (Ctrl+S). 저장 후 재생하세요.");
    }

    [MenuItem(kMenu + "3. 현재 설정 점검 (콘솔 출력)", false, 30)]
    private static void Inspect()
    {
        var all = Object.FindObjectsByType<BehaviorParameters>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);

        var sb = new StringBuilder();
        sb.AppendLine("================ ML Debug 설정 점검 ================");
        sb.AppendLine($"BehaviorParameters 총 {all.Length}개");
        sb.AppendLine();

        var behaviorType = new Dictionary<string, int>();
        var behaviorName = new Dictionary<string, int>();
        var device = new Dictionary<string, int>();
        var deterministic = new Dictionary<string, int>();
        var teamId = new Dictionary<string, int>();
        var modelName = new Dictionary<string, int>();
        var decisionPeriod = new Dictionary<string, int>();
        var takeBetween = new Dictionary<string, int>();

        Object firstModel = null;

        foreach (var bp in all)
        {
            var so = new SerializedObject(bp);
            Bump(behaviorType, Enum(so, "m_BehaviorType"));
            Bump(behaviorName, Str(so, "m_BehaviorName"));
            Bump(device, Enum(so, "m_InferenceDevice"));
            Bump(deterministic, Bool(so, "m_DeterministicInference"));
            Bump(teamId, Int(so, "m_TeamID"));

            var mp = so.FindProperty("m_Model");
            var mo = mp != null ? mp.objectReferenceValue : null;
            Bump(modelName, mo != null ? mo.name : "(없음)");
            if (mo != null && firstModel == null) firstModel = mo;

            var dr = bp.GetComponent<Unity.MLAgents.DecisionRequester>();
            if (dr != null)
            {
                var dso = new SerializedObject(dr);
                Bump(decisionPeriod, Int(dso, "DecisionPeriod"));
                Bump(takeBetween, Bool(dso, "TakeActionsBetweenDecisions"));
            }
        }

        Dump(sb, "BehaviorType", behaviorType);
        Dump(sb, "BehaviorName", behaviorName);
        Dump(sb, "InferenceDevice", device);
        Dump(sb, "DeterministicInference", deterministic);
        Dump(sb, "TeamId", teamId);
        Dump(sb, "Model", modelName);
        Dump(sb, "DecisionPeriod", decisionPeriod);
        Dump(sb, "TakeActionsBetweenDecisions", takeBetween);

        // ---- ONNX 파일 실체 점검 (외부 가중치 파일 여부) ----
        sb.AppendLine();
        sb.AppendLine("---- 할당된 ONNX 파일 점검 ----");
        if (firstModel == null)
        {
            sb.AppendLine("  모델이 할당된 BehaviorParameters가 없습니다.");
        }
        else
        {
            string ap = AssetDatabase.GetAssetPath(firstModel);
            string full = Path.GetFullPath(ap);
            sb.AppendLine($"  경로: {ap}");
            if (File.Exists(full))
            {
                long size = new FileInfo(full).Length;
                sb.AppendLine($"  .onnx 크기: {size:N0} bytes");

                string dataPath = full + ".data";
                if (File.Exists(dataPath))
                {
                    long dsize = new FileInfo(dataPath).Length;
                    sb.AppendLine($"  ⚠ 같은 이름의 외부 가중치 파일이 있습니다: .onnx.data ({dsize:N0} bytes)");
                    sb.AppendLine("     ONNX가 external data 형식으로 저장되었다는 뜻입니다.");
                    sb.AppendLine("     Unity 임포터가 .onnx.data를 함께 읽지 못하면 가중치가 온전하지 않을 수 있습니다.");
                    sb.AppendLine("     .onnx 본체가 .onnx.data보다 현저히 작다면 이 경로를 최우선으로 의심하십시오.");
                }
                else
                {
                    sb.AppendLine("  외부 가중치 파일(.onnx.data) 없음 -> 단일 파일 형식. 정상.");
                }
            }
        }

        sb.AppendLine("=====================================================");
        Debug.Log(sb.ToString());
    }

    // ---------------- 헬퍼 ----------------

    private static string Enum(SerializedObject so, string path)
    {
        var p = so.FindProperty(path);
        if (p == null) return "(속성없음)";
        if (p.enumDisplayNames != null && p.enumValueIndex >= 0 && p.enumValueIndex < p.enumDisplayNames.Length)
            return p.enumDisplayNames[p.enumValueIndex];
        return p.enumValueIndex.ToString();
    }

    private static string Str(SerializedObject so, string path)
    {
        var p = so.FindProperty(path);
        return p == null ? "(속성없음)" : p.stringValue;
    }

    private static string Bool(SerializedObject so, string path)
    {
        var p = so.FindProperty(path);
        return p == null ? "(속성없음)" : p.boolValue.ToString();
    }

    private static string Int(SerializedObject so, string path)
    {
        var p = so.FindProperty(path);
        return p == null ? "(속성없음)" : p.intValue.ToString();
    }

    private static void Bump(Dictionary<string, int> d, string key)
    {
        d.TryGetValue(key, out int c);
        d[key] = c + 1;
    }

    private static void Dump(StringBuilder sb, string label, Dictionary<string, int> d)
    {
        if (d.Count == 0) return;
        sb.Append(label).Append(" : ");
        bool first = true;
        foreach (var kv in d)
        {
            if (!first) sb.Append(" / ");
            sb.Append(kv.Key).Append(" x").Append(kv.Value);
            first = false;
        }
        if (d.Count > 1) sb.Append("   <-- 값이 섞여 있습니다. 확인 필요");
        sb.AppendLine();
    }
}
#endif
