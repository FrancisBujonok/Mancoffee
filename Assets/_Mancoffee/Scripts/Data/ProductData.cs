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

        [Tooltip("Costo aproximado de insumos (solo informativo: los insumos se pagan al comprarlos).")]
        public int ingredientCost = 2;

        [Tooltip("Segundos de preparación.")]
        public float prepTime = 4f;

        public Sprite icon;

        [Tooltip("En qué se sirve en mesa. Las tazas que no hay limpias se reemplazan por vasos descartables.")]
        public DishKind dish = DishKind.Taza;

        [Tooltip("Insumos que se gastan al empezar a prepararlo.")]
        public System.Collections.Generic.List<IngredientUse> ingredients = new();

        /// <summary>Costo real de insumos por unidad, según el precio de cada paquete.</summary>
        public float UnitCost
        {
            get
            {
                if (ingredients == null || ingredients.Count == 0) return ingredientCost;
                float total = 0f;
                foreach (IngredientUse use in ingredients)
                    if (use.ingredient != null) total += use.ingredient.UnitCost * use.amount;
                return total;
            }
        }
    }
}
