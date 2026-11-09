using System.Collections.Generic;
using System.Linq;
using Mancoffee.Data;
using UnityEngine;

namespace Mancoffee.Core
{
    /// <summary>Un lote de un insumo que llegó junto (comparte vencimiento).</summary>
    public class StockBatch
    {
        public IngredientData Ingredient;
        public int Amount;

        /// <summary>Último día en que se puede usar. int.MaxValue = no se vence.</summary>
        public int ExpiresDay;
    }

    /// <summary>Un pedido al proveedor que todavía no llegó.</summary>
    public class Delivery
    {
        public IngredientData Ingredient;
        public int Amount;
        public int ArriveDay;
        public float ArriveMinutes;
    }

    /// <summary>
    /// La despensa y la vajilla del local: insumos por lote (con vencimiento), pedidos en camino,
    /// tazas y platos limpios, y los precios que fijó el jugador para cada producto.
    /// </summary>
    [DefaultExecutionOrder(-85)]
    public class Inventory : MonoBehaviour
    {
        public static Inventory Instance { get; private set; }

        [SerializeField] private GameDatabase database;
        [Tooltip("Minutos de juego que tarda en llegar un pedido hecho con el local abierto.")]
        [SerializeField] private float deliveryMinutes = 60f;
        [SerializeField] private int startingCups = 10;
        [SerializeField] private int startingPlates = 8;
        [SerializeField] private int dishPackSize = 4;
        [SerializeField] private int cupPackPrice = 12;
        [SerializeField] private int platePackPrice = 10;

        private readonly List<StockBatch> batches = new();
        private readonly List<Delivery> deliveries = new();
        private readonly Dictionary<ProductData, int> prices = new();
        private readonly int[] totalDishes = new int[2];
        private readonly int[] cleanDishes = new int[2];
        private readonly HashSet<IngredientData> warnedEmpty = new();
        private int expiredDay = -1;

        public GameDatabase Database => database;
        public IReadOnlyList<Delivery> Deliveries => deliveries;
        public IReadOnlyList<StockBatch> Batches => batches;
        public int DishPackSize => dishPackSize;
        public float DeliveryMinutes => deliveryMinutes;

        /// <summary>Mientras se carga una partida (para que los muebles destruidos no devuelvan vajilla).</summary>
        public bool IsLoading { get; set; }

        private static CafeState Cafe => CafeState.Instance;
        private static int Today => CafeState.Instance != null ? CafeState.Instance.Day : 1;

        private void Awake()
        {
            Instance = this;
            if (database == null && GridSystem.GridManager.Instance != null) database = GridSystem.GridManager.Instance.Database;
            ResetNewGame();
        }

        // =========================== Partida ===========================

        public void ResetNewGame()
        {
            batches.Clear();
            deliveries.Clear();
            prices.Clear();
            warnedEmpty.Clear();
            totalDishes[(int)DishKind.Taza] = cleanDishes[(int)DishKind.Taza] = startingCups;
            totalDishes[(int)DishKind.Plato] = cleanDishes[(int)DishKind.Plato] = startingPlates;
            if (database == null) return;
            foreach (IngredientData i in database.ingredients)
                if (i != null && i.startPacks > 0) Add(i, i.startPacks * i.packSize, ExpiryFor(i, Today));
        }

        public void LoadDishes(int cups, int cleanCups, int plates, int cleanPlates)
        {
            totalDishes[0] = Mathf.Max(0, cups);
            cleanDishes[0] = Mathf.Clamp(cleanCups, 0, totalDishes[0]);
            totalDishes[1] = Mathf.Max(0, plates);
            cleanDishes[1] = Mathf.Clamp(cleanPlates, 0, totalDishes[1]);
        }

        public void ClearStock()
        {
            batches.Clear();
            deliveries.Clear();
            prices.Clear();
            warnedEmpty.Clear();
        }

        public void AddDelivery(IngredientData i, int amount, int day, float minutes) =>
            deliveries.Add(new Delivery { Ingredient = i, Amount = amount, ArriveDay = day, ArriveMinutes = minutes });

        // =========================== Insumos ===========================

        public int Count(IngredientData ingredient)
        {
            int n = 0;
            foreach (StockBatch b in batches) if (b.Ingredient == ingredient) n += b.Amount;
            return n;
        }

        /// <summary>Cuánto de un insumo se tira hoy al cerrar.</summary>
        public int ExpiringToday(IngredientData ingredient)
        {
            int n = 0;
            foreach (StockBatch b in batches) if (b.Ingredient == ingredient && b.ExpiresDay <= Today) n += b.Amount;
            return n;
        }

        public int Incoming(IngredientData ingredient)
        {
            int n = 0;
            foreach (Delivery d in deliveries) if (d.Ingredient == ingredient) n += d.Amount;
            return n;
        }

        public bool HasIngredients(ProductData product) => Missing(product).Count == 0;

        public List<IngredientData> Missing(ProductData product)
        {
            var missing = new List<IngredientData>();
            if (product?.ingredients == null) return missing;
            foreach (IngredientUse use in product.ingredients)
                if (use.ingredient != null && Count(use.ingredient) < use.amount) missing.Add(use.ingredient);
            return missing;
        }

        /// <summary>Gasta los insumos de un producto (los que se vencen antes, primero).</summary>
        public bool ConsumeIngredients(ProductData product)
        {
            if (!HasIngredients(product)) return false;
            if (product.ingredients != null)
                foreach (IngredientUse use in product.ingredients)
                    if (use.ingredient != null) Consume(use.ingredient, use.amount);
            return true;
        }

        public bool Consume(IngredientData ingredient, int amount)
        {
            if (ingredient == null || amount <= 0) return true;
            if (Count(ingredient) < amount) return false;

            foreach (StockBatch b in batches.Where(b => b.Ingredient == ingredient).OrderBy(b => b.ExpiresDay).ToList())
            {
                int take = Mathf.Min(b.Amount, amount);
                b.Amount -= take;
                amount -= take;
                if (b.Amount <= 0) batches.Remove(b);
                if (amount <= 0) break;
            }

            if (Count(ingredient) == 0 && warnedEmpty.Add(ingredient))
                Cafe?.Notify($"Se acabó: {ingredient.displayName.ToLowerInvariant()}. Pedí más en la Despensa (I)", NoteKind.Malo);
            return true;
        }

        public void Add(IngredientData ingredient, int amount, int expiresDay)
        {
            if (ingredient == null || amount <= 0) return;
            StockBatch same = batches.Find(b => b.Ingredient == ingredient && b.ExpiresDay == expiresDay);
            if (same != null) same.Amount += amount;
            else batches.Add(new StockBatch { Ingredient = ingredient, Amount = amount, ExpiresDay = expiresDay });
            warnedEmpty.Remove(ingredient);
        }

        public static int ExpiryFor(IngredientData ingredient, int arrivalDay) =>
            ingredient.shelfDays > 0 ? arrivalDay + ingredient.shelfDays - 1 : int.MaxValue;

        /// <summary>Pide un paquete al proveedor. Con el local abierto tarda; con el local cerrado llega al abrir.</summary>
        public bool Order(IngredientData ingredient, int packs = 1)
        {
            if (ingredient == null || packs <= 0) return false;
            if (!Cafe.TrySpend(ingredient.packPrice * packs, MoneyCategory.Ingredientes)) return false;

            DayCycle day = DayCycle.Instance;
            var delivery = new Delivery { Ingredient = ingredient, Amount = ingredient.packSize * packs };
            if (day == null)
            {
                delivery.ArriveDay = Today;
                delivery.ArriveMinutes = 0f;
            }
            else if (day.Phase == DayPhase.Resumen)
            {
                delivery.ArriveDay = Today + 1;
                delivery.ArriveMinutes = day.OpenMinutes;
            }
            else
            {
                delivery.ArriveDay = Today;
                delivery.ArriveMinutes = day.Minutes + deliveryMinutes;
            }
            deliveries.Add(delivery);
            Cafe.Notify($"Pediste {ingredient.packName.ToLowerInvariant()}: llega {ArrivalText(delivery)}");
            return true;
        }

        public string ArrivalText(Delivery d)
        {
            if (d.ArriveDay > Today) return "al abrir mañana";
            int h = (int)(d.ArriveMinutes / 60f) % 24, m = (int)(d.ArriveMinutes % 60f);
            return $"a las {h:00}:{m:00}";
        }

        // =========================== Vajilla ===========================

        public int TotalDishes(DishKind kind) => totalDishes[(int)kind];
        public int CleanDishes(DishKind kind) => cleanDishes[(int)kind];
        public int DirtyDishes(DishKind kind) => totalDishes[(int)kind] - cleanDishes[(int)kind];
        public int DishPackPrice(DishKind kind) => kind == DishKind.Taza ? cupPackPrice : platePackPrice;

        /// <summary>Saca una pieza limpia para servir. False si no queda ninguna.</summary>
        public bool TakeClean(DishKind kind)
        {
            if (cleanDishes[(int)kind] <= 0) return false;
            cleanDishes[(int)kind]--;
            return true;
        }

        /// <summary>Vuelven piezas limpias (lavadas, o que nunca llegaron a usarse).</summary>
        public void ReturnClean(DishKind kind, int amount)
        {
            int k = (int)kind;
            cleanDishes[k] = Mathf.Min(totalDishes[k], cleanDishes[k] + Mathf.Max(0, amount));
        }

        public bool BuyDishes(DishKind kind)
        {
            if (!Cafe.TrySpend(DishPackPrice(kind), MoneyCategory.Construccion)) return false;
            totalDishes[(int)kind] += dishPackSize;
            cleanDishes[(int)kind] += dishPackSize;
            Cafe.Notify($"Compraste {dishPackSize} {(kind == DishKind.Taza ? "tazas" : "platos")}", NoteKind.Bueno);
            return true;
        }

        // =========================== Precios ===========================

        public int PriceOf(ProductData product) =>
            product == null ? 0 : prices.TryGetValue(product, out int p) ? p : product.price;

        public float PriceRatio(ProductData product) =>
            product == null || product.price <= 0 ? 1f : (float)PriceOf(product) / product.price;

        public void SetPrice(ProductData product, int price)
        {
            if (product == null) return;
            price = Mathf.Clamp(price, 1, Mathf.Max(2, product.price * 3));
            if (price == product.price) prices.Remove(product);
            else prices[product] = price;
        }

        public IEnumerable<KeyValuePair<ProductData, int>> CustomPrices => prices;

        // =========================== Ciclo ===========================

        private void Update()
        {
            DayCycle day = DayCycle.Instance;
            if (day == null) return;

            for (int i = deliveries.Count - 1; i >= 0; i--)
            {
                Delivery d = deliveries[i];
                bool arrived = Today > d.ArriveDay || (Today == d.ArriveDay && day.Phase != DayPhase.Resumen && day.Minutes >= d.ArriveMinutes);
                if (!arrived) continue;
                deliveries.RemoveAt(i);
                Add(d.Ingredient, d.Amount, ExpiryFor(d.Ingredient, Today));
                Cafe.Notify($"Llegó el pedido: {d.Amount} {d.Ingredient.unitName} de {d.Ingredient.displayName.ToLowerInvariant()}", NoteKind.Bueno);
            }

            // Al cerrar el día se tira lo que vence hoy (queda en el resumen).
            if (day.Phase == DayPhase.Resumen && expiredDay != Today)
            {
                expiredDay = Today;
                ExpireUpTo(Today);
            }
        }

        private void ExpireUpTo(int day)
        {
            for (int i = batches.Count - 1; i >= 0; i--)
            {
                StockBatch b = batches[i];
                if (b.ExpiresDay > day) continue;
                batches.RemoveAt(i);
                DailyLedger ledger = Cafe.Today;
                ledger.Wasted.TryGetValue(b.Ingredient, out int v);
                ledger.Wasted[b.Ingredient] = v + b.Amount;
            }
        }
    }
}
