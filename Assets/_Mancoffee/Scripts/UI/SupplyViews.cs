using System.Collections.Generic;
using System.Linq;
using Mancoffee.Building;
using Mancoffee.Core;
using Mancoffee.Customers;
using Mancoffee.Data;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mancoffee.UI
{
    // =====================================================================
    // Tarjeta "La barra": vajilla, bacha, cafetera y faltantes
    // =====================================================================

    public class SuppliesView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly Meter cups;
        private readonly Meter plates;
        private readonly Label sink;
        private readonly Meter machine;
        private readonly VisualElement machineRow;
        private readonly VisualElement alerts;
        private string alertKey = "";

        private class Meter
        {
            public VisualElement Row;
            public VisualElement Fill;
            public Label Value;
        }

        public SuppliesView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.Shadowed(parent, out VisualElement paper, "supplies-wrap", "supplies");
            Root.AddToClassList("rot-c");
            Root.AddManipulator(new Clickable(() => ctx.Hud.OpenPanel(HudPanel.Despensa)));

            VisualElement head = UI.El(paper, "row-between");
            UI.Text(head, "La barra", "supplies-title", "f-display");
            UI.Text(head, "Despensa · I", "supplies-hint");

            cups = AddMeter(paper, "Tazas");
            plates = AddMeter(paper, "Platos");
            VisualElement sinkRow = UI.El(paper, "supply-row");
            UI.Text(sinkRow, "Bacha", "supply-name");
            sink = UI.Text(sinkRow, "", "supply-value", "grow");
            machine = AddMeter(paper, "Cafetera");
            machineRow = machine.Row;
            alerts = UI.El(paper, "supply-alerts");
        }

        private static Meter AddMeter(VisualElement parent, string name)
        {
            var m = new Meter { Row = UI.El(parent, "supply-row") };
            UI.Text(m.Row, name, "supply-name");
            VisualElement bar = UI.El(m.Row, "supply-bar");
            m.Fill = UI.El(bar, "supply-fill");
            m.Value = UI.Text(m.Row, "", "supply-value");
            return m;
        }

        public void Refresh()
        {
            Inventory inv = Inventory.Instance;
            if (inv == null) return;

            SetDishes(cups, inv, DishKind.Taza);
            SetDishes(plates, inv, DishKind.Plato);

            List<FurnitureState> sinks = ctx.Grid.AllFurniture.Where(f => f.Data.isSink).Select(f => f.State).Where(s => s != null).ToList();
            if (sinks.Count == 0)
            {
                sink.text = "Falta construir una";
                sink.EnableInClassList("supply-bad", true);
            }
            else
            {
                int waiting = sinks.Sum(s => s.DirtyCount);
                int onTables = ctx.Grid.AllFurniture.Where(f => f.State != null && f.State.IsTable).Sum(f => f.State.DirtyCount);
                sink.text = waiting == 0 && onTables == 0 ? "Todo lavado"
                    : $"{waiting} para lavar" + (onTables > 0 ? $" · {onTables} en mesas" : "");
                sink.EnableInClassList("supply-bad", waiting + onTables >= 5);
            }

            FurnitureState worst = ctx.Grid.AllFurniture
                .Select(f => f.State)
                .Where(s => s != null && s.NeedsMaintenance)
                .OrderByDescending(s => (float)s.Uses / s.MaintenanceEvery)
                .FirstOrDefault();
            UI.Show(machineRow, worst != null);
            if (worst != null)
            {
                float ratio = Mathf.Clamp01((float)worst.Uses / worst.MaintenanceEvery);
                UI.SetFill(machine.Fill, ratio);
                machine.Fill.EnableInClassList("supply-fill--bad", worst.NeedsPurge);
                machine.Fill.EnableInClassList("supply-fill--warn", !worst.NeedsPurge && ratio >= 0.7f);
                machine.Value.text = worst.NeedsPurge ? "¡Purgar!" : $"{worst.Uses}/{worst.MaintenanceEvery}";
                machine.Value.EnableInClassList("supply-bad", worst.NeedsPurge);
            }

            // Faltantes: sin stock (rojo) o con poco y nada en camino (amarillo).
            var chips = new List<(string text, bool bad)>();
            foreach (IngredientData i in inv.Database.ingredients)
            {
                int count = inv.Count(i);
                if (count == 0) chips.Add(($"Sin {i.displayName.ToLowerInvariant()}", true));
                else if (count <= Mathf.Max(2, i.packSize / 4) && inv.Incoming(i) == 0) chips.Add(($"Poco: {i.displayName.ToLowerInvariant()}", false));
            }
            string key = string.Join("|", chips.Select(c => c.text));
            if (key == alertKey) return;
            alertKey = key;
            alerts.Clear();
            foreach ((string text, bool bad) in chips)
                UI.Text(alerts, text, "supply-chip", bad ? "supply-chip--bad" : "supply-chip--warn", "f-heavy");
        }

        private static void SetDishes(Meter m, Inventory inv, DishKind kind)
        {
            int clean = inv.CleanDishes(kind), total = inv.TotalDishes(kind);
            float ratio = total > 0 ? (float)clean / total : 0f;
            UI.SetFill(m.Fill, ratio);
            bool bad = clean <= 2;
            m.Fill.EnableInClassList("supply-fill--bad", bad);
            m.Fill.EnableInClassList("supply-fill--warn", !bad && ratio < 0.5f);
            m.Value.text = $"{clean}/{total}";
            m.Value.EnableInClassList("supply-bad", bad);
        }
    }

    // =====================================================================
    // Despensa: insumos, pedidos al proveedor y vajilla
    // =====================================================================

    public class DespensaView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly VisualElement list;
        private readonly Label cash;
        private string key = "";

        private class Row
        {
            public IngredientData Ingredient;
            public Label Stock;
            public Label Incoming;
            public VisualElement Buy;
        }

        private readonly List<Row> rows = new();
        private Label cupsLabel;
        private Label platesLabel;

        public DespensaView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.El(parent, "layer");
            UI.El(Root, "dim");
            UI.Shadowed(Root, out VisualElement board, "pantry-wrap", "chalkboard", "pantry");
            VisualElement head = UI.El(board, "row-between");
            VisualElement titles = UI.El(head);
            UI.Text(titles, "Despensa", "carta-title", "f-display");
            UI.Text(titles, "Con el local abierto, lo que pedís llega en una hora de juego. Con el local cerrado, llega al abrir.", "carta-sub");
            VisualElement right = UI.El(head, "row");
            cash = UI.Text(right, "", "pantry-cash", "f-mono");
            VisualElement close = UI.Button(right, null, () => ctx.Hud.ClosePanel(), "btn-chalk");
            UI.Icon(close, IconKind.Close);
            UI.Text(close, "Cerrar", "btn-label", "f-heavy");
            list = UI.El(board, "pantry-list");
        }

        public void Refresh()
        {
            Inventory inv = Inventory.Instance;
            if (inv == null) return;
            cash.text = UI.Money(ctx.Cafe.Money);

            string newKey = string.Join(",", inv.Database.ingredients.Select(i => i.id));
            if (newKey != key)
            {
                key = newKey;
                Build(inv);
            }

            foreach (Row r in rows)
            {
                IngredientData i = r.Ingredient;
                int count = inv.Count(i);
                int expiring = inv.ExpiringToday(i);
                string stock = count == 0 ? "No queda nada" : $"Quedan {count} {i.unitName}";
                if (expiring > 0 && i.shelfDays > 0) stock += $" · {expiring} se tiran hoy al cerrar";
                r.Stock.text = stock;
                r.Stock.EnableInClassList("pantry-bad", count == 0);

                List<Delivery> coming = inv.Deliveries.Where(d => d.Ingredient == i).ToList();
                r.Incoming.text = coming.Count == 0 ? "" : $"En camino: {coming.Sum(d => d.Amount)} {i.unitName}, llega {inv.ArrivalText(coming[0])}";
                r.Buy.SetEnabled(ctx.Cafe.CanAfford(i.packPrice));
            }

            cupsLabel.text = $"{inv.CleanDishes(DishKind.Taza)} limpias de {inv.TotalDishes(DishKind.Taza)}";
            platesLabel.text = $"{inv.CleanDishes(DishKind.Plato)} limpios de {inv.TotalDishes(DishKind.Plato)}";
        }

        private void Build(Inventory inv)
        {
            list.Clear();
            rows.Clear();

            foreach (IngredientData ingredient in inv.Database.ingredients)
            {
                IngredientData i = ingredient;
                VisualElement row = UI.El(list, "pantry-row");
                VisualElement icon = UI.El(row, "pantry-icon");
                if (i.icon != null) UI.Pixel(icon, i.icon, 4f);
                VisualElement info = UI.El(row, "grow");
                UI.Text(info, i.displayName, "pantry-name", "f-display");
                string shelf = i.shelfDays == 0 ? "no se vence" : i.shelfDays == 1 ? "se vence el mismo día" : $"dura {i.shelfDays} días";
                UI.Text(info, $"{i.packName} · {shelf}", "pantry-pack");
                var r = new Row { Ingredient = i };
                r.Stock = UI.Text(info, "", "pantry-stock", "f-heavy");
                r.Incoming = UI.Text(info, "", "pantry-incoming");
                r.Buy = UI.Button(row, $"Pedir · ${i.packPrice}", () => inv.Order(i), "btn-tomato", "pantry-buy");
                rows.Add(r);
            }

            UI.Text(list, "Vajilla", "pantry-section", "f-display");
            cupsLabel = DishRow(inv, DishKind.Taza, "Tazas", "Las bebidas en mesa van en taza. Sin tazas limpias se sirven en vaso descartable (menos propina).");
            platesLabel = DishRow(inv, DishKind.Plato, "Platos", "La pastelería en mesa va en plato. Sin platos limpios va en servilleta.");
        }

        private Label DishRow(Inventory inv, DishKind kind, string name, string help)
        {
            VisualElement row = UI.El(list, "pantry-row");
            VisualElement icon = UI.El(row, "pantry-icon");
            UI.Icon(icon, IconKind.Cup);
            VisualElement info = UI.El(row, "grow");
            UI.Text(info, name, "pantry-name", "f-display");
            UI.Text(info, help, "pantry-pack");
            Label value = UI.Text(info, "", "pantry-stock", "f-heavy");
            UI.Button(row, $"Comprar {inv.DishPackSize} · ${inv.DishPackPrice(kind)}", () => inv.BuyDishes(kind), "btn-tomato", "pantry-buy");
            return value;
        }
    }

    // =====================================================================
    // Panel de pruebas (F1)
    // =====================================================================

    public class DebugView
    {
        public VisualElement Root;

        public DebugView(VisualElement parent, HudContext ctx)
        {
            Root = UI.El(parent, "layer");
            Root.pickingMode = PickingMode.Ignore;
            VisualElement wrap = UI.Shadowed(Root, out VisualElement board, "debug-wrap", "chalkboard", "debug");
            VisualElement head = UI.El(board, "row-between");
            UI.Text(head, "Pruebas", "options-title", "f-display");
            UI.Text(head, "F1 cierra", "supplies-hint");

            VisualElement grid = UI.El(board, "debug-grid");
            UI.Button(grid, "+$500", () => ctx.Cafe.AddIncome(500, MoneyCategory.Reembolsos), "opt-btn", "debug-btn");
            foreach (CustomerProfile p in CustomerProfile.All)
            {
                CustomerProfile profile = p;
                UI.Button(grid, $"Traer {p.Name.ToLowerInvariant()}", () => ctx.Spawner.Spawn(profile), "opt-btn", "debug-btn");
            }
            UI.Button(grid, "Llenar despensa", () =>
            {
                Inventory inv = Inventory.Instance;
                foreach (IngredientData i in inv.Database.ingredients) inv.Add(i, i.packSize * 2, Inventory.ExpiryFor(i, ctx.Cafe.Day));
                ctx.Cafe.Notify("Despensa llena (prueba)");
            }, "opt-btn", "debug-btn");
            UI.Button(grid, "Vaciar despensa", () =>
            {
                Inventory inv = Inventory.Instance;
                foreach (IngredientData i in inv.Database.ingredients) inv.Consume(i, inv.Count(i));
            }, "opt-btn", "debug-btn");
            UI.Button(grid, "Cafetera con vapor", () =>
            {
                foreach (PlacedFurniture f in ctx.Grid.AllFurniture)
                {
                    FurnitureState st = f.State;
                    if (st == null || !st.NeedsMaintenance) continue;
                    while (!st.NeedsPurge) st.AddUse();
                }
            }, "opt-btn", "debug-btn");
            UI.Button(grid, "Ensuciar tazas", () =>
            {
                Inventory inv = Inventory.Instance;
                PlacedFurniture sink = ctx.Grid.AllFurniture.FirstOrDefault(f => f.Data.isSink);
                int n = 0;
                while (inv.TakeClean(DishKind.Taza)) n++;
                if (sink != null) sink.State.AddDirty(DishKind.Taza, n);
                else inv.ReturnClean(DishKind.Taza, n);
                ctx.Cafe.Notify(sink != null ? $"{n} tazas sucias en la bacha (prueba)" : "No hay bacha para dejar las tazas");
            }, "opt-btn", "debug-btn");
            UI.Button(grid, "Velocidad x3", () => ctx.Day.SetSpeed(3), "opt-btn", "debug-btn");
        }
    }
}
