using System;
using System.Collections.Generic;
using Mancoffee.Building;
using Mancoffee.Core;
using Mancoffee.Customers;
using Mancoffee.Data;
using Mancoffee.GridSystem;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mancoffee.Staff
{
    /// <summary>Una opción del menú circular.</summary>
    public class PieOption
    {
        public string Label;
        public Sprite Icon;
        public Color IconTint = Color.white;
        public bool Enabled = true;
        public Action OnPick;
    }

    /// <summary>
    /// Control de empleados al estilo Los Sims:
    /// clic en un empleado (o en su foto) lo selecciona; con alguien seleccionado, clic en un cliente,
    /// una estación o el suelo abre un menú circular con lo que puede hacer ahí, y la acción va a su cola.
    /// Sin nadie seleccionado, clic en un cliente con una tarea ofrece a quién dársela.
    /// </summary>
    public class SelectionController : MonoBehaviour
    {
        [SerializeField] private GridManager grid;
        [SerializeField] private Camera cam;
        [SerializeField] private StaffManager staff;
        [SerializeField] private CustomerSpawner customers;
        [SerializeField] private BuildModeController build;
        [SerializeField] private Sprite ringSprite;
        [SerializeField] private Sprite orderIcon;
        [SerializeField] private Sprite billIcon;

        private SpriteRenderer ring;

        public Employee Selected { get; private set; }

        /// <summary>Pide a la interfaz que muestre un menú circular: posición en pantalla (origen abajo a la izquierda), título y opciones.</summary>
        public event Action<Vector2, string, List<PieOption>> PieRequested;

        /// <summary>Pide cerrar el menú circular.</summary>
        public event Action PieClosed;

        private void Awake()
        {
            if (cam == null) cam = Camera.main;
            var ringObject = new GameObject("AnilloSeleccion");
            ringObject.transform.SetParent(transform, false);
            ring = ringObject.AddComponent<SpriteRenderer>();
            ring.sprite = ringSprite;
            ring.sortingOrder = -45;
            ring.enabled = false;
        }

        public void Select(Employee employee)
        {
            Selected = employee;
            PieClosed?.Invoke();
        }

        /// <summary>Pasa al siguiente empleado (Tab), como el cambio de Sim activo.</summary>
        public void SelectNext()
        {
            IReadOnlyList<Employee> list = staff.Employees;
            if (list.Count == 0) { Select(null); return; }
            int index = -1;
            for (int i = 0; i < list.Count; i++) if (list[i] == Selected) index = i;
            Select(list[(index + 1) % list.Count]);
        }

        private void Update()
        {
            if (Selected == null && ring.enabled) ring.enabled = false;
            if (Selected != null)
            {
                ring.enabled = true;
                ring.transform.position = Selected.transform.position;
            }

            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null || UiBlocker.ModalOpen) return;

            if (kb.tabKey.wasPressedThisFrame && !build.IsBuildMode) SelectNext();
            if (build.IsBuildMode) return;

            Vector2 screen = mouse.position.ReadValue();
            if (UiBlocker.IsPointerOverUI(screen)) return;

            if (mouse.rightButton.wasPressedThisFrame)
            {
                PieClosed?.Invoke();
                Selected = null;
                return;
            }

            if (!mouse.leftButton.wasPressedThisFrame) return;

            Vector3 world = cam.ScreenToWorldPoint(screen);
            world.z = 0f;

            Employee clickedEmployee = EmployeeAt(world);
            if (clickedEmployee != null)
            {
                Select(clickedEmployee == Selected ? null : clickedEmployee);
                return;
            }

            List<PieOption> options = BuildOptions(world, out string title);
            if (options.Count > 0) PieRequested?.Invoke(screen, title, options);
            else PieClosed?.Invoke();
        }

        // =========================== Qué hay bajo el mouse ===========================

        private Employee EmployeeAt(Vector2 world)
        {
            Employee best = null;
            foreach (Employee e in staff.Employees)
            {
                if (!e.ContainsPoint(world)) continue;
                if (best == null || e.transform.position.y < best.transform.position.y) best = e;
            }
            return best;
        }

        private Customer CustomerAt(Vector2 world, Vector2Int cell)
        {
            Customer best = null;
            foreach (Customer c in customers.Active)
            {
                if (c == null || c.State == CustomerState.Saliendo) continue;
                bool hit = c.ContainsPoint(world) || (c.IsSeated && c.SeatCell == cell);
                if (!hit) continue;
                if (best == null || c.transform.position.y < best.transform.position.y) best = c;
            }
            return best;
        }

        // =========================== Opciones del menú circular ===========================

        private List<PieOption> BuildOptions(Vector2 world, out string title)
        {
            var options = new List<PieOption>();
            Vector2Int cell = grid.WorldToCell(world);
            title = Selected != null ? Selected.DisplayName : "Asignar";

            Customer customer = CustomerAt(world, cell);
            if (customer != null)
            {
                StaffTask task = TaskManager.Instance.FindTaskFor(customer);
                title = Selected != null ? $"{Selected.DisplayName} · {customer.TableLabel}" : customer.TableLabel;
                if (task == null) return options;
                if (Selected != null) options.Add(OptionForTask(task, Selected));
                else options.AddRange(AssignOptions(task));
                return options;
            }

            if (Selected == null) return options;

            PlacedFurniture furniture = grid.GetFurnitureAt(cell);
            if (furniture != null && (furniture.Data.category == FurnitureCategory.Equipo || furniture.Data.category == FurnitureCategory.Mostrador))
            {
                title = $"{Selected.DisplayName} · {furniture.Data.displayName}";
                foreach (StaffTask t in TaskManager.Instance.AllCustomerTasks())
                {
                    if (t.Type != TaskType.PrepararYServir || t.Product == null || t.Product.station != furniture.Data) continue;
                    options.Add(OptionForTask(t, Selected));
                    if (options.Count >= 6) break;
                }
                if (options.Count == 0)
                    options.Add(new PieOption { Label = "Nada para preparar acá", Enabled = false });
                return options;
            }

            if (grid.IsWalkable(cell))
            {
                Employee e = Selected;
                options.Add(new PieOption
                {
                    Label = "Ir aquí",
                    Enabled = e.HasRoom,
                    OnPick = () => e.WalkTo(cell)
                });
            }
            return options;
        }

        /// <summary>La opción de darle una tarea concreta a un empleado.</summary>
        public PieOption OptionForTask(StaffTask task, Employee employee)
        {
            string label = TaskLabel(task);
            bool mine = task.Assignee == employee;
            if (mine) label += " (ya en su cola)";
            else if (task.Assignee != null) label += $" (se lo saca a {task.Assignee.DisplayName})";

            return new PieOption
            {
                Label = label,
                Icon = IconFor(task),
                Enabled = !mine && employee.HasRoom,
                OnPick = () => TaskManager.Instance.AssignAny(task, employee)
            };
        }

        /// <summary>Una opción por empleado para darle la tarea ("Que lo haga Ana · libre").</summary>
        public List<PieOption> AssignOptions(StaffTask task)
        {
            var list = new List<PieOption>();
            Employee suggested = TaskManager.Instance.BestEmployeeFor(task);
            foreach (Employee e in staff.Employees)
            {
                Employee target = e;
                string load = e.Load == 0 ? "libre" : $"{e.Load} en cola";
                string label = $"{e.DisplayName} · {load}";
                if (e == suggested) label += " ★";
                if (task.Assignee == e) label = $"{e.DisplayName} · ya la tiene";
                list.Add(new PieOption
                {
                    Label = label,
                    Icon = e.Portrait,
                    IconTint = e.Tint,
                    Enabled = task.Assignee != e && e.HasRoom,
                    OnPick = () => TaskManager.Instance.AssignAny(task, target)
                });
            }
            return list;
        }

        public static string TaskLabel(StaffTask task) => task.Type switch
        {
            TaskType.TomarPedido => "Tomar pedido",
            TaskType.PrepararYServir => task.Product != null ? $"Preparar y llevar {task.Product.displayName.ToLowerInvariant()}" : "Preparar pedido",
            TaskType.Cobrar => "Cobrar",
            _ => "Ir aquí"
        };

        /// <summary>Ícono pixel de una tarea: el producto, o el globo del cliente.</summary>
        public Sprite IconFor(StaffTask task)
        {
            return task.Type switch
            {
                TaskType.PrepararYServir => task.Product != null ? task.Product.icon : null,
                TaskType.TomarPedido => orderIcon,
                TaskType.Cobrar => billIcon,
                _ => null
            };
        }
    }
}
