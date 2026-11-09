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
    /// Atiende clientes, prepara pedidos y mantiene el local: levanta vajilla, lava en la bacha y purga la cafetera.
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
            GoingToCell,
            GoingToTarget,
            WorkingAtTarget,
            GoingToSink,
            DroppingAtSink
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
        [SerializeField] private float pickupTime = 0.8f;
        [SerializeField] private float dropTime = 0.5f;
        [SerializeField] private float washSecondsPerDish = 1.2f;
        [SerializeField] private float purgeTime = 10f;
        [SerializeField] private float progressBarWidthPx = 12f;

        private static float nextSupplyNote;

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
        private bool arrived;
        private string blockedReason;

        // Vajilla que lleva en la mano o que está lavando.
        private int carriedCups, carriedPlates;
        private PlacedFurniture carriedFrom;
        private PlacedFurniture sink;
        private int washingCups, washingPlates;

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

        private bool IsWorking => phase == Phase.Preparing || phase == Phase.AtCustomer ||
                                  phase == Phase.WorkingAtTarget || phase == Phase.DroppingAtSink;

        /// <summary>Avance de lo que está haciendo ahora, de 0 a 1.</summary>
        public float Progress => IsWorking && timerTotal > 0f ? 1f - Mathf.Clamp01(timer / timerTotal) : 0f;

        public string StatusText
        {
            get
            {
                if (current == null)
                    return Mode == StaffMode.Manual ? "Esperando órdenes" : "Libre";
                if (!string.IsNullOrEmpty(blockedReason)) return blockedReason;

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
                    Phase.FindingStation => "Espera estación libre",
                    Phase.GoingToStation => $"Va a preparar {product}",
                    Phase.Preparing => station != null && station.State != null && station.State.NeedsPurge
                        ? $"Prepara {product} (lento)"
                        : $"Prepara {product}",
                    Phase.GoingToTarget => current.Type switch
                    {
                        TaskType.LevantarVajilla => "Va a levantar vajilla",
                        TaskType.Lavar => "Va a la bacha",
                        _ => "Va a purgar la cafetera"
                    },
                    Phase.WorkingAtTarget => current.Type switch
                    {
                        TaskType.LevantarVajilla => "Levanta vajilla",
                        TaskType.Lavar => "Lava vajilla",
                        _ => "Purga el filtro"
                    },
                    Phase.GoingToSink => "Lleva vajilla a la bacha",
                    Phase.DroppingAtSink => "Deja la vajilla",
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
            foreach (StaffTask t in queue)
                if (t.Customer == customer) t.ReturnUnusedVessel();
            queue.RemoveAll(t => t.Customer == customer);
            if (current != null && current.Customer == customer)
            {
                current.Cancelled = true;
                current.ReturnUnusedVessel();
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
            blockedReason = null;

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
                case TaskType.LevantarVajilla:
                case TaskType.Lavar:
                case TaskType.Purgar:
                    if (!current.TargetAlive) { Finish(); break; }
                    phase = Phase.GoingToTarget;
                    GoToTarget();
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

            if (current.IsMaintenance)
            {
                RunMaintenance();
                return;
            }

            Customer customer = current.Customer;
            if (current.Cancelled || customer == null || customer.State == CustomerState.Saliendo)
            {
                current.Cancelled = true;
                current.ReturnUnusedVessel();
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
                        arrived = true;
                        waitTime = 0f;
                    }
                    if (!arrived)
                    {
                        RetryIfStuck(GoToStation);
                        break;
                    }
                    if (!current.Supplied && !TrySupply())
                    {
                        // Falta un insumo o no hay en qué servir: espera un poco y después lo suelta.
                        waitTime += Time.deltaTime;
                        if (waitTime > 5f)
                        {
                            current.RetryAfter = Time.time + 6f;
                            GiveUp();
                        }
                        break;
                    }
                    blockedReason = null;
                    phase = Phase.Preparing;
                    float speed = station.State != null ? station.State.SpeedFactor : 1f;
                    if (speed < 1f) CafeState.Instance.Today.SlowPreparations++;
                    timerTotal = timer = Mathf.Max(0.1f, current.Product.prepTime / speed);
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

                    station.State?.AddUse();
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
                    customer.ReceiveOrder(task.Vessel);
                    // Para llevar: se cobra en el momento.
                    if (customer.IsTakeaway) Charge(customer);
                    break;

                case TaskType.Cobrar:
                    Charge(customer);
                    break;
            }

            // Si el cliente se fue mientras tanto, la tarea ya se anuló.
            if (current == task) Finish();
        }

        private static void Charge(Customer customer)
        {
            if (!customer.TryPay(out int price, out int tip)) return;
            CafeState.Instance.AddIncome(price, MoneyCategory.Ventas);
            CafeState.Instance.AddIncome(tip, MoneyCategory.Propinas);
        }

        // =========================== Insumos y vajilla del pedido ===========================

        /// <summary>
        /// Gasta los insumos y elige en qué se sirve: taza o plato limpio en mesa; vaso descartable para llevar
        /// o si no quedan tazas limpias. Devuelve false si falta algo.
        /// </summary>
        private bool TrySupply()
        {
            Inventory inv = Inventory.Instance;
            if (inv == null)
            {
                current.Supplied = true;
                return true;
            }

            ProductData product = current.Product;
            Customer customer = current.Customer;
            List<IngredientData> missing = inv.Missing(product);
            if (missing.Count > 0)
            {
                blockedReason = $"Falta {missing[0].displayName.ToLowerInvariant()}";
                customer.MarkOrderBlocked();
                SupplyNote($"No hay {missing[0].displayName.ToLowerInvariant()} para el {product.displayName.ToLowerInvariant()} de {customer.TableLabel.ToLowerInvariant()}");
                return false;
            }

            IngredientData cup = inv.Database != null ? inv.Database.disposableCup : null;
            bool drink = product.dish == DishKind.Taza;
            bool cupsLeft = cup == null || inv.Count(cup) > 0;
            ServeVessel vessel;

            if (customer.IsTakeaway)
            {
                if (drink && !cupsLeft)
                {
                    blockedReason = "Sin vasos descartables";
                    SupplyNote("No quedan vasos descartables para los pedidos para llevar");
                    return false;
                }
                vessel = ServeVessel.Descartable;
            }
            else if (inv.CleanDishes(product.dish) > 0)
            {
                vessel = drink ? ServeVessel.Taza : ServeVessel.Plato;
            }
            else if (!drink)
            {
                vessel = ServeVessel.Descartable; // en servilleta
            }
            else if (cupsLeft)
            {
                vessel = ServeVessel.Descartable;
                CafeState.Instance.Today.ServedWithoutCup++;
                SupplyNote("No quedan tazas limpias: se sirve en vaso descartable. ¡Alguien tiene que lavar!");
            }
            else
            {
                blockedReason = "Sin tazas ni vasos";
                SupplyNote("No hay tazas limpias ni vasos descartables: hay que lavar o pedir vasos");
                return false;
            }

            inv.ConsumeIngredients(product);
            if (vessel == ServeVessel.Taza) inv.TakeClean(DishKind.Taza);
            else if (vessel == ServeVessel.Plato) inv.TakeClean(DishKind.Plato);
            else if (drink && cup != null) inv.Consume(cup, 1);

            current.Vessel = vessel;
            current.Supplied = true;
            return true;
        }

        private static void SupplyNote(string text)
        {
            if (Time.unscaledTime < nextSupplyNote) return;
            nextSupplyNote = Time.unscaledTime + 8f;
            CafeState.Instance?.Notify(text, NoteKind.Malo);
        }

        // =========================== Mantenimiento ===========================

        private void RunMaintenance()
        {
            bool atTarget = phase == Phase.GoingToTarget || phase == Phase.WorkingAtTarget;
            if (current.Cancelled || (atTarget && (!current.TargetAlive || !current.Target.IsPlaced)))
            {
                Abort();
                return;
            }

            PlacedFurniture target = current.TargetAlive ? current.Target : null;
            FurnitureState state = target != null ? target.State : null;

            switch (phase)
            {
                case Phase.GoingToTarget:
                    if (mover.ConsumeArrived()) arrived = true;
                    if (!arrived)
                    {
                        RetryIfStuck(GoToTarget);
                        break;
                    }
                    StartWorkAtTarget(target, state);
                    break;

                case Phase.WorkingAtTarget:
                    timer -= Time.deltaTime;
                    if (timer > 0f) break;
                    FinishWorkAtTarget(target, state);
                    break;

                case Phase.GoingToSink:
                    if (sink == null || !sink.IsPlaced)
                    {
                        sink = FindSink();
                        if (sink == null) { NoSink(); break; }
                        GoToSink();
                    }
                    if (mover.ConsumeArrived())
                    {
                        phase = Phase.DroppingAtSink;
                        timerTotal = timer = dropTime;
                        break;
                    }
                    RetryIfStuck(GoToSink);
                    break;

                case Phase.DroppingAtSink:
                    timer -= Time.deltaTime;
                    if (timer > 0f) break;
                    FurnitureState sinkState = sink != null ? sink.State : null;
                    if (sinkState == null) { NoSink(); break; }
                    int cups = carriedCups, plates = carriedPlates;
                    carriedCups = carriedPlates = 0;
                    carriedFrom = null;
                    sinkState.AddDirty(DishKind.Taza, cups);
                    sinkState.AddDirty(DishKind.Plato, plates);
                    Finish();
                    break;
            }
        }

        private void StartWorkAtTarget(PlacedFurniture target, FurnitureState state)
        {
            switch (current.Type)
            {
                case TaskType.LevantarVajilla:
                    if (state == null || state.DirtyCount == 0) { Finish(); return; }
                    timerTotal = timer = pickupTime;
                    break;

                case TaskType.Lavar:
                    if (state == null || state.DirtyCount == 0) { Finish(); return; }
                    (washingCups, washingPlates) = state.TakeAllDirty();
                    timerTotal = timer = (washingCups + washingPlates) * washSecondsPerDish;
                    break;

                case TaskType.Purgar:
                    if (state == null || !state.NeedsMaintenance) { Finish(); return; }
                    if (target.ReservedBy != null && !ReferenceEquals(target.ReservedBy, this))
                    {
                        // Alguien la está usando: espera a que termine.
                        blockedReason = "Espera que liberen la máquina";
                        waitTime += Time.deltaTime;
                        if (waitTime > 15f)
                        {
                            current.RetryAfter = Time.time + 5f;
                            GiveUp();
                        }
                        return;
                    }
                    blockedReason = null;
                    target.ReservedBy = this;
                    station = target;
                    timerTotal = timer = purgeTime;
                    break;
            }
            phase = Phase.WorkingAtTarget;
        }

        private void FinishWorkAtTarget(PlacedFurniture target, FurnitureState state)
        {
            switch (current.Type)
            {
                case TaskType.LevantarVajilla:
                    (carriedCups, carriedPlates) = state.TakeAllDirty();
                    carriedFrom = target;
                    if (carriedCups + carriedPlates == 0) { Finish(); return; }
                    sink = FindSink();
                    if (sink == null) { NoSink(); return; }
                    phase = Phase.GoingToSink;
                    failCount = 0;
                    GoToSink();
                    break;

                case TaskType.Lavar:
                    Inventory.Instance?.ReturnClean(DishKind.Taza, washingCups);
                    Inventory.Instance?.ReturnClean(DishKind.Plato, washingPlates);
                    washingCups = washingPlates = 0;
                    Finish();
                    break;

                case TaskType.Purgar:
                    state.Purge();
                    Finish();
                    break;
            }
        }

        private void NoSink()
        {
            SupplyNote("No hay bacha: construí una para poder lavar la vajilla");
            current.RetryAfter = Time.time + 10f;
            GiveUp();
        }

        /// <summary>Si suelta la tarea con vajilla en la mano o a medio lavar, la vajilla vuelve a donde estaba.</summary>
        private void DropDishes()
        {
            if (carriedCups + carriedPlates > 0)
            {
                FurnitureState from = carriedFrom != null && carriedFrom.IsPlaced ? carriedFrom.State : null;
                if (from != null)
                {
                    from.AddDirty(DishKind.Taza, carriedCups);
                    from.AddDirty(DishKind.Plato, carriedPlates);
                }
                else
                {
                    Inventory.Instance?.ReturnClean(DishKind.Taza, carriedCups);
                    Inventory.Instance?.ReturnClean(DishKind.Plato, carriedPlates);
                }
                carriedCups = carriedPlates = 0;
                carriedFrom = null;
            }

            if (washingCups + washingPlates > 0)
            {
                FurnitureState sinkState = current != null && current.TargetAlive ? current.Target.State : null;
                if (sinkState != null)
                {
                    sinkState.AddDirty(DishKind.Taza, washingCups);
                    sinkState.AddDirty(DishKind.Plato, washingPlates);
                }
                else
                {
                    Inventory.Instance?.ReturnClean(DishKind.Taza, washingCups);
                    Inventory.Instance?.ReturnClean(DishKind.Plato, washingPlates);
                }
                washingCups = washingPlates = 0;
            }
        }

        // =========================== Movimiento ===========================

        private bool GoToCustomer() => mover.MoveToAny(grid.GetAccessCells(new[] { current.Customer.SeatCell }));

        private bool GoToStation()
        {
            arrived = false;
            return mover.MoveToAny(grid.GetAccessCells(station.OccupiedCells));
        }

        private bool GoToTarget()
        {
            arrived = false;
            return current.TargetAlive && mover.MoveToAny(grid.GetAccessCells(current.Target.OccupiedCells));
        }

        private bool GoToSink() => sink != null && mover.MoveToAny(grid.GetAccessCells(sink.OccupiedCells));

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
            return FindNearest(f => f.Data == product.station && (f.ReservedBy == null || ReferenceEquals(f.ReservedBy, this)));
        }

        private PlacedFurniture FindSink() => FindNearest(f => f.Data.isSink);

        private PlacedFurniture FindNearest(Predicate<PlacedFurniture> match)
        {
            PlacedFurniture best = null;
            int bestLength = int.MaxValue;
            foreach (PlacedFurniture f in grid.AllFurniture)
            {
                if (!f.IsPlaced || !match(f)) continue;
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
            blockedReason = null;
            sink = null;
        }

        /// <summary>Corta la tarea actual sin devolverla (por ejemplo, porque el cliente se fue).</summary>
        private void Abort()
        {
            if (phase == Phase.AtCustomer && current?.Customer != null) current.Customer.SetAttended(false);
            DropDishes();
            ReleaseStation();
            mover.Stop();
            current = null;
            phase = Phase.None;
            delivering = false;
            blockedReason = null;
            sink = null;
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
            progressBar.gameObject.SetActive(IsWorking);
            if (IsWorking)
            {
                float ratio = timerTotal > 0f ? 1f - Mathf.Clamp01(timer / timerTotal) : 1f;
                progressFill.transform.localScale = new Vector3(Mathf.Max(1f, Mathf.Round(progressBarWidthPx * ratio)), 2f, 1f);
            }

            Sprite carried = null;
            if (delivering && current != null && current.Product != null) carried = current.Product.icon;
            else if (carriedCups + carriedPlates > 0 && grid != null && grid.Database != null)
                carried = carriedCups > 0 ? grid.Database.dirtyCupIcon : grid.Database.dirtyPlateIcon;
            carriedIcon.enabled = carried != null;
            if (carried != null) carriedIcon.sprite = carried;
        }
    }
}
