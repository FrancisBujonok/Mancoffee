using System.Collections.Generic;
using Mancoffee.GridSystem;
using UnityEngine;
using UnityEngine.Rendering;

namespace Mancoffee.Building
{
    public enum WallViewMode
    {
        /// <summary>Las paredes exteriores del frente se ven bajas para no tapar el interior.</summary>
        Auto,
        Bajas,
        Altas
    }

    /// <summary>
    /// Dibuja las paredes que guarda el GridManager. Cada pared es un panel parado sobre su borde;
    /// su orden de dibujado sale del punto medio del borde.
    /// </summary>
    public class WallRenderer : MonoBehaviour
    {
        [SerializeField] private GridManager grid;
        [SerializeField] private Transform wallsParent;

        [Header("Sprites. X = borde superior derecho de la celda, Y = borde superior izquierdo")]
        [SerializeField] private Sprite wallX;
        [SerializeField] private Sprite wallY;
        [SerializeField] private Sprite lowX;
        [SerializeField] private Sprite lowY;
        [SerializeField] private Sprite doorX;
        [SerializeField] private Sprite doorY;
        [SerializeField] private Sprite lowDoorX;
        [SerializeField] private Sprite lowDoorY;
        [SerializeField] private Sprite windowX;
        [SerializeField] private Sprite windowY;

        private readonly Dictionary<WallEdge, SpriteRenderer> views = new();

        public WallViewMode Mode { get; private set; } = WallViewMode.Auto;

        private void Start()
        {
            grid.WallChanged += RefreshEdge;
            grid.LayoutReloaded += RebuildAll;
            RebuildAll();
        }

        private void OnDestroy()
        {
            if (grid == null) return;
            grid.WallChanged -= RefreshEdge;
            grid.LayoutReloaded -= RebuildAll;
        }

        public void SetMode(WallViewMode mode)
        {
            Mode = mode;
            RebuildAll();
        }

        public void CycleMode() => SetMode((WallViewMode)(((int)Mode + 1) % 3));

        public void RebuildAll()
        {
            foreach (SpriteRenderer view in views.Values)
                if (view != null) Destroy(view.transform.parent.gameObject);
            views.Clear();

            foreach (KeyValuePair<WallEdge, WallKind> kv in grid.Walls) RefreshEdge(kv.Key);
        }

        private void RefreshEdge(WallEdge edge)
        {
            if (!grid.TryGetWall(edge, out WallKind kind))
            {
                if (views.TryGetValue(edge, out SpriteRenderer old))
                {
                    if (old != null) Destroy(old.transform.parent.gameObject);
                    views.Remove(edge);
                }
                return;
            }

            if (!views.TryGetValue(edge, out SpriteRenderer view) || view == null)
            {
                var root = new GameObject($"Pared {edge}");
                root.transform.SetParent(wallsParent, false);
                root.AddComponent<SortingGroup>();
                var child = new GameObject("Sprite");
                child.transform.SetParent(root.transform, false);
                view = child.AddComponent<SpriteRenderer>();
                views[edge] = view;
            }

            grid.GetEdgeEnds(edge, out Vector3 lower, out Vector3 upper);
            view.transform.parent.position = (lower + upper) * 0.5f;
            view.transform.position = lower;
            view.sprite = GetSprite(kind, edge.Axis, IsLow(edge));
        }

        /// <summary>En modo Auto se bajan las paredes cuyo frente da a la calle: las del borde delantero del local.</summary>
        public bool IsLow(WallEdge edge) =>
            Mode == WallViewMode.Bajas || (Mode == WallViewMode.Auto && !grid.IsFloor(edge.FrontCell));

        public Sprite GetSprite(WallKind kind, EdgeAxis axis, bool low)
        {
            bool x = axis == EdgeAxis.X;
            if (low) return kind == WallKind.Puerta ? (x ? lowDoorX : lowDoorY) : (x ? lowX : lowY);
            return kind switch
            {
                WallKind.Puerta => x ? doorX : doorY,
                WallKind.Ventana => x ? windowX : windowY,
                _ => x ? wallX : wallY
            };
        }
    }
}
