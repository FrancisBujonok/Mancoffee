using System;
using System.Collections.Generic;
using Mancoffee.Building;
using Mancoffee.Data;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Mancoffee.GridSystem
{
    /// <summary>
    /// Fuente de verdad del local: suelo (y su tipo), paredes en los bordes, muebles por celda y entrada.
    /// Lo visual (Tilemap, sprites) solo refleja estos datos; la lógica del juego consulta acá.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class GridManager : MonoBehaviour
    {
        private static readonly Vector2Int[] Orthogonal =
            { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

        public static GridManager Instance { get; private set; }

        [SerializeField] private Grid grid;
        [SerializeField] private Tilemap floorTilemap;
        [SerializeField] private GameDatabase database;

        [Tooltip("Celda de suelo por donde entran y salen los clientes. No se puede construir encima.")]
        [SerializeField] private Vector2Int entranceCell;
        [SerializeField] private TileBase entranceTile;

        private readonly Dictionary<Vector2Int, FloorTypeData> floor = new();
        private readonly Dictionary<WallEdge, WallKind> walls = new();
        private readonly Dictionary<Vector2Int, PlacedFurniture> occupancy = new();
        private readonly HashSet<PlacedFurniture> allFurniture = new();

        /// <summary>Cambió algo que afecta por dónde se puede caminar (los agentes recalculan su camino).</summary>
        public event Action GridChanged;

        /// <summary>Cambió una pared o algo que afecta cómo se dibuja.</summary>
        public event Action<WallEdge> WallChanged;

        /// <summary>Se reemplazó todo el local (por ejemplo, al cargar una partida).</summary>
        public event Action LayoutReloaded;

        public Grid Grid => grid;
        public GameDatabase Database => database;
        public Vector2Int EntranceCell => entranceCell;

        /// <summary>Celda fuera del local, pegada a la entrada: ahí aparecen y desaparecen los clientes.</summary>
        public Vector2Int OutsideCell { get; private set; }

        /// <summary>El borde entre la entrada y la calle. Solo admite puerta (o nada).</summary>
        public WallEdge EntranceEdge => WallEdge.Between(entranceCell, OutsideCell);

        public IReadOnlyCollection<PlacedFurniture> AllFurniture => allFurniture;
        public IReadOnlyDictionary<Vector2Int, FloorTypeData> Floor => floor;
        public IReadOnlyDictionary<WallEdge, WallKind> Walls => walls;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            // El suelo pintado en la escena es el local inicial.
            floorTilemap.CompressBounds();
            foreach (Vector3Int pos in floorTilemap.cellBounds.allPositionsWithin)
            {
                TileBase tile = floorTilemap.GetTile(pos);
                if (tile == null) continue;
                floor[(Vector2Int)pos] = database.FindFloorByTile(tile) ?? database.DefaultFloor;
            }

            if (!floor.ContainsKey(entranceCell)) floor[entranceCell] = database.DefaultFloor;
            OutsideCell = entranceCell + Vector2Int.left;
            foreach (Vector2Int dir in Orthogonal)
            {
                if (!IsFloor(entranceCell + dir))
                {
                    OutsideCell = entranceCell + dir;
                    break;
                }
            }
            RefreshTile(entranceCell);
        }

        // =========================== Consultas ===========================

        public bool IsFloor(Vector2Int cell) => floor.ContainsKey(cell);

        public FloorTypeData GetFloorType(Vector2Int cell)
        {
            floor.TryGetValue(cell, out FloorTypeData type);
            return type;
        }

        public bool IsFree(Vector2Int cell) => IsFloor(cell) && !occupancy.ContainsKey(cell);

        /// <summary>Se puede estar parado en la celda: tiene suelo y ningún mueble.</summary>
        public bool IsWalkable(Vector2Int cell) => IsFree(cell);

        public PlacedFurniture GetFurnitureAt(Vector2Int cell)
        {
            occupancy.TryGetValue(cell, out PlacedFurniture furniture);
            return furniture;
        }

        public bool TryGetWall(WallEdge edge, out WallKind kind) => walls.TryGetValue(edge, out kind);

        /// <summary>¿Hay cualquier cosa (pared, puerta o ventana) entre dos celdas vecinas?</summary>
        public bool HasWallBetween(Vector2Int a, Vector2Int b) => walls.ContainsKey(WallEdge.Between(a, b));

        /// <summary>¿Hay algo que impida pasar entre dos celdas vecinas? Las puertas dejan pasar.</summary>
        public bool BlocksMovement(Vector2Int a, Vector2Int b) =>
            walls.TryGetValue(WallEdge.Between(a, b), out WallKind kind) && kind != WallKind.Puerta;

        /// <summary>Un mueble entra si todas sus celdas están libres y ninguna pared lo cruza por dentro.</summary>
        public bool CanPlace(IEnumerable<Vector2Int> cells)
        {
            var set = new HashSet<Vector2Int>(cells);
            foreach (Vector2Int c in set)
            {
                if (!IsFree(c) || c == entranceCell) return false;
                if (set.Contains(c + Vector2Int.right) && HasWallBetween(c, c + Vector2Int.right)) return false;
                if (set.Contains(c + Vector2Int.up) && HasWallBetween(c, c + Vector2Int.up)) return false;
            }
            return true;
        }

        /// <summary>
        /// Celdas desde donde se puede usar algo que ocupa estas celdas (atender una mesa, usar una estación):
        /// vecinas caminables sin pared de por medio.
        /// </summary>
        public List<Vector2Int> GetAccessCells(IEnumerable<Vector2Int> cells)
        {
            var set = new HashSet<Vector2Int>(cells);
            var result = new List<Vector2Int>();
            foreach (Vector2Int c in set)
            foreach (Vector2Int dir in Orthogonal)
            {
                Vector2Int n = c + dir;
                if (set.Contains(n) || result.Contains(n)) continue;
                if (!IsWalkable(n) || BlocksMovement(c, n)) continue;
                result.Add(n);
            }
            return result;
        }

        // =========================== Muebles ===========================

        public void Occupy(PlacedFurniture furniture)
        {
            foreach (Vector2Int cell in furniture.OccupiedCells)
                occupancy[cell] = furniture;
            allFurniture.Add(furniture);
            furniture.SetPlaced(true);
            GridChanged?.Invoke();
        }

        public void Release(PlacedFurniture furniture)
        {
            foreach (Vector2Int cell in furniture.OccupiedCells)
                if (occupancy.TryGetValue(cell, out PlacedFurniture current) && current == furniture)
                    occupancy.Remove(cell);
            allFurniture.Remove(furniture);
            furniture.SetPlaced(false);
            // Quien lo tenía reservado (un cliente yendo a esa silla, un empleado usando la cafetera) lo pierde.
            furniture.ReservedBy = null;
            GridChanged?.Invoke();
        }

        // =========================== Suelo ===========================

        public bool CanSetFloor(Vector2Int cell, FloorTypeData type) =>
            type != null && cell != OutsideCell && (!floor.TryGetValue(cell, out FloorTypeData current) || current != type);

        public void SetFloor(Vector2Int cell, FloorTypeData type)
        {
            bool isNew = !floor.ContainsKey(cell);
            floor[cell] = type;
            RefreshTile(cell);
            NotifyWallsAround(cell);
            if (isNew) GridChanged?.Invoke();
        }

        public bool CanRemoveFloor(Vector2Int cell) =>
            floor.ContainsKey(cell) && cell != entranceCell && !occupancy.ContainsKey(cell);

        public void RemoveFloor(Vector2Int cell)
        {
            if (!CanRemoveFloor(cell)) return;
            floor.Remove(cell);
            floorTilemap.SetTile((Vector3Int)cell, null);

            // Las paredes que quedan sin suelo de ningún lado desaparecen.
            foreach (Vector2Int dir in Orthogonal)
            {
                WallEdge edge = WallEdge.Between(cell, cell + dir);
                if (walls.ContainsKey(edge) && !IsFloor(cell + dir))
                {
                    walls.Remove(edge);
                    WallChanged?.Invoke(edge);
                }
            }
            NotifyWallsAround(cell);
            GridChanged?.Invoke();
        }

        private void RefreshTile(Vector2Int cell)
        {
            TileBase tile = cell == entranceCell && entranceTile != null ? entranceTile : floor[cell].GetTile(cell);
            floorTilemap.SetTile((Vector3Int)cell, tile);
        }

        // Cambiar el suelo puede cambiar cómo se ven las paredes vecinas (corte automático).
        private void NotifyWallsAround(Vector2Int cell)
        {
            foreach (Vector2Int dir in Orthogonal)
            {
                WallEdge edge = WallEdge.Between(cell, cell + dir);
                if (walls.ContainsKey(edge)) WallChanged?.Invoke(edge);
            }
        }

        // =========================== Paredes ===========================

        public bool CanSetWall(WallEdge edge, WallKind kind)
        {
            if (!IsFloor(edge.FrontCell) && !IsFloor(edge.BackCell)) return false;
            if (edge.Equals(EntranceEdge) && kind != WallKind.Puerta) return false;

            // No partir un mueble de varias celdas al medio.
            PlacedFurniture front = GetFurnitureAt(edge.FrontCell);
            if (front != null && front == GetFurnitureAt(edge.BackCell)) return false;

            return !walls.TryGetValue(edge, out WallKind current) || current != kind;
        }

        public void SetWall(WallEdge edge, WallKind kind)
        {
            walls[edge] = kind;
            WallChanged?.Invoke(edge);
            GridChanged?.Invoke();
        }

        public bool RemoveWall(WallEdge edge)
        {
            if (!walls.Remove(edge)) return false;
            WallChanged?.Invoke(edge);
            GridChanged?.Invoke();
            return true;
        }

        /// <summary>Rodea todo el suelo con paredes, con una puerta en la entrada. Para partidas nuevas.</summary>
        public void BuildPerimeterWalls()
        {
            foreach (Vector2Int cell in new List<Vector2Int>(floor.Keys))
            foreach (Vector2Int dir in Orthogonal)
            {
                Vector2Int n = cell + dir;
                if (IsFloor(n)) continue;
                WallEdge edge = WallEdge.Between(cell, n);
                if (walls.ContainsKey(edge)) continue;
                walls[edge] = edge.Equals(EntranceEdge) ? WallKind.Puerta : WallKind.Pared;
            }
            LayoutReloaded?.Invoke();
            GridChanged?.Invoke();
        }

        // =========================== Carga de partida ===========================

        /// <summary>Reemplaza suelo y paredes. Los muebles los vuelve a colocar quien carga.</summary>
        public void LoadLayout(IEnumerable<KeyValuePair<Vector2Int, FloorTypeData>> floorData,
                               IEnumerable<KeyValuePair<WallEdge, WallKind>> wallData)
        {
            floor.Clear();
            walls.Clear();
            occupancy.Clear();
            allFurniture.Clear();
            floorTilemap.ClearAllTiles();

            foreach (KeyValuePair<Vector2Int, FloorTypeData> kv in floorData)
                if (kv.Value != null) floor[kv.Key] = kv.Value;
            if (!floor.ContainsKey(entranceCell)) floor[entranceCell] = database.DefaultFloor;
            floor.Remove(OutsideCell);

            foreach (Vector2Int cell in floor.Keys) RefreshTile(cell);
            foreach (KeyValuePair<WallEdge, WallKind> kv in wallData) walls[kv.Key] = kv.Value;

            LayoutReloaded?.Invoke();
            GridChanged?.Invoke();
        }

        // =========================== Coordenadas ===========================

        public Vector2Int WorldToCell(Vector3 worldPosition) => (Vector2Int)grid.WorldToCell(worldPosition);

        /// <summary>Esquina inferior del rombo de la celda.</summary>
        public Vector3 CellToWorld(Vector2Int cell) => grid.CellToWorld((Vector3Int)cell);

        /// <summary>Centro del rombo de la celda (con z = 0).</summary>
        public Vector3 CellCenterWorld(Vector2Int cell) => CellToWorldInterpolated(new Vector2(cell.x + 0.5f, cell.y + 0.5f));

        /// <summary>Posición en el mundo de una coordenada de celda fraccionaria (z = 0).</summary>
        public Vector3 CellToWorldInterpolated(Vector2 cellPosition)
        {
            Vector3 p = grid.LocalToWorld(grid.CellToLocalInterpolated(new Vector3(cellPosition.x, cellPosition.y, 0f)));
            p.z = 0f;
            return p;
        }

        /// <summary>El borde de celda más cercano a un punto del mundo (para colocar paredes con el mouse).</summary>
        public WallEdge WorldToEdge(Vector3 worldPosition)
        {
            Vector3 f = grid.LocalToCellInterpolated(grid.WorldToLocal(worldPosition));
            int cx = Mathf.FloorToInt(f.x), cy = Mathf.FloorToInt(f.y);
            float u = f.x - cx, v = f.y - cy;
            var cell = new Vector2Int(cx, cy);

            float toHighX = 1f - u, toLowX = u, toHighY = 1f - v, toLowY = v;
            float min = Mathf.Min(Mathf.Min(toHighX, toLowX), Mathf.Min(toHighY, toLowY));

            if (min == toHighX) return new WallEdge(cell, EdgeAxis.X);
            if (min == toLowX) return new WallEdge(cell + Vector2Int.left, EdgeAxis.X);
            if (min == toHighY) return new WallEdge(cell, EdgeAxis.Y);
            return new WallEdge(cell + Vector2Int.down, EdgeAxis.Y);
        }

        /// <summary>Extremos del borde en el mundo: el de más abajo en pantalla y el de más arriba.</summary>
        public void GetEdgeEnds(WallEdge edge, out Vector3 lower, out Vector3 upper)
        {
            upper = CellToWorld(edge.Cell + Vector2Int.one);
            lower = edge.Axis == EdgeAxis.X
                ? CellToWorld(edge.Cell + Vector2Int.right)
                : CellToWorld(edge.Cell + Vector2Int.up);
        }

        /// <summary>Rectángulo en el mundo que contiene todo el suelo (para limitar la cámara).</summary>
        public Rect GetFloorWorldRect()
        {
            if (floor.Count == 0) return new Rect(-1, -1, 2, 2);

            Vector2 min = new(float.MaxValue, float.MaxValue);
            Vector2 max = new(float.MinValue, float.MinValue);
            foreach (Vector2Int c in floor.Keys)
            {
                Vector3 bottom = CellToWorld(c);
                min = Vector2.Min(min, new Vector2(bottom.x - 0.5f, bottom.y));
                max = Vector2.Max(max, new Vector2(bottom.x + 0.5f, bottom.y + 0.5f));
            }
            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }
    }
}
