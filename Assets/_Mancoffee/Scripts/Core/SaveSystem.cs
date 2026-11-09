using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mancoffee.Building;
using Mancoffee.Customers;
using Mancoffee.Data;
using Mancoffee.GridSystem;
using Mancoffee.Staff;
using UnityEngine;

namespace Mancoffee.Core
{
    [Serializable]
    public class SaveData
    {
        public int version = 1;
        public int day;
        public int money;
        public float reputation;
        public int wallMode;
        public List<FloorEntry> floor = new();
        public List<WallEntry> walls = new();
        public List<FurnitureEntry> furniture = new();
        public List<EmployeeEntry> employees = new();
    }

    [Serializable] public class FloorEntry { public int x, y; public string type; }
    [Serializable] public class WallEntry { public int x, y, axis, kind; }
    [Serializable] public class FurnitureEntry { public string id; public int x, y, rotation; }
    [Serializable] public class EmployeeEntry { public string name; public int mode; public int salary; }

    /// <summary>
    /// Guarda y carga la partida en JSON. Se guarda el local (suelo, paredes, muebles), el personal,
    /// el dinero, la reputación y el día. Los clientes no: el guardado automático ocurre entre días.
    /// </summary>
    public class SaveSystem : MonoBehaviour
    {
        [SerializeField] private GridManager grid;
        [SerializeField] private StaffManager staff;
        [SerializeField] private CustomerSpawner spawner;
        [SerializeField] private WallRenderer wallRenderer;
        [SerializeField] private Transform furnitureParent;

        public static string SavePath => Path.Combine(Application.persistentDataPath, "mancoffee_partida.json");
        public static bool HasSave => File.Exists(SavePath);

        public void Save()
        {
            CafeState cafe = CafeState.Instance;
            var data = new SaveData
            {
                day = cafe.Day,
                money = cafe.Money,
                reputation = cafe.Reputation,
                wallMode = (int)wallRenderer.Mode
            };

            foreach (KeyValuePair<Vector2Int, FloorTypeData> kv in grid.Floor)
                data.floor.Add(new FloorEntry { x = kv.Key.x, y = kv.Key.y, type = kv.Value.id });

            foreach (KeyValuePair<WallEdge, WallKind> kv in grid.Walls)
                data.walls.Add(new WallEntry { x = kv.Key.Cell.x, y = kv.Key.Cell.y, axis = (int)kv.Key.Axis, kind = (int)kv.Value });

            foreach (PlacedFurniture f in grid.AllFurniture)
                data.furniture.Add(new FurnitureEntry { id = f.Data.id, x = f.Origin.x, y = f.Origin.y, rotation = f.Rotation });

            foreach (Employee e in staff.Employees)
                data.employees.Add(new EmployeeEntry { name = e.DisplayName, mode = (int)e.Mode, salary = e.Salary });

            try
            {
                File.WriteAllText(SavePath, JsonUtility.ToJson(data, true));
                cafe.Notify("Partida guardada");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Mancoffee] No se pudo guardar: {ex}");
                cafe.Notify("No se pudo guardar la partida");
            }
        }

        public bool Load()
        {
            if (!HasSave) return false;

            SaveData data;
            try
            {
                data = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
            }
            catch (Exception ex)
            {
                Debug.LogError($"[Mancoffee] No se pudo leer la partida: {ex}");
                CafeState.Instance.Notify("La partida guardada está dañada");
                return false;
            }

            // Limpiar lo que hay ahora.
            spawner.ClearAll();
            staff.ClearAll();
            TaskManager.Instance.Clear();
            foreach (PlacedFurniture f in grid.AllFurniture.ToList()) Destroy(f.gameObject);

            GameDatabase db = grid.Database;
            grid.LoadLayout(
                data.floor.Select(e => new KeyValuePair<Vector2Int, FloorTypeData>(
                    new Vector2Int(e.x, e.y), db.FindFloor(e.type) ?? db.DefaultFloor)),
                data.walls.Select(w => new KeyValuePair<WallEdge, WallKind>(
                    new WallEdge(new Vector2Int(w.x, w.y), (EdgeAxis)w.axis), (WallKind)w.kind)));

            foreach (FurnitureEntry entry in data.furniture)
            {
                FurnitureData furnitureData = db.FindFurniture(entry.id);
                if (furnitureData == null) continue;
                var origin = new Vector2Int(entry.x, entry.y);
                if (!grid.CanPlace(furnitureData.GetCells(origin, entry.rotation))) continue;
                grid.Occupy(FurnitureFactory.Create(furnitureData, origin, entry.rotation, grid, furnitureParent));
            }

            foreach (EmployeeEntry e in data.employees)
                staff.Hire(false, e.name, (StaffMode)e.mode, e.salary);

            CafeState.Instance.LoadState(data.day, data.money, data.reputation);
            DayCycle.Instance.ResetDay();
            wallRenderer.SetMode((WallViewMode)data.wallMode);
            CafeState.Instance.Notify("Partida cargada");
            return true;
        }
    }
}
