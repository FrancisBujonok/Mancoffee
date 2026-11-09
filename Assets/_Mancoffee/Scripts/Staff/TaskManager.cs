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
        Cobrar
    }

    /// <summary>
    /// Un trabajo pendiente del local. No sabe quién lo hará: lo toma un empleado en automático
    /// o lo asigna el jugador desde el panel.
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

        public string Description
        {
            get
            {
                string where = Customer != null ? $"mesa ({Customer.SeatCell.x},{Customer.SeatCell.y})" : "mesa ?";
                return Type switch
                {
                    TaskType.TomarPedido => $"Tomar pedido · {where}",
                    TaskType.PrepararYServir => $"Preparar {(Product != null ? Product.displayName : "?")} · {where}",
                    _ => $"Cobrar · {where}"
                };
            }
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

        /// <summary>Asignación manual desde el panel: la tarea pasa a la cola del empleado.</summary>
        public bool Assign(StaffTask task, Employee employee)
        {
            if (!pending.Remove(task)) return false;
            task.Assignee = employee;
            employee.Enqueue(task);
            return true;
        }

        /// <summary>Un empleado suelta una tarea sin terminarla: vuelve a la bolsa.</summary>
        public void ReturnToPool(StaffTask task)
        {
            task.Assignee = null;
            if (task.Cancelled || task.Customer == null || pending.Contains(task)) return;
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

        public void Clear() => pending.Clear();
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
    }
}
