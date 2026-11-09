using System.Collections.Generic;
using Mancoffee.Data;
using Mancoffee.GridSystem;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mancoffee.Building
{
    /// <summary>
    /// Un mueble concreto en el local: qué es, dónde está, cómo está rotado y si alguien lo reservó.
    /// La raíz se ubica en el centro de las celdas que ocupa (desde ahí se calcula el orden de dibujado,
    /// lo que funciona bien junto a paredes); el sprite hijo se apoya en la punta inferior.
    /// </summary>
    [RequireComponent(typeof(SortingGroup))]
    public class PlacedFurniture : MonoBehaviour
    {
        private SpriteRenderer spriteRenderer;
        private GridManager grid;

        public FurnitureData Data { get; private set; }
        public Vector2Int Origin { get; private set; }
        public int Rotation { get; private set; }

        /// <summary>Falso mientras el jugador lo tiene levantado para moverlo.</summary>
        public bool IsPlaced { get; private set; }

        /// <summary>Quién lo reservó: el cliente que va a esa silla, el empleado que usa esa cafetera...</summary>
        public object ReservedBy { get; set; }

        public Vector2Int Size => Data.GetSize(Rotation);
        public IEnumerable<Vector2Int> OccupiedCells => Data.GetCells(Origin, Rotation);

        /// <summary>Punto usado para el orden de dibujado (centro de su huella).</summary>
        public Vector3 SortPosition => transform.position;

        public void Init(FurnitureData data, GridManager gridManager, SpriteRenderer renderer, Vector2Int origin, int rotation)
        {
            Data = data;
            grid = gridManager;
            spriteRenderer = renderer;
            Relocate(origin, rotation);
        }

        /// <summary>Cambia posición y rotación. No toca la ocupación de la grilla: eso lo hace GridManager.</summary>
        public void Relocate(Vector2Int origin, int rotation)
        {
            Origin = origin;
            Rotation = rotation;

            Vector2Int s = Size;
            transform.position = grid.CellToWorldInterpolated(new Vector2(origin.x + s.x * 0.5f, origin.y + s.y * 0.5f));
            spriteRenderer.transform.position = grid.CellToWorld(origin);
            spriteRenderer.sprite = Data.GetSprite(rotation);
            name = $"{Data.displayName} ({origin.x},{origin.y})";
        }

        public void SetVisible(bool visible) => spriteRenderer.enabled = visible;

        // Solo GridManager debería llamar a esto (al ocupar o liberar celdas).
        internal void SetPlaced(bool placed) => IsPlaced = placed;
    }

    /// <summary>Crea el GameObject de un mueble. No lo registra en la grilla: después hay que llamar a GridManager.Occupy.</summary>
    public static class FurnitureFactory
    {
        public static PlacedFurniture Create(FurnitureData data, Vector2Int origin, int rotation, GridManager grid, Transform parent)
        {
            var root = new GameObject(data.displayName);
            root.transform.SetParent(parent, false);

            var spriteObject = new GameObject("Sprite");
            spriteObject.transform.SetParent(root.transform, false);
            var renderer = spriteObject.AddComponent<SpriteRenderer>();

            var furniture = root.AddComponent<PlacedFurniture>();
            furniture.Init(data, grid, renderer, origin, rotation);
            return furniture;
        }
    }
}
