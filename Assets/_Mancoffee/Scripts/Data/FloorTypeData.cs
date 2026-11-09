using UnityEngine;
using UnityEngine.Tilemaps;

namespace Mancoffee.Data
{
    /// <summary>
    /// Un tipo de suelo (madera, baldosa, alfombra...). Usa dos tiles alternados en damero
    /// para que se distingan las celdas.
    /// </summary>
    [CreateAssetMenu(menuName = "Mancoffee/Tipo de suelo", fileName = "NuevoSuelo")]
    public class FloorTypeData : ScriptableObject
    {
        public string id;
        public string displayName;
        public TileBase tileA;
        public TileBase tileB;

        [Tooltip("Costo por celda al pintar o ampliar el suelo.")]
        public int price = 5;

        public TileBase GetTile(Vector2Int cell)
        {
            bool even = ((cell.x + cell.y) & 1) == 0;
            return even || tileB == null ? tileA : tileB;
        }
    }
}
