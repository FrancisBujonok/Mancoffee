using System;
using UnityEngine;

namespace Mancoffee.Data
{
    /// <summary>Tipo de vajilla. Las bebidas van en taza y la pastelería en plato.</summary>
    public enum DishKind
    {
        Taza,
        Plato
    }

    /// <summary>Cuánto de un insumo usa un producto.</summary>
    [Serializable]
    public struct IngredientUse
    {
        public IngredientData ingredient;
        public int amount;
    }

    /// <summary>
    /// Un insumo que se compra al proveedor por paquete (café en grano, leche, vasos descartables...).
    /// Los perecederos se vencen a los <see cref="shelfDays"/> días de llegar.
    /// </summary>
    [CreateAssetMenu(menuName = "Mancoffee/Insumo", fileName = "NuevoInsumo")]
    public class IngredientData : ScriptableObject
    {
        public string id;
        public string displayName;

        [Tooltip("Cómo se llama una unidad: dosis, porciones, vasos...")]
        public string unitName = "unidades";

        [Tooltip("Cómo se llama el paquete que se compra: Bolsa de 20 dosis...")]
        public string packName = "Paquete";

        public int packSize = 10;
        public int packPrice = 10;

        [Tooltip("0 = no se vence. 1 = se tira al cerrar el día en que llegó.")]
        public int shelfDays;

        [Tooltip("Paquetes con los que arranca una partida nueva.")]
        public int startPacks = 1;

        public Sprite icon;

        public float UnitCost => packSize > 0 ? (float)packPrice / packSize : 0f;
    }
}
