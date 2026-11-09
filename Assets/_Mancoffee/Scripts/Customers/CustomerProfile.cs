using System.Collections.Generic;
using Mancoffee.Data;
using UnityEngine;

namespace Mancoffee.Customers
{
    /// <summary>
    /// Un tipo de cliente: cuándo llega, cuánto está dispuesto a pagar, cuánta paciencia tiene,
    /// si suele pedir para llevar y qué le gusta pedir.
    /// </summary>
    public class CustomerProfile
    {
        public string Id;
        public string Name;
        public Color[] Tints;

        /// <summary>Multiplica la paciencia base.</summary>
        public float Patience = 1f;

        /// <summary>Hasta qué proporción del precio de lista paga sin quejarse (1 = precio de lista).</summary>
        public float Budget = 1f;

        /// <summary>Multiplica la propina.</summary>
        public float Tip = 1f;

        /// <summary>Probabilidad de pedir para llevar en el mostrador (si hay mostrador).</summary>
        public float Takeaway;

        /// <summary>Multiplica cuánto se queda comiendo.</summary>
        public float Stay = 1f;

        /// <summary>Peso de cada producto (por id) al elegir. Los que no figuran valen 0,5.</summary>
        public Dictionary<string, float> Prefs = new();

        /// <summary>Llegadas por hora (índice = hora del día). 1 = un cliente cada ~9 s a velocidad 1x.</summary>
        public float[] Hourly = new float[25];

        public float PrefFor(ProductData product) =>
            product != null && Prefs.TryGetValue(product.id, out float w) ? w : 0.5f;

        /// <summary>Cuántos de este tipo llegan a esta hora del día (interpolado entre horas).</summary>
        public float WeightAt(float minutes)
        {
            float h = Mathf.Clamp(minutes / 60f, 0f, 23.99f);
            int a = Mathf.FloorToInt(h);
            return Mathf.Lerp(Hourly[a], Hourly[a + 1], h - a);
        }

        /// <summary>Qué tan dispuesto está a pagar un precio (1 = lo paga sin dudar, 0 = ni loco).</summary>
        public float Acceptance(float priceRatio) =>
            priceRatio <= Budget ? 1f : Mathf.Clamp01(1f - (priceRatio - Budget) * 2.5f);

        /// <summary>Qué tan contento se va con lo que pagó (más de 1 si le resultó barato).</summary>
        public float ValueFor(float priceRatio) => Mathf.Clamp(Budget / Mathf.Max(0.1f, priceRatio), 0.5f, 1.3f);

        // =========================== Tipos ===========================

        public static readonly CustomerProfile Estudiante = new()
        {
            Id = "estudiante",
            Name = "Estudiante",
            Tints = new[] { new Color(0.55f, 0.85f, 0.55f), new Color(0.5f, 0.82f, 0.8f), new Color(0.7f, 0.9f, 0.45f) },
            Patience = 1.3f,
            Budget = 0.85f,
            Tip = 0.6f,
            Takeaway = 0.1f,
            Stay = 1.6f,
            Prefs = new() { ["cafe"] = 3f, ["te"] = 2f, ["medialuna"] = 3f, ["cappuccino"] = 1f, ["torta"] = 0.5f },
            Hourly = Curve((8, 0.2f), (10, 0.6f), (12, 0.4f), (14, 0.5f), (16, 0.9f), (18, 0.7f), (20, 0.2f))
        };

        public static readonly CustomerProfile Oficinista = new()
        {
            Id = "oficinista",
            Name = "Oficinista",
            Tints = new[] { new Color(0.55f, 0.65f, 0.85f), new Color(0.7f, 0.72f, 0.78f), new Color(0.45f, 0.55f, 0.75f) },
            Patience = 0.75f,
            Budget = 1.1f,
            Tip = 1f,
            Takeaway = 0.75f,
            Stay = 0.6f,
            Prefs = new() { ["cafe"] = 4f, ["cappuccino"] = 2f, ["medialuna"] = 1.5f, ["te"] = 0.5f, ["torta"] = 0.3f },
            Hourly = Curve((7, 0f), (8, 1.6f), (9, 1.4f), (10, 0.4f), (12, 0.5f), (13, 1f), (14, 0.3f), (17, 0.5f), (19, 0.2f), (20, 0f))
        };

        public static readonly CustomerProfile Turista = new()
        {
            Id = "turista",
            Name = "Turista",
            Tints = new[] { new Color(0.95f, 0.75f, 0.45f), new Color(0.95f, 0.6f, 0.65f), new Color(0.85f, 0.6f, 0.9f) },
            Patience = 1f,
            Budget = 1.45f,
            Tip = 1.4f,
            Takeaway = 0.15f,
            Stay = 1.2f,
            Prefs = new() { ["cappuccino"] = 3f, ["torta"] = 3f, ["te"] = 1f, ["medialuna"] = 1f, ["cafe"] = 1f },
            Hourly = Curve((8, 0f), (10, 0.2f), (12, 0.5f), (15, 0.6f), (16, 1f), (18, 0.8f), (20, 0.2f))
        };

        public static readonly CustomerProfile[] All = { Estudiante, Oficinista, Turista };

        public static CustomerProfile Find(string id)
        {
            foreach (CustomerProfile p in All)
                if (p.Id == id) return p;
            return null;
        }

        /// <summary>Arma la curva por hora uniendo puntos (hora, peso) con rectas.</summary>
        private static float[] Curve(params (int hour, float weight)[] points)
        {
            var curve = new float[25];
            for (int h = 0; h <= 24; h++)
            {
                if (h <= points[0].hour) { curve[h] = h == points[0].hour ? points[0].weight : 0f; continue; }
                if (h >= points[^1].hour) { curve[h] = h == points[^1].hour ? points[^1].weight : 0f; continue; }
                for (int i = 0; i < points.Length - 1; i++)
                {
                    if (h < points[i].hour || h > points[i + 1].hour) continue;
                    float t = (h - points[i].hour) / (float)(points[i + 1].hour - points[i].hour);
                    curve[h] = Mathf.Lerp(points[i].weight, points[i + 1].weight, t);
                    break;
                }
            }
            return curve;
        }
    }
}
