using System.Collections.Generic;
using UnityEngine;

namespace Mancoffee.GridSystem
{
    /// <summary>
    /// A* sobre la grilla del local, en 8 direcciones. Respeta muebles y paredes (las puertas dejan pasar)
    /// y no corta esquinas. Devuelve las celdas a recorrer (sin la de partida) o null si no hay camino.
    /// Las celdas destino se aceptan aunque tengan un mueble (por ejemplo, la silla donde sentarse).
    /// </summary>
    public static class GridPathfinder
    {
        private const int StraightCost = 10;
        private const int DiagonalCost = 14;
        private const int MaxExpandedNodes = 20000;

        private static readonly Vector2Int[] Directions =
        {
            new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
            new(1, 1), new(1, -1), new(-1, 1), new(-1, -1)
        };

        public static List<Vector2Int> FindPath(GridManager grid, Vector2Int start, Vector2Int goal) =>
            FindPathToAny(grid, start, new HashSet<Vector2Int> { goal });

        /// <summary>Camino más corto hacia cualquiera de las celdas destino.</summary>
        public static List<Vector2Int> FindPathToAny(GridManager grid, Vector2Int start, ICollection<Vector2Int> goals)
        {
            if (goals == null || goals.Count == 0) return null;
            if (goals.Contains(start)) return new List<Vector2Int>();

            var open = new MinHeap();
            var cameFrom = new Dictionary<Vector2Int, Vector2Int>();
            var gScore = new Dictionary<Vector2Int, int> { [start] = 0 };
            var closed = new HashSet<Vector2Int>();

            open.Push(start, Heuristic(start, goals));

            while (open.Count > 0 && closed.Count < MaxExpandedNodes)
            {
                Vector2Int current = open.Pop();
                if (goals.Contains(current)) return Reconstruct(cameFrom, start, current);
                if (!closed.Add(current)) continue;

                foreach (Vector2Int dir in Directions)
                {
                    Vector2Int next = current + dir;
                    if (closed.Contains(next)) continue;

                    bool isGoal = goals.Contains(next);
                    if (!grid.IsWalkable(next) && !(isGoal && grid.IsFloor(next))) continue;

                    bool diagonal = dir.x != 0 && dir.y != 0;
                    if (!diagonal)
                    {
                        if (grid.BlocksMovement(current, next)) continue;
                    }
                    else
                    {
                        // En diagonal no se cortan esquinas de muebles ni se pasa junto a ninguna pared (ni por puertas).
                        Vector2Int sideA = current + new Vector2Int(dir.x, 0);
                        Vector2Int sideB = current + new Vector2Int(0, dir.y);
                        if (!grid.IsWalkable(sideA) || !grid.IsWalkable(sideB)) continue;
                        if (grid.HasWallBetween(current, sideA) || grid.HasWallBetween(current, sideB) ||
                            grid.HasWallBetween(sideA, next) || grid.HasWallBetween(sideB, next))
                            continue;
                    }

                    int tentative = gScore[current] + (diagonal ? DiagonalCost : StraightCost);
                    if (gScore.TryGetValue(next, out int known) && tentative >= known) continue;

                    gScore[next] = tentative;
                    cameFrom[next] = current;
                    open.Push(next, tentative + Heuristic(next, goals));
                }
            }

            return null;
        }

        /// <summary>Distancia "octil" hasta el destino más cercano: exacta en una grilla de 8 direcciones sin obstáculos.</summary>
        public static int Octile(Vector2Int a, Vector2Int b)
        {
            int dx = Mathf.Abs(a.x - b.x);
            int dy = Mathf.Abs(a.y - b.y);
            return StraightCost * (dx + dy) + (DiagonalCost - 2 * StraightCost) * Mathf.Min(dx, dy);
        }

        private static int Heuristic(Vector2Int a, ICollection<Vector2Int> goals)
        {
            int best = int.MaxValue;
            foreach (Vector2Int g in goals) best = Mathf.Min(best, Octile(a, g));
            return best;
        }

        private static List<Vector2Int> Reconstruct(Dictionary<Vector2Int, Vector2Int> cameFrom, Vector2Int start, Vector2Int end)
        {
            var path = new List<Vector2Int>();
            Vector2Int current = end;
            while (current != start)
            {
                path.Add(current);
                current = cameFrom[current];
            }
            path.Reverse();
            return path;
        }

        /// <summary>Cola de prioridad mínima (montículo binario). Admite duplicados; los viejos se descartan al sacarlos.</summary>
        private class MinHeap
        {
            private readonly List<(Vector2Int cell, int priority)> items = new();

            public int Count => items.Count;

            public void Push(Vector2Int cell, int priority)
            {
                items.Add((cell, priority));
                int i = items.Count - 1;
                while (i > 0)
                {
                    int parent = (i - 1) / 2;
                    if (items[parent].priority <= items[i].priority) break;
                    (items[parent], items[i]) = (items[i], items[parent]);
                    i = parent;
                }
            }

            public Vector2Int Pop()
            {
                Vector2Int top = items[0].cell;
                int last = items.Count - 1;
                items[0] = items[last];
                items.RemoveAt(last);

                int i = 0;
                while (true)
                {
                    int left = 2 * i + 1, right = left + 1, smallest = i;
                    if (left < items.Count && items[left].priority < items[smallest].priority) smallest = left;
                    if (right < items.Count && items[right].priority < items[smallest].priority) smallest = right;
                    if (smallest == i) break;
                    (items[smallest], items[i]) = (items[i], items[smallest]);
                    i = smallest;
                }
                return top;
            }
        }
    }
}
