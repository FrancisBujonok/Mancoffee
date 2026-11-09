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
        /// <summary>1 = local y personal. 2 = además despensa, vajilla, precios y estado de los muebles.</summary>
        public int version = SaveSystem.CurrentVersion;
        public int day;
        public int money;
        public float reputation;
        public int wallMode;
        public List<FloorEntry> floor = new();
        public List<WallEntry> walls = new();
        public List<FurnitureEntry> furniture = new();
        public List<EmployeeEntry> employees = new();

        // Versión 2
        public List<StockEntry> stock = new();
        public List<DeliveryEntry> deliveries = new();
        public List<PriceEntry> prices = new();
        public int cups = -1, cleanCups, plates = -1, cleanPlates;
    }

    [Serializable] public class FloorEntry { public int x, y; public string type; }
    [Serializable] public class WallEntry { public int x, y, axis, kind; }
    [Serializable] public class FurnitureEntry { public string id; public int x, y, rotation; public int uses, dirtyCups, dirtyPlates; }
    [Serializable] public class EmployeeEntry { public string name; public int mode; public int salary; }
    [Serializable] public class StockEntry { public string id; public int amount; public int expires; }
    [Serializable] public class DeliveryEntry { public string id; public int amount; public int day; public float minutes; }
    [Serializable] public class PriceEntry { public string id; public int price; }

    /// <summary>
    /// Guarda y carga la partida en JSON: el local (suelo, paredes, muebles y su estado), el personal,
    /// la despensa, la vajilla, los precios, el dinero, la reputación y el día.
    /// Los clientes no: el guardado automático ocurre entre días. Las partidas de la versión 1 se actualizan solas.
    /// </summary>
    public class SaveSystem : MonoBehaviour
    {
        public const int CurrentVersion = 2;

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
            Inventory inv = Inventory.Instance;
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
            {
                FurnitureState st = f.State;
                data.furniture.Add(new FurnitureEntry
                {
                    id = f.Data.id, x = f.Origin.x, y = f.Origin.y, rotation = f.Rotation,
                    uses = st != null ? st.Uses : 0,
                    dirtyCups = st != null ? st.DirtyCups : 0,
                    dirtyPlates = st != null ? st.DirtyPlates : 0
                });
            }

            foreach (Employee e in staff.Employees)
                data.employees.Add(new EmployeeEntry { name = e.DisplayName, mode = (int)e.Mode, salary = e.Salary });

            if (inv != null)
            {
                foreach (StockBatch b in inv.Batches)
                    data.stock.Add(new StockEntry { id = b.Ingredient.id, amount = b.Amount, expires = b.ExpiresDay });
                foreach (Delivery d in inv.Deliveries)
                    data.deliveries.Add(new DeliveryEntry { id = d.Ingredient.id, amount = d.Amount, day = d.ArriveDay, minutes = d.ArriveMinutes });
                foreach (KeyValuePair<ProductData, int> kv in inv.CustomPrices)
                    data.prices.Add(new PriceEntry { id = kv.Key.id, price = kv.Value });
                data.cups = inv.TotalDishes(DishKind.Taza);
                data.cleanCups = inv.CleanDishes(DishKind.Taza);
                data.plates = inv.TotalDishes(DishKind.Plato);
                data.cleanPlates = inv.CleanDishes(DishKind.Plato);
            }

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

            Inventory inv = Inventory.Instance;
            if (inv != null) inv.IsLoading = true;
            try
            {
                LoadInto(data, inv);
            }
            finally
            {
                if (inv != null) inv.IsLoading = false;
            }
            CafeState.Instance.Notify(data.version < CurrentVersion ? "Partida cargada y actualizada" : "Partida cargada");
            return true;
        }

        private void LoadInto(SaveData data, Inventory inv)
        {
            // Limpiar lo que hay ahora.
            spawner.ClearAll();
            staff.ClearAll();
            TaskManager.Instance.Clear();
            foreach (PlacedFurniture f in grid.AllFurniture.ToList())
            {
                f.State?.TakeAllDirty(); // que no devuelva vajilla al destruirse
                Destroy(f.gameObject);
            }

            GameDatabase db = grid.Database;
            grid.LoadLayout(
                data.floor.Select(e => new KeyValuePair<Vector2Int, FloorTypeData>(
                    new Vector2Int(e.x, e.y), db.FindFloor(e.type) ?? db.DefaultFloor)),
                data.walls.Select(w => new KeyValuePair<WallEdge, WallKind>(
                    new WallEdge(new Vector2Int(w.x, w.y), (EdgeAxis)w.axis), (WallKind)w.kind)));

            CafeState.Instance.LoadState(data.day, data.money, data.reputation);

            // Despensa y vajilla (antes que los muebles, para que la vajilla sucia cuadre).
            if (inv != null)
            {
                if (data.version < 2 || data.cups < 0)
                {
                    inv.ResetNewGame();
                }
                else
                {
                    inv.ClearStock();
                    foreach (StockEntry s in data.stock)
                        inv.Add(db.FindIngredient(s.id), s.amount, s.expires);
                    foreach (DeliveryEntry d in data.deliveries)
                    {
                        IngredientData i = db.FindIngredient(d.id);
                        if (i != null) inv.AddDelivery(i, d.amount, d.day, d.minutes);
                    }
                    foreach (PriceEntry p in data.prices)
                    {
                        ProductData product = db.products.Find(x => x != null && x.id == p.id);
                        if (product != null) inv.SetPrice(product, p.price);
                    }
                    inv.LoadDishes(data.cups, data.cleanCups, data.plates, data.cleanPlates);
                }
            }

            int dirtyCups = 0, dirtyPlates = 0;
            foreach (FurnitureEntry entry in data.furniture)
            {
                FurnitureData furnitureData = db.FindFurniture(entry.id);
                if (furnitureData == null) continue;
                var origin = new Vector2Int(entry.x, entry.y);
                if (!grid.CanPlace(furnitureData.GetCells(origin, entry.rotation))) continue;
                PlacedFurniture placed = FurnitureFactory.Create(furnitureData, origin, entry.rotation, grid, furnitureParent);
                grid.Occupy(placed);
                placed.State?.LoadState(entry.uses, entry.dirtyCups, entry.dirtyPlates);
                dirtyCups += entry.dirtyCups;
                dirtyPlates += entry.dirtyPlates;
            }

            // Lo que estaba en manos de clientes al guardar vuelve limpio.
            if (inv != null && data.version >= 2 && data.cups >= 0)
            {
                inv.ReturnClean(DishKind.Taza, inv.DirtyDishes(DishKind.Taza) - dirtyCups);
                inv.ReturnClean(DishKind.Plato, inv.DirtyDishes(DishKind.Plato) - dirtyPlates);
            }

            foreach (EmployeeEntry e in data.employees)
                staff.Hire(false, e.name, (StaffMode)e.mode, e.salary);

            DayCycle.Instance.ResetDay();
            wallRenderer.SetMode((WallViewMode)data.wallMode);
        }
    }
}
