using System.Collections.Generic;
using UnityEngine;

namespace Mancoffee.Core
{
    /// <summary>
    /// Registro de las zonas de pantalla ocupadas por la interfaz, para que un clic sobre un panel
    /// no coloque muebles ni mueva la cámara por detrás.
    /// </summary>
    public static class UiBlocker
    {
        private static readonly List<Rect> rects = new();
        private static readonly List<Rect> building = new();

        /// <summary>Hay una ventana modal abierta (resumen del día, menú inicial): se bloquea todo el mundo.</summary>
        public static bool ModalOpen { get; set; }

        public static void BeginRepaint() => building.Clear();

        /// <summary>Rect en píxeles de pantalla, con origen arriba a la izquierda (como OnGUI).</summary>
        public static void Register(Rect screenRect) => building.Add(screenRect);

        public static void EndRepaint()
        {
            rects.Clear();
            rects.AddRange(building);
        }

        /// <summary>Posición del mouse en pantalla (origen abajo a la izquierda, como el Input System).</summary>
        public static bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (ModalOpen) return true;
            var p = new Vector2(screenPosition.x, Screen.height - screenPosition.y);
            foreach (Rect r in rects)
                if (r.Contains(p)) return true;
            return false;
        }
    }
}
