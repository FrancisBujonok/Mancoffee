using System.Collections.Generic;
using System.Linq;
using Mancoffee.Data;
using UnityEngine;

namespace Mancoffee.Core
{
    /// <summary>
    /// Arma los consejos del post-it "Para mañana" a partir de lo que pasó en el día.
    /// Son reglas simples: el motivo de enojo más común, lo más vendido y si el día cerró en rojo.
    /// </summary>
    public static class DayInsights
    {
        public static List<string> Build(DailyLedger day)
        {
            var tips = new List<string>();

            if (day.Angry > 0 && day.AngryReasons.Count > 0)
            {
                KeyValuePair<AngryReason, int> top = day.AngryReasons.OrderByDescending(kv => kv.Value).First();
                string people = top.Value == 1 ? "Un cliente se fue" : $"{top.Value} clientes se fueron";
                switch (top.Key)
                {
                    case AngryReason.SinMesa:
                        tips.Add($"{people} sin encontrar mesa. Más mesas con sillas alrededor ayudarían.");
                        break;
                    case AngryReason.SinMozo:
                        tips.Add($"{people} sin que nadie les tomara el pedido. Hace falta alguien más en el salón.");
                        break;
                    case AngryReason.SinPedido:
                    {
                        ProductData product = day.AngryProducts.Count > 0
                            ? day.AngryProducts.OrderByDescending(kv => kv.Value).First().Key
                            : null;
                        if (product != null && product.station != null)
                            tips.Add($"{people} esperando su {product.displayName.ToLowerInvariant()}. Otra {product.station.displayName.ToLowerInvariant()} o alguien más en el turno lo resolvería.");
                        else
                            tips.Add($"{people} esperando su pedido. Sumá personal o estaciones.");
                        break;
                    }
                    case AngryReason.SinCuenta:
                        tips.Add($"{people} esperando la cuenta. Cobrar rápido libera mesas.");
                        break;
                    case AngryReason.Caro:
                        tips.Add($"{people} sin pedir porque les pareció caro. Revisá los precios en la carta (M).");
                        break;
                    case AngryReason.SinStock:
                        tips.Add($"{people} porque faltaban insumos. Pedí en la despensa (I) antes de que se acaben: tarda en llegar.");
                        break;
                    case AngryReason.SinSilla:
                        tips.Add($"{people} porque les moviste la silla. Mejor reacomodar con el local cerrado.");
                        break;
                }
            }
            else if (day.Served > 0)
            {
                tips.Add("¡Nadie se fue enojado! El turno tuvo buen ritmo.");
            }

            if (day.Sold.Count > 0)
            {
                KeyValuePair<ProductData, int> best = day.Sold.OrderByDescending(kv => kv.Value).First();
                int price = Inventory.Instance != null ? Inventory.Instance.PriceOf(best.Key) : best.Key.price;
                int margin = Mathf.RoundToInt(price - best.Key.UnitCost);
                tips.Add($"{best.Key.displayName} fue lo más pedido ({best.Value}): deja ${margin} de ganancia por unidad.");
            }

            if (day.ServedWithoutCup > 0)
                tips.Add($"Serviste {day.ServedWithoutCup} bebidas en vaso descartable por falta de tazas limpias. Lavar antes del pico (o comprar más tazas) deja más propina.");

            if (day.SlowPreparations > 2)
                tips.Add($"La cafetera trabajó lenta {day.SlowPreparations} veces. Purgarla antes del pico de oficinistas ahorra tiempo.");

            if (day.Wasted.Count > 0)
            {
                string list = string.Join(", ", day.Wasted.Select(kv => $"{kv.Value} {kv.Key.unitName} de {kv.Key.displayName.ToLowerInvariant()}"));
                tips.Add($"Se venció: {list}. Pedí menos pastelería o pedila más cerca del momento de venderla.");
            }

            if (day.Net < 0)
                tips.Add("El día cerró en negativo. Revisá sueldos y obras antes de seguir gastando.");

            if (tips.Count == 0)
                tips.Add("Día tranquilo. Probá abrir con más mesas para atraer gente.");

            return tips.Take(4).ToList();
        }
    }
}
