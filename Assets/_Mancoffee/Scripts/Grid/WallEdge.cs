using System;
using UnityEngine;

namespace Mancoffee.GridSystem
{
    public enum EdgeAxis
    {
        /// <summary>Arista entre la celda y la celda +X (su borde superior derecho en pantalla).</summary>
        X,
        /// <summary>Arista entre la celda y la celda +Y (su borde superior izquierdo en pantalla).</summary>
        Y
    }

    public enum WallKind
    {
        Pared,
        Puerta,
        Ventana
    }

    /// <summary>
    /// Identifica el borde entre dos celdas vecinas. Las paredes viven en bordes, no en celdas.
    /// Siempre se guarda desde la celda "delantera" (la que queda más abajo en pantalla),
    /// así cada borde tiene una sola forma de escribirse.
    /// </summary>
    public readonly struct WallEdge : IEquatable<WallEdge>
    {
        public readonly Vector2Int Cell;
        public readonly EdgeAxis Axis;

        public WallEdge(Vector2Int cell, EdgeAxis axis)
        {
            Cell = cell;
            Axis = axis;
        }

        /// <summary>Celda delante del borde (más abajo en pantalla).</summary>
        public Vector2Int FrontCell => Cell;

        /// <summary>Celda detrás del borde (más arriba en pantalla).</summary>
        public Vector2Int BackCell => Axis == EdgeAxis.X ? Cell + Vector2Int.right : Cell + Vector2Int.up;

        /// <summary>El borde que separa dos celdas ortogonalmente vecinas.</summary>
        public static WallEdge Between(Vector2Int a, Vector2Int b)
        {
            Vector2Int d = b - a;
            if (d == Vector2Int.right) return new WallEdge(a, EdgeAxis.X);
            if (d == Vector2Int.left) return new WallEdge(b, EdgeAxis.X);
            if (d == Vector2Int.up) return new WallEdge(a, EdgeAxis.Y);
            return new WallEdge(b, EdgeAxis.Y);
        }

        public bool Equals(WallEdge other) => Cell == other.Cell && Axis == other.Axis;
        public override bool Equals(object obj) => obj is WallEdge other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Cell, (int)Axis);
        public override string ToString() => $"({Cell.x},{Cell.y}){Axis}";
    }
}
