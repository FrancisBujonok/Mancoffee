using System.Collections.Generic;
using Mancoffee.Building;
using Mancoffee.CameraSystem;
using Mancoffee.Core;
using Mancoffee.Customers;
using Mancoffee.GridSystem;
using Mancoffee.Staff;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace Mancoffee.UI
{
    /// <summary>
    /// Interfaz "Libreta del barista" hecha con UI Toolkit. Arma las capas, decide qué se ve
    /// según el modo (juego, construcción, paneles, cierre) y maneja los atajos de teclado.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MancoffeeHud : MonoBehaviour
    {
        [SerializeField] private UIDocument document;
        [SerializeField] private StyleSheet styleSheet;
        [SerializeField] private GridManager grid;
        [SerializeField] private BuildModeController build;
        [SerializeField] private StaffManager staff;
        [SerializeField] private TaskManager tasks;
        [SerializeField] private CustomerSpawner spawner;
        [SerializeField] private DayCycle day;
        [SerializeField] private SaveSystem save;
        [SerializeField] private WallRenderer walls;
        [SerializeField] private GameBootstrap bootstrap;
        [SerializeField] private CameraController camController;
        [SerializeField] private SelectionController selection;
        [SerializeField] private Camera worldCamera;

        private const float RefreshInterval = 0.15f;

        private HudContext ctx;
        private VisualElement root;
        private VisualElement worldLayer;
        private VisualElement baseLayer;
        private VisualElement panelLayer;
        private VisualElement notesLayer;
        private VisualElement pieLayer;
        private VisualElement dragLayer;
        private VisualElement modalLayer;

        private ChalkboardView chalkboard;
        private CompactBoardView compactBoard;
        private ReceiptView receipt;
        private TicketRailView rail;
        private ActionCardsView actionCards;
        private PolaroidsView polaroids;
        private ActionQueueView queue;
        private NotesView notes;
        private PriceTagView priceTag;
        private PieMenuView pie;
        private BuildCatalogView catalog;
        private BuildTipView tip;
        private StaffBoardView staffBoard;
        private CartaView carta;
        private FinanceView finance;
        private OptionsView options;
        private DayReportView report;
        private StartMenuView startMenu;
        private SuppliesView supplies;
        private DespensaView despensa;
        private DebugView debug;

        private readonly Dictionary<HudPanel, VisualElement> panels = new();
        private HudPanel openPanel = HudPanel.None;
        private float nextRefresh;
        private bool built;

        private void Reset() => document = GetComponent<UIDocument>();

        private void Awake()
        {
            if (document == null) document = GetComponent<UIDocument>();
            if (grid == null) grid = FindFirstObjectByType<GridManager>();
            if (build == null) build = FindFirstObjectByType<BuildModeController>();
            if (staff == null) staff = FindFirstObjectByType<StaffManager>();
            if (tasks == null) tasks = FindFirstObjectByType<TaskManager>();
            if (spawner == null) spawner = FindFirstObjectByType<CustomerSpawner>();
            if (day == null) day = FindFirstObjectByType<DayCycle>();
            if (save == null) save = FindFirstObjectByType<SaveSystem>();
            if (walls == null) walls = FindFirstObjectByType<WallRenderer>();
            if (bootstrap == null) bootstrap = FindFirstObjectByType<GameBootstrap>();
            if (camController == null) camController = FindFirstObjectByType<CameraController>();
            if (selection == null) selection = FindFirstObjectByType<SelectionController>();
            if (worldCamera == null) worldCamera = Camera.main;
        }

        private void OnEnable()
        {
            if (selection != null)
            {
                selection.PieRequested += OnPieRequested;
                selection.PieClosed += OnPieClosed;
            }
            UiBlocker.PointerOverProvider = IsPointerOverHud;
        }

        private void OnDisable()
        {
            if (selection != null)
            {
                selection.PieRequested -= OnPieRequested;
                selection.PieClosed -= OnPieClosed;
            }
            if (UiBlocker.PointerOverProvider == (System.Func<Vector2, bool>)IsPointerOverHud) UiBlocker.PointerOverProvider = null;
            built = false;
        }

        private void Start() => Build();

        // =========================== Armado ===========================

        private void Build()
        {
            root = document.rootVisualElement;
            if (root == null) return;
            root.Clear();
            if (styleSheet != null && !root.styleSheets.Contains(styleSheet)) root.styleSheets.Add(styleSheet);
            root.AddToClassList("hud-root");
            root.pickingMode = PickingMode.Ignore;

            ctx = new HudContext
            {
                Hud = this, Grid = grid, Build = build, Staff = staff, Tasks = tasks, Spawner = spawner, Day = day,
                Save = save, Walls = walls, Bootstrap = bootstrap, Cam = camController, Selection = selection
            };

            worldLayer = Layer();
            baseLayer = Layer();
            notesLayer = Layer();
            panelLayer = Layer();
            pieLayer = Layer();
            dragLayer = Layer();
            modalLayer = Layer();

            priceTag = new PriceTagView(worldLayer, ctx);

            rail = new TicketRailView(baseLayer, ctx);
            chalkboard = new ChalkboardView(baseLayer, ctx);
            supplies = new SuppliesView(baseLayer, ctx);
            compactBoard = new CompactBoardView(baseLayer, ctx);
            catalog = new BuildCatalogView(baseLayer, ctx);
            receipt = new ReceiptView(baseLayer, ctx);
            queue = new ActionQueueView(baseLayer, ctx);
            polaroids = new PolaroidsView(baseLayer, ctx);
            tip = new BuildTipView(baseLayer, ctx);
            actionCards = new ActionCardsView(baseLayer, ctx);

            notes = new NotesView(notesLayer, ctx);

            staffBoard = new StaffBoardView(panelLayer, dragLayer, ctx);
            carta = new CartaView(panelLayer, ctx);
            finance = new FinanceView(panelLayer, ctx);
            options = new OptionsView(panelLayer, ctx);
            despensa = new DespensaView(panelLayer, ctx);
            debug = new DebugView(panelLayer, ctx);
            panels[HudPanel.Despensa] = despensa.Root;
            panels[HudPanel.Pruebas] = debug.Root;
            panels[HudPanel.Personal] = staffBoard.Root;
            panels[HudPanel.Carta] = carta.Root;
            panels[HudPanel.Finanzas] = finance.Root;
            panels[HudPanel.Opciones] = options.Root;
            foreach (VisualElement p in panels.Values) UI.Show(p, false);

            pie = new PieMenuView(pieLayer);
            report = new DayReportView(modalLayer, ctx);
            startMenu = new StartMenuView(modalLayer, ctx);

            built = true;
            RefreshAll();
        }

        private VisualElement Layer()
        {
            VisualElement layer = UI.El(root, "layer");
            layer.pickingMode = PickingMode.Ignore;
            return layer;
        }

        // =========================== API para las vistas ===========================

        public void ShowPie(Vector2 panelPos, string title, List<PieOption> optionsList) => pie?.Show(panelPos, title, optionsList);

        public void OpenPanel(HudPanel panel)
        {
            if (build.IsBuildMode) build.SetBuildMode(false);
            pie?.Hide();
            openPanel = panel;
            foreach (KeyValuePair<HudPanel, VisualElement> kv in panels) UI.Show(kv.Value, kv.Key == panel);
            RefreshAll();
        }

        public void ClosePanel() => OpenPanel(HudPanel.None);

        public void TogglePanel(HudPanel panel) => OpenPanel(openPanel == panel ? HudPanel.None : panel);

        public Vector2 ScreenToPanel(Vector2 screen)
        {
            if (root?.panel == null) return screen;
            return RuntimePanelUtils.ScreenToPanel(root.panel, new Vector2(screen.x, Screen.height - screen.y));
        }

        public Vector2 WorldToPanel(Vector3 world)
        {
            if (worldCamera == null) return Vector2.zero;
            Vector3 screen = worldCamera.WorldToScreenPoint(world);
            return ScreenToPanel(screen);
        }

        private bool IsPointerOverHud(Vector2 screen)
        {
            if (!built || root?.panel == null) return false;
            VisualElement picked = root.panel.Pick(ScreenToPanel(screen));
            return picked != null && picked != root && picked != root.panel.visualTree;
        }

        private void OnPieRequested(Vector2 screenPos, string title, List<PieOption> optionsList) =>
            ShowPie(ScreenToPanel(screenPos), title, optionsList);

        private void OnPieClosed() => pie?.Hide();

        // =========================== Ciclo ===========================

        private bool StartMenuOpen => bootstrap != null && bootstrap.AwaitingChoice;
        private bool ReportOpen => day != null && day.Phase == DayPhase.Resumen;

        private void Update()
        {
            if (!built) return;

            bool modal = StartMenuOpen || ReportOpen;
            UiBlocker.ModalOpen = modal;
            if (modal)
            {
                pie.Hide();
                if (openPanel != HudPanel.None) ClosePanel();
            }

            HandleKeys(modal);
            if (build.IsBuildMode && openPanel != HudPanel.None) ClosePanel();

            priceTag.Update();
            if (Time.unscaledTime >= nextRefresh) RefreshAll();
        }

        private void HandleKeys(bool modal)
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb.escapeKey.wasPressedThisFrame) HandleEscape(modal);
            if (modal) return;

            if (kb.spaceKey.wasPressedThisFrame) day.TogglePause();
            if (kb.vKey.wasPressedThisFrame) walls.CycleMode();
            if (kb.f5Key.wasPressedThisFrame)
            {
                save.Save();
                CafeState.Instance.Notify("Partida guardada", NoteKind.Bueno);
            }
            if (kb.f9Key.wasPressedThisFrame && save.Load()) CafeState.Instance.Notify("Partida cargada", NoteKind.Bueno);
            if (kb.tKey.wasPressedThisFrame) TogglePanel(HudPanel.Personal);
            if (kb.mKey.wasPressedThisFrame) TogglePanel(HudPanel.Carta);
            if (kb.fKey.wasPressedThisFrame) TogglePanel(HudPanel.Finanzas);
            if (kb.iKey.wasPressedThisFrame) TogglePanel(HudPanel.Despensa);
            if (kb.f1Key.wasPressedThisFrame) TogglePanel(HudPanel.Pruebas);
        }

        private void HandleEscape(bool modal)
        {
            if (pie.IsOpen) { pie.Hide(); return; }
            if (modal) return;
            if (openPanel != HudPanel.None) { ClosePanel(); return; }
            if (build.IsBuildMode) return; // el controlador de construcción usa Esc para soltar el mueble
            if (selection.Selected != null) { selection.Select(null); return; }
            OpenPanel(HudPanel.Opciones);
        }

        private void RefreshAll()
        {
            if (!built) return;
            nextRefresh = Time.unscaledTime + RefreshInterval;

            bool starting = StartMenuOpen;
            bool reporting = ReportOpen && !starting;
            bool building = build.IsBuildMode;
            bool playing = !starting && !reporting;

            UI.Show(baseLayer, playing);
            UI.Show(notesLayer, playing);
            UI.Show(worldLayer, playing);
            startMenu.Refresh(starting);
            report.Refresh(reporting);
            if (!playing) return;

            UI.Show(chalkboard.Root, !building);
            UI.Show(supplies.Root, !building);
            UI.Show(rail.Root, !building);
            UI.Show(polaroids.Root, !building);
            UI.Show(compactBoard.Root, building);
            UI.Show(catalog.Root, building);
            UI.Show(tip.Root, building);
            receipt.SetVisible(true);

            if (building)
            {
                compactBoard.Refresh();
                catalog.Refresh();
                tip.Refresh();
            }
            else
            {
                chalkboard.Refresh();
                supplies.Refresh();
                rail.Refresh();
                polaroids.Refresh();
            }

            receipt.Refresh();
            queue.Refresh(!building && openPanel == HudPanel.None);
            actionCards.Refresh(openPanel);
            notes.Refresh();

            switch (openPanel)
            {
                case HudPanel.Personal: staffBoard.Refresh(); break;
                case HudPanel.Carta: carta.Refresh(); break;
                case HudPanel.Finanzas: finance.Refresh(); break;
                case HudPanel.Opciones: options.Refresh(); break;
                case HudPanel.Despensa: despensa.Refresh(); break;
            }
        }
    }
}
