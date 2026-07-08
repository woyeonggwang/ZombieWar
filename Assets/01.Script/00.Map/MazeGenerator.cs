using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public class MazeGenerator : MonoBehaviour
{
    [SerializeField]
    public MazeCell mazeCellPrefab;

    [SerializeField]
    private int mazeWidth;
    [SerializeField] 
    private int mazeDepth;

    private MazeCell[,] mazeGrid;

    void  Start()
    {
        mazeGrid = new MazeCell[mazeWidth,mazeDepth];
        for (int x = 0; x < mazeWidth; x++)
        {
            for (int z = 0; z < mazeDepth; z++)
            {
                mazeGrid[x,z]=Instantiate(mazeCellPrefab, new Vector3(x,0,z), Quaternion.identity);
            }
        }
        GenerateMaze(null, mazeGrid[0,0]);
    }
    void GenerateMaze(MazeCell previousCell, MazeCell currentCell)
    {
        currentCell.Visit();
        CelarWalls(previousCell,currentCell);

        MazeCell nextCell;
        do
        {
            nextCell = GetNextUnvisitedCell(currentCell);
            if (nextCell != null)
            {
                GenerateMaze(currentCell, nextCell);
            }

        }
        while (nextCell != null);
    }
    private MazeCell GetNextUnvisitedCell(MazeCell currentCell)
    {
        var unvisitedCells = GetUnvisitedCells(currentCell);
        return unvisitedCells.OrderBy(_ => Random.Range(1, 10)).FirstOrDefault();
    }
    private IEnumerable<MazeCell> GetUnvisitedCells(MazeCell currentCell)
    {
        int x = (int)currentCell.transform.position.x;
        int z = (int)currentCell.transform.position.z;

        if (x + 1 < mazeWidth)
        {
            var cellToRight = mazeGrid[x + 1, z];
            if (cellToRight.IsVisited == false)
            {
                yield return cellToRight;
            }
        }
        if (x - 1 >= 0)
        {
            var cellToLeft = mazeGrid[x - 1, z];
            if (cellToLeft.IsVisited == false)
            {
                yield return cellToLeft;
            }
        }
        if (z + 1 < mazeDepth)
        {
            var cellToFront = mazeGrid[x, z + 1];
            if (cellToFront.IsVisited == false)
            {
                yield return cellToFront;
            }
        }
        if (z - 1 >= 0)
        {
            var cellToBack = mazeGrid[x, z - 1];
            if (cellToBack.IsVisited == false)
            {
                yield return cellToBack;
            }
        }
    }
    private void CelarWalls(MazeCell previousCell, MazeCell currentCell)
    {
        if (previousCell == null)
        {
            return;
        }
        if(previousCell.transform.position.x<currentCell.transform.position.x)
        {
            previousCell.ClearRightWall();
            currentCell.ClearLeftWall();
            return;
        }
        if (previousCell.transform.position.x > currentCell.transform.position.x)
        {
            previousCell.ClearLeftWall();
            currentCell.ClearRightWall();
        }
        if (previousCell.transform.position.z < currentCell.transform.position.z)
        {
            previousCell.clearFrontWall();
            currentCell.ClearBackWall();
            return;
        }
        if (previousCell.transform.position.z > currentCell.transform.position.z)
        {
            previousCell.ClearBackWall();
            currentCell.clearFrontWall(); 
            return;
        }

    }
}



