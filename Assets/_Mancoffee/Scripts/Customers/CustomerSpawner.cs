using System.Collections.Generic;
using Mancoffee.Core;
using Mancoffee.GridSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mancoffee.Customers
{
    /// <summary>
    /// Hace llegar clientes mientras el local está abierto, en oleadas según la hora:
    /// oficinistas a la mañana y al mediodía, estudiantes a media mañana y a la tarde, turistas a la merienda.
    /// Con más reputación llega más gente.
    /// </summary>
    public class CustomerSpawner : MonoBehaviour
    {
        [SerializeField] private GridManager grid;
        [SerializeField] private Customer customerPrefab;
        [SerializeField] private Transform customersParent;

        [Tooltip("Segundos entre llegadas cuando el peso total de la hora es 1 y la reputación es 3.")]
        [SerializeField] private Vector2 spawnInterval = new(7f, 14f);
        [SerializeField] private int maxCustomers = 14;

        private readonly List<Customer> active = new();
        private float spawnTimer = 3f;

        public int ActiveCount => active.Count;
        public IReadOnlyList<Customer> Active => active;

        private bool IsOpen => DayCycle.Instance == null || DayCycle.Instance.IsOpen;
        private float Minutes => DayCycle.Instance != null ? DayCycle.Instance.Minutes : 9 * 60f;

        /// <summary>Cuánta gente llega a esta hora (suma de los tipos). Sirve para el pronóstico de la pizarra.</summary>
        public float RushAt(float minutes)
        {
            float total = 0f;
            foreach (CustomerProfile p in CustomerProfile.All) total += p.WeightAt(minutes);
            return total;
        }

        /// <summary>El tipo que más llega a esta hora.</summary>
        public CustomerProfile DominantAt(float minutes)
        {
            CustomerProfile best = null;
            float bestWeight = 0f;
            foreach (CustomerProfile p in CustomerProfile.All)
            {
                float w = p.WeightAt(minutes);
                if (w <= bestWeight) continue;
                best = p;
                bestWeight = w;
            }
            return best;
        }

        private void Update()
        {
            if (IsOpen)
            {
                float rush = RushAt(Minutes);
                float reputation = CafeState.Instance != null ? CafeState.Instance.Reputation : 3f;
                // Reputación 1 → llega ~55% de la gente; reputación 5 → ~160%.
                float repFactor = Mathf.Lerp(0.55f, 1.6f, (reputation - 1f) / 4f);
                spawnTimer -= Time.deltaTime * rush * repFactor;
                if (spawnTimer <= 0f)
                {
                    spawnTimer = Random.Range(spawnInterval.x, spawnInterval.y);
                    Spawn(PickProfile());
                }
            }

            // Atajo de prueba: traer un cliente ya.
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame && IsOpen && !UiBlocker.ModalOpen) Spawn(PickProfile());
        }

        private CustomerProfile PickProfile()
        {
            float total = RushAt(Minutes);
            if (total <= 0.001f) return CustomerProfile.All[Random.Range(0, CustomerProfile.All.Length)];
            float pick = Random.Range(0f, total);
            foreach (CustomerProfile p in CustomerProfile.All)
            {
                pick -= p.WeightAt(Minutes);
                if (pick <= 0f) return p;
            }
            return CustomerProfile.All[CustomerProfile.All.Length - 1];
        }

        public Customer Spawn() => Spawn(PickProfile());

        public Customer Spawn(CustomerProfile profile)
        {
            if (active.Count >= maxCustomers) return null;

            Customer customer = Instantiate(customerPrefab, customersParent);
            customer.Init(grid, this, profile);
            active.Add(customer);
            return customer;
        }

        public void OnCustomerLeft(Customer customer) => active.Remove(customer);

        /// <summary>Al cerrar: todos se van sin enojarse.</summary>
        public void SendEveryoneHome()
        {
            foreach (Customer c in active.ToArray()) c.ForceLeave();
        }

        /// <summary>Elimina a todos al instante (al cargar una partida).</summary>
        public void ClearAll()
        {
            foreach (Customer c in active) if (c != null) Destroy(c.gameObject);
            active.Clear();
        }
    }
}
