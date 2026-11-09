using System.Collections.Generic;
using System.Linq;
using Mancoffee.Building;
using Mancoffee.Core;
using Mancoffee.Customers;
using Mancoffee.Data;
using Mancoffee.GridSystem;
using UnityEngine;

namespace Mancoffee.Staff
{
    public enum TaskType
    {
        TomarPedido,
        PrepararYServir,
        Cobrar,
        /// <summary>Orden directa del jugador: caminar hasta una celda (como "Ir aquí" en Los Sims).</summary>
        IrA,
        /// <summary>Levantar la vajilla sucia de una mesa y llevarla a la bacha.</summary>
        LevantarVajilla,
        /// <summary>Lavar lo que hay en la bacha: vuelven tazas y platos limpios.</summary>
        Lavar,
        /// <summary>Purgar y limpiar el filtro de una máquina que larga vapor.</summary>
        Purgar
    }

    /// <summary>En qué se sirvió (o se va a servir) un pedido.</summary>
    public enum ServeVessel
    {
        Ninguno,
        Taza,
        Plato,
        /// <summary>Vaso descartable (bebidas) o servilleta/bolsa (pastelería).</summary>
        Descartable
    }

    /// <summary>
    /// Un trabajo del local. No sabe quién lo hará: lo toma un empleado en automático
    /// o se lo asigna el jugador (tablero de turno, menú circular, comandas).
    /// </summary>
    public class StaffTask
    {
        public StaffTask(TaskType type, Customer customer, ProductData product)
        {
            Type = type;
            Customer = customer;
            Product = product;
        }

        public TaskType Type { get; }
        public Customer Customer { get; }
        public ProductData Product { get; }

        /// <summary>Destino de las tareas IrA.</summary>
        public Vector2Int Cell { get; set; }

        /// <summary>Mueble de las tareas de mantenimiento (la mesa sucia, la bacha, la cafetera).</summary>
        public PlacedFurniture Target { get; set; }

        public Employee Assignee { get; internal set; }
        public bool Cancelled { get; internal set; }

        /// <summary>Ya se gastaron los insumos y se eligió la vajilla.</summary>
        public bool Supplied { get; internal set; }

        public ServeVessel Vessel { get; internal set; }

        /// <summary>Los empleados automáticos no la toman antes de este momento (por ejemplo, si no había estación libre).</summary>
        public float RetryAfter { get; internal set; }

        public bool IsMaintenance => Type == TaskType.LevantarVajilla || Type == TaskType.Lavar || Type == TaskType.Purgar;

        /// <summary>Si el mueble de la tarea sigue existiendo (Unity anula los objetos destruidos).</summary>
        public bool TargetAlive => Target != null;

        /// <summary>Menor = más urgente.</summary>
        public int Priority => Type switch
        {
            TaskType.Cobrar => 0,
            TaskType.PrepararYServir => 1,
            TaskType.Lavar => Inventory.Instance != null && Inventory.Instance.CleanDishes(DishKind.Taza) <= 2 ? 1 : 4,
            TaskType.TomarPedido => 2,
            TaskType.LevantarVajilla => 3,
            TaskType.Purgar => 4,
            _ => 5
        };

        /// <summary>Celda de referencia para medir distancias.</summary>
        public Vector2Int TargetCell =>
            Customer != null ? Customer.SeatCell : TargetAlive ? Target.Origin : Cell;

        /// <summary>"Mesa 3", "Para llevar", "Bacha"...</summary>
        public string Where => Customer != null ? Customer.TableLabel : TargetAlive ? Target.Label : "";

        public string Title => Type switch
        {
            TaskType.TomarPedido => "Tomar pedido",
            TaskType.PrepararYServir => Product != null ? $"Preparar {Product.displayName.ToLowerInvariant()}" : "Preparar pedido",
            TaskType.Cobrar => "Cobrar la cuenta",
            TaskType.LevantarVajilla => "Levantar vajilla",
            TaskType.Lavar => TargetAlive ? $"Lavar vajilla ({Target.State.DirtyCount})" : "Lavar vajilla",
            TaskType.Purgar => "Purgar el filtro",
            _ => "Ir a un lugar"
        };

        public string Description => Type == TaskType.IrA ? Title : $"{Title} · {Where}";

        /// <summary>Paciencia del cliente (1 = tranquilo). Las tareas sin cliente no apuran.</summary>
        public float Patience => Customer != null ? Customer.PatienceRatio : 1f;

        /// <summary>Si se anula un pedido que ya tenía taza o plato limpio, la pieza vuelve sin usar.</summary>
        internal void ReturnUnusedVessel()
        {
            if (!Supplied || Inventory.Instance == null) return;
            if (Vessel == ServeVessel.Taza) Inventory.Instance.ReturnClean(DishKind.Taza, 1);
            else if (Vessel == ServeVessel.Plato) Inventory.Instance.ReturnClean(DishKind.Plato, 1);
            Vessel = ServeVessel.Ninguno;
        }
    }

    /// <summary>
    /// Bolsa de tareas sin asignar. Los empleados en automático piden la mejor para ellos;
    /// el jugador puede asignar cualquiera a cualquier empleado.
    /// </summary>
    [DefaultExecutionOrder(-90)]
    public class TaskManager : MonoBehaviour
    {
        public static TaskManager Instance { get; private set; }

        private readonly List<StaffTask> pending = new();

        public IReadOnlyList<StaffTask> Pending => pending;

        private void Awake() => Instance = this;

        public StaffTask Request(TaskType type, Customer customer, ProductData product = null)
        {
            var task = new StaffTask(type, customer, product);
            pending.Add(task);
            return task;
        }

        /// <summary>Pide una tarea sobre un mueble, salvo que ya exista una igual (pendiente o en manos de alguien).</summary>
        public StaffTask RequestFurniture(TaskType type, PlacedFurniture target)
        {
            if (target == null) return null;
            StaffTask existing = FindFurnitureTask(type, target);
            if (existing != null) return existing;
            var task = new StaffTask(type, null, null) { Target = target };
            pending.Add(task);
            return task;
        }

        public StaffTask FindFurnitureTask(TaskType type, PlacedFurniture target)
        {
            foreach (StaffTask t in pending)
                if (t.Type == type && t.Target == target && !t.Cancelled) return t;

            StaffManager staff = StaffManager.Instance;
            if (staff == null) return null;
            foreach (Employee e in staff.Employees)
                foreach (StaffTask t in e.AllTasks)
                    if (t.Type == type && t.Target == target && !t.Cancelled) return t;
            return null;
        }

        /// <summary>El cliente se fue: se anulan sus tareas, estén pendientes o en manos de un empleado.</summary>
        public void CancelTasksFor(Customer customer)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].Customer != customer) continue;
                pending[i].Cancelled = true;
                pending[i].ReturnUnusedVessel();
                pending.RemoveAt(i);
            }

            StaffManager staff = StaffManager.Instance;
            if (staff == null) return;
            foreach (Employee e in staff.Employees.ToArray()) e.CancelTasksFor(customer);
        }

        /// <summary>
        /// Asigna una tarea a un empleado, esté donde esté: en la bolsa o en manos de otro (se la quita).
        /// Devuelve false si ya era suya o si su cola está llena.
        /// </summary>
        public bool AssignAny(StaffTask task, Employee employee)
        {
            if (task == null || employee == null || task.Cancelled) return false;
            if (task.Assignee == employee) return false;
            if (!employee.HasRoom)
            {
                CafeStateNotify($"La cola de {employee.DisplayName} está llena ({Employee.MaxActions} acciones)");
                return false;
            }

            if (!pending.Remove(task))
            {
                if (task.Assignee == null || !task.Assignee.Release(task)) return false;
            }

            task.Assignee = employee;
            task.RetryAfter = 0f;
            employee.Enqueue(task);
            return true;
        }

        /// <summary>Compatibilidad: asignar una tarea de la bolsa.</summary>
        public bool Assign(StaffTask task, Employee employee) => AssignAny(task, employee);

        /// <summary>Una tarea vuelve a la bolsa (el jugador la canceló o el empleado no pudo hacerla).</summary>
        public void ReturnToPool(StaffTask task)
        {
            task.Assignee = null;
            if (task.Cancelled || task.Type == TaskType.IrA || pending.Contains(task)) return;
            if (task.Customer == null && !task.IsMaintenance) return;
            if (task.IsMaintenance)
            {
                if (!task.TargetAlive) return;
                // Si mientras tanto se pidió otra igual, esta sobra.
                StaffTask other = FindFurnitureTask(task.Type, task.Target);
                if (other != null && other != task) return;
            }
            pending.Add(task);
        }

        /// <summary>Para empleados en automático: la tarea más urgente y, entre iguales, la más cercana.</summary>
        public StaffTask TakeBestFor(Employee employee)
        {
            pending.RemoveAll(t => t.IsMaintenance && !t.TargetAlive);

            StaffTask best = null;
            int bestScore = int.MaxValue;
            foreach (StaffTask task in pending)
            {
                if (task.RetryAfter > Time.time) continue;
                if (task.Customer == null && !task.IsMaintenance) continue;
                if (task.IsMaintenance && !task.Target.IsPlaced) continue;
                int score = task.Priority * 10000 + GridPathfinder.Octile(employee.CurrentCell, task.TargetCell);
                if (score >= bestScore) continue;
                best = task;
                bestScore = score;
            }

            if (best == null) return null;
            pending.Remove(best);
            best.Assignee = employee;
            return best;
        }

        /// <summary>La tarea activa de un cliente (en la bolsa o en manos de alguien), o null.</summary>
        public StaffTask FindTaskFor(Customer customer)
        {
            foreach (StaffTask t in pending)
                if (t.Customer == customer) return t;

            StaffManager staff = StaffManager.Instance;
            if (staff == null) return null;
            foreach (Employee e in staff.Employees)
                foreach (StaffTask t in e.AllTasks)
                    if (t.Customer == customer) return t;
            return null;
        }

        /// <summary>Todas las tareas del local salvo las órdenes de caminar: las de la bolsa y las de cada empleado.</summary>
        public List<StaffTask> AllCustomerTasks()
        {
            var list = pending.Where(t => !t.IsMaintenance || t.TargetAlive).ToList();
            StaffManager staff = StaffManager.Instance;
            if (staff != null)
                foreach (Employee e in staff.Employees)
                    foreach (StaffTask t in e.AllTasks)
                        if (t.Type != TaskType.IrA) list.Add(t);
            return list;
        }

        /// <summary>A quién conviene darle una tarea: el que tenga menos trabajo y, entre iguales, el más cercano.</summary>
        public Employee BestEmployeeFor(StaffTask task)
        {
            StaffManager staff = StaffManager.Instance;
            if (staff == null) return null;

            Employee best = null;
            int bestScore = int.MaxValue;
            foreach (Employee e in staff.Employees)
            {
                if (!e.HasRoom || e == task.Assignee) continue;
                int score = e.Load * 10000 + GridPathfinder.Octile(e.CurrentCell, task.TargetCell);
                if (score >= bestScore) continue;
                best = e;
                bestScore = score;
            }
            return best;
        }

        public void Clear() => pending.Clear();

        private static void CafeStateNotify(string text) => CafeState.Instance?.Notify(text, NoteKind.Malo);
    }

    /// <summary>Qué se puede pedir ahora: productos con su estación en el local y con insumos en la despensa.</summary>
    public static class MenuService
    {
        public static List<ProductData> GetAvailableProducts(GridManager grid) =>
            grid.Database.products.Where(p => IsAvailable(grid, p)).ToList();

        public static bool HasStation(GridManager grid, ProductData product)
        {
            if (product == null || product.station == null) return false;
            foreach (var f in grid.AllFurniture)
                if (f.Data == product.station) return true;
            return false;
        }

        public static bool IsAvailable(GridManager grid, ProductData product) =>
            HasStation(grid, product) && (Inventory.Instance == null || Inventory.Instance.HasIngredients(product));
    }
}
