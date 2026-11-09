using System;
using System.Linq;
using Mancoffee.Building;
using Mancoffee.Customers;
using Mancoffee.Data;
using Mancoffee.Staff;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Mancoffee.Core
{
    /// <summary>
    /// Interfaz provisional hecha con OnGUI: barra superior, panel de construcción, panel de personal
    /// y tareas, avisos, resumen del día y menú inicial. Más adelante se reemplaza por UI definitiva;
    /// toda la lógica vive en los sistemas, acá solo se muestra y se llaman sus métodos.
    /// </summary>
    public class GameHud : MonoBehaviour
    {
        private static readonly string[] ToolNames = { "Muebles", "Paredes", "Puertas", "Ventanas", "Suelo" };

        [SerializeField] private BuildModeController build;
        [SerializeField] private StaffManager staff;
        [SerializeField] private TaskManager tasks;
        [SerializeField] private CustomerSpawner spawner;
        [SerializeField] private DayCycle day;
        [SerializeField] private SaveSystem saveSystem;
        [SerializeField] private WallRenderer wallRenderer;
        [SerializeField] private GameBootstrap bootstrap;
        [Tooltip("Alto de pantalla de referencia: por encima, la interfaz se agranda.")]
        [SerializeField] private float referenceHeight = 900f;

        private bool showStaff = true;
        private Vector2 staffScroll;
        private float scale = 1f;
        private GUIStyle text, bold, header, leftButton;

        // Las acciones de los botones se ejecutan al final de OnGUI, para no cambiar listas mientras se dibujan.
        private Action deferred;

        private CafeState Cafe => CafeState.Instance;

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.tKey.wasPressedThisFrame) showStaff = !showStaff;
            if (UiBlocker.ModalOpen) return;
            if (kb.spaceKey.wasPressedThisFrame) day.TogglePause();
            if (kb.vKey.wasPressedThisFrame) wallRenderer.CycleMode();
            if (kb.f5Key.wasPressedThisFrame) saveSystem.Save();
            if (kb.f9Key.wasPressedThisFrame) saveSystem.Load();
        }

        private void OnGUI()
        {
            EnsureStyles();
            scale = Mathf.Max(1f, Screen.height / referenceHeight);
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale, h = Screen.height / scale;

            bool repaint = Event.current.type == EventType.Repaint;
            if (repaint) UiBlocker.BeginRepaint();

            bool modal = bootstrap.AwaitingChoice || day.Phase == DayPhase.Resumen;
            UiBlocker.ModalOpen = modal;
            deferred = null;

            GUI.enabled = !modal;
            DrawTopBar(w);
            DrawBuildPanel(h);
            if (showStaff) DrawStaffPanel(w, h);
            else DrawStaffCollapsed(w);
            DrawMessage(w, h);
            GUI.enabled = true;

            if (modal)
            {
                // Oscurece el fondo para que la ventana se lea bien.
                GUI.color = new Color(0f, 0f, 0f, 0.65f);
                GUI.DrawTexture(new Rect(0f, 0f, w, h), Texture2D.whiteTexture);
                GUI.color = Color.white;
            }

            if (bootstrap.AwaitingChoice) DrawStartMenu(w, h);
            else if (day.Phase == DayPhase.Resumen) DrawDayReport(w, h);

            if (repaint) UiBlocker.EndRepaint();
            deferred?.Invoke();
        }

        // =========================== Paneles ===========================

        private void Panel(Rect area, Action content)
        {
            GUILayout.BeginArea(area);
            GUILayout.BeginVertical(GUI.skin.box);
            content();
            GUILayout.EndVertical();
            if (Event.current.type == EventType.Repaint)
            {
                Rect r = GUILayoutUtility.GetLastRect();
                UiBlocker.Register(new Rect((area.x + r.x) * scale, (area.y + r.y) * scale, r.width * scale, r.height * scale));
            }
            GUILayout.EndArea();
        }

        private void DrawTopBar(float w)
        {
            Panel(new Rect(w * 0.5f - 390f, 6f, 780f, 110f), () =>
            {
                GUILayout.BeginHorizontal();
                string phase = day.Phase switch
                {
                    DayPhase.Abierto => "Abierto",
                    DayPhase.Cerrando => "Cerrando",
                    _ => "Cerrado"
                };
                GUILayout.Label($"Día {Cafe.Day}   {day.ClockText}   {phase}", bold);
                GUILayout.FlexibleSpace();
                GUILayout.Label($"Caja: ${Cafe.Money}", bold);
                GUILayout.Space(12);
                GUILayout.Label($"Reputación {Cafe.Reputation:0.0}/5", text);
                GUILayout.Space(12);
                GUILayout.Label($"Clientes: {spawner.ActiveCount}", text);
                GUILayout.EndHorizontal();

                GUILayout.BeginHorizontal();
                SpeedButton("||", 0);
                SpeedButton("1x", 1);
                SpeedButton("2x", 2);
                SpeedButton("3x", 3);
                GUILayout.Space(10);
                if (GUILayout.Button($"Paredes: {wallRenderer.Mode} (V)")) deferred += wallRenderer.CycleMode;
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Guardar (F5)")) deferred += saveSystem.Save;
                if (GUILayout.Button("Cargar (F9)")) deferred += () => saveSystem.Load();
                GUILayout.EndHorizontal();

                DailyLedger today = Cafe.Today;
                GUILayout.Label($"Hoy: {today.Served} atendidos · {today.Angry} se fueron enojados · resultado ${today.Net}", text);
            });
        }

        private void SpeedButton(string label, int speed)
        {
            bool active = day.Speed == speed;
            if (GUILayout.Toggle(active, label, GUI.skin.button, GUILayout.Width(38)) && !active)
                deferred += () => day.SetSpeed(speed);
        }

        private void DrawBuildPanel(float h)
        {
            Panel(new Rect(8f, 124f, 310f, h - 132f), () =>
            {
                if (!build.IsBuildMode)
                {
                    if (GUILayout.Button("Construir (B)", GUILayout.Height(28))) deferred += () => build.SetBuildMode(true);
                    GUILayout.Label("Espacio: pausa · T: personal · V: vista de paredes\nF5: guardar · F9: cargar · C: traer un cliente", text);
                    return;
                }

                if (GUILayout.Button("Salir de construcción (B)", GUILayout.Height(28))) deferred += () => build.SetBuildMode(false);
                int selectedTool = GUILayout.SelectionGrid((int)build.Tool, ToolNames, 3);
                if (selectedTool != (int)build.Tool) deferred += () => build.SetTool((BuildTool)selectedTool);
                GUILayout.Space(4);

                switch (build.Tool)
                {
                    case BuildTool.Muebles:
                        for (int i = 0; i < build.Catalog.Count; i++)
                        {
                            FurnitureData d = build.Catalog[i];
                            int index = i;
                            string mark = i == build.SelectedIndex ? "> " : "";
                            if (GUILayout.Button($"{mark}{i + 1}. {d.displayName} ({d.size.x}x{d.size.y})  ${d.price}", leftButton))
                                deferred += () => build.SelectFurniture(index);
                        }
                        break;

                    case BuildTool.Suelo:
                        for (int i = 0; i < build.FloorTypes.Count; i++)
                        {
                            FloorTypeData f = build.FloorTypes[i];
                            int index = i;
                            string mark = i == build.FloorIndex ? "> " : "";
                            if (GUILayout.Button($"{mark}{i + 1}. {f.displayName}  ${f.price} por celda", leftButton))
                                deferred += () => build.SelectFloor(index);
                        }
                        break;

                    default:
                        GUILayout.Label($"Precio: ${build.WallPrice(build.CurrentWallKind)} por tramo · quitar devuelve 50%", text);
                        break;
                }

                GUILayout.Space(6);
                GUILayout.Label(build.HelpText, text);
            });
        }

        private void DrawStaffCollapsed(float w)
        {
            Panel(new Rect(w - 160f, 124f, 152f, 40f), () =>
            {
                if (GUILayout.Button("Personal (T)")) deferred += () => showStaff = true;
            });
        }

        private void DrawStaffPanel(float w, float h)
        {
            Panel(new Rect(w - 388f, 124f, 380f, h - 132f), () =>
            {
                GUILayout.BeginHorizontal();
                GUILayout.Label($"Personal ({staff.Employees.Count}/{staff.MaxStaff})", header);
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Ocultar (T)")) deferred += () => showStaff = false;
                GUILayout.EndHorizontal();

                if (GUILayout.Button($"Contratar (${staff.HireCost}, sueldo ${staff.BaseSalary}/día)"))
                    deferred += () => staff.Hire(true);

                staffScroll = GUILayout.BeginScrollView(staffScroll, GUILayout.MaxHeight(h - 220f));

                foreach (Employee e in staff.Employees)
                {
                    Employee employee = e;
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.BeginHorizontal();
                    GUILayout.Label($"{e.DisplayName} · ${e.Salary}/día", bold);
                    GUILayout.FlexibleSpace();
                    string modeLabel = e.Mode == StaffMode.Automatico ? "Automático" : "Manual";
                    if (GUILayout.Button(modeLabel, GUILayout.Width(92)))
                        deferred += () => employee.Mode = employee.Mode == StaffMode.Automatico ? StaffMode.Manual : StaffMode.Automatico;
                    if (GUILayout.Button("Despedir", GUILayout.Width(70))) deferred += () => staff.Fire(employee);
                    GUILayout.EndHorizontal();
                    GUILayout.Label(e.StatusText, text);

                    foreach (StaffTask t in e.Queue)
                    {
                        StaffTask task = t;
                        GUILayout.BeginHorizontal();
                        GUILayout.Label("· " + t.Description, text);
                        if (GUILayout.Button("x", GUILayout.Width(24))) deferred += () => employee.Unassign(task);
                        GUILayout.EndHorizontal();
                    }
                    GUILayout.EndVertical();
                }

                GUILayout.Space(6);
                GUILayout.Label($"Tareas sin asignar ({tasks.Pending.Count})", header);
                if (tasks.Pending.Count == 0) GUILayout.Label("No hay tareas pendientes.", text);

                string[] employeeNames = staff.Employees.Select(e => e.DisplayName).ToArray();
                foreach (StaffTask t in tasks.Pending)
                {
                    StaffTask task = t;
                    GUILayout.BeginVertical(GUI.skin.box);
                    GUILayout.Label(t.Description, text);
                    if (employeeNames.Length > 0)
                    {
                        GUILayout.Label("Asignar a:", text);
                        int chosen = GUILayout.SelectionGrid(-1, employeeNames, 4);
                        if (chosen >= 0)
                        {
                            Employee target = staff.Employees[chosen];
                            deferred += () => tasks.Assign(task, target);
                        }
                    }
                    GUILayout.EndVertical();
                }

                GUILayout.EndScrollView();
            });
        }

        private void DrawMessage(float w, float h)
        {
            string message = Cafe.Message;
            if (message == null) return;
            Panel(new Rect(w * 0.5f - 230f, h - 56f, 460f, 48f), () => GUILayout.Label(message, bold));
        }

        // =========================== Ventanas modales ===========================

        private void DrawDayReport(float w, float h)
        {
            DailyLedger r = day.LastReport;
            if (r == null) return;

            Panel(new Rect(w * 0.5f - 210f, Mathf.Max(8f, h * 0.5f - 230f), 420f, 460f), () =>
            {
                GUILayout.Label($"Fin del día {Cafe.Day}", header);
                GUILayout.Space(4);
                Row("Ventas", r.Get(MoneyCategory.Ventas));
                Row("Propinas", r.Get(MoneyCategory.Propinas));
                Row("Reembolsos de obra", r.Get(MoneyCategory.Reembolsos));
                Row("Ingredientes", -r.Get(MoneyCategory.Ingredientes));
                Row("Construcción", -r.Get(MoneyCategory.Construccion));
                Row("Contrataciones", -r.Get(MoneyCategory.Contrataciones));
                Row("Sueldos", -r.Get(MoneyCategory.Sueldos));
                GUILayout.Space(4);
                Row("Resultado del día", r.Net, bold);
                GUILayout.Space(6);
                GUILayout.Label($"Clientes atendidos: {r.Served} · Se fueron enojados: {r.Angry}", text);
                GUILayout.Label($"Reputación: {Cafe.Reputation:0.0}/5 · Caja: ${Cafe.Money}", text);
                if (Cafe.Money < 0) GUILayout.Label("La caja está en negativo: conviene recortar gastos.", bold);
                GUILayout.Space(10);
                if (GUILayout.Button($"Empezar el día {Cafe.Day + 1} (se guarda la partida)", GUILayout.Height(32)))
                    deferred += day.StartNextDay;
            });
        }

        private void DrawStartMenu(float w, float h)
        {
            Panel(new Rect(w * 0.5f - 180f, h * 0.5f - 90f, 360f, 180f), () =>
            {
                GUILayout.Label("Mancoffee", header);
                GUILayout.Label("Hay una partida guardada.", text);
                GUILayout.Space(8);
                if (GUILayout.Button("Continuar", GUILayout.Height(30))) deferred += bootstrap.ChooseContinue;
                if (GUILayout.Button("Nueva partida", GUILayout.Height(30))) deferred += bootstrap.ChooseNewGame;
                GUILayout.Label("La partida nueva reemplaza a la anterior al guardar.", text);
            });
        }

        private void Row(string label, int value, GUIStyle style = null)
        {
            GUIStyle s = style ?? text;
            GUILayout.BeginHorizontal();
            GUILayout.Label(label, s);
            GUILayout.FlexibleSpace();
            GUILayout.Label((value < 0 ? "-$" : "$") + Mathf.Abs(value), s);
            GUILayout.EndHorizontal();
        }

        private void EnsureStyles()
        {
            if (text != null) return;
            text = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true };
            text.normal.textColor = new Color(0.93f, 0.93f, 0.93f);
            bold = new GUIStyle(text) { fontStyle = FontStyle.Bold };
            header = new GUIStyle(bold) { fontSize = 15 };
            leftButton = new GUIStyle(GUI.skin.button) { alignment = TextAnchor.MiddleLeft };
        }
    }
}
