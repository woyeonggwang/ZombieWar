using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 좌측 상단 미니맵.
/// - 직교 카메라를 플레이어 위에 두고 RenderTexture로 그립니다.
/// - 카메라 위치를 미로 경계 안으로 clamp 해서 맵 바깥 빈 공간이 보이지 않게 합니다.
/// - 플레이어/적 위치를 UI 마커로 표시하고, 아래에 남은 적 수를 "X n"으로 씁니다.
/// </summary>
public class MinimapController : MonoBehaviour
{
    [Header("참조")]
    public Camera minimapCamera;
    public RectTransform mapRect;      // RawImage의 RectTransform
    public RectTransform markerRoot;   // 마커들이 놓일 부모
    public Transform player;
    public MazeGenerator maze;
    public Text enemyCountText;
    public Sprite markerSprite;

    [Header("설정")]
    [Tooltip("미니맵에 보이는 반경 (월드 단위). 작을수록 확대됩니다")]
    public float viewRadius = 9f;
    public float playerMarkerSize = 12f;
    public float enemyMarkerSize = 10f;
    public Color playerColor = new Color(0.25f, 0.8f, 1f);
    public Color enemyColor = Color.red;
    [Tooltip("적 수 텍스트 앞에 붙일 문자")]
    public string countPrefix = "X ";

    private Image _playerMarker;
    private readonly List<Image> _pool = new List<Image>();
    private readonly List<PlayerAgent> _enemies = new List<PlayerAgent>();

    private void Awake()
    {
        _playerMarker = MakeMarker("PlayerMarker", playerColor, playerMarkerSize);
    }

    private Image MakeMarker(string n, Color c, float size)
    {
        var go = new GameObject(n, typeof(RectTransform));
        go.transform.SetParent(markerRoot, false);
        var rt = (RectTransform)go.transform;
        rt.anchorMin = new Vector2(0.5f, 0.5f);
        rt.anchorMax = new Vector2(0.5f, 0.5f);
        rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(size, size);
        var img = go.AddComponent<Image>();
        img.sprite = markerSprite;
        img.color = c;
        img.raycastTarget = false;
        return img;
    }

    private void CollectEnemies()
    {
        _enemies.Clear();
        var all = Object.FindObjectsByType<PlayerAgent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var a in all)
        {
            if (a == null) continue;
            if (player != null && a.transform == player) continue;
            if (a.team != AgentTeam.Red) continue;
            var h = a.GetComponent<AgentHealth>();
            if (h == null) continue;
            if (!a.gameObject.activeInHierarchy || h.IsDead) continue;
            _enemies.Add(a);
        }
    }

    private void LateUpdate()
    {
        if (player == null || maze == null || minimapCamera == null || mapRect == null) return;

        // ── 미로 경계 계산
        Vector3 o = maze.transform.position;
        float minX = o.x, maxX = o.x + maze.MaxX;
        float minZ = o.z, maxZ = o.z + maze.MaxZ;

        float half = viewRadius;

        // 카메라를 경계 안으로 clamp (맵 밖 빈 공간이 보이지 않도록)
        float cx, cz;
        if (maxX - minX <= half * 2f) cx = (minX + maxX) * 0.5f;
        else cx = Mathf.Clamp(player.position.x, minX + half, maxX - half);
        if (maxZ - minZ <= half * 2f) cz = (minZ + maxZ) * 0.5f;
        else cz = Mathf.Clamp(player.position.z, minZ + half, maxZ - half);

        minimapCamera.orthographic = true;
        minimapCamera.orthographicSize = half;
        minimapCamera.transform.position = new Vector3(cx, o.y + 60f, cz);
        minimapCamera.transform.rotation = Quaternion.Euler(90f, 0f, 0f);

        Vector2 mapSize = mapRect.rect.size;
        Vector3 camPos = new Vector3(cx, 0f, cz);

        // ── 플레이어 마커
        PlaceMarker(_playerMarker, player.position, camPos, half, mapSize);

        // ── 적 마커
        CollectEnemies();
        for (int i = 0; i < _enemies.Count; i++)
        {
            if (i >= _pool.Count) _pool.Add(MakeMarker("EnemyMarker", enemyColor, enemyMarkerSize));
            PlaceMarker(_pool[i], _enemies[i].transform.position, camPos, half, mapSize);
        }
        for (int i = _enemies.Count; i < _pool.Count; i++)
            if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);

        // ── 남은 적 수
        if (enemyCountText != null) enemyCountText.text = countPrefix + _enemies.Count;
    }

    private void PlaceMarker(Image marker, Vector3 world, Vector3 camPos, float half, Vector2 mapSize)
    {
        if (marker == null) return;

        float u = (world.x - camPos.x) / (half * 2f); // -0.5 ~ 0.5
        float v = (world.z - camPos.z) / (half * 2f);

        bool inside = Mathf.Abs(u) <= 0.5f && Mathf.Abs(v) <= 0.5f;
        if (marker.gameObject.activeSelf != inside) marker.gameObject.SetActive(inside);
        if (!inside) return;

        var rt = (RectTransform)marker.transform;
        rt.anchoredPosition = new Vector2(u * mapSize.x, v * mapSize.y);
    }
}
