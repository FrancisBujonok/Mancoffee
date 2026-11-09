using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Mancoffee.Building;
using Mancoffee.CameraSystem;
using Mancoffee.Core;
using Mancoffee.Customers;
using Mancoffee.GridSystem;
using Mancoffee.Staff;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mancoffee.UI
{
    /// <summary>Los paneles grandes que se abren con las fichas de acción.</summary>
    public enum HudPanel
    {
        None,
        Personal,
        Carta,
        Finanzas,
        Opciones
    }

    /// <summary>Referencias que comparten todas las vistas de la interfaz.</summary>
    public class HudContext
    {
        public MancoffeeHud Hud;
        public GridManager Grid;
        public BuildModeController Build;
        public StaffManager Staff;
        public TaskManager Tasks;
        public CustomerSpawner Spawner;
        public DayCycle Day;
        public SaveSystem Save;
        public WallRenderer Walls;
        public GameBootstrap Bootstrap;
        public CameraController Cam;
        public SelectionController Selection;
        public CafeState Cafe => CafeState.Instance;

        public static string Key(object o) => o == null ? "0" : RuntimeHelpers.GetHashCode(o).ToString();
    }

    // =====================================================================
    // Comanda (ticket de papel) — se usa en el riel, el tablero y al arrastrar
    // =====================================================================

    public class TicketView
    {
        public VisualElement Wrap;
        public StaffTask Task;
        private VisualElement edge;
        private VisualElement fill;
        private VisualElement patRow;
        private Label pct;
        private Label none;
        private VisualElement whoRow;
        private Image whoImg;
        private Label who;
        public VisualElement Head;
        private bool whoHidden;

        public static TicketView Create(VisualElement parent, StaffTask task, HudContext ctx, string wrapClass)
        {
            var t = new TicketView { Task = task };
            t.Wrap = UI.Shadowed(parent, out VisualElement paper, wrapClass, "ticket");
            t.edge = UI.El(paper, "ticket-edge");
            VisualElement body = UI.El(paper, "ticket-body");
            t.Head = UI.El(body, "row-between");
            string where = task.Type == TaskType.IrA ? "IR" : task.Where.ToUpperInvariant();
            UI.Text(t.Head, where, "ticket-mesa", "f-mono");
            Sprite icon = ctx.Selection.IconFor(task);
            if (icon != null) UI.Pixel(t.Head, icon, 3f);
            else if (task.Type == TaskType.IrA) UI.Icon(t.Head, IconKind.Pin);
            UI.Text(body, task.Title, "ticket-task", "f-heavy");

            t.patRow = UI.El(body, "ticket-pat");
            VisualElement bar = UI.El(t.patRow, "patbar");
            t.fill = UI.El(bar, "patbar-fill");
            t.pct = UI.Text(t.patRow, "", "ticket-pct", "f-mono");

            t.none = UI.Text(body, "Sin asignar", "ticket-who", "ticket-none", "f-heavy");
            t.whoRow = UI.El(body, "ticket-who");
            t.whoImg = UI.Pixel(t.whoRow, null, 1f, "ticket-who-img");
            t.who = UI.Text(t.whoRow, "", "f-heavy");
            t.Refresh();
            return t;
        }

        public void Refresh()
        {
            bool hasCustomer = Task.Customer != null;
            UI.Show(patRow, hasCustomer);
            float ratio = Task.Patience;
            Color c = hasCustomer ? UI.PatienceColor(ratio) : new Color(0.42f, 0.29f, 0.22f);
            edge.style.backgroundColor = c;
            if (hasCustomer)
            {
                fill.style.backgroundColor = c;
                UI.SetFill(fill, ratio);
                pct.text = UI.Pct(ratio);
            }

            Employee a = Task.Assignee;
            UI.Show(none, !whoHidden && a == null && hasCustomer);
            UI.Show(whoRow, !whoHidden && a != null);
            if (a != null)
            {
                who.text = a.DisplayName;
                UI.SetPixel(whoImg, a.Portrait, 1f);
                whoImg.tintColor = a.Tint;
            }
        }

        /// <summary>Versión compacta para el tablero: sin la fila de "quién la tiene".</summary>
        public void HideWho()
        {
            whoHidden = true;
            UI.Show(none, false);
            UI.Show(whoRow, false);
        }
    }

    // =====================================================================
    // Pizarra: día, hora, horario y velocidad
    // =====================================================================

    public class ChalkboardView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly Label day;
        private readonly Label clock;
        private readonly VisualElement pill;
        private readonly Label pillText;
        private readonly VisualElement fill;
        private readonly Label openLabel;
        private readonly Label closeLabel;
        private readonly VisualElement[] speeds = new VisualElement[4];

        public ChalkboardView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.Shadowed(parent, out VisualElement board, "board-wrap", "chalkboard");
            UI.El(Root, "board-string", "board-string--l");
            UI.El(Root, "board-string", "board-string--r");

            VisualElement row = UI.El(board, "row-between");
            day = UI.Text(row, "Día 1", "board-day", "f-display");
            pill = UI.El(row, "pill");
            UI.El(pill, "pill-dot");
            pillText = UI.Text(pill, "Abierto", "f-heavy");

            clock = UI.Text(board, "08:00", "board-clock", "f-display");
            VisualElement bar = UI.El(board, "daybar");
            fill = UI.El(bar, "daybar-fill");
            VisualElement labels = UI.El(board, "row-between", "daybar-labels");
            openLabel = UI.Text(labels, "Abre 8:00");
            closeLabel = UI.Text(labels, "Cierra 20:00");

            VisualElement speedRow = UI.El(board, "speed-row");
            IconKind[] icons = { IconKind.Pause, IconKind.Play, IconKind.Fast2, IconKind.Fast3 };
            for (int i = 0; i < 4; i++)
            {
                int speed = i;
                speeds[i] = UI.Button(speedRow, null, () => ctx.Day.SetSpeed(speed), "speed-btn");
                UI.Icon(speeds[i], icons[i]);
            }
        }

        public void Refresh()
        {
            DayCycle d = ctx.Day;
            day.text = $"Día {ctx.Cafe.Day}";
            clock.text = d.ClockText;
            UI.SetFill(fill, d.DayProgress);
            openLabel.text = $"Abre {d.OpenHour}:00";
            closeLabel.text = $"Cierra {d.CloseHour}:00";

            pill.EnableInClassList("pill--closing", d.Phase == DayPhase.Cerrando);
            pill.EnableInClassList("pill--closed", d.Phase == DayPhase.Resumen);
            pillText.text = d.Phase switch
            {
                DayPhase.Abierto => "Abierto",
                DayPhase.Cerrando => "Cerrando",
                _ => "Cerrado"
            };

            for (int i = 0; i < 4; i++) speeds[i].EnableInClassList("speed-btn--on", d.Speed == i);
        }
    }

    /// <summary>Pizarra chica del modo construcción.</summary>
    public class CompactBoardView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly Label title;

        public CompactBoardView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.El(parent, "board-compact");
            title = UI.Text(Root, "Día 1 · 08:00", "compact-title", "f-display");
            VisualElement pill = UI.El(Root, "pill", "pill--build");
            UI.Text(pill, "Construyendo", "f-heavy");
            UI.Text(Root, "El tiempo sigue · Espacio pausa", "compact-hint");
        }

        public void Refresh() => title.text = $"Día {ctx.Cafe.Day} · {ctx.Day.ClockText}";
    }

    // =====================================================================
    // Ticket de caja: dinero, balance de hoy y reputación
    // =====================================================================

    public class ReceiptView
    {
        public VisualElement Root;
        private readonly VisualElement slot;
        private readonly HudContext ctx;
        private readonly Label money;
        private readonly VisualElement normal;
        private readonly VisualElement preview;
        private readonly Label today;
        private readonly Label income;
        private readonly Label expenses;
        private readonly Label reputation;
        private readonly CupMeter cups;
        private readonly Label itemName;
        private readonly Label itemCost;
        private readonly Label remaining;

        public ReceiptView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            slot = UI.El(parent, "receipt-slot");
            Root = UI.Shadowed(parent, out VisualElement paper, "receipt-wrap", "receipt", "f-mono");
            parent.Remove(slot);
            parent.Add(slot);

            VisualElement head = UI.El(paper, "row-between", "r-muted");
            UI.Text(head, "CAJA");
            UI.Text(head, "MANCOFFEE");
            money = UI.Text(paper, "$0", "r-big");
            Root.Add(new ZigzagEdge());

            normal = UI.El(paper);
            Sep(normal);
            VisualElement r1 = UI.El(normal, "row-between", "r-mid");
            UI.Text(r1, "HOY");
            today = UI.Text(r1, "+$0", "r-good");
            VisualElement r2 = UI.El(normal, "row-between", "r-muted");
            income = UI.Text(r2, "Ingresos $0");
            expenses = UI.Text(r2, "Gastos $0");
            Sep(normal);
            VisualElement r3 = UI.El(normal, "row-between");
            UI.Text(r3, "REPUTACIÓN", "r-muted");
            reputation = UI.Text(r3, "3,0", "r-mid");
            cups = new CupMeter();
            cups.AddToClassList("cups");
            normal.Add(cups);

            preview = UI.El(paper);
            Sep(preview);
            VisualElement p1 = UI.El(preview, "row-between", "r-mid");
            itemName = UI.Text(p1, "");
            itemCost = UI.Text(p1, "", "r-bad");
            VisualElement p2 = UI.El(preview, "row-between", "r-mid");
            UI.Text(p2, "Queda");
            remaining = UI.Text(p2, "");
        }

        private static void Sep(VisualElement parent)
        {
            var line = new DashedLine();
            line.AddToClassList("r-sep");
            parent.Add(line);
        }

        public void SetVisible(bool visible)
        {
            UI.Show(Root, visible);
            UI.Show(slot, visible);
        }

        public void Refresh()
        {
            CafeState cafe = ctx.Cafe;
            money.text = UI.Money(cafe.Money);
            BuildModeController build = ctx.Build;

            bool showPreview = build.IsBuildMode && build.PreviewData != null;
            UI.Show(normal, !showPreview);
            UI.Show(preview, showPreview);

            if (showPreview)
            {
                bool moving = build.Moving != null;
                itemName.text = moving ? $"Mover {build.PreviewData.displayName.ToLowerInvariant()}" : build.PreviewData.displayName;
                int cost = moving ? 0 : build.PreviewData.price;
                itemCost.text = cost > 0 ? $"-${cost}" : "gratis";
                remaining.text = UI.Money(cafe.Money - cost);
                return;
            }

            DailyLedger day = cafe.Today;
            today.text = UI.Signed(day.Net);
            today.EnableInClassList("r-good", day.Net >= 0);
            today.EnableInClassList("r-bad", day.Net < 0);
            income.text = $"Ingresos {UI.Money(day.Income)}";
            expenses.text = $"Gastos {UI.Money(day.Expenses)}";
            reputation.text = UI.Dec(cafe.Reputation);
            cups.Value = cafe.Reputation;
        }
    }

    // =====================================================================
    // Riel de comandas: las tareas en vivo
    // =====================================================================

    public class TicketRailView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly VisualElement tickets;
        private readonly List<TicketView> views = new();
        private string key = "";
        private const int MaxVisible = 4;

        public TicketRailView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.El(parent, "rail");
            Root.pickingMode = PickingMode.Ignore;
            UI.El(Root, "rail-bar");
            tickets = UI.El(Root, "rail-tickets");
            tickets.pickingMode = PickingMode.Ignore;
        }

        public void Refresh()
        {
            List<StaffTask> all = ctx.Tasks.AllCustomerTasks()
                .OrderBy(t => t.Assignee != null ? 1 : 0)
                .ThenBy(t => t.Patience)
                .ToList();
            List<StaffTask> visible = all.Take(MaxVisible).ToList();
            string newKey = string.Join(",", visible.Select(HudContext.Key)) + "|" + all.Count;

            if (newKey != key)
            {
                key = newKey;
                tickets.Clear();
                views.Clear();
                for (int i = 0; i < visible.Count; i++)
                {
                    StaffTask task = visible[i];
                    TicketView view = TicketView.Create(tickets, task, ctx, "ticket-wrap");
                    view.Wrap.AddToClassList(UI.Tilt(i));
                    VisualElement wrap = view.Wrap;
                    wrap.AddManipulator(new Clickable(() =>
                    {
                        Vector2 at = new(wrap.worldBound.center.x, wrap.worldBound.yMax + 120f);
                        ctx.Hud.ShowPie(at, task.Where, ctx.Selection.AssignOptions(task));
                    }));
                    views.Add(view);
                }

                if (all.Count > MaxVisible)
                    UI.Button(tickets, $"+{all.Count - MaxVisible} más", () => ctx.Hud.OpenPanel(HudPanel.Personal), "rail-more");
                else if (all.Count == 0)
                    UI.Text(tickets, "Sin comandas por ahora", "rail-empty", "f-heavy");
            }

            foreach (TicketView v in views) v.Refresh();
        }
    }

    // =====================================================================
    // Fichas de acción
    // =====================================================================

    public class ActionCardsView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly VisualElement build;
        private readonly Label buildKey;
        private readonly Dictionary<HudPanel, VisualElement> cards = new();

        public ActionCardsView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.El(parent, "cards");
            build = Card("Construir", "B", IconKind.Hammer, "rot-e", () => ctx.Build.SetBuildMode(!ctx.Build.IsBuildMode), out buildKey);
            cards[HudPanel.Personal] = Card("Personal", "T", IconKind.Users, "rot-b", () => ctx.Hud.TogglePanel(HudPanel.Personal), out _);
            cards[HudPanel.Carta] = Card("Menú", "M", IconKind.Book, "rot-a", () => ctx.Hud.TogglePanel(HudPanel.Carta), out _);
            cards[HudPanel.Finanzas] = Card("Finanzas", "F", IconKind.Chart, "rot-f", () => ctx.Hud.TogglePanel(HudPanel.Finanzas), out _);
            cards[HudPanel.Opciones] = Card("Opciones", "Esc", IconKind.Gear, "rot-c", () => ctx.Hud.TogglePanel(HudPanel.Opciones), out _);
        }

        private VisualElement Card(string label, string key, IconKind icon, string tilt, System.Action onClick, out Label keyLabel)
        {
            VisualElement card = UI.Button(Root, null, onClick, "card", tilt);
            UI.Icon(card, icon);
            UI.Text(card, label, "card-label", "f-display");
            keyLabel = UI.Text(card, key, "keycap", "f-heavy");
            return card;
        }

        public void Refresh(HudPanel open)
        {
            bool building = ctx.Build.IsBuildMode;
            Root.EnableInClassList("cards--build", building);
            build.EnableInClassList("card--on", building);
            buildKey.text = "B";
            foreach (KeyValuePair<HudPanel, VisualElement> kv in cards)
                kv.Value.EnableInClassList("card--on", kv.Key == open);
        }
    }

    // =====================================================================
    // Polaroids del personal y cola de acciones (estilo Los Sims)
    // =====================================================================

    public class PolaroidsView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private string key = "";
        private const int MaxVisible = 5;

        private class Card
        {
            public Employee Employee;
            public VisualElement Wrap;
            public Label Mode;
            public VisualElement Fill;
            public Label Status;
        }

        private readonly List<Card> cards = new();

        public PolaroidsView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.El(parent, "polaroids");
            Root.pickingMode = PickingMode.Ignore;
        }

        public void Refresh()
        {
            IReadOnlyList<Employee> staff = ctx.Staff.Employees;
            string newKey = string.Join(",", staff.Select(HudContext.Key));
            if (newKey != key)
            {
                key = newKey;
                Rebuild(staff);
            }

            Employee selected = ctx.Selection.Selected;
            foreach (Card c in cards)
            {
                Employee e = c.Employee;
                if (e == null) continue;
                bool manual = e.Mode == StaffMode.Manual;
                c.Mode.text = manual ? "MANUAL" : "AUTO";
                c.Mode.EnableInClassList("polaroid-mode--manual", manual);
                UI.SetFill(c.Fill, e.Progress);
                c.Status.text = e.StatusText;
                c.Wrap.EnableInClassList("polaroid-wrap--sel", e == selected);
            }
        }

        private void Rebuild(IReadOnlyList<Employee> staff)
        {
            Root.Clear();
            cards.Clear();

            if (staff.Count == 0)
            {
                UI.Button(Root, "+ Contratar", () => ctx.Hud.OpenPanel(HudPanel.Personal), "polaroid-more", "f-display");
                return;
            }

            for (int i = 0; i < staff.Count && i < MaxVisible; i++)
            {
                Employee e = staff[i];
                var card = new Card { Employee = e };
                card.Wrap = UI.Shadowed(Root, out VisualElement paper, "polaroid-wrap", "polaroid");
                card.Wrap.AddToClassList(i % 2 == 0 ? "rot-f" : "rot-a");
                VisualElement photo = UI.El(paper, "polaroid-photo");
                Image img = UI.Pixel(photo, e.Portrait, 4f);
                img.tintColor = e.Tint;
                card.Mode = UI.Text(photo, "AUTO", "polaroid-mode", "f-mono");
                VisualElement prog = UI.El(photo, "polaroid-prog");
                card.Fill = UI.El(prog, "polaroid-prog-fill");
                UI.Text(paper, e.DisplayName, "polaroid-name", "f-display");
                card.Status = UI.Text(paper, "", "polaroid-status");

                card.Wrap.AddManipulator(new Clickable(() =>
                {
                    if (ctx.Selection.Selected == e) ctx.Cam.FocusOn(e.transform.position);
                    else ctx.Selection.Select(e);
                }));
                cards.Add(card);
            }

            if (staff.Count > MaxVisible)
                UI.Button(Root, $"+{staff.Count - MaxVisible}", () => ctx.Hud.OpenPanel(HudPanel.Personal), "polaroid-more", "f-display");
        }
    }

    /// <summary>
    /// La cola de acciones del empleado seleccionado, como en Los Sims: la actual primero,
    /// hasta 8, y cada una se cancela tocándola.
    /// </summary>
    public class ActionQueueView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly Label title;
        private readonly Label sub;
        private readonly VisualElement items;
        private readonly VisualElement modeChip;
        private readonly Label modeLabel;
        private string key = "";
        private VisualElement currentFill;

        public ActionQueueView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.Shadowed(parent, out VisualElement panel, "queue-wrap", "queue");
            VisualElement head = UI.El(panel, "queue-head");
            title = UI.Text(head, "", "queue-title", "f-display");
            sub = UI.Text(head, "", "queue-sub");
            items = UI.El(panel, "row");
            modeChip = UI.Button(panel, null, ToggleMode, "mode-chip");
            UI.Icon(modeChip, IconKind.Swap);
            modeLabel = UI.Text(modeChip, "", "f-heavy");
            UI.Show(Root, false);
        }

        private void ToggleMode()
        {
            Employee e = ctx.Selection.Selected;
            if (e == null) return;
            e.Mode = e.Mode == StaffMode.Automatico ? StaffMode.Manual : StaffMode.Automatico;
        }

        public void Refresh(bool allowed)
        {
            Employee e = ctx.Selection.Selected;
            bool visible = allowed && e != null;
            UI.Show(Root, visible);
            if (!visible) return;

            List<StaffTask> tasks = e.AllTasks.ToList();
            string newKey = HudContext.Key(e) + ":" + string.Join(",", tasks.Select(HudContext.Key)) + ":" + e.Mode;
            if (newKey != key)
            {
                key = newKey;
                Rebuild(e, tasks);
            }

            title.text = $"Cola de {e.DisplayName}";
            sub.text = $"{e.Load} de {Employee.MaxActions} · {e.StatusText}";
            bool manual = e.Mode == StaffMode.Manual;
            modeLabel.text = manual ? "Manual" : "Automático";
            modeChip.EnableInClassList("mode-chip--manual", manual);
            if (currentFill != null) UI.SetFill(currentFill, e.Progress);
        }

        private void Rebuild(Employee e, List<StaffTask> tasks)
        {
            items.Clear();
            currentFill = null;

            if (tasks.Count == 0)
            {
                string text = e.Mode == StaffMode.Manual
                    ? "Esperando órdenes: clic en un cliente o una estación"
                    : "Sin órdenes: elige solo lo más urgente";
                UI.Text(items, text, "queue-empty");
                return;
            }

            for (int i = 0; i < tasks.Count; i++)
            {
                StaffTask task = tasks[i];
                bool isCurrent = i == 0 && task == e.Current;
                VisualElement item = UI.El(items, "queue-item");
                if (isCurrent) item.AddToClassList("queue-item--now");
                item.tooltip = task.Description;

                Sprite icon = ctx.Selection.IconFor(task);
                if (icon != null) UI.Pixel(item, icon, 3f);
                else UI.Icon(item, task.Type == TaskType.IrA ? IconKind.Pin : IconKind.Check);

                if (isCurrent)
                {
                    VisualElement prog = UI.El(item, "queue-prog");
                    currentFill = UI.El(prog, "queue-prog-fill");
                }

                VisualElement x = UI.El(item, "queue-x");
                UI.Icon(x, IconKind.Close);
                item.AddManipulator(new Clickable(() => e.CancelTask(task)));
            }
        }
    }

    // =====================================================================
    // Post-its: avisos urgentes y mensajes
    // =====================================================================

    public class NotesView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private string key = "";
        private Label urgentText;
        private StaffTask urgentTask;

        public NotesView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.El(parent, "notes");
            Root.pickingMode = PickingMode.Ignore;
        }

        public void Refresh()
        {
            StaffTask urgent = ctx.Tasks.Pending
                .Where(t => t.Customer != null && t.Assignee == null && t.Patience < 0.35f)
                .OrderBy(t => t.Patience)
                .FirstOrDefault();
            GameNote note = ctx.Cafe.LatestNote(4f);

            string newKey = HudContext.Key(urgent) + "|" + (note != null ? note.Id : 0);
            if (newKey != key)
            {
                key = newKey;
                Rebuild(urgent, note);
            }

            if (urgentTask != null && urgentText != null) urgentText.text = UrgentText(urgentTask);
        }

        private void Rebuild(StaffTask urgent, GameNote note)
        {
            Root.Clear();
            urgentTask = urgent;
            urgentText = null;

            if (urgent != null)
            {
                VisualElement wrap = UI.Shadowed(Root, out VisualElement paper, "postit-wrap", "postit");
                wrap.AddToClassList("rot-e");
                UI.El(wrap, "tape");
                VisualElement titleRow = UI.El(paper, "postit-title");
                UI.Icon(titleRow, IconKind.Alert);
                UI.Text(titleRow, $"La {urgent.Where.ToLowerInvariant()} se impacienta", "f-heavy");
                urgentText = UI.Text(paper, UrgentText(urgent), "postit-text");
                VisualElement actions = UI.El(paper, "postit-actions");
                UI.Button(actions, "Asignar a alguien", () => AssignBest(urgent), "btn-tomato");
                UI.Button(actions, "Ir a la mesa", () => ctx.Cam.FocusOn(urgent.Customer.transform.position), "btn-ghost");
            }

            if (note != null)
            {
                VisualElement wrap = UI.Shadowed(Root, out VisualElement paper, "postit-wrap", "postit");
                wrap.AddToClassList("rot-f");
                if (note.Kind == NoteKind.Bueno) paper.AddToClassList("postit--good");
                if (note.Kind == NoteKind.Malo) paper.AddToClassList("postit--bad");
                UI.El(wrap, "tape");
                UI.Text(paper, note.Text, "postit-text", "f-heavy");
            }
        }

        private static string UrgentText(StaffTask t)
        {
            string pct = UI.Pct(t.Patience);
            return t.Type switch
            {
                TaskType.TomarPedido => $"Nadie le tomó el pedido. Le queda {pct} de paciencia.",
                TaskType.PrepararYServir => $"Pidió {t.Product?.displayName.ToLowerInvariant()} y nadie lo está preparando. Le queda {pct} de paciencia.",
                TaskType.Cobrar => $"Quiere pagar y nadie le cobra. Le queda {pct} de paciencia.",
                _ => ""
            };
        }

        private void AssignBest(StaffTask task)
        {
            Employee best = ctx.Tasks.BestEmployeeFor(task);
            if (best == null)
            {
                ctx.Cafe.Notify("No hay nadie libre: contratá a alguien en Personal", NoteKind.Malo);
                return;
            }
            if (ctx.Tasks.AssignAny(task, best)) ctx.Cafe.Notify($"Se lo diste a {best.DisplayName}", NoteKind.Bueno);
        }
    }

    // =====================================================================
    // Etiqueta colgante junto al fantasma de construcción
    // =====================================================================

    public class PriceTagView
    {
        public VisualElement Root;
        private readonly HudContext ctx;
        private readonly Label name;
        private readonly Label price;
        private readonly VisualElement state;
        private readonly IconElement stateIcon;
        private readonly Label stateText;

        public PriceTagView(VisualElement parent, HudContext ctx)
        {
            this.ctx = ctx;
            Root = UI.Shadowed(parent, out VisualElement tag, "tag-wrap", "tag");
            Root.pickingMode = PickingMode.Ignore;
            Root.Query<VisualElement>().ForEach(e => e.pickingMode = PickingMode.Ignore);
            UI.El(tag, "tag-hole").pickingMode = PickingMode.Ignore;
            name = UI.Text(tag, "", "tag-name", "f-display");
            price = UI.Text(tag, "", "tag-price", "f-mono");
            state = UI.El(tag, "tag-state");
            state.pickingMode = PickingMode.Ignore;
            stateIcon = UI.Icon(state, IconKind.Check);
            stateText = UI.Text(state, "", "f-heavy");
            UI.Show(Root, false);
        }

        public void Update()
        {
            BuildModeController b = ctx.Build;
            bool visible = b.IsBuildMode && b.PreviewVisible && b.PreviewData != null;
            UI.Show(Root, visible);
            if (!visible) return;

            Vector2 p = ctx.Hud.WorldToPanel(b.PreviewWorld);
            Root.style.left = p.x + 70f;
            Root.style.top = p.y - 150f;

            bool moving = b.Moving != null;
            name.text = b.PreviewData.displayName;
            price.text = moving ? "Mover" : $"${b.PreviewData.price}";
            bool ok = b.PreviewFits && b.PreviewAffordable;
            state.EnableInClassList("tag-state--bad", !ok);
            stateIcon.Kind = ok ? IconKind.Check : IconKind.Close;
            stateText.text = ok ? "Entra acá" : !b.PreviewFits ? "No entra acá" : "Falta dinero";
        }
    }

    // =====================================================================
    // Menú circular (pie menu de Los Sims)
    // =====================================================================

    public class PieMenuView
    {
        public VisualElement Root;
        public bool IsOpen { get; private set; }

        public PieMenuView(VisualElement parent)
        {
            Root = UI.El(parent, "layer");
            Root.pickingMode = PickingMode.Ignore;
            UI.Show(Root, false);
        }

        public void Show(Vector2 at, string title, List<PieOption> options)
        {
            Root.Clear();
            if (options == null || options.Count == 0)
            {
                Hide();
                return;
            }

            VisualElement catcher = UI.El(Root, "pie-catcher");
            catcher.RegisterCallback<PointerDownEvent>(e => { Hide(); e.StopPropagation(); });

            float radius = options.Count <= 2 ? 110f : options.Count <= 4 ? 140f : 170f;
            at.x = Mathf.Clamp(at.x, radius + 160f, 1920f - radius - 160f);
            at.y = Mathf.Clamp(at.y, radius + 60f, 1080f - radius - 60f);

            VisualElement dot = UI.El(Root, "pie-dot");
            dot.style.left = at.x;
            dot.style.top = at.y;

            for (int i = 0; i < options.Count; i++)
            {
                PieOption option = options[i];
                float angle = options.Count == 1 ? -90f : -90f + 360f * i / options.Count;
                float rad = angle * Mathf.Deg2Rad;
                Vector2 pos = at + new Vector2(Mathf.Cos(rad) * radius * 1.35f, Mathf.Sin(rad) * radius);

                VisualElement button = UI.Button(Root, null, () =>
                {
                    if (!option.Enabled) return;
                    Hide();
                    option.OnPick?.Invoke();
                }, "pie-opt");
                if (!option.Enabled) button.AddToClassList("pie-opt--off");
                if (option.Icon != null)
                {
                    Image img = UI.Pixel(button, option.Icon, option.Icon.rect.height > 16 ? 1.5f : 3f, "pie-icon");
                    img.tintColor = option.IconTint;
                }
                UI.Text(button, option.Label, "btn-label", "f-heavy");
                button.style.left = pos.x;
                button.style.top = pos.y;
            }

            Label center = UI.Text(Root, title, "pie-center", "f-display");
            center.style.left = at.x;
            center.style.top = at.y - 34f;

            UI.Show(Root, true);
            IsOpen = true;
        }

        public void Hide()
        {
            if (!IsOpen && Root.childCount == 0) return;
            Root.Clear();
            UI.Show(Root, false);
            IsOpen = false;
        }
    }
}
