using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 개선된 미로 생성기.
/// - cellSize로 통로 폭 조절 가능 (봇 크기보다 크게 설정)
/// - 셀을 이 오브젝트의 자식으로 생성 (로컬 좌표 기반 → 추후 Arena 복제 가능)
/// - Generate()를 외부에서 호출해 에피소드마다 새 미로 재생성 가능
/// - 재귀 대신 스택 방식 DFS (큰 미로에서도 스택 오버플로 없음)
/// </summary>
public class MazeGenerator : MonoBehaviour
{
    [SerializeField]
    public MazeCell mazeCellPrefab;

    [SerializeField]
    private int mazeWidth = 20;
    [SerializeField]
    private int mazeDepth = 20;

    [Tooltip("셀 한 칸의 크기(월드 단위). 통로 폭 = cellSize * 0.8 이므로 봇보다 충분히 크게 설정")]
    [SerializeField]
    private float cellSize = 2f;

    [Tooltip("체크 시 게임 시작 때 자동 생성. TeamBattleManager가 제어하는 경우 꺼둠 것")]
    [SerializeField]
    private bool generateOnStart = true;

    private MazeCell[,] mazeGrid;

    public int Width { get { return mazeWidth; } }
    public int Depth { get { return mazeDepth; } }
    public float CellSize { get { return cellSize; } }
    /// <summary> 미로의 한 변 전체 길이 (관측 정규화용) </summary>
    public float WorldSize { get { return Mathf.Max(mazeWidth, mazeDepth) * cellSize; } }

    private void Start()
    {
        if (generateOnStart)
        {
            Generate();
        }
    }

    /// <summary> 기존 미로를 지우고 새 미로를 생성합니다. 에피소드마다 호출 가능. </summary>
    public void Generate()
    {
        Clear();

        mazeGrid = new MazeCell[mazeWidth, mazeDepth];
        for (int x = 0; x < mazeWidth; x++)
        {
            for (int z = 0; z < mazeDepth; z++)
            {
                MazeCell cell = Instantiate(mazeCellPrefab, transform);
                cell.transform.localPosition = new Vector3(x * cellSize, 0f, z * cellSize);
                cell.transform.localScale = Vector3.one * cellSize;
                mazeGrid[x, z] = cell;
            }
        }

        // 스택 기반 DFS (기존 재귀 방식과 동일한 결과, 더 안전함)
        Stack<Vector2Int> stack = new Stack<Vector2Int>();
        mazeGrid[0, 0].Visit();
        stack.Push(new Vector2Int(0, 0));

        List<Vector2Int> neighbours = new List<Vector2Int>();
        while (stack.Count > 0)
        {
            Vector2Int current = stack.Peek();
            CollectUnvisitedNeighbours(current, neighbours);

            if (neighbours.Count == 0)
            {
                stack.Pop();
                continue;
            }

            Vector2Int next = neighbours[Random.Range(0, neighbours.Count)];
            ClearWalls(current, next);
            mazeGrid[next.x, next.y].Visit();
            stack.Push(next);
        }
    }

    /// <summary> 생성된 모든 셀을 제거합니다. </summary>
    public void Clear()
    {
        for (int i = transform.childCount - 1; i >= 0; i--)
        {
            GameObject child = transform.GetChild(i).gameObject;
            if (Application.isPlaying) Destroy(child);
            else DestroyImmediate(child);
        }
        mazeGrid = null;
    }

    /// <summary> (x,z) 셀의 중심 월드 좌표. 스폰 위치 계산용. </summary>
    public Vector3 GetCellWorldPosition(int x, int z)
    {
        return transform.TransformPoint(new Vector3(x * cellSize, 0f, z * cellSize));
    }

    private void CollectUnvisitedNeighbours(Vector2Int cell, List<Vector2Int> result)
    {
        result.Clear();
        int x = cell.x;
        int z = cell.y;

        if (x + 1 < mazeWidth && !mazeGrid[x + 1, z].IsVisited) result.Add(new Vector2Int(x + 1, z));
        if (x - 1 >= 0 && !mazeGrid[x - 1, z].IsVisited) result.Add(new Vector2Int(x - 1, z));
        if (z + 1 < mazeDepth && !mazeGrid[x, z + 1].IsVisited) result.Add(new Vector2Int(x, z + 1));
        if (z - 1 >= 0 && !mazeGrid[x, z - 1].IsVisited) result.Add(new Vector2Int(x, z - 1));
    }

    private void ClearWalls(Vector2Int prev, Vector2Int current)
    {
        MazeCell prevCell = mazeGrid[prev.x, prev.y];
        MazeCell currCell = mazeGrid[current.x, current.y];

        if (prev.x < current.x) { prevCell.ClearRightWall(); currCell.ClearLeftWall(); return; }
        if (prev.x > current.x) { prevCell.ClearLeftWall(); currCell.ClearRightWall(); return; }
        if (prev.y < current.y) { prevCell.clearFrontWall(); currCell.ClearBackWall(); return; }
        if (prev.y > current.y) { prevCell.ClearBackWall(); currCell.clearFrontWall(); }
    }
}



