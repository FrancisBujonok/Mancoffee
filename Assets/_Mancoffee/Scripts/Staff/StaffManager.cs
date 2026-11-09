using System.Collections.Generic;
using System.Linq;
using Mancoffee.Core;
using Mancoffee.GridSystem;
using UnityEngine;

namespace Mancoffee.Staff
{
    /// <summary>Contratar, despedir y listar al personal. Los sueldos se cobran al cerrar cada día.</summary>
    [DefaultExecutionOrder(-90)]
    public class StaffManager : MonoBehaviour
    {
        public static StaffManager Instance { get; private set; }

        [SerializeField] private GridManager grid;
        [SerializeField] private Employee employeePrefab;
        [SerializeField] private Transform staffParent;

        [SerializeField] private int hireCost = 200;
        [SerializeField] private int baseSalary = 35;
        [SerializeField] private int maxStaff = 8;

        [SerializeField] private string[] names =
        {
            "Ana", "Beto", "Caro", "Dani", "Eli", "Fede", "Gabi", "Hugo", "Iara", "Juan",
            "Lu", "Mati", "Nico", "Oli", "Pau", "Sol", "Tomi", "Vale"
        };

        [SerializeField] private Color[] tints =
        {
            new(0.45f, 0.6f, 0.95f), new(0.95f, 0.6f, 0.4f), new(0.55f, 0.85f, 0.55f), new(0.9f, 0.5f, 0.75f),
            new(0.6f, 0.9f, 0.9f), new(0.95f, 0.85f, 0.45f), new(0.7f, 0.6f, 0.95f), new(0.85f, 0.85f, 0.85f)
        };

        private readonly List<Employee> employees = new();

        public IReadOnlyList<Employee> Employees => employees;
        public int HireCost => hireCost;
        public int BaseSalary => baseSalary;
        public int MaxStaff => maxStaff;
        public int TotalSalaries => employees.Sum(e => e.Salary);

        private void Awake() => Instance = this;

        /// <summary>Contrata a alguien. Con charge = false no cobra (empleados iniciales o al cargar partida).</summary>
        public Employee Hire(bool charge, string displayName = null, StaffMode mode = StaffMode.Automatico, int salary = -1)
        {
            if (employees.Count >= maxStaff)
            {
                CafeState.Instance?.Notify("No entra más personal");
                return null;
            }
            if (charge && !CafeState.Instance.TrySpend(hireCost, MoneyCategory.Contrataciones)) return null;

            Employee employee = Instantiate(employeePrefab, staffParent);
            employee.Init(grid, displayName ?? PickName(), mode, salary < 0 ? baseSalary : salary,
                          tints[employees.Count % tints.Length]);
            employees.Add(employee);
            return employee;
        }

        public void Fire(Employee employee)
        {
            if (!employees.Remove(employee)) return;
            employee.ReleaseAllTasks();
            Destroy(employee.gameObject);
        }

        public void ClearAll()
        {
            foreach (Employee e in employees) if (e != null) Destroy(e.gameObject);
            employees.Clear();
        }

        private string PickName()
        {
            foreach (string n in names)
                if (employees.All(e => e.DisplayName != n)) return n;
            return $"Empleado {employees.Count + 1}";
        }
    }
}
