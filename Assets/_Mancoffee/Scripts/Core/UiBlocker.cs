using System;
using UnityEngine;

namespace Mancoffee.Core
{
    /// <summary>
    /// Avisa si el mouse está sobre la interfaz, para que un clic sobre un panel no coloque muebles,
    /// mueva la cámara ni dé órdenes por detrás. La interfaz registra cómo averiguarlo.
    /// </summary>
    public static class UiBlocker
    {
        /// <summary>Hay una ventana modal abierta (resumen del día, menú inicial): se bloquea todo el mundo.</summary>
        public static bool ModalOpen { get; set; }

        /// <summary>
        /// Lo registra la interfaz: recibe la posición del mouse (origen abajo a la izquierda, como el Input System)
        /// y devuelve si hay un elemento de la interfaz debajo.
        /// </summary>
        public static Func<Vector2, bool> PointerOverProvider { get; set; }

        public static bool IsPointerOverUI(Vector2 screenPosition)
        {
            if (ModalOpen) return true;
            return PointerOverProvider != null && PointerOverProvider(screenPosition);
        }
    }
}
