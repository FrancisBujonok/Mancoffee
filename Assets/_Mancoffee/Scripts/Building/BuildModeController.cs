using System.Collections.Generic;
using Mancoffee.Core;
using Mancoffee.Data;
using Mancoffee.GridSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mancoffee.Building
{
    public enum BuildTool
    {
        Muebles,
        Paredes,
        Puertas,
        Ventanas,
        Suelo
    }

    /// <summary>
    /// Modo construcción: muebles (colocar, rotar, mover, quitar), paredes, puertas, ventanas y suelo.
    /// Traduce la entrada del jugador en pedidos al GridManager y cobra cada obra a CafeState.
    /// </summary>
    public class BuildModeController : MonoBehaviour
    {
        [SerializeField] private GridManager gridManager;
        [SerializeField] private Camera cam;
        [SerializeField] private GameDatabase database;
        [SerializeField] private Transform furnitureParent;
        [SerializeField] private SpriteRenderer cellCursor;
        [SerializeField] private WallRenderer wallRenderer;

        [Header("Precios de obra")]
        [SerializeField] private int wallPrice = 10;
        [SerializeField] private int doorPrice = 60;
        [SerializeField] private int windowPrice = 40;
        [SerializeField, Range(0f, 1f)] private float refundRatio = 0.5f;

        [Header("Colores de previsualización")]
        [SerializeField] private Color validColor = new(0.45f, 1f, 0.55f, 0.75f);
        [SerializeField] private Color invalidColor = new(1f, 0.35f, 0.35f, 0.75f);

        private bool buildMode;
        private BuildTool tool;
        private int selectedIndex = -1;
        private int floorIndex;
        private int rotation;
        private Vector2Int hoverCell;
        private WallEdge hoverEdge;
        private SpriteRenderer ghost;

        // Mueble que el jugador levantó para moverlo, y dónde estaba (para cancelar).
        private PlacedFurniture moving;
        private Vector2Int movingOriginalOrigin;
        private int movingOriginalRotation;

        public bool IsBuildMode => buildMode;
        public BuildTool Tool => tool;
        public int SelectedIndex => selectedIndex;
        public int FloorIndex => floorIndex;
        public PlacedFurniture Moving => moving;
        public IReadOnlyList<FurnitureData> Catalog => database.furniture;
        public IReadOnlyList<FloorTypeData> FloorTypes => database.floorTypes;

        public WallKind CurrentWallKind => tool switch
        {
            BuildTool.Puertas => WallKind.Puerta,
            BuildTool.Ventanas => WallKind.Ventana,
            _ => WallKind.Pared
        };

        public int WallPrice(WallKind kind) => kind switch
        {
            WallKind.Puerta => doorPrice,
            WallKind.Ventana => windowPrice,
            _ => wallPrice
        };

        private CafeState Cafe => CafeState.Instance;

        private FurnitureData SelectedFromCatalog =>
            selectedIndex >= 0 && selectedIndex < Catalog.Count ? Catalog[selectedIndex] : null;

        /// <summary>Lo que se previsualiza: el mueble que se mueve o el elegido del catálogo.</summary>
        private FurnitureData CurrentData => moving != null ? moving.Data : SelectedFromCatalog;

        public string HelpText
        {
            get
            {
                switch (tool)
                {
                    case BuildTool.Muebles:
                        if (moving != null)
                            return $"Moviendo {moving.Data.displayName}. Clic izquierdo: soltar · R: rotar · Clic derecho o Esc: cancelar";
                        if (SelectedFromCatalog != null)
                            return "Clic izquierdo: colocar · R: rotar · Clic derecho: quitar (devuelve 50%) · Esc: soltar";
                        return "Elegí un mueble (1-9) o hacé clic en uno colocado para moverlo. Clic derecho: quitar.";
                    case BuildTool.Paredes:
                        return "Clic izquierdo (arrastrando): levantar paredes · Clic derecho: quitar";
                    case BuildTool.Puertas:
                        return "Clic izquierdo en un borde: poner puerta · Clic derecho: quitar";
                    case BuildTool.Ventanas:
                        return "Clic izquierdo en un borde: poner ventana · Clic derecho: quitar";
                    default:
                        return "Clic izquierdo (arrastrando): pintar o ampliar suelo · Clic derecho: quitar suelo vacío";
                }
            }
        }

        private void Awake()
        {
            if (cam == null) cam = Camera.main;

            var ghostObject = new GameObject("Fantasma");
            ghostObject.transform.SetParent(transform, false);
            ghost = ghostObject.AddComponent<SpriteRenderer>();
            ghost.sortingOrder = 1000;
            ghost.enabled = false;

            if (cellCursor != null) cellCursor.enabled = false;
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (kb.bKey.wasPressedThisFrame && !UiBlocker.ModalOpen) SetBuildMode(!buildMode);
            if (!buildMode) return;

            HandleKeys(kb);

            Vector2 screen = mouse.position.ReadValue();
            Vector3 world = cam.ScreenToWorldPoint(screen);
            world.z = 0f;
            hoverCell = gridManager.WorldToCell(world);
            hoverEdge = gridManager.WorldToEdge(world);

            bool overUI = UiBlocker.IsPointerOverUI(screen);
            UpdatePreview(overUI);
            if (overUI) return;

            switch (tool)
            {
                case BuildTool.Muebles: HandleFurnitureInput(mouse); break;
                case BuildTool.Suelo: HandleFloorInput(mouse); break;
                default: HandleWallInput(mouse); break;
            }
        }

        // =========================== Selección ===========================

        public void SetBuildMode(bool active)
        {
            buildMode = active;
            if (active) return;
            if (moving != null) CancelMove();
            HidePreview();
        }

        public void SetTool(BuildTool newTool)
        {
            if (moving != null) CancelMove();
            tool = newTool;
        }

        public void SelectFurniture(int index)
        {
            if (moving != null) CancelMove();
            tool = BuildTool.Muebles;
            selectedIndex = selectedIndex == index ? -1 : index;
        }

        public void SelectFloor(int index)
        {
            tool = BuildTool.Suelo;
            floorIndex = Mathf.Clamp(index, 0, FloorTypes.Count - 1);
        }

        private void HandleKeys(Keyboard kb)
        {
            for (int i = 0; i < 9; i++)
            {
                if (!kb[(Key)((int)Key.Digit1 + i)].wasPressedThisFrame) continue;
                if (tool == BuildTool.Muebles && i < Catalog.Count) SelectFurniture(i);
                else if (tool == BuildTool.Suelo && i < FloorTypes.Count) SelectFloor(i);
            }

            if (kb.rKey.wasPressedThisFrame) rotation = (rotation + 1) % 4;

            if (kb.escapeKey.wasPressedThisFrame)
            {
                if (moving != null) CancelMove();
                else selectedIndex = -1;
            }
        }

        // =========================== Previsualización ===========================

        private void HidePreview()
        {
            ghost.enabled = false;
            if (cellCursor != null) cellCursor.enabled = false;
        }

        private void ShowCursor(Color color)
        {
            if (cellCursor == null) return;
            cellCursor.enabled = true;
            cellCursor.color = color;
            cellCursor.transform.position = gridManager.CellCenterWorld(hoverCell);
        }

        private void UpdatePreview(bool overUI)
        {
            HidePreview();
            if (overUI) return;

            switch (tool)
            {
                case BuildTool.Muebles:
                {
                    if (gridManager.IsFloor(hoverCell)) ShowCursor(Color.white);
                    FurnitureData data = CurrentData;
                    if (data == null) break;

                    ghost.enabled = true;
                    ghost.sprite = data.GetSprite(rotation);
                    ghost.transform.position = gridManager.CellToWorld(hoverCell);
                    bool ok = gridManager.CanPlace(data.GetCells(hoverCell, rotation)) &&
                              (moving != null || Cafe.CanAfford(data.price));
                    ghost.color = ok ? validColor : invalidColor;
                    break;
                }
                case BuildTool.Suelo:
                {
                    FloorTypeData type = FloorTypes[floorIndex];
                    bool ok = gridManager.CanSetFloor(hoverCell, type) && Cafe.CanAfford(type.price);
                    ShowCursor(ok ? validColor : invalidColor);
                    break;
                }
                default:
                {
                    WallKind kind = CurrentWallKind;
                    ghost.enabled = true;
                    ghost.sprite = wallRenderer.GetSprite(kind, hoverEdge.Axis, false);
                    gridManager.GetEdgeEnds(hoverEdge, out Vector3 lower, out _);
                    ghost.transform.position = lower;
                    bool ok = gridManager.CanSetWall(hoverEdge, kind) && Cafe.CanAfford(WallPrice(kind));
                    ghost.color = ok ? validColor : invalidColor;
                    break;
                }
            }
        }

        // =========================== Muebles ===========================

        private void HandleFurnitureInput(Mouse mouse)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                if (moving != null) TryDropMoving();
                else if (SelectedFromCatalog != null) TryPlaceNew();
                else TryPickUp();
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                if (moving != null) CancelMove();
                else TryRemove();
            }
        }

        private void TryPlaceNew()
        {
            FurnitureData data = SelectedFromCatalog;
            if (!gridManager.CanPlace(data.GetCells(hoverCell, rotation))) return;
            if (!Cafe.TrySpend(data.price, MoneyCategory.Construccion)) return;

            PlacedFurniture placed = FurnitureFactory.Create(data, hoverCell, rotation, gridManager, furnitureParent);
            gridManager.Occupy(placed);
        }

        private void TryRemove()
        {
            PlacedFurniture furniture = gridManager.GetFurnitureAt(hoverCell);
            if (furniture == null) return;

            gridManager.Release(furniture);
            Cafe.AddIncome(Mathf.RoundToInt(furniture.Data.price * refundRatio), MoneyCategory.Reembolsos);
            Destroy(furniture.gameObject);
        }

        /// <summary>Levanta el mueble bajo el cursor: libera sus celdas y pasa a ser el fantasma.</summary>
        private void TryPickUp()
        {
            PlacedFurniture furniture = gridManager.GetFurnitureAt(hoverCell);
            if (furniture == null) return;

            moving = furniture;
            movingOriginalOrigin = furniture.Origin;
            movingOriginalRotation = furniture.Rotation;
            rotation = furniture.Rotation;

            gridManager.Release(furniture);
            furniture.SetVisible(false);
        }

        private void TryDropMoving()
        {
            if (!gridManager.CanPlace(moving.Data.GetCells(hoverCell, rotation))) return;
            PutDown(hoverCell, rotation);
        }

        /// <summary>Devuelve el mueble a su lugar. Siempre es válido: mientras está levantado no se puede construir otra cosa.</summary>
        private void CancelMove() => PutDown(movingOriginalOrigin, movingOriginalRotation);

        private void PutDown(Vector2Int origin, int rot)
        {
            moving.Relocate(origin, rot);
            moving.SetVisible(true);
            gridManager.Occupy(moving);
            moving = null;
        }

        // =========================== Paredes, puertas y ventanas ===========================

        private void HandleWallInput(Mouse mouse)
        {
            WallKind kind = CurrentWallKind;
            bool drag = tool == BuildTool.Paredes;
            bool place = drag ? mouse.leftButton.isPressed : mouse.leftButton.wasPressedThisFrame;
            bool erase = drag ? mouse.rightButton.isPressed : mouse.rightButton.wasPressedThisFrame;

            if (place && gridManager.CanSetWall(hoverEdge, kind) &&
                Cafe.TrySpend(WallPrice(kind), MoneyCategory.Construccion))
            {
                gridManager.SetWall(hoverEdge, kind);
            }
            else if (erase && gridManager.TryGetWall(hoverEdge, out WallKind existing))
            {
                gridManager.RemoveWall(hoverEdge);
                Cafe.AddIncome(Mathf.RoundToInt(WallPrice(existing) * refundRatio), MoneyCategory.Reembolsos);
            }
        }

        // =========================== Suelo ===========================

        private void HandleFloorInput(Mouse mouse)
        {
            FloorTypeData type = FloorTypes[floorIndex];

            if (mouse.leftButton.isPressed && gridManager.CanSetFloor(hoverCell, type) &&
                Cafe.TrySpend(type.price, MoneyCategory.Construccion))
            {
                gridManager.SetFloor(hoverCell, type);
            }
            else if (mouse.rightButton.isPressed && gridManager.CanRemoveFloor(hoverCell))
            {
                gridManager.RemoveFloor(hoverCell);
            }
        }
    }
}
