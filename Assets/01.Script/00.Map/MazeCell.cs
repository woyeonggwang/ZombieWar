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

    public void ClearBackWall()
    {
        backWall.SetActive(false);
    }


}
