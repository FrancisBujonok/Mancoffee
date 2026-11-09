using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace Mancoffee.UI
{
    public enum IconKind
    {
        Hammer, Users, Book, Chart, Gear, Pause, Play, Fast2, Fast3, Alert, Check, Close, Plus,
        Save, Walls, Door, Window, Floor, Chair, Pin, Swap, Coin, Layers, Grip
    }

    /// <summary>Atajos para armar la interfaz por código. El aspecto vive en Mancoffee.uss.</summary>
    public static class UI
    {
        private static readonly CultureInfo Es = new("es-AR");

        public static VisualElement El(VisualElement parent, params string[] classes)
        {
            var e = new VisualElement();
            foreach (string c in classes) e.AddToClassList(c);
            parent?.Add(e);
            return e;
        }

        public static Label Text(VisualElement parent, string text, params string[] classes)
        {
            var l = new Label(text) { pickingMode = PickingMode.Ignore };
            foreach (string c in classes) l.AddToClassList(c);
            parent?.Add(l);
            return l;
        }

        /// <summary>Un "botón" hecho con un VisualElement y un Clickable, para no heredar el estilo del tema.</summary>
        public static VisualElement Button(VisualElement parent, string text, Action onClick, params string[] classes)
        {
            VisualElement b = El(parent, classes);
            if (!string.IsNullOrEmpty(text)) Text(b, text, "btn-label", "f-heavy");
            b.AddManipulator(new Clickable(() => onClick?.Invoke()));
            return b;
        }

        public static IconElement Icon(VisualElement parent, IconKind kind, params string[] classes)
        {
            var icon = new IconElement(kind);
            foreach (string c in classes) icon.AddToClassList(c);
            parent?.Add(icon);
            return icon;
        }

        /// <summary>Un sprite pixel art escalado en múltiplos enteros (la textura ya usa filtro Point).</summary>
        public static Image Pixel(VisualElement parent, Sprite sprite, float scale, params string[] classes)
        {
            var img = new Image { scaleMode = ScaleMode.ScaleToFit, pickingMode = PickingMode.Ignore };
            foreach (string c in classes) img.AddToClassList(c);
            SetPixel(img, sprite, scale);
            parent?.Add(img);
            return img;
        }

        public static void SetPixel(Image img, Sprite sprite, float scale)
        {
            img.sprite = sprite;
            if (sprite == null)
            {
                img.style.display = DisplayStyle.None;
                return;
            }
            img.style.display = DisplayStyle.Flex;
            img.style.width = sprite.rect.width * scale;
            img.style.height = sprite.rect.height * scale;
        }

        /// <summary>Envoltorio con sombra suave detrás del panel (USS no tiene box-shadow).</summary>
        public static VisualElement Shadowed(VisualElement parent, out VisualElement panel, string wrapClass, params string[] panelClasses)
        {
            VisualElement wrap = El(parent, wrapClass);
            VisualElement shadow = El(wrap, "soft-shadow");
            shadow.pickingMode = PickingMode.Ignore;
            panel = El(wrap, panelClasses);
            return wrap;
        }

        public static string Money(int v) => (v < 0 ? "-$" : "$") + Math.Abs(v).ToString("#,0", Es);
        public static string Signed(int v) => (v >= 0 ? "+$" : "-$") + Math.Abs(v).ToString("#,0", Es);
        public static string Dec(float v) => v.ToString("0.0", Es);
        public static string Pct(float ratio) => Mathf.RoundToInt(Mathf.Clamp01(ratio) * 100f) + "%";

        public static Color Hex(string hex)
        {
            ColorUtility.TryParseHtmlString(hex, out Color c);
            return c;
        }

        public static readonly Color Sage = Hex("#3F7A47");
        public static readonly Color Mustard = Hex("#D99A2B");
        public static readonly Color Tomato = Hex("#C8432C");

        public static Color PatienceColor(float ratio) => ratio >= 0.6f ? Sage : ratio >= 0.35f ? Mustard : Tomato;
        public static string PatienceMood(float ratio) => ratio >= 0.6f ? "Tranquilo" : ratio >= 0.35f ? "Esperando" : "Urgente";

        public static void Show(VisualElement e, bool visible) => e.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;

        public static void SetFill(VisualElement fill, float ratio) => fill.style.width = Length.Percent(Mathf.Clamp01(ratio) * 100f);

        /// <summary>Clase de rotación "a mano" según la posición, para que los papeles no queden todos derechos.</summary>
        public static string Tilt(int index) => (index % 4) switch { 0 => "rot-a", 1 => "rot-b", 2 => "rot-c", _ => "rot-d" };
    }

    /// <summary>
    /// Íconos de trazo dibujados con Painter2D sobre una grilla de 24×24 (como los íconos SVG del mockup).
    /// El color sale de la propiedad USS --icon-color.
    /// </summary>
    public class IconElement : VisualElement
    {
        private static readonly CustomStyleProperty<Color> ColorProperty = new("--icon-color");

        private IconKind kind;
        private Color color = new(0.227f, 0.141f, 0.094f);

        public IconKind Kind
        {
            get => kind;
            set { kind = value; MarkDirtyRepaint(); }
        }

        public IconElement(IconKind kind)
        {
            this.kind = kind;
            pickingMode = PickingMode.Ignore;
            AddToClassList("icon");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyle);
        }

        private void OnCustomStyle(CustomStyleResolvedEvent evt)
        {
            if (evt.customStyle.TryGetValue(ColorProperty, out Color c))
            {
                color = c;
                MarkDirtyRepaint();
            }
        }

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 1f || r.height < 1f) return;

            float s = Mathf.Min(r.width, r.height) / 24f;
            var o = new Vector2((r.width - 24f * s) * 0.5f, (r.height - 24f * s) * 0.5f);
            Painter2D p = ctx.painter2D;
            p.strokeColor = color;
            p.fillColor = color;
            p.lineWidth = 2f * s;
            p.lineCap = LineCap.Round;
            p.lineJoin = LineJoin.Round;

            Vector2 P(float x, float y) => o + new Vector2(x * s, y * s);

            void Poly(bool closed, params float[] xy)
            {
                p.BeginPath();
                p.MoveTo(P(xy[0], xy[1]));
                for (int i = 2; i < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
                if (closed) p.ClosePath();
                p.Stroke();
            }

            void FillPoly(params float[] xy)
            {
                p.BeginPath();
                p.MoveTo(P(xy[0], xy[1]));
                for (int i = 2; i < xy.Length; i += 2) p.LineTo(P(xy[i], xy[i + 1]));
                p.ClosePath();
                p.Fill();
            }

            void Circle(float cx, float cy, float radius, bool fill)
            {
                p.BeginPath();
                p.Arc(P(cx, cy), radius * s, Angle.Degrees(0f), Angle.Degrees(360f));
                p.ClosePath();
                if (fill) p.Fill(); else p.Stroke();
            }

            switch (kind)
            {
                case IconKind.Hammer:
                    Poly(false, 3, 21, 12, 12);
                    Poly(true, 12.5f, 5.5f, 15.5f, 2.5f, 21.5f, 8.5f, 18.5f, 11.5f);
                    Poly(false, 11, 7, 17, 13);
                    break;
                case IconKind.Users:
                    Circle(9, 8, 3.5f, false);
                    p.BeginPath();
                    p.MoveTo(P(2.5f, 20));
                    p.BezierCurveTo(P(2.5f, 16.4f), P(5.4f, 14), P(9, 14));
                    p.BezierCurveTo(P(12.6f, 14), P(15.5f, 16.4f), P(15.5f, 20));
                    p.Stroke();
                    Circle(17, 9, 2.5f, false);
                    p.BeginPath();
                    p.MoveTo(P(16, 14.2f));
                    p.QuadraticCurveTo(P(21.2f, 14.6f), P(21.5f, 20));
                    p.Stroke();
                    break;
                case IconKind.Book:
                    p.BeginPath();
                    p.MoveTo(P(5, 3));
                    p.LineTo(P(16, 3));
                    p.Arc(P(16, 6), 3 * s, Angle.Degrees(-90), Angle.Degrees(0));
                    p.LineTo(P(19, 21));
                    p.LineTo(P(8, 21));
                    p.Arc(P(8, 18), 3 * s, Angle.Degrees(90), Angle.Degrees(180));
                    p.ClosePath();
                    p.Stroke();
                    p.BeginPath();
                    p.MoveTo(P(5, 18));
                    p.Arc(P(8, 18), 3 * s, Angle.Degrees(180), Angle.Degrees(270));
                    p.LineTo(P(19, 15));
                    p.Stroke();
                    Poly(false, 9, 7, 15, 7);
                    Poly(false, 9, 10, 13, 10);
                    break;
                case IconKind.Chart:
                    Poly(false, 4, 20, 4, 11);
                    Poly(false, 10, 20, 10, 5);
                    Poly(false, 16, 20, 16, 13);
                    Poly(false, 2, 20, 22, 20);
                    break;
                case IconKind.Gear:
                    Circle(12, 12, 3, false);
                    Poly(false, 12, 2, 12, 5); Poly(false, 12, 19, 12, 22);
                    Poly(false, 2, 12, 5, 12); Poly(false, 19, 12, 22, 12);
                    Poly(false, 4.9f, 4.9f, 7, 7); Poly(false, 17, 17, 19.1f, 19.1f);
                    Poly(false, 4.9f, 19.1f, 7, 17); Poly(false, 17, 7, 19.1f, 4.9f);
                    break;
                case IconKind.Pause:
                    p.lineWidth = 2.8f * s;
                    Poly(false, 8, 5, 8, 19);
                    Poly(false, 16, 5, 16, 19);
                    break;
                case IconKind.Play:
                    FillPoly(7, 4, 19, 12, 7, 20);
                    break;
                case IconKind.Fast2:
                    FillPoly(3, 5, 11, 12, 3, 19);
                    FillPoly(12, 5, 20, 12, 12, 19);
                    break;
                case IconKind.Fast3:
                    FillPoly(1, 6, 7, 12, 1, 18);
                    FillPoly(8.5f, 6, 14.5f, 12, 8.5f, 18);
                    FillPoly(16, 6, 22, 12, 16, 18);
                    break;
                case IconKind.Alert:
                    p.lineWidth = 2.4f * s;
                    Poly(true, 12, 3, 22, 21, 2, 21);
                    Poly(false, 12, 10, 12, 14);
                    Circle(12, 17.6f, 1.3f, true);
                    break;
                case IconKind.Check:
                    p.lineWidth = 3f * s;
                    Poly(false, 5, 12.5f, 9.5f, 17, 19, 7.5f);
                    break;
                case IconKind.Close:
                    p.lineWidth = 2.6f * s;
                    Poly(false, 6, 6, 18, 18);
                    Poly(false, 18, 6, 6, 18);
                    break;
                case IconKind.Plus:
                    p.lineWidth = 2.6f * s;
                    Poly(false, 12, 5, 12, 19);
                    Poly(false, 5, 12, 19, 12);
                    break;
                case IconKind.Save:
                    Poly(true, 5, 3, 16, 3, 19, 6, 19, 21, 5, 21);
                    Poly(false, 8, 3, 8, 8, 15, 8, 15, 3);
                    Poly(false, 8, 21, 8, 14, 16, 14, 16, 21);
                    break;
                case IconKind.Walls:
                    Poly(false, 3, 20, 3, 8, 12, 3, 21, 8, 21, 20);
                    Poly(false, 3, 12, 21, 12);
                    break;
                case IconKind.Door:
                    Poly(true, 6, 3, 18, 3, 18, 21, 6, 21);
                    Circle(15, 12, 1.1f, true);
                    break;
                case IconKind.Window:
                    Poly(true, 4, 4, 20, 4, 20, 20, 4, 20);
                    Poly(false, 12, 4, 12, 20);
                    Poly(false, 4, 12, 20, 12);
                    break;
                case IconKind.Floor:
                    Poly(true, 12, 4, 21, 9, 12, 14, 3, 9);
                    Poly(false, 7.5f, 6.5f, 16.5f, 11.5f);
                    Poly(false, 16.5f, 6.5f, 7.5f, 11.5f);
                    break;
                case IconKind.Chair:
                    Poly(false, 4, 10, 20, 10);
                    Poly(false, 6, 10, 6, 19);
                    Poly(false, 18, 10, 18, 19);
                    Poly(false, 7, 10, 7, 5, 17, 5, 17, 10);
                    break;
                case IconKind.Pin:
                    p.BeginPath();
                    p.MoveTo(P(12, 21));
                    p.LineTo(P(6.8f, 12));
                    p.Arc(P(12, 9), 6 * s, Angle.Degrees(150), Angle.Degrees(390));
                    p.ClosePath();
                    p.Stroke();
                    Circle(12, 9, 2, false);
                    break;
                case IconKind.Swap:
                    Poly(false, 4, 8, 18, 8);
                    Poly(false, 15, 5, 18, 8, 15, 11);
                    Poly(false, 20, 16, 6, 16);
                    Poly(false, 9, 13, 6, 16, 9, 19);
                    break;
                case IconKind.Coin:
                    Circle(12, 12, 9, false);
                    Poly(false, 12, 6.5f, 12, 17.5f);
                    break;
                case IconKind.Layers:
                    Poly(true, 12, 3, 21, 8, 12, 13, 3, 8);
                    Poly(false, 3, 13, 12, 18, 21, 13);
                    break;
                case IconKind.Grip:
                    Circle(9, 6, 1.6f, true); Circle(15, 6, 1.6f, true);
                    Circle(9, 12, 1.6f, true); Circle(15, 12, 1.6f, true);
                    Circle(9, 18, 1.6f, true); Circle(15, 18, 1.6f, true);
                    break;
            }
        }
    }

    /// <summary>Cinco tazas para la reputación: llenas, la parcial más clara y el resto vacías.</summary>
    public class CupMeter : VisualElement
    {
        private float value;
        private static readonly Color Full = new(0.784f, 0.263f, 0.173f);
        private static readonly Color Partial = new(0.914f, 0.627f, 0.569f);
        private static readonly Color Empty = new(1f, 0.992f, 0.969f);
        private static readonly Color Ink = new(0.227f, 0.141f, 0.094f);

        public float Value
        {
            get => value;
            set
            {
                if (Mathf.Approximately(this.value, value)) return;
                this.value = value;
                MarkDirtyRepaint();
            }
        }

        public CupMeter()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 1f) return;
            float cupW = 40f, gap = 8f;
            float s = Mathf.Min(r.height / 20f, cupW / 24f);
            Painter2D p = ctx.painter2D;
            p.lineWidth = 1.8f * s;
            p.strokeColor = Ink;
            p.lineJoin = LineJoin.Round;
            p.lineCap = LineCap.Round;

            for (int i = 0; i < 5; i++)
            {
                var o = new Vector2(i * (cupW + gap), (r.height - 20f * s) * 0.5f);
                Vector2 P(float x, float y) => o + new Vector2(x * s, y * s);

                p.fillColor = value >= i + 1 ? Full : value > i ? Partial : Empty;
                p.BeginPath();
                p.MoveTo(P(3, 6));
                p.LineTo(P(16, 6));
                p.LineTo(P(16, 11));
                p.Arc(P(11, 11), 5 * s, Angle.Degrees(0), Angle.Degrees(90));
                p.LineTo(P(8, 16));
                p.Arc(P(8, 11), 5 * s, Angle.Degrees(90), Angle.Degrees(180));
                p.ClosePath();
                p.Fill();
                p.Stroke();

                p.BeginPath();
                p.MoveTo(P(16, 8));
                p.LineTo(P(17.5f, 8));
                p.Arc(P(17.5f, 10.3f), 2.3f * s, Angle.Degrees(-90), Angle.Degrees(90));
                p.LineTo(P(16, 12.6f));
                p.Stroke();

                p.BeginPath(); p.MoveTo(P(7, 1.5f)); p.LineTo(P(7, 4)); p.Stroke();
                p.BeginPath(); p.MoveTo(P(11, 1.5f)); p.LineTo(P(11, 4)); p.Stroke();
            }
        }
    }

    /// <summary>Borde dentado del papel de ticket, con el color de --fill-color.</summary>
    public class ZigzagEdge : VisualElement
    {
        private static readonly CustomStyleProperty<Color> FillProperty = new("--fill-color");
        private Color fill = new(1f, 0.992f, 0.969f);

        public ZigzagEdge()
        {
            pickingMode = PickingMode.Ignore;
            AddToClassList("zigzag");
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(e =>
            {
                if (e.customStyle.TryGetValue(FillProperty, out Color c)) { fill = c; MarkDirtyRepaint(); }
            });
        }

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 1f) return;
            Painter2D p = ctx.painter2D;
            p.fillColor = fill;
            p.BeginPath();
            p.MoveTo(new Vector2(0, 0));
            p.LineTo(new Vector2(r.width, 0));
            float tooth = 10f;
            bool down = true;
            for (float x = r.width; x > 0f; x -= tooth)
            {
                p.LineTo(new Vector2(Mathf.Max(0f, x - tooth), down ? r.height : 3f));
                down = !down;
            }
            p.LineTo(new Vector2(0, 0));
            p.ClosePath();
            p.Fill();
        }
    }

    /// <summary>Línea punteada (USS no tiene bordes punteados). Color en --line-color.</summary>
    public class DashedLine : VisualElement
    {
        private static readonly CustomStyleProperty<Color> LineProperty = new("--line-color");
        private Color line = new(0.804f, 0.749f, 0.651f);

        public DashedLine()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
            RegisterCallback<CustomStyleResolvedEvent>(e =>
            {
                if (e.customStyle.TryGetValue(LineProperty, out Color c)) { line = c; MarkDirtyRepaint(); }
            });
        }

        private void Draw(MeshGenerationContext ctx)
        {
            Rect r = contentRect;
            if (r.width < 1f) return;
            Painter2D p = ctx.painter2D;
            p.strokeColor = line;
            p.lineWidth = 2f;
            float y = r.height * 0.5f;
            for (float x = 0f; x < r.width; x += 11f)
            {
                p.BeginPath();
                p.MoveTo(new Vector2(x, y));
                p.LineTo(new Vector2(Mathf.Min(r.width, x + 6f), y));
                p.Stroke();
            }
        }
    }
}
