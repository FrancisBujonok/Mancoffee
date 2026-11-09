using System.Collections.Generic;
using Mancoffee.Data;
using UnityEngine;

namespace Mancoffee.Core
{
    public enum MoneyCategory
    {
        Ventas,
        Propinas,
        Reembolsos,
        Ingredientes,
        Construccion,
        Sueldos,
        Contrataciones
    }

    /// <summary>Por qué se fue enojado un cliente. Sirve para el resumen y los consejos del día.</summary>
    public enum AngryReason
    {
        SinMesa,
        SinMozo,
        SinPedido,
        SinCuenta,
        SinSilla,
        /// <summary>Le pareció todo caro y no pidió nada.</summary>
        Caro,
        /// <summary>No había insumos para lo que quería.</summary>
        SinStock
    }

    public enum NoteKind
    {
        Info,
        Bueno,
        Malo
    }

    public class GameNote
    {
        public int Id;
        public string Text;
        public NoteKind Kind;
        public float Time;
    }

    /// <summary>Movimientos de dinero y clientes de un día. Los gastos se guardan en positivo.</summary>
    public class DailyLedger
    {
        private readonly Dictionary<MoneyCategory, int> totals = new();

        public int Served;
        public int Angry;

        /// <summary>Productos cobrados hoy.</summary>
        public readonly Dictionary<ProductData, int> Sold = new();

        /// <summary>Motivos por los que se fueron clientes enojados.</summary>
        public readonly Dictionary<AngryReason, int> AngryReasons = new();

        /// <summary>Qué producto esperaban los que se fueron sin recibir su pedido.</summary>
        public readonly Dictionary<ProductData, int> AngryProducts = new();

        /// <summary>Insumos que se vencieron al cerrar.</summary>
        public readonly Dictionary<IngredientData, int> Wasted = new();

        /// <summary>Clientes atendidos por tipo (estudiante, oficinista...).</summary>
        public readonly Dictionary<string, int> ServedByType = new();

        /// <summary>Bebidas servidas en mesa con vaso descartable porque no había tazas limpias.</summary>
        public int ServedWithoutCup;

        /// <summary>Veces que la cafetera trabajó a mitad de velocidad por falta de purga.</summary>
        public int SlowPreparations;

        public void Add(MoneyCategory category, int amount)
        {
            totals.TryGetValue(category, out int value);
            totals[category] = value + amount;
        }

        public int Get(MoneyCategory category) => totals.TryGetValue(category, out int value) ? value : 0;

        public int Income => Get(MoneyCategory.Ventas) + Get(MoneyCategory.Propinas) + Get(MoneyCategory.Reembolsos);

        public int Expenses => Get(MoneyCategory.Ingredientes) + Get(MoneyCategory.Construccion) +
                               Get(MoneyCategory.Sueldos) + Get(MoneyCategory.Contrataciones);

        public int Net => Income - Expenses;

        public int OrdersSold
        {
            get
            {
                int n = 0;
                foreach (int v in Sold.Values) n += v;
                return n;
            }
        }

        public static void Increment<T>(Dictionary<T, int> dict, T key)
        {
            dict.TryGetValue(key, out int v);
            dict[key] = v + 1;
        }
    }

    /// <summary>
    /// Estado general de la cafetería: dinero, reputación, día actual, el balance de hoy y los avisos.
    /// Todo gasto e ingreso pasa por acá, así el resumen del día siempre cuadra.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class CafeState : MonoBehaviour
    {
        public static CafeState Instance { get; private set; }

        [SerializeField] private int startingMoney = 1500;
        [SerializeField] private float startingReputation = 3f;

        private readonly List<GameNote> notes = new();
        private int nextNoteId = 1;

        public int Money { get; private set; }

        /// <summary>De 1 a 5. Sube con clientes contentos y baja con enojados; afecta cuántos clientes llegan.</summary>
        public float Reputation { get; private set; }

        public int Day { get; private set; }
        public DailyLedger Today { get; private set; } = new();

        public IReadOnlyList<GameNote> Notes => notes;

        /// <summary>Último aviso, si tiene menos de 3 segundos.</summary>
        public string Message
        {
            get
            {
                GameNote note = LatestNote(3f);
                return note?.Text;
            }
        }

        private void Awake()
        {
            Instance = this;
            ResetNewGame();
        }

        public void ResetNewGame() => LoadState(1, startingMoney, startingReputation);

        public void LoadState(int day, int money, float reputation)
        {
            Day = day;
            Money = money;
            Reputation = reputation;
            Today = new DailyLedger();
        }

        public bool CanAfford(int amount) => Money >= amount;

        /// <summary>Gasta si alcanza; si no, avisa y devuelve false.</summary>
        public bool TrySpend(int amount, MoneyCategory category)
        {
            if (amount <= 0) return true;
            if (Money < amount)
            {
                Notify($"No alcanza el dinero (faltan ${amount - Money})", NoteKind.Malo);
                return false;
            }
            Money -= amount;
            Today.Add(category, amount);
            return true;
        }

        /// <summary>Gasta aunque no alcance (sueldos, ingredientes ya usados). La caja puede quedar en negativo.</summary>
        public void ForceSpend(int amount, MoneyCategory category)
        {
            Money -= amount;
            Today.Add(category, amount);
        }

        public void AddIncome(int amount, MoneyCategory category)
        {
            if (amount <= 0) return;
            Money += amount;
            Today.Add(category, amount);
        }

        /// <param name="satisfaction">1 = normal; baja si le cobraste caro o le serviste en vaso descartable.</param>
        public void RegisterHappyCustomer(ProductData product, float satisfaction = 1f, string profileId = null)
        {
            Today.Served++;
            if (product != null) DailyLedger.Increment(Today.Sold, product);
            if (!string.IsNullOrEmpty(profileId)) DailyLedger.Increment(Today.ServedByType, profileId);
            Reputation = Mathf.Min(5f, Reputation + 0.04f * Mathf.Clamp(satisfaction, 0.25f, 1.3f));
        }

        public void RegisterAngryCustomer(AngryReason reason, ProductData waitingFor)
        {
            Today.Angry++;
            DailyLedger.Increment(Today.AngryReasons, reason);
            if (reason == AngryReason.SinPedido && waitingFor != null) DailyLedger.Increment(Today.AngryProducts, waitingFor);
            Reputation = Mathf.Max(1f, Reputation - (reason == AngryReason.Caro ? 0.05f : 0.1f));
        }

        public void StartNewDay()
        {
            Day++;
            Today = new DailyLedger();
        }

        /// <summary>Deja un aviso para el jugador (los post-its de la interfaz).</summary>
        public void Notify(string text, NoteKind kind = NoteKind.Info)
        {
            notes.Add(new GameNote { Id = nextNoteId++, Text = text, Kind = kind, Time = UnityEngine.Time.unscaledTime });
            if (notes.Count > 20) notes.RemoveAt(0);
        }

        /// <summary>El aviso más reciente si no es más viejo que maxAge segundos.</summary>
        public GameNote LatestNote(float maxAge)
        {
            if (notes.Count == 0) return null;
            GameNote last = notes[notes.Count - 1];
            return UnityEngine.Time.unscaledTime - last.Time <= maxAge ? last : null;
        }
    }
}
