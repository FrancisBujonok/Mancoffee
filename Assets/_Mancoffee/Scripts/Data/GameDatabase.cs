using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Mancoffee.Data
{
    /// <summary>
    /// Índice de todo el contenido del juego. El guardado usa los id de acá para reconstruir la partida.
    /// </summary>
    [CreateAssetMenu(menuName = "Mancoffee/Base de datos", fileName = "BaseDeDatos")]
    public class GameDatabase : ScriptableObject
    {
        public List<FurnitureData> furniture = new();
        public List<ProductData> products = new();

        [Tooltip("El primero es el suelo por defecto.")]
        public List<FloorTypeData> floorTypes = new();

        public FloorTypeData DefaultFloor => floorTypes.Count > 0 ? floorTypes[0] : null;

        public FurnitureData FindFurniture(string id) => furniture.Find(f => f != null && f.id == id);
        public FloorTypeData FindFloor(string id) => floorTypes.Find(f => f != null && f.id == id);

        public FloorTypeData FindFloorByTile(TileBase tile) =>
            floorTypes.Find(f => f != null && (f.tileA == tile || f.tileB == tile));
    }
}
