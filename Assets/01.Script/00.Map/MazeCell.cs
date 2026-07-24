using Unity.VisualScripting;
using UnityEngine;

public class MazeCell : MonoBehaviour
{
    [SerializeField]
    private GameObject leftWall;
    [SerializeField]
    private GameObject rightWall;
    [SerializeField]
    private GameObject frontWall;
    [SerializeField] 
    private GameObject backWall;
    [SerializeField]
    private GameObject unvisitedBlock;

    public bool IsVisited { get; private set; }
    public void Visit()
    {
        IsVisited = true;
        unvisitedBlock.SetActive(false);
    }
    public void ClearLeftWall()
    {
        leftWall.SetActive(false);
    }

    public void ClearRightWall()
    {
        rightWall.SetActive(false);
    }
    
    public void clearFrontWall()
    {
        frontWall.SetActive(false);
    }

        /// <summary> 현재 살아있는 벽 각각을 확률적으로 제거해 통로를 넓힘. </summary>
        /// <summary> 이 셀의 모든 벽을 제거 (스폰 공간 확보용). </summary>
    public void ClearAllWalls()
    {
        if (leftWall != null) leftWall.SetActive(false);
        if (rightWall != null) rightWall.SetActive(false);
        if (frontWall != null) frontWall.SetActive(false);
        if (backWall != null) backWall.SetActive(false);
    }

public void RandomlyRemoveWalls(float removeChance)
    {
        if (leftWall != null && leftWall.activeSelf && Random.value < removeChance) leftWall.SetActive(false);
        if (rightWall != null && rightWall.activeSelf && Random.value < removeChance) rightWall.SetActive(false);
        if (frontWall != null && frontWall.activeSelf && Random.value < removeChance) frontWall.SetActive(false);
        if (backWall != null && backWall.activeSelf && Random.value < removeChance) backWall.SetActive(false);
    }

public void ClearBackWall()
    {
        backWall.SetActive(false);
    }


}
