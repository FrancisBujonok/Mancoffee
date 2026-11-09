using System.IO;
using Mancoffee.Building;
using Mancoffee.Customers;
using Mancoffee.GridSystem;
using Mancoffee.Staff;
using Mancoffee.UI;
using UnityEditor;
using UnityEngine;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;

namespace Mancoffee.EditorTools
{
    /// <summary>
    /// Genera los assets de la interfaz (fuentes SDF, sombra, anillo de selección, tema y PanelSettings)
    /// y agrega el objeto "Interfaz" a la escena.
    /// </summary>
    public static class MancoffeeUiSetup
    {
        public const string UiFolder = "Assets/_Mancoffee/UI";
        private const string FontFolder = UiFolder + "/Fonts";
        private const string TextureFolder = UiFolder + "/Textures";
        public const string StylePath = UiFolder + "/Mancoffee.uss";
        private const string ThemePath = UiFolder + "/MancoffeeTheme.tss";
        private const string PanelPath = UiFolder + "/MancoffeePanel.asset";
        private const string ShadowPath = TextureFolder + "/Sombra.png";
        private const string RingPath = TextureFolder + "/AnilloSeleccion.png";
        private const string ArtFolder = "Assets/_Mancoffee/Art/Placeholder";

        private static readonly string[] Fonts = { "Fredoka-SemiBold", "Nunito-Bold", "Nunito-ExtraBold", "VT323-Regular" };

        [MenuItem("Mancoffee/4. Generar assets de la interfaz")]
        public static void GenerateUiAssets()
        {
            if (!AssetDatabase.IsValidFolder(TextureFolder)) AssetDatabase.CreateFolder(UiFolder, "Textures");

            foreach (string name in Fonts) CreateFontAsset(name);
            SaveTexture(ShadowPath, DrawShadow(), false);
            SaveTexture(RingPath, DrawRing(), true);

            if (!File.Exists(ThemePath))
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath);
            }

            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelPath);
            }
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1920, 1080);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0.5f;
            panel.sortingOrder = 10;
            EditorUtility.SetDirty(panel);
            AssetDatabase.SaveAssets();
            Debug.Log("[Mancoffee] Assets de interfaz generados");
        }

        private static void CreateFontAsset(string name)
        {
            string fontPath = $"{FontFolder}/{name}.ttf";
            string assetPath = $"{FontFolder}/{name}_SDF.asset";
            var font = AssetDatabase.LoadAssetAtPath<Font>(fontPath);
            if (font == null)
            {
                Debug.LogWarning("[Mancoffee] Falta la fuente " + fontPath);
                return;
            }
            if (AssetDatabase.LoadAssetAtPath<FontAsset>(assetPath) != null) return;

            FontAsset fa = FontAsset.CreateFontAsset(font, 90, 9, GlyphRenderMode.SDFAA, 1024, 1024, AtlasPopulationMode.Dynamic, true);
            fa.name = name + "_SDF";
            AssetDatabase.CreateAsset(fa, assetPath);
            if (fa.atlasTextures != null && fa.atlasTextures.Length > 0 && fa.atlasTextures[0] != null)
            {
                fa.atlasTextures[0].name = name + " Atlas";
                AssetDatabase.AddObjectToAsset(fa.atlasTextures[0], fa);
            }
            if (fa.material != null)
            {
                fa.material.name = name + " Material";
                AssetDatabase.AddObjectToAsset(fa.material, fa);
            }
            EditorUtility.SetDirty(fa);
            AssetDatabase.SaveAssets();
        }

        private static Texture2D DrawShadow()
        {
            const int size = 96, inset = 26, radius = 14;
            const float sigma = 9f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float cx = Mathf.Clamp(px, inset + radius, size - inset - radius);
                float cy = Mathf.Clamp(py, inset + radius, size - inset - radius);
                float d = Vector2.Distance(new Vector2(px, py), new Vector2(cx, cy)) - radius;
                float a = d <= 0f ? 1f : Mathf.Exp(-(d * d) / (2f * sigma * sigma));
                tex.SetPixel(x, y, new Color(0f, 0f, 0f, a));
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D DrawRing()
        {
            const int w = 34, h = 18;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var clear = new Color(0, 0, 0, 0);
            var tomato = new Color(0.78f, 0.26f, 0.17f, 1f);
            var light = new Color(1f, 0.62f, 0.5f, 1f);
            float rx = w / 2f - 0.5f, ry = h / 2f - 0.5f;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float nx = (x + 0.5f - w / 2f) / rx, ny = (y + 0.5f - h / 2f) / ry;
                float r = Mathf.Sqrt(nx * nx + ny * ny);
                Color c = clear;
                if (r <= 1f && r >= 0.8f) c = tomato;
                else if (r < 0.8f && r >= 0.7f) c = light;
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        private static void SaveTexture(string path, Texture2D tex, bool pixelSprite)
        {
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(path);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = pixelSprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp;
            if (pixelSprite)
            {
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 32;
                importer.filterMode = FilterMode.Point;
            }
            else importer.filterMode = FilterMode.Bilinear;
            importer.SaveAndReimport();
        }

        /// <summary>Agrega el selector de empleados y la interfaz a la escena abierta.</summary>
        public static void AddInterface(Camera cam, GridManager grid, StaffManager staff, CustomerSpawner spawner, BuildModeController build)
        {
            GameObject systems = grid.gameObject;

            // Despensa y vajilla (fase 1)
            var inventory = systems.GetComponent<Mancoffee.Core.Inventory>();
            if (inventory == null) inventory = systems.AddComponent<Mancoffee.Core.Inventory>();
            var iso = new SerializedObject(inventory);
            iso.FindProperty("database").objectReferenceValue = grid.Database;
            iso.ApplyModifiedPropertiesWithoutUndo();

            if (spawner != null)
            {
                var sso = new SerializedObject(spawner);
                sso.FindProperty("maxCustomers").intValue = 14;
                sso.FindProperty("spawnInterval").vector2Value = new Vector2(7f, 14f);
                sso.ApplyModifiedPropertiesWithoutUndo();
            }
            var selection = systems.GetComponent<SelectionController>();
            if (selection == null) selection = systems.AddComponent<SelectionController>();
            var so = new SerializedObject(selection);
            so.FindProperty("grid").objectReferenceValue = grid;
            so.FindProperty("cam").objectReferenceValue = cam;
            so.FindProperty("staff").objectReferenceValue = staff;
            so.FindProperty("customers").objectReferenceValue = spawner;
            so.FindProperty("build").objectReferenceValue = build;
            so.FindProperty("ringSprite").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>(RingPath);
            so.FindProperty("orderIcon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtFolder}/Icono_pedido.png");
            so.FindProperty("billIcon").objectReferenceValue = AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtFolder}/Icono_cuenta.png");
            so.ApplyModifiedPropertiesWithoutUndo();

            GameObject ui = GameObject.Find("Interfaz");
            if (ui == null) ui = new GameObject("Interfaz");
            var doc = ui.GetComponent<UIDocument>();
            if (doc == null) doc = ui.AddComponent<UIDocument>();
            doc.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelPath);
            var hud = ui.GetComponent<MancoffeeHud>();
            if (hud == null) hud = ui.AddComponent<MancoffeeHud>();
            var hso = new SerializedObject(hud);
            hso.FindProperty("document").objectReferenceValue = doc;
            hso.FindProperty("styleSheet").objectReferenceValue = AssetDatabase.LoadAssetAtPath<StyleSheet>(StylePath);
            hso.FindProperty("selection").objectReferenceValue = selection;
            hso.FindProperty("worldCamera").objectReferenceValue = cam;
            hso.ApplyModifiedPropertiesWithoutUndo();

            // Entrada del mouse/teclado para la interfaz con el Input System
            if (Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
                System.Type module = System.Type.GetType("UnityEngine.InputSystem.UI.InputSystemUIInputModule, Unity.InputSystem");
                if (module != null) es.AddComponent(module);
            }
        }

        [MenuItem("Mancoffee/5. Agregar interfaz a la escena abierta")]
        public static void AddInterfaceToOpenScene()
        {
            var grid = Object.FindFirstObjectByType<GridManager>();
            if (grid == null)
            {
                Debug.LogError("[Mancoffee] No encontré la escena del juego (falta GridManager)");
                return;
            }
            AddInterface(Camera.main, grid, Object.FindFirstObjectByType<StaffManager>(),
                Object.FindFirstObjectByType<CustomerSpawner>(), Object.FindFirstObjectByType<BuildModeController>());
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(grid.gameObject.scene);
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(grid.gameObject.scene);
        }
    }
}
