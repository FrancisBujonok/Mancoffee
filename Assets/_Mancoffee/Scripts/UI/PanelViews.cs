using System.Collections.Generic;
using System.Linq;
using Mancoffee.Building;
using Mancoffee.Core;
using Mancoffee.Data;
using Mancoffee.GridSystem;
using Mancoffee.Staff;
using UnityEngine;
using UnityEngine.Tilemaps;
using UnityEngine.UIElements;

namespace Mancoffee.UI
{
    // =====================================================================
    // Catálogo de construcción (carpeta kraft con pestañas)
    // =====================================================================

    public class BuildCatalogView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly VisualElement tabs;
        private readonly VisualElement body;
        private readonly Label cash;
        private string key = "";
        private FurnitureCategory? filter;

        private static readonly (BuildTool tool, string label, IconKind icon)[] Tabs =
        {
            (BuildTool.Muebles, "Muebles", IconKind.Chair),
            (BuildTool.Paredes, "Paredes", IconKind.Walls),
            (BuildTool.Puertas, "Puertas", IconKind.Door),
            (BuildTool.Ventanas, "Ventanas", IconKind.Window),
            (BuildTool.Suelo, "Suelo", IconKind.Floor)
        };

        public BuildCatalogView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.El(parent, "catalog-wrap");
            tabs = UI.El(Root, "tabs");
            VisualElement panelWrap = UI.Shadowed(Root, out VisualElement panel, "catalog-shadow", "catalog");
            panelWrap.AddToClassList("grow");
            VisualElement head = UI.El(panel, "row-between");
            UI.Text(head, "Catálogo", "cat-title", "f-display");
            VisualElement cashBox = UI.El(head, "cat-cash");
            UI.Icon(cashBox, IconKind.Coin);
            cash = UI.Text(cashBox, "$0", "cat-cash-value", "f-mono");
            body = UI.El(panel, "cat-body");
        }

        public void Refresh()
        {
            BuildModeController b = ctx.Build;
            cash.text = UI.Money(ctx.Cafe.Money);
            string newKey = $"{b.Tool}|{b.SelectedIndex}|{b.FloorIndex}|{filter}|{b.Catalog.Count}|{b.FloorTypes.Count}";
            if (newKey == key) return;
            key = newKey;

            tabs.Clear();
            foreach ((BuildTool tool, string label, IconKind icon) in Tabs)
            {
                BuildTool t = tool;
                VisualElement tab = UI.Button(tabs, null, () => ctx.Build.SetTool(t), "tab");
                if (b.Tool == tool) tab.AddToClassList("tab--on");
                UI.Icon(tab, icon);
                UI.Text(tab, label, "tab-label", "f-display");
            }

            body.Clear();
            switch (b.Tool)
            {
                case BuildTool.Muebles: BuildFurniture(b); break;
                case BuildTool.Suelo: BuildFloors(b); break;
                default: BuildWalls(b); break;
            }
        }

        private void BuildFurniture(BuildModeController b)
        {
            VisualElement chips = UI.El(body, "chips");
            Chip(chips, "Todo", null);
            Chip(chips, "Mesas", FurnitureCategory.Mesa);
            Chip(chips, "Asientos", FurnitureCategory.Asiento);
            Chip(chips, "Equipos", FurnitureCategory.Equipo);
            Chip(chips, "Deco", FurnitureCategory.Decoracion);

            VisualElement grid = UI.El(body, "grid");
            for (int i = 0; i < b.Catalog.Count; i++)
            {
                FurnitureData data = b.Catalog[i];
                if (filter.HasValue && !Matches(data.category, filter.Value)) continue;
                int index = i;
                VisualElement item = UI.Button(grid, null, () => ctx.Build.SelectFurniture(index), "item");
                bool on = b.SelectedIndex == i;
                if (on) item.AddToClassList("item--on");
                VisualElement plate = UI.El(item, "item-plate");
                Sprite sprite = data.GetSprite(0);
                float scale = sprite != null ? Mathf.Max(1f, Mathf.Floor(Mathf.Min(70f / sprite.rect.height, 120f / sprite.rect.width))) : 1f;
                UI.Pixel(plate, sprite, scale);
                if (i < 9) UI.Text(plate, (i + 1).ToString(), "item-num", "f-mono");
                UI.Text(item, data.displayName, "item-name", "f-heavy");
                VisualElement foot = UI.El(item, "item-foot");
                UI.Text(foot, $"{data.size.x}×{data.size.y}", "item-size");
                Label price = UI.Text(foot, $"${data.price}", "price-tag", "f-mono");
                if (!ctx.Cafe.CanAfford(data.price)) price.AddToClassList("price-tag--bad");
                if (on)
                {
                    VisualElement check = UI.El(item, "item-check");
                    UI.Icon(check, IconKind.Check);
                }
            }

            if (b.SelectedIndex >= 0 && b.SelectedIndex < b.Catalog.Count)
            {
                FurnitureData sel = b.Catalog[b.SelectedIndex];
                var sep = new DashedLine();
                sep.AddToClassList("cat-sep");
                body.Add(sep);
                VisualElement row = UI.El(body, "row-between");
                UI.Text(row, sel.displayName, "detail-title", "f-display");
                UI.Text(row, $"${sel.price}", "detail-price", "f-mono");
                UI.Text(body, Describe(sel), "detail-text");
            }

            VisualElement keys = UI.El(body, "keys");
            Key(keys, "R", "rotar");
            Key(keys, "Clic", "colocar");
            Key(keys, "Clic en mueble", "mover");
            Key(keys, "Esc", "soltar");
        }

        private static bool Matches(FurnitureCategory c, FurnitureCategory f) =>
            f == FurnitureCategory.Equipo ? c == FurnitureCategory.Equipo || c == FurnitureCategory.Mostrador : c == f;

        private void Chip(VisualElement parent, string label, FurnitureCategory? category)
        {
            VisualElement chip = UI.Button(parent, label, () => filter = category, "chip");
            if (filter == category) chip.AddToClassList("chip--on");
        }

        private static void Key(VisualElement parent, string key, string text)
        {
            VisualElement hint = UI.El(parent, "key-hint");
            UI.Text(hint, key, "key", "f-heavy");
            UI.Text(hint, text);
        }

        private string Describe(FurnitureData f)
        {
            string sells = string.Join(", ", ctx.Grid.Database.products.Where(p => p.station == f).Select(p => p.displayName.ToLowerInvariant()));
            return f.category switch
            {
                FurnitureCategory.Mesa => "Los clientes se sientan en las sillas que la tocan. Cada mesa recibe un número.",
                FurnitureCategory.Asiento => "Ponela al lado de una mesa: sin mesa nadie se sienta.",
                FurnitureCategory.Mostrador => "Es donde se paga y se arman los pedidos.",
                FurnitureCategory.Equipo => sells.Length > 0 ? $"Permite vender {sells}." : "Equipo de cocina.",
                _ => "Decoración: hace más lindo el lugar."
            };
        }

        private void BuildFloors(BuildModeController b)
        {
            UI.Text(body, "Pintá el suelo con clic sostenido. Clic derecho quita suelo.", "detail-text");
            VisualElement list = UI.El(body, "floor-list");
            for (int i = 0; i < b.FloorTypes.Count; i++)
            {
                FloorTypeData f = b.FloorTypes[i];
                int index = i;
                VisualElement item = UI.Button(list, null, () => ctx.Build.SelectFloor(index), "floor-item");
                if (b.FloorIndex == i) item.AddToClassList("floor-item--on");
                VisualElement swatch = UI.El(item, "floor-swatch");
                Sprite s = (f.tileA as Tile)?.sprite;
                if (s != null) UI.Pixel(swatch, s, 1f);
                UI.Text(item, f.displayName, "floor-name", "f-heavy");
                UI.Text(item, $"${f.price}", "price-tag", "f-mono");
            }
        }

        private void BuildWalls(BuildModeController b)
        {
            WallKind kind = b.Tool switch
            {
                BuildTool.Puertas => WallKind.Puerta,
                BuildTool.Ventanas => WallKind.Ventana,
                _ => WallKind.Pared
            };
            string title = kind switch { WallKind.Puerta => "Puerta", WallKind.Ventana => "Ventana", _ => "Pared" };
            string text = kind switch
            {
                WallKind.Puerta => "Clic en un borde para poner una puerta. La entrada principal no se puede tapar.",
                WallKind.Ventana => "Clic en un borde con pared para cambiarla por una ventana.",
                _ => "Clic en un borde del suelo para levantar pared. Clic derecho la quita."
            };
            VisualElement info = UI.El(body, "tool-info");
            VisualElement row = UI.El(info, "row-between");
            UI.Text(row, title, "tool-info-title", "f-display");
            UI.Text(row, $"${b.WallPrice(kind)}", "detail-price", "f-mono");
            UI.Text(info, text, "tool-info-text");
            VisualElement keys = UI.El(body, "keys");
            Key(keys, "Clic", "poner");
            Key(keys, "Clic der.", "quitar");
            Key(keys, "V", "ver paredes");
        }
    }

    /// <summary>Nota con la ayuda del modo construcción.</summary>
    public class BuildTipView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly Label text;

        public BuildTipView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.Shadowed(parent, out VisualElement paper, "tip-wrap", "postit");
            UI.El(Root, "tape");
            text = UI.Text(paper, "", "postit-text");
        }

        public void Refresh() => text.text = ctx.Build.HelpText;
    }

    // =====================================================================
    // Tablero de personal: columnas por empleado y arrastrar comandas
    // =====================================================================

    public class StaffBoardView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly VisualElement dragLayer;
        private readonly ScrollView columns;
        private readonly Label hireLabel;
        private string key = "";
        private bool dragging;
        private Employee confirmFire;

        private readonly List<TicketView> tickets = new();
        private readonly List<(Employee e, Label status, VisualElement fill, VisualElement auto, VisualElement man)> cards = new();
        private readonly List<(VisualElement col, Employee e)> dropTargets = new();

        public StaffBoardView(VisualElement parent, VisualElement dragLayer, HudContext ctx)
        {
            this.ctx = ctx;
            this.dragLayer = dragLayer;
            Root = UI.El(parent, "layer");
            UI.El(Root, "dim");
            VisualElement wrap = UI.Shadowed(Root, out VisualElement board, "staffboard-wrap", "staffboard", "chalkboard");

            VisualElement head = UI.El(board, "row-between");
            VisualElement titles = UI.El(head);
            UI.Text(titles, "Personal y comandas", "sb-title", "f-display");
            UI.Text(titles, "Arrastrá una comanda hasta una persona para dársela. Tocá la × para sacársela.", "sb-sub");
            VisualElement buttons = UI.El(head, "row");
            VisualElement hire = UI.Button(buttons, null, Hire, "btn-tomato", "btn-tomato--lg");
            UI.Icon(hire, IconKind.Plus);
            hireLabel = UI.Text(hire, "Contratar", "btn-label", "f-heavy");
            VisualElement close = UI.Button(buttons, null, () => ctx.Hud.ClosePanel(), "btn-chalk");
            UI.Icon(close, IconKind.Close);
            UI.Text(close, "Cerrar", "btn-label", "f-heavy");

            columns = new ScrollView(ScrollViewMode.Horizontal) { verticalScrollerVisibility = ScrollerVisibility.Hidden };
            columns.AddToClassList("sb-body");
            columns.contentContainer.AddToClassList("sb-scroll");
            board.Add(columns);

            VisualElement foot = UI.El(board, "sb-foot");
            VisualElement legend = UI.El(foot, "legend");
            Legend(legend, UI.Sage, "Tranquilo");
            Legend(legend, UI.Mustard, "Esperando");
            Legend(legend, UI.Tomato, "Urgente");
            UI.Text(foot, "Automático: elige solo cuando su cola está vacía. Manual: solo hace lo que le das.", "col-hint");
        }

        private static void Legend(VisualElement parent, Color c, string label)
        {
            VisualElement item = UI.El(parent, "legend-item");
            UI.El(item, "legend-swatch").style.backgroundColor = c;
            UI.Text(item, label);
        }

        private void Hire()
        {
            Employee e = ctx.Staff.Hire(true);
            if (e == null)
            {
                string why = ctx.Staff.Employees.Count >= ctx.Staff.MaxStaff ? "Ya tenés el equipo completo" : "No alcanza el dinero para contratar";
                ctx.Cafe.Notify(why, NoteKind.Malo);
            }
            else ctx.Cafe.Notify($"Contrataste a {e.DisplayName}", NoteKind.Bueno);
        }

        public void Refresh()
        {
            hireLabel.text = $"Contratar · ${ctx.Staff.HireCost}";
            List<StaffTask> pool = ctx.Tasks.Pending.Where(t => t.Assignee == null && t.Customer != null).OrderBy(t => t.Patience).ToList();
            IReadOnlyList<Employee> staff = ctx.Staff.Employees;
            string newKey = string.Join(",", pool.Select(HudContext.Key)) + "|" +
                            string.Join(";", staff.Select(e => HudContext.Key(e) + ":" + string.Join(",", e.AllTasks.Select(HudContext.Key)))) +
                            "|" + HudContext.Key(confirmFire);

            if (!dragging && newKey != key)
            {
                key = newKey;
                Rebuild(pool, staff);
            }

            foreach (TicketView t in tickets) t.Refresh();
            foreach (var c in cards)
            {
                if (c.e == null) continue;
                c.status.text = c.e.StatusText;
                UI.SetFill(c.fill, c.e.Progress);
                bool manual = c.e.Mode == StaffMode.Manual;
                c.auto.EnableInClassList("toggle-opt--on", !manual);
                c.man.EnableInClassList("toggle-opt--on", manual);
            }
        }

        private void Rebuild(List<StaffTask> pool, IReadOnlyList<Employee> staff)
        {
            columns.Clear();
            tickets.Clear();
            cards.Clear();
            dropTargets.Clear();

            // Columna de comandas sin dueño
            VisualElement poolCol = UI.El(columns, "sb-col", "sb-col--pool");
            VisualElement poolHead = UI.El(poolCol, "col-head");
            UI.Text(poolHead, "Sin asignar", "col-title", "f-display");
            UI.Text(poolHead, pool.Count.ToString(), "count-pill", "f-heavy");
            UI.El(poolCol, "rail-small");
            ScrollView poolScroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
            poolScroll.AddToClassList("col-scroll");
            poolCol.Add(poolScroll);
            if (pool.Count == 0) UI.Text(poolScroll, "No hay comandas esperando", "col-empty");
            for (int i = 0; i < pool.Count; i++) AddTicket(poolScroll, pool[i], i, false);
            UI.El(poolCol, "drop-zone").Add(new Label("Soltá acá para devolverla") { pickingMode = PickingMode.Ignore });
            dropTargets.Add((poolCol, null));

            foreach (Employee e in staff)
            {
                VisualElement col = UI.El(columns, "sb-col");
                VisualElement cardWrap = UI.Shadowed(col, out VisualElement card, "emp-wrap", "emp-card");
                VisualElement top = UI.El(card, "row");
                VisualElement photo = UI.El(top, "emp-photo");
                Image img = UI.Pixel(photo, e.Portrait, 3f);
                img.tintColor = e.Tint;
                VisualElement info = UI.El(top, "grow");
                UI.Text(info, e.DisplayName, "emp-name", "f-display");
                UI.Text(info, $"Sueldo ${e.Salary} por día", "emp-salary");

                VisualElement toggle = UI.El(card, "toggle");
                Employee emp = e;
                VisualElement auto = UI.Button(toggle, "Automático", () => emp.Mode = StaffMode.Automatico, "toggle-opt", "toggle-opt--auto");
                VisualElement man = UI.Button(toggle, "Manual", () => emp.Mode = StaffMode.Manual, "toggle-opt", "toggle-opt--man");
                Label status = UI.Text(card, "", "emp-status");
                VisualElement prog = UI.El(card, "emp-prog");
                VisualElement fill = UI.El(prog, "emp-prog-fill");
                cards.Add((e, status, fill, auto, man));

                VisualElement colHead = UI.El(col, "col-head");
                UI.Text(colHead, "Su cola", "col-title", "f-heavy");
                UI.Text(colHead, $"{e.Load}/{Employee.MaxActions}", "count-pill", "f-heavy");

                ScrollView scroll = new ScrollView(ScrollViewMode.Vertical) { horizontalScrollerVisibility = ScrollerVisibility.Hidden };
                scroll.AddToClassList("col-scroll");
                col.Add(scroll);
                List<StaffTask> tasks = e.AllTasks.ToList();
                if (tasks.Count == 0)
                    UI.Text(scroll, e.Mode == StaffMode.Manual ? "Sin órdenes. Arrastrá una comanda acá." : "Libre: va a buscar lo más urgente.", "col-empty");
                for (int i = 0; i < tasks.Count; i++) AddTicket(scroll, tasks[i], i, true);

                bool confirming = confirmFire == e;
                UI.Button(col, confirming ? "¿Seguro? Tocá de nuevo" : "Despedir", () =>
                {
                    if (confirmFire == emp)
                    {
                        confirmFire = null;
                        ctx.Staff.Fire(emp);
                        ctx.Cafe.Notify($"{emp.DisplayName} ya no trabaja acá");
                        if (ctx.Selection.Selected == emp) ctx.Selection.Select(null);
                    }
                    else confirmFire = emp;
                }, "link-btn", confirming ? "link-btn--danger" : "link-btn--plain");

                UI.El(col, "drop-zone").Add(new Label($"Soltá para dársela a {e.DisplayName}") { pickingMode = PickingMode.Ignore });
                dropTargets.Add((col, e));
            }

            if (staff.Count < ctx.Staff.MaxStaff)
            {
                VisualElement add = UI.Button(columns, null, Hire, "sb-col", "sb-col--add");
                UI.Icon(add, IconKind.Plus);
                UI.Text(add, "Contratar a alguien", "col-title", "f-display");
                UI.Text(add, $"${ctx.Staff.HireCost} y ${ctx.Staff.BaseSalary} por día", "col-hint");
            }
        }

        private void AddTicket(VisualElement parent, StaffTask task, int index, bool cancellable)
        {
            TicketView view = TicketView.Create(parent, task, ctx, "bticket-wrap");
            view.Wrap.AddToClassList(UI.Tilt(index));
            if (cancellable)
            {
                view.HideWho();
                VisualElement x = UI.Button(view.Head, null, () =>
                {
                    task.Assignee?.CancelTask(task);
                }, "ticket-x");
                x.RegisterCallback<PointerDownEvent>(ev => ev.StopPropagation());
                UI.Icon(x, IconKind.Close);
            }
            if (task.Customer != null) view.Wrap.AddManipulator(new TicketDrag(this, task));
            tickets.Add(view);
        }

        // ---------------------------- Arrastre ----------------------------

        private VisualElement ghost;

        internal void BeginDrag(StaffTask task, Vector2 pos)
        {
            dragging = true;
            ghost = TicketView.Create(dragLayer, task, ctx, "drag-ghost").Wrap;
            ghost.pickingMode = PickingMode.Ignore;
            ghost.Query<VisualElement>().ForEach(v => v.pickingMode = PickingMode.Ignore);
            MoveDrag(pos);
        }

        internal void MoveDrag(Vector2 pos)
        {
            if (ghost == null) return;
            Vector2 local = dragLayer.WorldToLocal(pos);
            ghost.style.left = local.x - 75f;
            ghost.style.top = local.y - 30f;
            foreach (var d in dropTargets) d.col.EnableInClassList("sb-col--target", d.col.worldBound.Contains(pos));
        }

        internal void EndDrag(StaffTask task, Vector2 pos, bool drop)
        {
            dragging = false;
            ghost?.RemoveFromHierarchy();
            ghost = null;
            foreach (var d in dropTargets) d.col.RemoveFromClassList("sb-col--target");
            if (!drop) return;

            foreach (var d in dropTargets)
            {
                if (!d.col.worldBound.Contains(pos)) continue;
                if (d.e == null)
                {
                    if (task.Assignee != null) task.Assignee.CancelTask(task);
                }
                else if (task.Assignee != d.e)
                {
                    if (ctx.Tasks.AssignAny(task, d.e)) ctx.Cafe.Notify($"{d.e.DisplayName}: {task.Title.ToLowerInvariant()}", NoteKind.Bueno);
                }
                break;
            }
            key = "";
        }

        private class TicketDrag : PointerManipulator
        {
            private readonly StaffBoardView board;
            private readonly StaffTask task;
            private Vector2 start;
            private bool pressed;
            private bool active;
            private int pointerId = -1;

            public TicketDrag(StaffBoardView board, StaffTask task)
            {
                this.board = board;
                this.task = task;
                activators.Add(new ManipulatorActivationFilter { button = MouseButton.LeftMouse });
            }

            protected override void RegisterCallbacksOnTarget()
            {
                target.RegisterCallback<PointerDownEvent>(OnDown);
                target.RegisterCallback<PointerMoveEvent>(OnMove);
                target.RegisterCallback<PointerUpEvent>(OnUp);
                target.RegisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            }

            protected override void UnregisterCallbacksFromTarget()
            {
                target.UnregisterCallback<PointerDownEvent>(OnDown);
                target.UnregisterCallback<PointerMoveEvent>(OnMove);
                target.UnregisterCallback<PointerUpEvent>(OnUp);
                target.UnregisterCallback<PointerCaptureOutEvent>(OnCaptureOut);
            }

            private void OnDown(PointerDownEvent e)
            {
                if (!CanStartManipulation(e)) return;
                start = e.position;
                pressed = true;
                pointerId = e.pointerId;
                target.CapturePointer(pointerId);
            }

            private void OnMove(PointerMoveEvent e)
            {
                if (!pressed || !target.HasPointerCapture(e.pointerId)) return;
                Vector2 pos = e.position;
                if (!active && (pos - start).sqrMagnitude > 36f)
                {
                    active = true;
                    board.BeginDrag(task, pos);
                }
                if (active) board.MoveDrag(pos);
            }

            private void OnUp(PointerUpEvent e)
            {
                if (!pressed || !CanStopManipulation(e)) return;
                pressed = false;
                if (active) board.EndDrag(task, e.position, true);
                active = false;
                if (target.HasPointerCapture(e.pointerId)) target.ReleasePointer(e.pointerId);
                e.StopPropagation();
            }

            private void OnCaptureOut(PointerCaptureOutEvent e)
            {
                if (active) board.EndDrag(task, Vector2.zero, false);
                active = false;
                pressed = false;
            }
        }
    }

    // =====================================================================
    // Cierre del día: ticket térmico, sello y post-it
    // =====================================================================

    public class DayReportView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private string key = "";

        public DayReportView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.El(parent, "layer");
            UI.Show(Root, false);
        }

        public void Refresh(bool visible)
        {
            UI.Show(Root, visible);
            if (!visible)
            {
                key = "";
                return;
            }
            DailyLedger r = ctx.Day.LastReport;
            if (r == null) return;
            string newKey = HudContext.Key(r) + ctx.Cafe.Day;
            if (newKey == key) return;
            key = newKey;
            Build(r);
        }

        private void Build(DailyLedger r)
        {
            Root.Clear();
            UI.El(Root, "report-dim");
            UI.El(Root, "report-slot");
            VisualElement wrap = UI.Shadowed(Root, out VisualElement paper, "report-wrap", "report", "f-mono");
            int day = ctx.Cafe.Day;

            VisualElement center = UI.El(paper, "rep-center");
            UI.Text(center, "MANCOFFEE", "rep-title");
            UI.Text(center, $"CIERRE DEL DÍA {day}", "r-muted");
            UI.Text(center, $"{r.Served} atendidos · {r.Angry} enojados", "r-muted");
            Sep(paper);

            foreach (KeyValuePair<ProductData, int> kv in r.Sold.OrderByDescending(k => k.Value))
                Line(paper, $"{kv.Value} × {kv.Key.displayName}", UI.Money(kv.Value * kv.Key.price));
            if (r.Sold.Count == 0) Line(paper, "Sin ventas hoy", "");
            Sep(paper);

            Money(paper, "Ventas", r.Get(MoneyCategory.Ventas), false);
            Money(paper, "Propinas", r.Get(MoneyCategory.Propinas), false);
            Money(paper, "Ingredientes", r.Get(MoneyCategory.Ingredientes), true);
            Money(paper, "Construcción", r.Get(MoneyCategory.Construccion), true);
            Money(paper, "Sueldos", r.Get(MoneyCategory.Sueldos), true);
            Money(paper, "Contrataciones", r.Get(MoneyCategory.Contrataciones), true);
            Money(paper, "Reembolsos", r.Get(MoneyCategory.Reembolsos), true);
            Sep(paper);

            VisualElement total = UI.El(paper, "row-between", "rep-total");
            UI.Text(total, "RESULTADO");
            Label net = UI.Text(total, UI.Signed(r.Net), r.Net >= 0 ? "r-good" : "r-bad");
            net.AddToClassList("rep-double");
            VisualElement cash = UI.El(paper, "row-between", "rep-cash");
            UI.Text(cash, "CAJA PARA MAÑANA");
            UI.Text(cash, UI.Money(ctx.Cafe.Money));

            float repDelta = r.Served * 0.04f - r.Angry * 0.1f;
            VisualElement rep = UI.El(paper, "row-between", "r-mid");
            UI.Text(rep, "REPUTACIÓN");
            UI.Text(rep, $"{UI.Dec(ctx.Cafe.Reputation)} ({(repDelta >= 0 ? "+" : "")}{UI.Dec(repDelta)})");

            VisualElement barcode = UI.El(paper, "barcode");
            var rng = new System.Random(day * 7919);
            for (int i = 0; i < 46; i++)
            {
                VisualElement bar = UI.El(barcode, "bar");
                bar.style.width = rng.Next(1, 4) * 2;
                bar.style.marginRight = rng.Next(1, 3) * 2;
            }
            UI.Text(paper, "GRACIAS POR SU VISITA", "rep-center", "r-muted");
            wrap.Add(new ZigzagEdge());

            // Sello
            VisualElement stamp = UI.El(Root, "stamp");
            VisualElement inner = UI.El(stamp, "stamp-inner");
            UI.Text(inner, $"DÍA {day}", "stamp-day", "f-display");
            UI.Text(inner, r.Net >= 0 ? "BUEN DÍA" : "A MEJORAR", "stamp-word", "f-display");
            int stars = Mathf.Clamp(Mathf.RoundToInt(ctx.Cafe.Reputation), 0, 5);
            UI.Text(inner, new string('★', stars) + new string('☆', 5 - stars), "stamp-stars");

            // Post-it con consejos
            VisualElement noteWrap = UI.Shadowed(Root, out VisualElement note, "report-note", "postit");
            noteWrap.AddToClassList("rot-b");
            UI.El(noteWrap, "tape");
            UI.Text(note, "Para mañana", "note-title", "f-display");
            foreach (string tip in DayInsights.Build(r)) UI.Text(note, "• " + tip, "postit-text");

            // Botón
            VisualElement cta = UI.El(Root, "report-cta");
            VisualElement go = UI.Button(cta, null, () => ctx.Day.StartNextDay(), "btn-big");
            UI.Icon(go, IconKind.Play);
            UI.Text(go, $"Abrir el día {day + 1}", "btn-label", "f-display");
            UI.Text(cta, "La partida se guarda al continuar", "cta-note");
        }

        private static void Sep(VisualElement parent)
        {
            var line = new DashedLine();
            line.AddToClassList("r-sep");
            parent.Add(line);
        }

        private static void Line(VisualElement parent, string left, string right, string cls = null)
        {
            VisualElement row = UI.El(parent, "row-between", "r-mid");
            UI.Text(row, left);
            Label r = UI.Text(row, right);
            if (cls != null) r.AddToClassList(cls);
        }

        private static void Money(VisualElement parent, string label, int amount, bool expense)
        {
            if (amount == 0) return;
            Line(parent, label, (expense ? "-" : "") + UI.Money(amount), expense ? "r-bad" : null);
        }
    }

    // =====================================================================
    // Menú inicial
    // =====================================================================

    public class StartMenuView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly VisualElement cont;

        public StartMenuView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.El(parent, "layer");
            UI.El(Root, "report-dim");
            VisualElement wrap = UI.Shadowed(Root, out VisualElement board, "start-wrap", "chalkboard", "start-board");
            UI.El(wrap, "board-string", "board-string--l");
            UI.El(wrap, "board-string", "board-string--r");
            UI.Text(board, "Mancoffee", "start-title", "f-display");
            UI.Text(board, "Tu cafetería, tu equipo, tus reglas.", "start-sub");
            cont = UI.Button(board, "Continuar", () => ctx.Bootstrap.ChooseContinue(), "btn-big");
            UI.Button(board, "Nueva partida", () => ctx.Bootstrap.ChooseNewGame(), "btn-chalk", "start-new");
            UI.Show(Root, false);
        }

        public void Refresh(bool visible)
        {
            UI.Show(Root, visible);
            if (visible) UI.Show(cont, SaveSystem.HasSave);
        }
    }

    // =====================================================================
    // Carta: productos, precios y márgenes
    // =====================================================================

    public class CartaView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly VisualElement list;
        private string key = "";

        public CartaView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.El(parent, "layer");
            UI.El(Root, "dim");
            VisualElement wrap = UI.Shadowed(Root, out VisualElement board, "carta-wrap", "chalkboard", "carta");
            VisualElement head = UI.El(board, "row-between");
            VisualElement titles = UI.El(head);
            UI.Text(titles, "La carta", "carta-title", "f-display");
            UI.Text(titles, "Lo que se puede pedir hoy depende de los equipos que tengas.", "carta-sub");
            VisualElement close = UI.Button(head, null, () => ctx.Hud.ClosePanel(), "btn-chalk");
            UI.Icon(close, IconKind.Close);
            UI.Text(close, "Cerrar", "btn-label", "f-heavy");
            list = UI.El(board, "carta-list");
        }

        public void Refresh()
        {
            List<ProductData> products = ctx.Grid.Database.products;
            string newKey = string.Join(",", products.Select(p => MenuService.IsAvailable(ctx.Grid, p) ? "1" : "0"));
            if (newKey == key) return;
            key = newKey;
            list.Clear();

            foreach (ProductData p in products)
            {
                bool on = MenuService.IsAvailable(ctx.Grid, p);
                VisualElement item = UI.El(list, "carta-item");
                if (!on) item.AddToClassList("carta-item--off");
                VisualElement row = UI.El(item, "carta-row");
                VisualElement icon = UI.El(row, "carta-icon");
                if (p.icon != null) UI.Pixel(icon, p.icon, 3f);
                UI.Text(row, p.displayName, "carta-name", "f-display");
                UI.El(row, "carta-leader");
                UI.Text(row, $"${p.price}", "carta-price", "f-display");
                int margin = p.price - p.ingredientCost;
                string station = p.station != null ? p.station.displayName : "—";
                string info = on
                    ? $"Ingredientes ${p.ingredientCost} · deja ${margin} · se hace en: {station}"
                    : $"No disponible: necesitás {station}";
                UI.Text(item, info, "carta-info");
            }
        }
    }

    // =====================================================================
    // Finanzas: el ticket de hoy en vivo
    // =====================================================================

    public class FinanceView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly VisualElement paper;
        private readonly Label when;
        private string key = "";

        public FinanceView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.El(parent, "layer");
            UI.El(Root, "dim");
            VisualElement wrap = UI.Shadowed(Root, out VisualElement sheet, "finance-wrap", "report", "f-mono");
            wrap.Add(new ZigzagEdge());
            VisualElement head = UI.El(sheet, "row-between");
            UI.Text(head, "FINANZAS", "rep-title");
            VisualElement close = UI.Button(head, null, () => ctx.Hud.ClosePanel(), "panel-close");
            UI.Icon(close, IconKind.Close);
            when = UI.Text(sheet, "", "r-muted");
            paper = UI.El(sheet);
        }

        public void Refresh()
        {
            DailyLedger d = ctx.Cafe.Today;
            when.text = $"Día {ctx.Cafe.Day} · hasta las {ctx.Day.ClockText}";
            string newKey = $"{d.Income}|{d.Expenses}|{ctx.Cafe.Money}|{ctx.Staff.TotalSalaries}|" + string.Join(",", ctx.Staff.Employees.Select(e => e.TasksDoneToday));
            if (newKey == key) return;
            key = newKey;
            paper.Clear();
            Sep();

            Row("Ventas", d.Get(MoneyCategory.Ventas), false);
            Row("Propinas", d.Get(MoneyCategory.Propinas), false);
            Row("Ingredientes", d.Get(MoneyCategory.Ingredientes), true);
            Row("Construcción", d.Get(MoneyCategory.Construccion), true);
            Row("Contrataciones", d.Get(MoneyCategory.Contrataciones), true);
            Row("Reembolsos", d.Get(MoneyCategory.Reembolsos), true);
            Sep();

            VisualElement net = UI.El(paper, "row-between", "rep-total");
            UI.Text(net, "HOY");
            UI.Text(net, UI.Signed(d.Net), d.Net >= 0 ? "r-good" : "r-bad");

            int wages = ctx.Staff.TotalSalaries;
            VisualElement w = UI.El(paper, "row-between", "r-mid");
            UI.Text(w, "Sueldos al cerrar");
            UI.Text(w, "-" + UI.Money(wages), "r-bad");
            foreach (Employee e in ctx.Staff.Employees)
            {
                VisualElement r = UI.El(paper, "row-between", "r-muted");
                UI.Text(r, $"  {e.DisplayName} · {e.TasksDoneToday} tareas");
                UI.Text(r, UI.Money(e.Salary));
            }
            Sep();
            VisualElement est = UI.El(paper, "row-between", "r-mid");
            UI.Text(est, "Si cerrás ahora");
            int estimate = d.Net - wages;
            UI.Text(est, UI.Signed(estimate), estimate >= 0 ? "r-good" : "r-bad");
            VisualElement cash = UI.El(paper, "row-between", "rep-cash");
            UI.Text(cash, "CAJA");
            UI.Text(cash, UI.Money(ctx.Cafe.Money));
        }

        private void Sep()
        {
            var line = new DashedLine();
            line.AddToClassList("r-sep");
            paper.Add(line);
        }

        private void Row(string label, int amount, bool expense)
        {
            VisualElement row = UI.El(paper, "row-between", "r-mid");
            UI.Text(row, label);
            UI.Text(row, amount == 0 ? "$0" : (expense ? "-" : "+") + UI.Money(amount), amount == 0 ? "r-muted" : expense ? "r-bad" : "r-good");
        }
    }

    // =====================================================================
    // Opciones
    // =====================================================================

    public class OptionsView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly Label walls;

        public OptionsView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.El(parent, "layer");
            UI.El(Root, "dim");
            VisualElement wrap = UI.Shadowed(Root, out VisualElement board, "options", "chalkboard");
            UI.El(wrap, "board-string", "board-string--l");
            UI.El(wrap, "board-string", "board-string--r");
            UI.Text(board, "Opciones", "options-title", "f-display");
            UI.Button(board, "Volver al juego", () => ctx.Hud.ClosePanel(), "btn-big");
            Option(board, "Guardar", "F5", () => { ctx.Save.Save(); ctx.Cafe.Notify("Partida guardada", NoteKind.Bueno); });
            Option(board, "Cargar", "F9", () => { if (ctx.Save.Load()) ctx.Cafe.Notify("Partida cargada", NoteKind.Bueno); ctx.Hud.ClosePanel(); });
            walls = Option(board, "Paredes", "V", () => ctx.Walls.CycleMode());
            Option(board, "Salir", "", Quit);
        }

        private static Label Option(VisualElement parent, string label, string value, System.Action onClick)
        {
            VisualElement b = UI.Button(parent, label, onClick, "opt-btn");
            return UI.Text(b, value, "opt-value", "f-heavy");
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        public void Refresh()
        {
            string mode = ctx.Walls.Mode switch
            {
                WallViewMode.Bajas => "Bajas",
                WallViewMode.Altas => "Altas",
                _ => "Automáticas"
            };
            walls.text = $"{mode} · V";
        }
    }
}
