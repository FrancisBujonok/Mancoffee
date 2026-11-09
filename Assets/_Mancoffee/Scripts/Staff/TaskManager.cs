using System.Collections.Generic;
using System.Linq;
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
        IrA
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

        public Employee Assignee { get; internal set; }
        public bool Cancelled { get; internal set; }
        public bool IngredientsPaid { get; internal set; }

        /// <summary>Los empleados automáticos no la toman antes de este momento (por ejemplo, si no había estación libre).</summary>
        public float RetryAfter { get; internal set; }

        /// <summary>Menor = más urgente. Cobrar libera mesas; preparar evita que el pedido se enfríe.</summary>
        public int Priority => Type switch
        {
            TaskType.Cobrar => 0,
            TaskType.PrepararYServir => 1,
            _ => 2
        };

        /// <summary>"Mesa 3", o "Sin mesa".</summary>
        public string Where => Customer != null ? Customer.TableLabel : "";

        public string Title => Type switch
        {
            TaskType.TomarPedido => "Tomar pedido",
            TaskType.PrepararYServir => Product != null ? $"Preparar {Product.displayName.ToLowerInvariant()}" : "Preparar pedido",
            TaskType.Cobrar => "Cobrar la cuenta",
            _ => "Ir a un lugar"
        };

        public string Description => Type == TaskType.IrA ? Title : $"{Title} · {Where}";

        /// <summary>Paciencia del cliente (1 = tranquilo). Las tareas sin cliente no apuran.</summary>
        public float Patience => Customer != null ? Customer.PatienceRatio : 1f;
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

        /// <summary>El cliente se fue: se anulan sus tareas, estén pendientes o en manos de un empleado.</summary>
        public void CancelTasksFor(Customer customer)
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                if (pending[i].Customer != customer) continue;
                pending[i].Cancelled = true;
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
            if (task.Cancelled || task.Customer == null || task.Type == TaskType.IrA || pending.Contains(task)) return;
            pending.Add(task);
        }

        /// <summary>Para empleados en automático: la tarea más urgente y, entre iguales, la más cercana.</summary>
        public StaffTask TakeBestFor(Employee employee)
        {
            StaffTask best = null;
            int bestScore = int.MaxValue;

            foreach (StaffTask task in pending)
            {
                if (task.RetryAfter > Time.time || task.Customer == null) continue;
                int score = task.Priority * 10000 + GridPathfinder.Octile(employee.CurrentCell, task.Customer.SeatCell);
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

        /// <summary>Todas las tareas con cliente: las de la bolsa y las que tiene cada empleado.</summary>
        public List<StaffTask> AllCustomerTasks()
        {
            var list = new List<StaffTask>(pending);
            StaffManager staff = StaffManager.Instance;
            if (staff != null)
                foreach (Employee e in staff.Employees)
                    foreach (StaffTask t in e.AllTasks)
                        if (t.Customer != null) list.Add(t);
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
                int load = e.Load;
                int distance = task.Customer != null ? GridPathfinder.Octile(e.CurrentCell, task.Customer.SeatCell) : 0;
                int score = load * 10000 + distance;
                if (score >= bestScore) continue;
                best = e;
                bestScore = score;
            }
            return best;
        }

        public void Clear() => pending.Clear();

        private static void CafeStateNotify(string text) => Core.CafeState.Instance?.Notify(text, Core.NoteKind.Malo);
    }

    /// <summary>Qué se puede pedir hoy: los productos cuya estación existe en el local.</summary>
    public static class MenuService
    {
        public static List<ProductData> GetAvailableProducts(GridManager grid)
        {
            var stations = new HashSet<FurnitureData>();
            foreach (var f in grid.AllFurniture) stations.Add(f.Data);
            return grid.Database.products.Where(p => p != null && p.station != null && stations.Contains(p.station)).ToList();
        }

        public static bool IsAvailable(GridManager grid, ProductData product)
        {
            if (product == null || product.station == null) return false;
            foreach (var f in grid.AllFurniture)
                if (f.Data == product.station) return true;
            return false;
        }
    }
}
