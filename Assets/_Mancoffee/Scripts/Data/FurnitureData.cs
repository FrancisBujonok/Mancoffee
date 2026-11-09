using System.Collections.Generic;
using UnityEngine;

namespace Mancoffee.Data
{
    public enum FurnitureCategory
    {
        Mesa,
        Asiento,
        Mostrador,
        Equipo,
        Decoracion
    }

    /// <summary>
    /// Definición de un tipo de mueble (no una instancia colocada).
    /// Para sumar un mueble nuevo al juego se crea un asset de este tipo: no hace falta tocar código.
    /// </summary>
    [CreateAssetMenu(menuName = "Mancoffee/Mueble", fileName = "NuevoMueble")]
    public class FurnitureData : ScriptableObject
    {
        public string id;
        public string displayName;
        public FurnitureCategory category;

        [Tooltip("Celdas que ocupa en rotación 0 (X = hacia arriba-derecha, Y = hacia arriba-izquierda).")]
        public Vector2Int size = Vector2Int.one;

        public int price = 50;

        [Tooltip("Cada cuántos usos necesita mantenimiento (0 = nunca). Sin mantenimiento trabaja a mitad de velocidad.")]
        public int maintenanceEvery;

        [Tooltip("Es una bacha: ahí se deja y se lava la vajilla sucia.")]
        public bool isSink;

        [Tooltip("Un sprite por rotación (0 a 3). Si hay menos sprites que rotaciones, se reutilizan en ciclo.")]
        public Sprite[] sprites = new Sprite[0];

        /// <summary>En rotaciones impares el ancho y el fondo se intercambian.</summary>
        public Vector2Int GetSize(int rotation)
        {
            return rotation % 2 == 0 ? size : new Vector2Int(size.y, size.x);
        }

        public Sprite GetSprite(int rotation)
        {
            if (sprites == null || sprites.Length == 0) return null;
            return sprites[rotation % sprites.Length];
        }

        /// <summary>Todas las celdas que ocuparía este mueble con la celda de origen y rotación dadas.</summary>
        public IEnumerable<Vector2Int> GetCells(Vector2Int origin, int rotation)
        {
            Vector2Int s = GetSize(rotation);
            for (int x = 0; x < s.x; x++)
            for (int y = 0; y < s.y; y++)
                yield return new Vector2Int(origin.x + x, origin.y + y);
        }
    }
}
