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

    [Header("Simple Mode (빈 공간 + 가운데 벽만)")]
    [Tooltip("체크 시 꽉 찬 미로 대신 빈 공간 가운데에 벽 블럭 몇 개만 생성")]
    [SerializeField]
    private bool simpleMode = false;

    [Header("Maze Density (미로 밀도)")]
    [Tooltip("미로 완성 후 남은 벽을 이 비율만큼 무작위 제거해 통로를 넓힘 (0=꽉찬 미로, 0.5=절반 제거)")]
    [Range(0f, 1f)]
    [SerializeField]
    private float wallRemoveRatio = 0.5f;
    [Tooltip("스폰이 위치한 네 코너 주변 이 반경(칸)만큼 벽을 완전히 제거해 스폰 공간 확보")]
    [SerializeField]
    private int spawnClearRadius = 1;

    [Header("Editor 재생성 토글")]
    [Tooltip("체크하면 맵을 재생성하고 자동으로 다시 꺼집니다. 플레이 중 인스펙터에서 사용.")]
    public bool regenerateNow = false;


    [Tooltip("가운데 벽 배치 형태")]
    [SerializeField]
    private SimpleLayout simpleLayout = SimpleLayout.Cross;
    [Tooltip("벽 블럭 하나의 크기(월드 단위)")]
    [SerializeField]
    private float blockSize = 3f;
    [Tooltip("벽 블럭 높이")]
    [SerializeField]
    private float blockHeight = 3f;
    [Tooltip("미로 영역 바깥으로 둘 여유 공간(월드 단위). 이 값만큼 넓힌 사각형에 테두리 벽을 세움")]
    [SerializeField]
    private float borderPadding = 4f;
    [Tooltip("테두리 벽 두께")]
    [SerializeField]
    private float borderThickness = 1f;


    public enum SimpleLayout { Cross, Line }


    private MazeCell[,] mazeGrid;

    public int Width { get { return mazeWidth; } }
    public int Depth { get { return mazeDepth; } }
    public float CellSize { get { return cellSize; } }
    /// <summary> 미로의 한 변 전체 길이 (관측 정규화용) </summary>
    public float WorldSize { get { return Mathf.Max(mazeWidth, mazeDepth) * cellSize; } }
    /// <summary> 셀 중심 기준 X축 마지막 좌표 (로컬) </summary>
    public float MaxX { get { return (mazeWidth - 1) * cellSize; } }
    /// <summary> 셀 중심 기준 Z축 마지막 좌표 (로컬) </summary>
    public float MaxZ { get { return (mazeDepth - 1) * cellSize; } }
    /// <summary> 테두리 벽까지의 여유 공간 </summary>
    public float BorderPadding { get { return borderPadding; } }

    private void Start()
    {
        if (generateOnStart)
        {
            Generate();
        }
    }

    private void Update()
    {
        if (regenerateNow)
        {
            regenerateNow = false; // 먼저 꺼서 중복 방지
            Generate();
            Debug.Log("[MazeGenerator] 맵 재생성됨");
        }
    }


    /// <summary> 기존 미로를 지우고 새 미로를 생성합니다. 에피소드마다 호출 가능. </summary>
    public void Generate()
    {
        Clear();

        if (simpleMode) { GenerateSimple(); return; }

        
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

        // 미로 완성 후: 남은 벽을 무작위로 제거해 통로를 넓힘 (밀도 낮추기)
        if (wallRemoveRatio > 0f)
        {
            for (int x = 0; x < mazeWidth; x++)
                for (int z = 0; z < mazeDepth; z++)
                    if (mazeGrid[x, z] != null)
                        mazeGrid[x, z].RandomlyRemoveWalls(wallRemoveRatio);
        }

        ClearSpawnAreas(); // 스폰 코너 주변 벽 제거
        
CreateBorderWalls(); // 미로 영역을 둘러싸는 테두리 벽
    }

    /// <summary> 빈 공간 가운데에 벽 블럭 몇 개만 생성 (십자 5개 또는 일자 3개). </summary>
    /// <summary> 빈 공간 가운데에 벽 블럭(십자/일자)과 사각 테두리 벽을 생성. </summary>
    private void GenerateSimple()
    {
        // 미로 영역: 로컬 0 ~ (n-1)*cellSize, 중심 계산
        float maxX = (mazeWidth - 1) * cellSize;
        float maxZ = (mazeDepth - 1) * cellSize;
        float cx = maxX * 0.5f;
        float cz = maxZ * 0.5f;
        Vector3 center = new Vector3(cx, blockHeight * 0.5f, cz);

        // --- 가운데 십자/일자 벽 (길이 2배) ---
        List<Vector3> offsets = new List<Vector3>();
        offsets.Add(Vector3.zero);
        float d = blockSize * 2f; // 십자 팔 길이 2배
        if (simpleLayout == SimpleLayout.Cross)
        {
            offsets.Add(new Vector3(d, 0f, 0f));
            offsets.Add(new Vector3(-d, 0f, 0f));
            offsets.Add(new Vector3(0f, 0f, d));
            offsets.Add(new Vector3(0f, 0f, -d));
        }
        else // Line
        {
            offsets.Add(new Vector3(d, 0f, 0f));
            offsets.Add(new Vector3(-d, 0f, 0f));
        }
        foreach (Vector3 off in offsets)
        {
            GameObject block = GameObject.CreatePrimitive(PrimitiveType.Cube);
            block.name = "CenterWall";
            block.transform.SetParent(transform, false);
            block.transform.localPosition = center + off;
            block.transform.localScale = new Vector3(blockSize, blockHeight, blockSize);
        }

        // --- 사각 테두리 벽 ---
        float minB = -borderPadding;
        float maxBX = maxX + borderPadding;
        float maxBZ = maxZ + borderPadding;
        float width = maxBX - minB;   // 테두리 안쪽 가로 길이
        float depth = maxBZ - minB;   // 테두리 안쪽 세로 길이
        float midX = (minB + maxBX) * 0.5f;
        float midZ = (minB + maxBZ) * 0.5f;
        float y = blockHeight * 0.5f;
        float t = borderThickness;

        // 남/북 (Z 최소/최대) 벽: 가로로 길게
        CreateBorder(new Vector3(midX, y, minB), new Vector3(width + t, blockHeight, t));
        CreateBorder(new Vector3(midX, y, maxBZ), new Vector3(width + t, blockHeight, t));
        // 서/동 (X 최소/최대) 벽: 세로로 길게
        CreateBorder(new Vector3(minB, y, midZ), new Vector3(t, blockHeight, depth + t));
        CreateBorder(new Vector3(maxBX, y, midZ), new Vector3(t, blockHeight, depth + t));
    }

    /// <summary> 테두리 벽 큐브 하나 생성 (로컬 좌표/크기). </summary>
    /// <summary> 테두리 벽 큐브 하나 생성 (로컬 좌표/크기). 빨간색 적용. </summary>
    private void CreateBorder(Vector3 localPos, Vector3 localScale)
    {
        GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.name = "BorderWall";
        wall.transform.SetParent(transform, false);
        wall.transform.localPosition = localPos;
        wall.transform.localScale = localScale;
        var rend = wall.GetComponent<Renderer>();
        if (rend != null)
        {
            rend.material.color = Color.red;
        }
    }

    /// <summary> 미로 영역을 둘러싸는 사각 테두리 벽 생성. 일반 미로/심플 모드 공용. </summary>
    private void CreateBorderWalls()
    {
        float maxX = (mazeWidth - 1) * cellSize;
        float maxZ = (mazeDepth - 1) * cellSize;
        float minB = -borderPadding;
        float maxBX = maxX + borderPadding;
        float maxBZ = maxZ + borderPadding;
        float width = maxBX - minB;
        float depth = maxBZ - minB;
        float midX = (minB + maxBX) * 0.5f;
        float midZ = (minB + maxBZ) * 0.5f;
        float y = blockHeight * 0.5f;
        float t = borderThickness;
        CreateBorder(new Vector3(midX, y, minB), new Vector3(width + t, blockHeight, t));
        CreateBorder(new Vector3(midX, y, maxBZ), new Vector3(width + t, blockHeight, t));
        CreateBorder(new Vector3(minB, y, midZ), new Vector3(t, blockHeight, depth + t));
        CreateBorder(new Vector3(maxBX, y, midZ), new Vector3(t, blockHeight, depth + t));
    }

    /// <summary> 네 코너(스폰 위치) 주변 spawnClearRadius 칸의 벽을 모두 제거. </summary>
    private void ClearSpawnAreas()
    {
        if (mazeGrid == null || spawnClearRadius < 0) return;
        int r = spawnClearRadius;
        Vector2Int[] corners = new Vector2Int[]
        {
            new Vector2Int(0, 0),
            new Vector2Int(mazeWidth - 1, 0),
            new Vector2Int(0, mazeDepth - 1),
            new Vector2Int(mazeWidth - 1, mazeDepth - 1)
        };
        foreach (Vector2Int c in corners)
        {
            for (int dx = -r; dx <= r; dx++)
            {
                for (int dz = -r; dz <= r; dz++)
                {
                    int x = c.x + dx;
                    int z = c.y + dz;
                    if (x >= 0 && x < mazeWidth && z >= 0 && z < mazeDepth && mazeGrid[x, z] != null)
                        mazeGrid[x, z].ClearAllWalls();
                }
            }
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

    /// <summary> 지정한 셀 주변 radius 칸의 벽을 제거. 랜덤 스폰 지점이 갇히지 않도록 사용. </summary>
    public void ClearAreaAroundCell(int cx, int cz, int radius)
    {
        if (mazeGrid == null) return;
        for (int dx = -radius; dx <= radius; dx++)
        {
            for (int dz = -radius; dz <= radius; dz++)
            {
                int x = cx + dx;
                int z = cz + dz;
                if (x >= 0 && x < mazeWidth && z >= 0 && z < mazeDepth && mazeGrid[x, z] != null)
                    mazeGrid[x, z].ClearAllWalls();
            }
        }
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



