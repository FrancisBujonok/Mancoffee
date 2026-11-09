using System.Collections.Generic;
using UnityEngine;

namespace Mancoffee.GridSystem
{
    public enum MoveStatus
    {
        Idle,
        Moving,
        Arrived,
        Blocked
    }

    /// <summary>
    /// Movimiento sobre la grilla, compartido por clientes y empleados.
    /// Pide un camino al pathfinder, lo recorre celda por celda y lo recalcula si cambia el local.
    /// El dueño consulta Status; cuando vale Arrived, lo "consume" con ConsumeArrived().
    /// </summary>
    public class GridMover : MonoBehaviour
    {
        [SerializeField] private float speed = 1.4f;

        private GridManager grid;
        private readonly Queue<Vector2Int> path = new();
        private HashSet<Vector2Int> goals;
        private Vector2Int currentCell;
        private Vector2Int targetCell;
        private bool hasTarget;
        private bool direct;
        private bool needsRepath;

        public MoveStatus Status { get; private set; }
        public Vector2Int CurrentCell => currentCell;

        /// <summary>Desde dónde calcular un camino nuevo: si está a mitad de paso, desde la celda a la que va.</summary>
        public Vector2Int PathStart => hasTarget ? targetCell : currentCell;

        public void Init(GridManager gridManager, Vector2Int cell)
        {
            grid = gridManager;
            grid.GridChanged += OnGridChanged;
            Teleport(cell);
        }

        private void OnDestroy()
        {
            if (grid != null) grid.GridChanged -= OnGridChanged;
        }

        private void OnGridChanged()
        {
            if (Status == MoveStatus.Moving && !direct) needsRepath = true;
        }

        public void Teleport(Vector2Int cell)
        {
            path.Clear();
            hasTarget = false;
            goals = null;
            currentCell = cell;
            transform.position = grid.CellCenterWorld(cell);
            Status = MoveStatus.Idle;
        }

        public bool MoveTo(Vector2Int goal) => MoveToAny(new[] { goal });

        /// <summary>Camina hacia la más cercana de varias celdas. Devuelve false si no hay camino.</summary>
        public bool MoveToAny(IEnumerable<Vector2Int> goalCells)
        {
            goals = new HashSet<Vector2Int>(goalCells);
            direct = false;
            needsRepath = false;

            List<Vector2Int> newPath = GridPathfinder.FindPathToAny(grid, PathStart, goals);
            if (newPath == null)
            {
                path.Clear();
                Status = MoveStatus.Blocked;
                return false;
            }

            SetPath(newPath);
            Status = newPath.Count == 0 && !hasTarget ? MoveStatus.Arrived : MoveStatus.Moving;
            return true;
        }

        /// <summary>Un paso directo a una celda vecina, sin pathfinding (por ejemplo, salir a la calle).</summary>
        public void StepDirect(Vector2Int cell)
        {
            goals = null;
            direct = true;
            needsRepath = false;
            path.Clear();
            path.Enqueue(cell);
            Status = MoveStatus.Moving;
        }

        /// <summary>Deja de seguir el camino. Si estaba a mitad de paso, lo termina.</summary>
        public void Stop()
        {
            path.Clear();
            goals = null;
            needsRepath = false;
            Status = MoveStatus.Idle;
        }

        public bool ConsumeArrived()
        {
            if (Status != MoveStatus.Arrived) return false;
            Status = MoveStatus.Idle;
            return true;
        }

        private void SetPath(List<Vector2Int> cells)
        {
            path.Clear();
            foreach (Vector2Int c in cells) path.Enqueue(c);
        }

        private void Update()
        {
            if (grid == null) return;

            if (needsRepath && Status == MoveStatus.Moving)
            {
                needsRepath = false;
                List<Vector2Int> newPath = GridPathfinder.FindPathToAny(grid, PathStart, goals);
                if (newPath == null)
                {
                    path.Clear();
                    Status = MoveStatus.Blocked;
                }
                else
                {
                    SetPath(newPath);
                }
            }

            if (!hasTarget)
            {
                if (path.Count == 0) return;
                targetCell = path.Dequeue();
                hasTarget = true;
            }

            Vector3 destination = grid.CellCenterWorld(targetCell);
            transform.position = Vector3.MoveTowards(transform.position, destination, speed * Time.deltaTime);
            if ((transform.position - destination).sqrMagnitude > 0.00001f) return;

            currentCell = targetCell;
            hasTarget = false;
            if (path.Count == 0 && Status == MoveStatus.Moving) Status = MoveStatus.Arrived;
        }
    }
}
