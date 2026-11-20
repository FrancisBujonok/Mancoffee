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

        [Header("Estados y animaciones (opcional)")]
        [Tooltip("Versión con vajilla sucia, una por rotación. Si está vacío se muestra un ícono encima.")]
        public Sprite[] dirtySprites = new Sprite[0];

        [Tooltip("Cuadros que se repiten todo el tiempo (planta que se mece, brillo de la vitrina). Reemplazan al sprite.")]
        public Sprite[] idleFrames = new Sprite[0];
        public float idleFps = 4f;

        [Tooltip("Capa que se dibuja encima mientras un empleado lo usa (sale café, corre el agua).")]
        public Sprite[] workFrames = new Sprite[0];
        public float workFps = 6f;

        [Tooltip("Capa que se dibuja encima cuando necesita mantenimiento (vapor).")]
        public Sprite[] alertFrames = new Sprite[0];
        public float alertFps = 5f;

        public Sprite GetDirtySprite(int rotation)
        {
            if (dirtySprites == null || dirtySprites.Length == 0) return null;
            return dirtySprites[rotation % dirtySprites.Length];
        }

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
