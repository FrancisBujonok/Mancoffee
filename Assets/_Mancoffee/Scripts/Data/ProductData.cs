using UnityEngine;

namespace Mancoffee.Data
{
    /// <summary>
    /// Algo que se vende en el menú: dónde se prepara, cuánto tarda, cuánto cuesta y a cuánto se vende.
    /// Un producto solo aparece en el menú si en el local hay al menos una estación de su tipo.
    /// </summary>
    [CreateAssetMenu(menuName = "Mancoffee/Producto", fileName = "NuevoProducto")]
    public class ProductData : ScriptableObject
    {
        public string id;
        public string displayName;

        [Tooltip("Mueble donde se prepara (por ejemplo, la máquina de café).")]
        public FurnitureData station;

        [Tooltip("Precio de venta.")]
        public int price = 6;

        [Tooltip("Costo de ingredientes, se descuenta al empezar a prepararlo.")]
        public int ingredientCost = 2;

        [Tooltip("Segundos de preparación.")]
        public float prepTime = 4f;

        public Sprite icon;
    }
}
