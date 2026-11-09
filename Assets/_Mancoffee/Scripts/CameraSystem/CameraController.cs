using Mancoffee.Core;
using Mancoffee.GridSystem;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

namespace Mancoffee.CameraSystem
{
    /// <summary>
    /// Cámara de gestión: paneo con teclado o arrastrando con el clic medio, y zoom por niveles.
    /// El zoom cambia la resolución de referencia del Pixel Perfect Camera, así los píxeles
    /// siempre se escalan en múltiplos enteros y el pixel art no se deforma.
    /// </summary>
    [RequireComponent(typeof(Camera), typeof(PixelPerfectCamera))]
    public class CameraController : MonoBehaviour
    {
        [SerializeField] private float panSpeed = 6f;
        [SerializeField] private float boundsMargin = 2f;

        [Tooltip("Resoluciones de referencia de más lejos a más cerca.")]
        [SerializeField] private Vector2Int[] zoomLevels =
        {
            new(960, 540),
            new(640, 360),
            new(480, 270),
            new(320, 180)
        };

        [SerializeField] private int startZoomIndex = 1;

        private Camera cam;
        private PixelPerfectCamera pixelPerfect;
        private int zoomIndex;
        private bool dragging;
        private Vector3? focusTarget;

        /// <summary>Lleva la cámara suavemente hasta un punto del mundo (por ejemplo, "Ir a la mesa").</summary>
        public void FocusOn(Vector3 worldPosition)
        {
            focusTarget = new Vector3(worldPosition.x, worldPosition.y, transform.position.z);
        }
        private Vector3 dragOriginWorld;

        private void Awake()
        {
            cam = GetComponent<Camera>();
            pixelPerfect = GetComponent<PixelPerfectCamera>();
            SetZoom(startZoomIndex);
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            Mouse mouse = Mouse.current;
            if (kb == null || mouse == null) return;

            if (focusTarget.HasValue)
            {
                transform.position = Vector3.Lerp(transform.position, focusTarget.Value, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));
                if ((transform.position - focusTarget.Value).sqrMagnitude < 0.0004f) focusTarget = null;
            }

            HandleKeyboardPan(kb);
            HandleDragPan(mouse);
            HandleZoom(mouse);
            ClampToFloor();
        }

        private void HandleKeyboardPan(Keyboard kb)
        {
            Vector2 dir = Vector2.zero;
            if (kb.wKey.isPressed || kb.upArrowKey.isPressed) dir.y += 1;
            if (kb.sKey.isPressed || kb.downArrowKey.isPressed) dir.y -= 1;
            if (kb.aKey.isPressed || kb.leftArrowKey.isPressed) dir.x -= 1;
            if (kb.dKey.isPressed || kb.rightArrowKey.isPressed) dir.x += 1;

            if (dir == Vector2.zero) return;
            focusTarget = null;
            // Más lejos = paneo más rápido, para que se sienta igual en todos los zooms.
            float zoomFactor = zoomLevels[zoomIndex].x / (float)zoomLevels[startZoomIndex].x;
            transform.position += (Vector3)(dir.normalized * (panSpeed * zoomFactor * Time.unscaledDeltaTime));
        }

        private void HandleDragPan(Mouse mouse)
        {
            Vector3 mouseWorld = cam.ScreenToWorldPoint(mouse.position.ReadValue());

            if (mouse.middleButton.wasPressedThisFrame && !UiBlocker.IsPointerOverUI(mouse.position.ReadValue()))
            {
                dragging = true;
                dragOriginWorld = mouseWorld;
            }
            if (mouse.middleButton.wasReleasedThisFrame) dragging = false;

            if (dragging)
            {
                // Mueve la cámara para que el punto agarrado quede bajo el cursor.
                Vector3 delta = dragOriginWorld - mouseWorld;
                delta.z = 0f;
                transform.position += delta;
            }
        }

        private void HandleZoom(Mouse mouse)
        {
            if (UiBlocker.IsPointerOverUI(mouse.position.ReadValue())) return;
            float scroll = mouse.scroll.ReadValue().y;
            if (scroll > 0.01f) SetZoom(zoomIndex + 1);
            else if (scroll < -0.01f) SetZoom(zoomIndex - 1);
        }

        private void SetZoom(int index)
        {
            zoomIndex = Mathf.Clamp(index, 0, zoomLevels.Length - 1);
            pixelPerfect.refResolutionX = zoomLevels[zoomIndex].x;
            pixelPerfect.refResolutionY = zoomLevels[zoomIndex].y;
        }

        private void ClampToFloor()
        {
            GridManager grid = GridManager.Instance;
            if (grid == null) return;

            Rect r = grid.GetFloorWorldRect();
            Vector3 p = transform.position;
            p.x = Mathf.Clamp(p.x, r.xMin - boundsMargin, r.xMax + boundsMargin);
            p.y = Mathf.Clamp(p.y, r.yMin - boundsMargin, r.yMax + boundsMargin);
            transform.position = p;
        }
    }
}
