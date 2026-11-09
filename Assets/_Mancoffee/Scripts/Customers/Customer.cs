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
    /// Cliente con servicio en mesa:
    /// entra → busca silla junto a una mesa → espera al mozo → pide → espera el pedido → come → pide la cuenta → paga y se va.
    /// Pide tareas al TaskManager; los empleados las resuelven. Si la paciencia se agota, se va enojado sin pagar.
    /// </summary>
    [RequireComponent(typeof(SortingGroup), typeof(GridMover))]
    public class Customer : MonoBehaviour
    {
        private static readonly Vector2Int[] Orthogonal =
            { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

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

        private float patience;
        private float timer;
        private float retryTimer;
        private float stuckTime;
        private bool attended;
        private bool exitingDoor;

        public CustomerState State { get; private set; }
        public ProductData Order { get; private set; }
        public PlacedFurniture Seat => seat;
        public Vector2Int SeatCell => seatCell;
        public float PatienceRatio => Mathf.Clamp01(patience / maxPatience);

        /// <summary>"Mesa 3" mientras está sentado o yendo a sentarse.</summary>
        public string TableLabel { get; private set; } = "Sin mesa";

        public bool IsSeated => State == CustomerState.EsperandoMozo || State == CustomerState.EsperandoPedido ||
                                State == CustomerState.Comiendo || State == CustomerState.EsperandoCuenta;

        private void Awake() => mover = GetComponent<GridMover>();

        public void Init(GridManager gridManager, CustomerSpawner owner, Color tint)
        {
            grid = gridManager;
            spawner = owner;
            baseTint = tint;
            visual.color = tint;
            patience = maxPatience;

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
                        TryFindSeat();
                    }
                    break;

                case CustomerState.CaminandoAMesa:
                    if (!SeatStillMine() || mover.Status == MoveStatus.Blocked) { LoseSeat(); break; }
                    if (Drain(0.5f)) break;
                    if (mover.ConsumeArrived()) SitDown();
                    break;

                case CustomerState.EsperandoMozo:
                case CustomerState.EsperandoPedido:
                case CustomerState.EsperandoCuenta:
                    if (!SeatStillMine()) { Leave(true, AngryReason.SinSilla); break; }
                    if (!attended) Drain(State == CustomerState.EsperandoPedido ? 0.7f : 1f);
                    break;

                case CustomerState.Comiendo:
                    if (!SeatStillMine()) { Leave(true, AngryReason.SinSilla); break; }
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

        /// <summary>El cliente elige del menú disponible. Si no hay nada, se va enojado.</summary>
        public ProductData TakeOrder(IReadOnlyList<ProductData> menu)
        {
            if (State != CustomerState.EsperandoMozo) return null;
            if (menu == null || menu.Count == 0)
            {
                CafeState.Instance?.Notify("Un cliente se fue: no hay nada en el menú (faltan estaciones)", NoteKind.Malo);
                Leave(true, AngryReason.SinMozo);
                return null;
            }

            Order = menu[Random.Range(0, menu.Count)];
            State = CustomerState.EsperandoPedido;
            return Order;
        }

        public bool ReceiveOrder()
        {
            if (State != CustomerState.EsperandoPedido) return false;
            State = CustomerState.Comiendo;
            timer = eatDuration;
            patience = maxPatience; // comer lo pone de buen humor
            return true;
        }

        /// <summary>Paga el pedido más una propina que depende de cuánta paciencia le quedaba.</summary>
        public bool TryPay(out int price, out int tip)
        {
            price = tip = 0;
            if (State != CustomerState.EsperandoCuenta || Order == null) return false;

            price = Order.price;
            tip = Mathf.RoundToInt(Order.price * maxTipRatio * PatienceRatio);
            CafeState.Instance?.RegisterHappyCustomer(Order);
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
                if (f.Data.category != FurnitureCategory.Asiento || f.ReservedBy != null) continue;
                if (grid.GetTableNextTo(f.Origin) == null) continue;

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

        private bool SeatStillMine() =>
            seat != null && seat.IsPlaced && ReferenceEquals(seat.ReservedBy, this) && seat.Origin == seatCell;

        private void LoseSeat()
        {
            ReleaseSeat();
            mover.Stop();
            retryTimer = 0f;
            TableLabel = "Sin mesa";
            State = CustomerState.BuscandoMesa;
        }

        private void ReleaseSeat()
        {
            if (seat != null && ReferenceEquals(seat.ReservedBy, this)) seat.ReservedBy = null;
            seat = null;
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

        // =========================== Salida ===========================

        private void Leave(bool angry, AngryReason reason)
        {
            if (State == CustomerState.Saliendo) return;

            CustomerState previous = State;
            bool wasSeated = IsSeated;
            State = CustomerState.Saliendo;
            attended = false;
            TaskManager.Instance?.CancelTasksFor(this);

            if (angry)
            {
                visual.color = Color.Lerp(baseTint, new Color(1f, 0.25f, 0.25f), 0.6f);
                if (reason != AngryReason.SinSilla)
                {
                    reason = previous switch
                    {
                        CustomerState.EsperandoMozo => AngryReason.SinMozo,
                        CustomerState.EsperandoPedido => AngryReason.SinPedido,
                        CustomerState.EsperandoCuenta => AngryReason.SinCuenta,
                        _ => reason == AngryReason.SinMozo ? AngryReason.SinMozo : AngryReason.SinMesa
                    };
                }
                CafeState.Instance?.RegisterAngryCustomer(reason, Order);
            }

            ReleaseSeat();
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
                CustomerState.EsperandoCuenta => billIcon,
                _ => null
            };
            bool show = icon != null && !attended;
            bubble.enabled = show;
            bubbleIcon.enabled = show;
            if (show) bubbleIcon.sprite = icon;
        }
    }
}
