using System;
using System.Collections.Generic;
using Mancoffee.Building;
using Mancoffee.Core;
using Mancoffee.Customers;
using Mancoffee.Data;
using Mancoffee.GridSystem;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mancoffee.Staff
{
    public enum StaffMode
    {
        /// <summary>Con la cola vacía, elige solo qué hacer (el "libre albedrío" de Los Sims).</summary>
        Automatico,
        /// <summary>Solo hace lo que le asigna el jugador.</summary>
        Manual
    }

    /// <summary>
    /// Un empleado. Ejecuta las acciones de su cola en orden (como en Los Sims: hasta 8, cancelables)
    /// y, si está en automático y la cola queda vacía, toma solo la tarea pendiente más urgente.
    /// </summary>
    [RequireComponent(typeof(SortingGroup), typeof(GridMover))]
    public class Employee : MonoBehaviour
    {
        /// <summary>Máximo de acciones entre la actual y la cola.</summary>
        public const int MaxActions = 8;

        private enum Phase
        {
            None,
            GoingToCustomer,
            AtCustomer,
            FindingStation,
            GoingToStation,
            Preparing,
            GoingToCell
        }

        [Header("Referencias")]
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private Transform progressBar;
        [SerializeField] private SpriteRenderer progressFill;
        [SerializeField] private SpriteRenderer carriedIcon;

        [Header("Tiempos (segundos)")]
        [SerializeField] private float orderTime = 1.5f;
        [SerializeField] private float chargeTime = 1.2f;
        [SerializeField] private float deliverTime = 0.5f;
        [SerializeField] private float progressBarWidthPx = 12f;

        private GridManager grid;
        private GridMover mover;
        private readonly List<StaffTask> queue = new();
        private StaffTask current;
        private Phase phase;
        private float timer;
        private float timerTotal;
        private float retryTimer;
        private float waitTime;
        private int failCount;
        private PlacedFurniture station;
        private bool delivering;

        public string DisplayName { get; private set; }
        public StaffMode Mode { get; set; }
        public int Salary { get; private set; }
        public Color Tint { get; private set; } = Color.white;
        public Sprite Portrait => visual != null ? visual.sprite : null;
        public int TasksDoneToday { get; private set; }

        public IReadOnlyList<StaffTask> Queue => queue;
        public StaffTask Current => current;
        public Vector2Int CurrentCell => mover.PathStart;

        /// <summary>La actual primero y después la cola.</summary>
        public IEnumerable<StaffTask> AllTasks
        {
            get
            {
                if (current != null) yield return current;
                foreach (StaffTask t in queue) yield return t;
            }
        }

        /// <summary>Cuántas acciones tiene encima (para repartir trabajo).</summary>
        public int Load => queue.Count + (current != null ? 1 : 0);

        public bool HasRoom => Load < MaxActions;

        /// <summary>Avance de lo que está haciendo ahora (preparar, tomar pedido, cobrar), de 0 a 1.</summary>
        public float Progress =>
            (phase == Phase.Preparing || phase == Phase.AtCustomer) && timerTotal > 0f
                ? 1f - Mathf.Clamp01(timer / timerTotal)
                : 0f;

        public string StatusText
        {
            get
            {
                if (current == null)
                    return Mode == StaffMode.Manual ? "Esperando órdenes" : "Libre";

                string product = current.Product != null ? current.Product.displayName.ToLowerInvariant() : "";
                return phase switch
                {
                    Phase.GoingToCell => "Caminando",
                    Phase.GoingToCustomer => delivering ? $"Lleva {product}" :
                        current.Type == TaskType.TomarPedido ? "Va a tomar un pedido" : "Va a cobrar",
                    Phase.AtCustomer => current.Type switch
                    {
                        TaskType.TomarPedido => "Toma un pedido",
                        TaskType.Cobrar => "Cobra",
                        _ => "Sirve"
                    },
                    Phase.FindingStation => $"Espera estación libre",
                    Phase.GoingToStation => $"Va a preparar {product}",
                    Phase.Preparing => $"Prepara {product}",
                    _ => "Libre"
                };
            }
        }

        private void Awake() => mover = GetComponent<GridMover>();

        public void Init(GridManager gridManager, string displayName, StaffMode mode, int salary, Color tint)
        {
            grid = gridManager;
            DisplayName = displayName;
            Mode = mode;
            Salary = salary;
            Tint = tint;
            visual.color = tint;
            name = $"Empleado {displayName}";
            mover.Init(grid, grid.EntranceCell);
            RefreshVisuals();
        }

        public void ResetDayStats() => TasksDoneToday = 0;

        /// <summary>¿El punto del mundo cae sobre el dibujo del empleado?</summary>
        public bool ContainsPoint(Vector2 worldPoint)
        {
            Bounds b = visual.bounds;
            b.Expand(new Vector3(0.1f, 0.05f, 0f));
            return b.Contains(new Vector3(worldPoint.x, worldPoint.y, b.center.z));
        }

        // =========================== Cola de acciones ===========================

        /// <summary>Agrega una acción al final de la cola. Devuelve false si está llena.</summary>
        public bool Enqueue(StaffTask task)
        {
            if (!HasRoom) return false;
            task.Assignee = this;
            queue.Add(task);
            return true;
        }

        /// <summary>Orden directa "Ir aquí".</summary>
        public bool WalkTo(Vector2Int cell)
        {
            var task = new StaffTask(TaskType.IrA, null, null) { Cell = cell };
            return Enqueue(task);
        }

        /// <summary>Saca una tarea sin devolverla a la bolsa (otro empleado la va a tomar). Devuelve true si la tenía.</summary>
        public bool Release(StaffTask task)
        {
            if (queue.Remove(task)) return true;
            if (task != current) return false;
            Abort();
            return true;
        }

        /// <summary>Cancelar desde la interfaz (como tocar un ícono de la cola en Los Sims): la tarea vuelve a pendientes.</summary>
        public void CancelTask(StaffTask task)
        {
            if (!Release(task)) return;
            if (task.Type != TaskType.IrA) TaskManager.Instance.ReturnToPool(task);
        }

        /// <summary>Compatibilidad con el código anterior.</summary>
        public void Unassign(StaffTask task) => CancelTask(task);

        public void CancelTasksFor(Customer customer)
        {
            queue.RemoveAll(t => t.Customer == customer);
            if (current != null && current.Customer == customer)
            {
                current.Cancelled = true;
                Abort();
            }
        }

        /// <summary>Al despedirlo: todo lo que tenía vuelve a la bolsa.</summary>
        public void ReleaseAllTasks()
        {
            foreach (StaffTask t in queue) TaskManager.Instance.ReturnToPool(t);
            queue.Clear();
            if (current != null) GiveUp();
        }

        // =========================== Ciclo ===========================

        private void Update()
        {
            if (current == null) PickNextTask();
            if (current != null) RunTask();
            RefreshVisuals();
        }

        private void PickNextTask()
        {
            if (queue.Count > 0)
            {
                current = queue[0];
                queue.RemoveAt(0);
            }
            else if (Mode == StaffMode.Automatico && TaskManager.Instance != null)
            {
                current = TaskManager.Instance.TakeBestFor(this);
            }

            if (current == null) return;

            current.Assignee = this;
            failCount = 0;
            retryTimer = 0f;
            waitTime = 0f;
            delivering = false;

            switch (current.Type)
            {
                case TaskType.IrA:
                    phase = Phase.GoingToCell;
                    if (!mover.MoveTo(current.Cell))
                    {
                        CafeState.Instance?.Notify($"{DisplayName} no puede llegar ahí", NoteKind.Malo);
                        Finish();
                    }
                    break;
                case TaskType.PrepararYServir:
                    phase = Phase.FindingStation;
                    break;
                default:
                    phase = Phase.GoingToCustomer;
                    GoToCustomer();
                    break;
            }
        }

        private void RunTask()
        {
            if (current.Type == TaskType.IrA)
            {
                if (mover.ConsumeArrived() || mover.Status == MoveStatus.Blocked || mover.Status == MoveStatus.Idle) Finish();
                return;
            }

            Customer customer = current.Customer;
            if (current.Cancelled || customer == null || customer.State == CustomerState.Saliendo)
            {
                current.Cancelled = true;
                Abort();
                return;
            }

            switch (phase)
            {
                case Phase.GoingToCustomer:
                    if (mover.ConsumeArrived())
                    {
                        phase = Phase.AtCustomer;
                        timerTotal = timer = current.Type switch
                        {
                            TaskType.TomarPedido => orderTime,
                            TaskType.Cobrar => chargeTime,
                            _ => deliverTime
                        };
                        customer.SetAttended(true);
                        break;
                    }
                    RetryIfStuck(GoToCustomer);
                    break;

                case Phase.AtCustomer:
                    timer -= Time.deltaTime;
                    if (timer > 0f) break;
                    customer.SetAttended(false);
                    CompleteAtCustomer(customer);
                    break;

                case Phase.FindingStation:
                    waitTime += Time.deltaTime;
                    retryTimer -= Time.deltaTime;
                    if (retryTimer > 0f) break;
                    retryTimer = 0.5f;

                    station = FindStation(current.Product);
                    if (station == null)
                    {
                        // Nadie puede prepararlo ahora: se suelta para atender otras cosas y se reintenta luego.
                        if (waitTime > 6f)
                        {
                            current.RetryAfter = Time.time + 5f;
                            GiveUp();
                        }
                        break;
                    }

                    station.ReservedBy = this;
                    phase = Phase.GoingToStation;
                    failCount = 0;
                    GoToStation();
                    break;

                case Phase.GoingToStation:
                    if (!StationOk())
                    {
                        station = null;
                        mover.Stop();
                        phase = Phase.FindingStation;
                        break;
                    }
                    if (mover.ConsumeArrived())
                    {
                        phase = Phase.Preparing;
                        timerTotal = timer = Mathf.Max(0.1f, current.Product.prepTime);
                        if (!current.IngredientsPaid)
                        {
                            CafeState.Instance.ForceSpend(current.Product.ingredientCost, MoneyCategory.Ingredientes);
                            current.IngredientsPaid = true;
                        }
                        break;
                    }
                    RetryIfStuck(GoToStation);
                    break;

                case Phase.Preparing:
                    if (!StationOk())
                    {
                        station = null;
                        phase = Phase.FindingStation;
                        break;
                    }
                    timer -= Time.deltaTime;
                    if (timer > 0f) break;

                    ReleaseStation();
                    delivering = true;
                    failCount = 0;
                    phase = Phase.GoingToCustomer;
                    GoToCustomer();
                    break;
            }
        }

        private void CompleteAtCustomer(Customer customer)
        {
            StaffTask task = current;
            switch (task.Type)
            {
                case TaskType.TomarPedido:
                    ProductData product = customer.TakeOrder(MenuService.GetAvailableProducts(grid));
                    if (product != null)
                    {
                        // Como en Los Sims: lo que empezaste lo seguís. El empleado en manual se encola la preparación;
                        // el automático la deja en pendientes para quien esté más libre.
                        StaffTask prep = TaskManager.Instance.Request(TaskType.PrepararYServir, customer, product);
                        if (Mode == StaffMode.Manual) TaskManager.Instance.AssignAny(prep, this);
                    }
                    break;

                case TaskType.PrepararYServir:
                    customer.ReceiveOrder();
                    break;

                case TaskType.Cobrar:
                    if (customer.TryPay(out int price, out int tip))
                    {
                        CafeState.Instance.AddIncome(price, MoneyCategory.Ventas);
                        CafeState.Instance.AddIncome(tip, MoneyCategory.Propinas);
                    }
                    break;
            }

            // Si el cliente se fue mientras tanto, la tarea ya se anuló.
            if (current == task) Finish();
        }

        // =========================== Movimiento ===========================

        private bool GoToCustomer() => mover.MoveToAny(grid.GetAccessCells(new[] { current.Customer.SeatCell }));

        private bool GoToStation() => mover.MoveToAny(grid.GetAccessCells(station.OccupiedCells));

        /// <summary>Si quedó sin camino, reintenta cada segundo; tras varios fallos suelta la tarea.</summary>
        private void RetryIfStuck(Func<bool> retry)
        {
            if (mover.Status != MoveStatus.Blocked && mover.Status != MoveStatus.Idle) return;
            retryTimer -= Time.deltaTime;
            if (retryTimer > 0f) return;
            retryTimer = 1f;
            if (retry()) return;
            if (++failCount > 5)
            {
                CafeState.Instance?.Notify($"{DisplayName} no encuentra cómo llegar", NoteKind.Malo);
                current.RetryAfter = Time.time + 5f;
                GiveUp();
            }
        }

        // =========================== Estaciones ===========================

        private PlacedFurniture FindStation(ProductData product)
        {
            if (product == null || product.station == null) return null;

            PlacedFurniture best = null;
            int bestLength = int.MaxValue;
            foreach (PlacedFurniture f in grid.AllFurniture)
            {
                if (f.Data != product.station) continue;
                if (f.ReservedBy != null && !ReferenceEquals(f.ReservedBy, this)) continue;

                List<Vector2Int> access = grid.GetAccessCells(f.OccupiedCells);
                if (access.Count == 0) continue;
                List<Vector2Int> path = GridPathfinder.FindPathToAny(grid, mover.PathStart, access);
                if (path == null || path.Count >= bestLength) continue;
                best = f;
                bestLength = path.Count;
            }
            return best;
        }

        private bool StationOk() => station != null && station.IsPlaced && ReferenceEquals(station.ReservedBy, this);

        private void ReleaseStation()
        {
            if (station != null && ReferenceEquals(station.ReservedBy, this)) station.ReservedBy = null;
            station = null;
        }

        // =========================== Terminar o soltar ===========================

        private void Finish()
        {
            if (current != null && current.Type != TaskType.IrA) TasksDoneToday++;
            ReleaseStation();
            current = null;
            phase = Phase.None;
            delivering = false;
        }

        /// <summary>Corta la tarea actual sin devolverla (por ejemplo, porque el cliente se fue).</summary>
        private void Abort()
        {
            if (phase == Phase.AtCustomer && current?.Customer != null) current.Customer.SetAttended(false);
            ReleaseStation();
            mover.Stop();
            current = null;
            phase = Phase.None;
            delivering = false;
        }

        /// <summary>Suelta la tarea actual y la devuelve a la bolsa para que la haga otro.</summary>
        private void GiveUp()
        {
            StaffTask task = current;
            Abort();
            if (task != null) TaskManager.Instance.ReturnToPool(task);
        }

        // =========================== Visual ===========================

        private void RefreshVisuals()
        {
            bool working = phase == Phase.Preparing || phase == Phase.AtCustomer;
            progressBar.gameObject.SetActive(working);
            if (working)
            {
                float ratio = timerTotal > 0f ? 1f - Mathf.Clamp01(timer / timerTotal) : 1f;
                progressFill.transform.localScale = new Vector3(Mathf.Max(1f, Mathf.Round(progressBarWidthPx * ratio)), 2f, 1f);
            }

            bool carrying = delivering && current != null && current.Product != null;
            carriedIcon.enabled = carrying;
            if (carrying) carriedIcon.sprite = current.Product.icon;
        }
    }
}
