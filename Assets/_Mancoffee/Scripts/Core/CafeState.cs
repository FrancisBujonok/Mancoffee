using System.Collections.Generic;
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

    /// <summary>Movimientos de dinero y clientes de un día. Los gastos se guardan en positivo.</summary>
    public class DailyLedger
    {
        private readonly Dictionary<MoneyCategory, int> totals = new();

        public int Served;
        public int Angry;

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
    }

    /// <summary>
    /// Estado general de la cafetería: dinero, reputación, día actual y el balance de hoy.
    /// Todo gasto e ingreso pasa por acá, así el resumen del día siempre cuadra.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class CafeState : MonoBehaviour
    {
        public static CafeState Instance { get; private set; }

        [SerializeField] private int startingMoney = 1500;
        [SerializeField] private float startingReputation = 3f;

        private string message;
        private float messageUntil;

        public int Money { get; private set; }

        /// <summary>De 1 a 5. Sube con clientes contentos y baja con enojados; afecta cuántos clientes llegan.</summary>
        public float Reputation { get; private set; }

        public int Day { get; private set; }
        public DailyLedger Today { get; private set; } = new();

        /// <summary>Último aviso para el jugador, mientras siga vigente.</summary>
        public string Message => Time.unscaledTime < messageUntil ? message : null;

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
                Notify($"No alcanza el dinero (faltan ${amount - Money})");
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

        public void RegisterHappyCustomer()
        {
            Today.Served++;
            Reputation = Mathf.Min(5f, Reputation + 0.04f);
        }

        public void RegisterAngryCustomer()
        {
            Today.Angry++;
            Reputation = Mathf.Max(1f, Reputation - 0.1f);
        }

        public void StartNewDay()
        {
            Day++;
            Today = new DailyLedger();
        }

        public void Notify(string text, float seconds = 3f)
        {
            message = text;
            messageUntil = Time.unscaledTime + seconds;
        }
    }
}
