using System.Collections.Generic;
using Mancoffee.Building;
using Mancoffee.Core;
using Mancoffee.Data;
using Mancoffee.GridSystem;
using Mancoffee.Staff;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mancoffee.Customers
{
    public enum CustomerState
    {
        Entrando,
        BuscandoMesa,
        CaminandoAMesa,
        EsperandoMozo,
        EsperandoPedido,
        Comiendo,
        EsperandoCuenta,
        Saliendo
    }

    /// <summary>
    /// Cliente. Puede quedarse a comer (busca silla junto a una mesa limpia, pide, come, paga) o pedir
    /// para llevar en el mostrador (pide, espera de pie, paga al recibir y se va).
    /// Elige qué pedir según su tipo y los precios. Si la paciencia se agota, se va enojado sin pagar.
    /// Al irse deja la taza o el plato sucio en la mesa.
    /// </summary>
    [RequireComponent(typeof(SortingGroup), typeof(GridMover))]
    public class Customer : MonoBehaviour
    {
        /// <summary>Lugares del mostrador ocupados por clientes para llevar.</summary>
        private static readonly Dictionary<Vector2Int, Customer> CounterSpots = new();

        [Header("Referencias")]
        [SerializeField] private SpriteRenderer visual;
        [SerializeField] private Transform patienceBar;
        [SerializeField] private SpriteRenderer patienceFill;
        [SerializeField] private SpriteRenderer bubble;
        [SerializeField] private SpriteRenderer bubbleIcon;
        [SerializeField] private Sprite orderIcon;
        [SerializeField] private Sprite billIcon;

        [Header("Ajustes")]
        [SerializeField] private float maxPatience = 60f;
        [SerializeField] private float eatDuration = 8f;
        [SerializeField] private float seatedHeight = 0.18f;
        [SerializeField] private float maxTipRatio = 0.3f;
        [SerializeField] private float patienceBarWidthPx = 12f;

        private GridManager grid;
        private CustomerSpawner spawner;
        private GridMover mover;
        private Color baseTint;

        private PlacedFurniture seat;
        private Vector2Int seatCell;
        private bool hasSpot;

        private float patience;
        private float patienceMax;
        private float timer;
        private float retryTimer;
        private float stuckTime;
        private bool attended;
        private bool exitingDoor;
        private bool orderBlocked;
        private ServeVessel vessel;

        public CustomerState State { get; private set; }
        public ProductData Order { get; private set; }
        public CustomerProfile Profile { get; private set; }

        /// <summary>Pide para llevar en el mostrador.</summary>
        public bool IsTakeaway { get; private set; }

        public PlacedFurniture Seat => seat;

        /// <summary>Donde está esperando: la silla, o su lugar en el mostrador.</summary>
        public Vector2Int SeatCell => seatCell;

        public float PatienceRatio => Mathf.Clamp01(patience / Mathf.Max(1f, patienceMax));

        /// <summary>"Mesa 3", "Para llevar" o "Sin mesa".</summary>
        public string TableLabel { get; private set; } = "Sin mesa";

        public bool IsSeated => State == CustomerState.EsperandoMozo || State == CustomerState.EsperandoPedido ||
                                State == CustomerState.Comiendo || State == CustomerState.EsperandoCuenta;

        private void Awake() => mover = GetComponent<GridMover>();

        public void Init(GridManager gridManager, CustomerSpawner owner, CustomerProfile profile)
        {
            grid = gridManager;
            spawner = owner;
            Profile = profile ?? CustomerProfile.Estudiante;
            baseTint = Profile.Tints[Random.Range(0, Profile.Tints.Length)];
            visual.color = baseTint;
            patienceMax = maxPatience * Profile.Patience;
            patience = patienceMax;
            IsTakeaway = Random.value < Profile.Takeaway;

            mover.Init(grid, grid.OutsideCell);
            mover.StepDirect(grid.EntranceCell);
            State = CustomerState.Entrando;
            RefreshVisuals();
        }

        private void Update()
        {
            switch (State)
            {
                case CustomerState.Entrando:
                    if (mover.ConsumeArrived())
                    {
                        retryTimer = 0f;
                        State = CustomerState.BuscandoMesa;
                    }
                    break;

                case CustomerState.BuscandoMesa:
                    if (Drain(1f)) break;
                    retryTimer -= Time.deltaTime;
                    if (retryTimer <= 0f)
                    {
                        retryTimer = 0.5f;
                        if (IsTakeaway) TryFindCounterSpot();
                        else TryFindSeat();
                    }
                    break;

                case CustomerState.CaminandoAMesa:
                    if (!PlaceStillMine() || mover.Status == MoveStatus.Blocked) { LosePlace(); break; }
                    if (Drain(0.5f)) break;
                    if (mover.ConsumeArrived())
                    {
                        if (IsTakeaway) ArriveAtCounter();
                        else SitDown();
                    }
                    break;

                case CustomerState.EsperandoMozo:
                case CustomerState.EsperandoPedido:
                case CustomerState.EsperandoCuenta:
                    if (!PlaceStillMine()) { Leave(true, AngryReason.SinSilla); break; }
                    if (!attended) Drain(State == CustomerState.EsperandoPedido ? 0.7f : 1f);
                    break;

                case CustomerState.Comiendo:
                    if (!PlaceStillMine()) { Leave(true, AngryReason.SinSilla); break; }
                    timer -= Time.deltaTime;
                    if (timer <= 0f)
                    {
                        State = CustomerState.EsperandoCuenta;
                        TaskManager.Instance?.Request(TaskType.Cobrar, this);
                    }
                    break;

                case CustomerState.Saliendo:
                    UpdateLeaving();
                    break;
            }

            RefreshVisuals();
        }

        // =========================== Lo que usan los empleados ===========================

        /// <summary>Mientras un empleado lo atiende, la paciencia no baja.</summary>
        public void SetAttended(bool value) => attended = value;

        /// <summary>Un empleado no pudo preparar su pedido por falta de insumos.</summary>
        public void MarkOrderBlocked() => orderBlocked = true;

        /// <summary>
        /// Elige del menú según lo que le gusta a su tipo y lo que está dispuesto a pagar.
        /// Si no hay nada (o todo le parece caro), se va enojado.
        /// </summary>
        public ProductData TakeOrder(IReadOnlyList<ProductData> menu)
        {
            if (State != CustomerState.EsperandoMozo) return null;

            Inventory inv = Inventory.Instance;
            IngredientData cup = inv != null && inv.Database != null ? inv.Database.disposableCup : null;
            bool noCupsForTakeaway = IsTakeaway && cup != null && inv.Count(cup) <= 0;

            var options = new List<(ProductData product, float weight)>();
            bool anyOffered = false;
            float total = 0f;
            if (menu != null)
            {
                foreach (ProductData p in menu)
                {
                    if (noCupsForTakeaway && p.dish == DishKind.Taza) continue;
                    anyOffered = true;
                    float ratio = inv != null ? inv.PriceRatio(p) : 1f;
                    float weight = Profile.PrefFor(p) * Profile.Acceptance(ratio);
                    if (weight <= 0.001f) continue;
                    options.Add((p, weight));
                    total += weight;
                }
            }

            if (options.Count == 0)
            {
                if (anyOffered)
                {
                    CafeState.Instance?.Notify($"Un {Profile.Name.ToLowerInvariant()} se fue sin pedir: le pareció todo caro", NoteKind.Malo);
                    Leave(true, AngryReason.Caro);
                }
                else
                {
                    bool stations = grid.Database.products.Exists(p => MenuService.HasStation(grid, p));
                    CafeState.Instance?.Notify(stations
                        ? "Un cliente se fue: no había insumos para nada de la carta"
                        : "Un cliente se fue: no hay nada en el menú (faltan estaciones)", NoteKind.Malo);
                    Leave(true, stations ? AngryReason.SinStock : AngryReason.SinMozo);
                }
                return null;
            }

            float pick = Random.Range(0f, total);
            Order = options[options.Count - 1].product;
            foreach ((ProductData product, float weight) in options)
            {
                pick -= weight;
                if (pick > 0f) continue;
                Order = product;
                break;
            }

            State = CustomerState.EsperandoPedido;
            return Order;
        }

        /// <summary>Recibe el pedido servido en taza, plato o descartable.</summary>
        public bool ReceiveOrder(ServeVessel servedIn = ServeVessel.Ninguno)
        {
            if (State != CustomerState.EsperandoPedido) return false;
            vessel = servedIn;
            patience = patienceMax; // recibir el pedido lo pone de buen humor

            if (IsTakeaway)
            {
                State = CustomerState.EsperandoCuenta; // paga en el acto
                return true;
            }

            State = CustomerState.Comiendo;
            timer = eatDuration * Profile.Stay;
            return true;
        }

        /// <summary>Paga el pedido más una propina según la paciencia que le quedaba, el precio y en qué se lo serviste.</summary>
        public bool TryPay(out int price, out int tip)
        {
            price = tip = 0;
            if (State != CustomerState.EsperandoCuenta || Order == null) return false;

            Inventory inv = Inventory.Instance;
            price = inv != null ? inv.PriceOf(Order) : Order.price;
            float value = Profile.ValueFor(inv != null ? inv.PriceRatio(Order) : 1f);
            float vesselFactor = !IsTakeaway && vessel == ServeVessel.Descartable && Order.dish == DishKind.Taza ? 0.6f : 1f;
            tip = Mathf.RoundToInt(price * maxTipRatio * PatienceRatio * Profile.Tip * value * vesselFactor);
            CafeState.Instance?.RegisterHappyCustomer(Order, value * vesselFactor, Profile.Id);
            Leave(false, AngryReason.SinMesa);
            return true;
        }

        /// <summary>Se va sin enojarse ni pagar (por ejemplo, al cerrar el local).</summary>
        public void ForceLeave() => Leave(false, AngryReason.SinMesa);

        /// <summary>¿El punto del mundo cae sobre el dibujo del cliente?</summary>
        public bool ContainsPoint(Vector2 worldPoint)
        {
            Bounds b = visual.bounds;
            b.Expand(new Vector3(0.1f, 0.05f, 0f));
            return b.Contains(new Vector3(worldPoint.x, worldPoint.y, b.center.z));
        }

        // =========================== Asiento ===========================

        private void TryFindSeat()
        {
            PlacedFurniture best = null;
            List<Vector2Int> bestPath = null;

            foreach (PlacedFurniture f in grid.AllFurniture)
            {
                if (f.Data.category != FurnitureCategory.Asiento || f.ReservedBy != null || !f.IsPlaced) continue;
                PlacedFurniture table = grid.GetTableNextTo(f.Origin);
                if (table == null) continue;
                if (table.State != null && table.State.DirtyCount > 0) continue; // mesa sucia: nadie se sienta

                List<Vector2Int> candidate = GridPathfinder.FindPath(grid, mover.PathStart, f.Origin);
                if (candidate == null) continue;
                if (bestPath == null || candidate.Count < bestPath.Count)
                {
                    best = f;
                    bestPath = candidate;
                }
            }

            if (best == null) return;
            if (!mover.MoveTo(best.Origin)) return;

            seat = best;
            seatCell = best.Origin;
            best.ReservedBy = this;
            TableLabel = grid.TableLabelFor(seatCell);
            State = CustomerState.CaminandoAMesa;
        }

        /// <summary>Busca un lugar libre frente al mostrador. Si no hay mostrador, se queda a comer.</summary>
        private void TryFindCounterSpot()
        {
            bool anyCounter = false;
            Vector2Int best = default;
            List<Vector2Int> bestPath = null;

            foreach (PlacedFurniture f in grid.AllFurniture)
            {
                if (f.Data.category != FurnitureCategory.Mostrador || !f.IsPlaced) continue;
                anyCounter = true;
                foreach (Vector2Int cell in grid.GetAccessCells(f.OccupiedCells))
                {
                    if (CounterSpots.TryGetValue(cell, out Customer other) && other != null && other != this) continue;
                    List<Vector2Int> path = GridPathfinder.FindPath(grid, mover.PathStart, cell);
                    if (path == null) continue;
                    if (bestPath == null || path.Count < bestPath.Count)
                    {
                        best = cell;
                        bestPath = path;
                    }
                }
            }

            if (!anyCounter)
            {
                IsTakeaway = false;
                TryFindSeat();
                return;
            }

            if (bestPath == null || !mover.MoveTo(best)) return;
            seatCell = best;
            hasSpot = true;
            CounterSpots[best] = this;
            TableLabel = "Para llevar";
            State = CustomerState.CaminandoAMesa;
        }

        private bool PlaceStillMine()
        {
            if (IsTakeaway) return hasSpot && CounterSpots.TryGetValue(seatCell, out Customer c) && c == this;
            return seat != null && seat.IsPlaced && ReferenceEquals(seat.ReservedBy, this) && seat.Origin == seatCell;
        }

        private void LosePlace()
        {
            ReleasePlace();
            mover.Stop();
            retryTimer = 0f;
            TableLabel = "Sin mesa";
            State = CustomerState.BuscandoMesa;
        }

        private void ReleasePlace()
        {
            if (seat != null && ReferenceEquals(seat.ReservedBy, this)) seat.ReservedBy = null;
            seat = null;
            if (hasSpot && CounterSpots.TryGetValue(seatCell, out Customer c) && c == this) CounterSpots.Remove(seatCell);
            hasSpot = false;
        }

        private void SitDown()
        {
            State = CustomerState.EsperandoMozo;
            TableLabel = grid.TableLabelFor(seatCell);
            // La raíz queda apenas delante de la silla (orden de dibujado) y el dibujo sube para quedar sentado encima.
            transform.position = seat.SortPosition + new Vector3(0f, -0.01f, 0f);
            visual.transform.localPosition = new Vector3(0f, seatedHeight, 0f);
            TaskManager.Instance?.Request(TaskType.TomarPedido, this);
        }

        private void ArriveAtCounter()
        {
            State = CustomerState.EsperandoMozo;
            TableLabel = "Para llevar";
            TaskManager.Instance?.Request(TaskType.TomarPedido, this);
        }

        // =========================== Salida ===========================

        private void Leave(bool angry, AngryReason reason)
        {
            if (State == CustomerState.Saliendo) return;

            CustomerState previous = State;
            bool wasSeated = IsSeated && !IsTakeaway;
            State = CustomerState.Saliendo;
            attended = false;
            TaskManager.Instance?.CancelTasksFor(this);

            if (angry)
            {
                visual.color = Color.Lerp(baseTint, new Color(1f, 0.25f, 0.25f), 0.6f);
                if (reason != AngryReason.SinSilla && reason != AngryReason.Caro && reason != AngryReason.SinStock)
                {
                    reason = previous switch
                    {
                        CustomerState.EsperandoMozo => AngryReason.SinMozo,
                        CustomerState.EsperandoPedido => orderBlocked ? AngryReason.SinStock : AngryReason.SinPedido,
                        CustomerState.EsperandoCuenta => AngryReason.SinCuenta,
                        _ => reason == AngryReason.SinMozo ? AngryReason.SinMozo : AngryReason.SinMesa
                    };
                }
                CafeState.Instance?.RegisterAngryCustomer(reason, Order);
            }

            LeaveDishes();
            ReleasePlace();
            if (wasSeated)
            {
                visual.transform.localPosition = Vector3.zero;
                mover.Teleport(seatCell);
            }
            else
            {
                mover.Stop();
            }

            exitingDoor = false;
            retryTimer = 0f;
            stuckTime = 0f;
        }

        /// <summary>La taza o el plato queda sucio en la mesa (y aparece la tarea de levantarlo).</summary>
        private void LeaveDishes()
        {
            ServeVessel used = vessel;
            vessel = ServeVessel.Ninguno;
            if (used != ServeVessel.Taza && used != ServeVessel.Plato) return;
            DishKind kind = used == ServeVessel.Taza ? DishKind.Taza : DishKind.Plato;

            PlacedFurniture table = !IsTakeaway ? grid.GetTableNextTo(seatCell) : null;
            if (table != null && table.State != null) table.State.AddDirty(kind, 1);
            else Inventory.Instance?.ReturnClean(kind, 1);
        }

        private void UpdateLeaving()
        {
            if (exitingDoor)
            {
                if (mover.ConsumeArrived()) Despawn();
                return;
            }

            if (mover.ConsumeArrived())
            {
                exitingDoor = true;
                mover.StepDirect(grid.OutsideCell);
                return;
            }

            if (mover.Status == MoveStatus.Moving) return;

            retryTimer -= Time.deltaTime;
            if (retryTimer > 0f) return;
            retryTimer = 1f;

            if (!mover.MoveTo(grid.EntranceCell))
            {
                // Encerrado por muebles o paredes: reintenta y, si sigue así, desaparece para no trabar el juego.
                stuckTime += 1f;
                if (stuckTime > 10f) Despawn();
            }
        }

        private void Despawn()
        {
            spawner.OnCustomerLeft(this);
            Destroy(gameObject);
        }

        private void OnDestroy() => ReleasePlace();

        // =========================== Paciencia y globos ===========================

        /// <summary>Baja la paciencia. Devuelve true si se agotó (y el cliente empezó a irse).</summary>
        private bool Drain(float rate)
        {
            patience -= Time.deltaTime * rate;
            if (patience > 0f) return false;
            Leave(true, AngryReason.SinMesa);
            return true;
        }

        private void RefreshVisuals()
        {
            bool waiting = State == CustomerState.BuscandoMesa || State == CustomerState.CaminandoAMesa ||
                           State == CustomerState.EsperandoMozo || State == CustomerState.EsperandoPedido ||
                           State == CustomerState.EsperandoCuenta;
            patienceBar.gameObject.SetActive(waiting);
            if (waiting)
            {
                float ratio = PatienceRatio;
                float width = Mathf.Max(1f, Mathf.Round(patienceBarWidthPx * ratio));
                patienceFill.transform.localScale = new Vector3(width, 2f, 1f);
                patienceFill.color = Color.Lerp(new Color(0.9f, 0.25f, 0.2f), new Color(0.35f, 0.85f, 0.35f), ratio);
            }

            Sprite icon = State switch
            {
                CustomerState.EsperandoMozo => orderIcon,
                CustomerState.EsperandoPedido => Order != null ? Order.icon : null,
                CustomerState.EsperandoCuenta => IsTakeaway ? null : billIcon,
                _ => null
            };
            bool show = icon != null && !attended;
            bubble.enabled = show;
            bubbleIcon.enabled = show;
            if (show) bubbleIcon.sprite = icon;
        }
    }
}
