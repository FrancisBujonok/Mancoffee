using System.Collections.Generic;
using Mancoffee.Core;
using Mancoffee.GridSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mancoffee.Customers
{
    /// <summary>
    /// Hace llegar clientes mientras el local está abierto. Con más reputación, llegan más seguido.
    /// </summary>
    public class CustomerSpawner : MonoBehaviour
    {
        [SerializeField] private GridManager grid;
        [SerializeField] private Customer customerPrefab;
        [SerializeField] private Transform customersParent;

        [Tooltip("Segundos entre llegadas con reputación media (3/5).")]
        [SerializeField] private Vector2 spawnInterval = new(7f, 13f);
        [SerializeField] private int maxCustomers = 10;

        [SerializeField] private Color[] tints =
        {
            new(0.95f, 0.55f, 0.45f), new(0.5f, 0.7f, 0.95f), new(0.65f, 0.9f, 0.6f),
            new(0.95f, 0.85f, 0.45f), new(0.8f, 0.6f, 0.95f), new(0.6f, 0.9f, 0.9f)
        };

        private readonly List<Customer> active = new();
        private float spawnTimer = 3f;

        public int ActiveCount => active.Count;
        public IReadOnlyList<Customer> Active => active;

        private bool IsOpen => DayCycle.Instance == null || DayCycle.Instance.IsOpen;

        private void Update()
        {
            if (IsOpen)
            {
                spawnTimer -= Time.deltaTime;
                if (spawnTimer <= 0f)
                {
                    float reputation = CafeState.Instance != null ? CafeState.Instance.Reputation : 3f;
                    // Reputación 1 → llegan cada ~1.8x del intervalo; reputación 5 → cada ~0.6x.
                    float factor = Mathf.Lerp(1.8f, 0.6f, (reputation - 1f) / 4f);
                    spawnTimer = Random.Range(spawnInterval.x, spawnInterval.y) * factor;
                    Spawn();
                }
            }

            // Atajo de prueba: traer un cliente ya.
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.cKey.wasPressedThisFrame && IsOpen && !UiBlocker.ModalOpen) Spawn();
        }

        public Customer Spawn()
        {
            if (active.Count >= maxCustomers) return null;

            Customer customer = Instantiate(customerPrefab, customersParent);
            customer.Init(grid, this, tints[Random.Range(0, tints.Length)]);
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
