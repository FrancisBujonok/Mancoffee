using System.Collections.Generic;
using System.IO;
using Mancoffee.Building;
using Mancoffee.CameraSystem;
using Mancoffee.Core;
using Mancoffee.Customers;
using Mancoffee.Data;
using Mancoffee.GridSystem;
using Mancoffee.Staff;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Tilemaps;

namespace Mancoffee.EditorTools
{
    /// <summary>
    /// Genera el arte provisional, los datos (muebles, productos, suelos), los prefabs y la escena.
    /// Se puede volver a ejecutar: reemplaza lo que generó antes.
    /// Menú: Mancoffee > ...
    /// </summary>
    public static class MancoffeeSetup
    {
        private const int PPU = 32;            // 32 px = 1 unidad. Un tile de suelo mide 32x16 px.
        private const int WallHeight = 40;
        private const int LowWallHeight = 6;
        private const string Root = "Assets/_Mancoffee";
        private const string ArtFolder = Root + "/Art/Placeholder";
        private const string FurnitureFolder = Root + "/Data/Muebles";
        private const string ProductFolder = Root + "/Data/Productos";
        private const string FloorFolder = Root + "/Data/Suelos";
        private const string DatabasePath = Root + "/Data/BaseDeDatos.asset";
        private const string TileFolder = Root + "/Tiles";
        private const string PrefabFolder = Root + "/Prefabs";
        private const string ScenePath = Root + "/Scenes/Cafeteria.unity";
        private const string CustomerPrefabPath = PrefabFolder + "/Cliente.prefab";
        private const string EmployeePrefabPath = PrefabFolder + "/Empleado.prefab";
        private const int FloorWidth = 12;
        private const int FloorDepth = 10;
        private static readonly Vector2Int EntranceCell = new(0, 4);

        // ------------------------------------------------------------------ especificaciones

        private struct FurnitureSpec
        {
            public string id, name;
            public FurnitureCategory category;
            public int w, d, height, price;
            public Color color;
        }

        private static readonly FurnitureSpec[] FurnitureSpecs =
        {
            new() { id = "mesa", name = "Mesa", category = FurnitureCategory.Mesa, w = 1, d = 1, height = 12, price = 40, color = new Color(0.58f, 0.38f, 0.22f) },
            new() { id = "silla", name = "Silla", category = FurnitureCategory.Asiento, w = 1, d = 1, height = 8, price = 15, color = new Color(0.78f, 0.58f, 0.36f) },
            new() { id = "mesa_larga", name = "Mesa larga", category = FurnitureCategory.Mesa, w = 2, d = 1, height = 12, price = 70, color = new Color(0.5f, 0.32f, 0.18f) },
            new() { id = "cafetera", name = "Máquina de café", category = FurnitureCategory.Equipo, w = 1, d = 1, height = 22, price = 250, color = new Color(0.62f, 0.2f, 0.2f) },
            new() { id = "vitrina", name = "Vitrina de pastelería", category = FurnitureCategory.Equipo, w = 1, d = 1, height = 16, price = 180, color = new Color(0.72f, 0.84f, 0.92f) },
            new() { id = "mostrador", name = "Mostrador", category = FurnitureCategory.Mostrador, w = 2, d = 1, height = 14, price = 120, color = new Color(0.36f, 0.44f, 0.58f) },
            new() { id = "planta", name = "Planta", category = FurnitureCategory.Decoracion, w = 1, d = 1, height = 18, price = 25, color = new Color(0.3f, 0.62f, 0.32f) },
        };

        private struct FloorSpec
        {
            public string id, name;
            public Color a, b;
            public int price;
        }

        private static readonly FloorSpec[] FloorSpecs =
        {
            new() { id = "baldosa", name = "Baldosa", a = new Color(0.86f, 0.78f, 0.66f), b = new Color(0.8f, 0.71f, 0.58f), price = 5 },
            new() { id = "madera", name = "Madera", a = new Color(0.66f, 0.47f, 0.3f), b = new Color(0.6f, 0.42f, 0.27f), price = 8 },
            new() { id = "alfombra", name = "Alfombra", a = new Color(0.66f, 0.24f, 0.27f), b = new Color(0.6f, 0.21f, 0.24f), price = 10 },
            new() { id = "cemento", name = "Cemento", a = new Color(0.66f, 0.66f, 0.68f), b = new Color(0.6f, 0.6f, 0.62f), price = 3 },
        };

        private struct ProductSpec
        {
            public string id, name, station, icon;
            public int price, cost;
            public float prep;
            public Color liquid;
        }

        private static readonly ProductSpec[] ProductSpecs =
        {
            new() { id = "cafe", name = "Café", station = "cafetera", icon = "cup", price = 6, cost = 2, prep = 4f, liquid = new Color(0.4f, 0.24f, 0.14f) },
            new() { id = "cappuccino", name = "Cappuccino", station = "cafetera", icon = "cup", price = 9, cost = 3, prep = 6f, liquid = new Color(0.88f, 0.76f, 0.6f) },
            new() { id = "te", name = "Té", station = "cafetera", icon = "cup", price = 5, cost = 1, prep = 3f, liquid = new Color(0.55f, 0.75f, 0.35f) },
            new() { id = "medialuna", name = "Medialuna", station = "vitrina", icon = "croissant", price = 4, cost = 1, prep = 2f },
            new() { id = "torta", name = "Porción de torta", station = "vitrina", icon = "cake", price = 11, cost = 4, prep = 3f },
        };

        // ------------------------------------------------------------------ menú

        [MenuItem("Mancoffee/Setup completo (placeholders + escena)")]
        public static void FullSetup()
        {
            EnsureFolders();
            GenerateContent();
            BuildPrefabs();
            MancoffeeUiSetup.GenerateUiAssets();
            BuildScene();
        }

        [MenuItem("Mancoffee/1. Generar arte y datos")]
        public static void GenerateContent()
        {
            EnsureFolders();
            Outline = new Color(0.22f, 0.2f, 0.22f);

            // Suelos
            var floorTypes = new List<FloorTypeData>();
            foreach (FloorSpec spec in FloorSpecs)
            {
                Tile a = CreateTile($"Suelo_{spec.id}_A", DrawFloorTile(spec.a));
                Tile b = CreateTile($"Suelo_{spec.id}_B", DrawFloorTile(spec.b));
                var data = LoadOrCreate<FloorTypeData>($"{FloorFolder}/{spec.id}.asset");
                data.id = spec.id;
                data.displayName = spec.name;
                data.tileA = a;
                data.tileB = b;
                data.price = spec.price;
                EditorUtility.SetDirty(data);
                floorTypes.Add(data);
            }
            CreateTile("Entrada", DrawFloorTile(new Color(0.45f, 0.62f, 0.5f)));

            // Varios
            SaveSprite($"{ArtFolder}/CursorCelda.png", DrawCursorTile(), new Vector2(0.5f, 0.5f));
            SaveSprite($"{ArtFolder}/Cliente.png", DrawPerson(false), new Vector2(0.5f, 0f));
            SaveSprite($"{ArtFolder}/Empleado.png", DrawPerson(true), new Vector2(0.5f, 0f));
            SaveSprite($"{ArtFolder}/Pixel.png", DrawPixel(), new Vector2(0f, 0.5f));
            SaveSprite($"{ArtFolder}/Burbuja.png", DrawPattern(BubblePattern, Palette()), new Vector2(0.5f, 0f));
            SaveSprite($"{ArtFolder}/Icono_pedido.png", DrawPattern(ExclamationPattern, Palette()), new Vector2(0.5f, 0.5f));
            SaveSprite($"{ArtFolder}/Icono_cuenta.png", DrawPattern(DollarPattern, Palette()), new Vector2(0.5f, 0.5f));

            // Paredes: X = borde superior derecho (el extremo bajo queda a la derecha), Y = superior izquierdo.
            foreach (bool axisX in new[] { true, false })
            {
                string s = axisX ? "x" : "y";
                var pivot = new Vector2(axisX ? 1f : 0f, 0f);
                SaveSprite($"{ArtFolder}/Pared_{s}.png", DrawWall(axisX, WallHeight, WallKind.Pared), pivot);
                SaveSprite($"{ArtFolder}/Pared_baja_{s}.png", DrawWall(axisX, LowWallHeight, WallKind.Pared), pivot);
                SaveSprite($"{ArtFolder}/Puerta_{s}.png", DrawWall(axisX, WallHeight, WallKind.Puerta), pivot);
                SaveSprite($"{ArtFolder}/Puerta_baja_{s}.png", DrawWall(axisX, LowWallHeight, WallKind.Puerta), pivot);
                SaveSprite($"{ArtFolder}/Ventana_{s}.png", DrawWall(axisX, WallHeight, WallKind.Ventana), pivot);
            }

            // Muebles
            var furniture = new List<FurnitureData>();
            foreach (FurnitureSpec spec in FurnitureSpecs)
            {
                var sprites = new List<Sprite> { SaveBoxSprite($"{spec.id}_r0", spec.w, spec.d, spec.height, spec.color) };
                if (spec.w != spec.d) sprites.Add(SaveBoxSprite($"{spec.id}_r1", spec.d, spec.w, spec.height, spec.color));

                var data = LoadOrCreate<FurnitureData>($"{FurnitureFolder}/{spec.id}.asset");
                data.id = spec.id;
                data.displayName = spec.name;
                data.category = spec.category;
                data.size = new Vector2Int(spec.w, spec.d);
                data.price = spec.price;
                data.sprites = sprites.ToArray();
                EditorUtility.SetDirty(data);
                furniture.Add(data);
            }

            // Productos
            var products = new List<ProductData>();
            foreach (ProductSpec spec in ProductSpecs)
            {
                Dictionary<char, Color> palette = Palette();
                palette['l'] = spec.liquid;
                string[] pattern = spec.icon switch
                {
                    "croissant" => CroissantPattern,
                    "cake" => CakePattern,
                    _ => CupPattern
                };
                Sprite icon = SaveSprite($"{ArtFolder}/Icono_{spec.id}.png", DrawPattern(pattern, palette), new Vector2(0.5f, 0.5f));

                var data = LoadOrCreate<ProductData>($"{ProductFolder}/{spec.id}.asset");
                data.id = spec.id;
                data.displayName = spec.name;
                data.station = furniture.Find(f => f.id == spec.station);
                data.price = spec.price;
                data.ingredientCost = spec.cost;
                data.prepTime = spec.prep;
                data.icon = icon;
                EditorUtility.SetDirty(data);
                products.Add(data);
            }

            // Base de datos
            var db = LoadOrCreate<GameDatabase>(DatabasePath);
            db.furniture = furniture;
            db.products = products;
            db.floorTypes = floorTypes;
            EditorUtility.SetDirty(db);

            // Restos de versiones anteriores del setup.
            foreach (string old in new[] { $"{ArtFolder}/Suelo_A.png", $"{ArtFolder}/Suelo_B.png", $"{TileFolder}/Suelo_A.asset", $"{TileFolder}/Suelo_B.asset" })
                if (AssetDatabase.LoadMainAssetAtPath(old) != null) AssetDatabase.DeleteAsset(old);

            AssetDatabase.SaveAssets();
            Debug.Log("[Mancoffee] Arte y datos generados.");
        }

        // ------------------------------------------------------------------ prefabs

        [MenuItem("Mancoffee/2. Crear prefabs")]
        public static void BuildPrefabs()
        {
            const float px = 1f / PPU;
            Sprite pixel = LoadSprite("Pixel");

            // ----- Cliente -----
            var customerRoot = new GameObject("Cliente");
            var customer = customerRoot.AddComponent<Customer>();
            SpriteRenderer customerVisual = AddSprite("Visual", customerRoot.transform, LoadSprite("Cliente"), Vector3.zero, 0);
            Transform patienceBar = AddBar(customerVisual.transform, pixel, 25 * px, out SpriteRenderer patienceFill);
            SpriteRenderer bubble = AddSprite("Burbuja", customerVisual.transform, LoadSprite("Burbuja"), new Vector3(7 * px, 28 * px, 0f), 20);
            SpriteRenderer bubbleIcon = AddSprite("Icono", bubble.transform, null, new Vector3(0f, 7 * px, 0f), 21);
            Wire(customer,
                ("visual", customerVisual), ("patienceBar", patienceBar), ("patienceFill", patienceFill),
                ("bubble", bubble), ("bubbleIcon", bubbleIcon),
                ("orderIcon", LoadSprite("Icono_pedido")), ("billIcon", LoadSprite("Icono_cuenta")));
            PrefabUtility.SaveAsPrefabAsset(customerRoot, CustomerPrefabPath);
            Object.DestroyImmediate(customerRoot);

            // ----- Empleado -----
            var employeeRoot = new GameObject("Empleado");
            var employee = employeeRoot.AddComponent<Employee>();
            SpriteRenderer employeeVisual = AddSprite("Visual", employeeRoot.transform, LoadSprite("Empleado"), Vector3.zero, 0);
            Transform progressBar = AddBar(employeeVisual.transform, pixel, 25 * px, out SpriteRenderer progressFill);
            progressFill.color = new Color(0.95f, 0.8f, 0.3f);
            SpriteRenderer carried = AddSprite("Lleva", employeeVisual.transform, null, new Vector3(6 * px, 12 * px, 0f), 15);
            Wire(employee, ("visual", employeeVisual), ("progressBar", progressBar), ("progressFill", progressFill), ("carriedIcon", carried));
            PrefabUtility.SaveAsPrefabAsset(employeeRoot, EmployeePrefabPath);
            Object.DestroyImmediate(employeeRoot);

            Debug.Log("[Mancoffee] Prefabs creados.");
        }

        private static SpriteRenderer AddSprite(string name, Transform parent, Sprite sprite, Vector3 localPosition, int order)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.sortingOrder = order;
            return sr;
        }

        /// <summary>Barra de 12 px con fondo, encima de la cabeza.</summary>
        private static Transform AddBar(Transform parent, Sprite pixel, float height, out SpriteRenderer fill)
        {
            const float px = 1f / PPU;
            var bar = new GameObject("Barra");
            bar.transform.SetParent(parent, false);
            bar.transform.localPosition = new Vector3(0f, height, 0f);

            SpriteRenderer bg = AddSprite("Fondo", bar.transform, pixel, new Vector3(-7 * px, 0f, 0f), 10);
            bg.transform.localScale = new Vector3(14f, 4f, 1f);
            bg.color = new Color(0.1f, 0.1f, 0.12f);

            fill = AddSprite("Relleno", bar.transform, pixel, new Vector3(-6 * px, 0f, 0f), 11);
            fill.transform.localScale = new Vector3(12f, 2f, 1f);
            return bar.transform;
        }

        // ------------------------------------------------------------------ escena

        [MenuItem("Mancoffee/3. Armar escena")]
        public static void BuildScene()
        {
            if (!File.Exists(ScenePath))
                AssetDatabase.CopyAsset("Assets/Settings/Scenes/URP2DSceneTemplate.unity", ScenePath);
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            foreach (string n in new[] { "Grid", "Sistemas", "CursorCelda", "Clientes", "Personal", "Paredes" })
            {
                GameObject old = GameObject.Find(n);
                if (old != null) Object.DestroyImmediate(old);
            }

            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);

            // ----- Grilla isométrica y suelo inicial -----
            var gridGo = new GameObject("Grid");
            var grid = gridGo.AddComponent<Grid>();
            grid.cellLayout = GridLayout.CellLayout.Isometric;
            grid.cellSize = new Vector3(1f, 0.5f, 1f);

            var floorGo = new GameObject("Suelo");
            floorGo.transform.SetParent(gridGo.transform, false);
            var floor = floorGo.AddComponent<Tilemap>();
            floorGo.AddComponent<TilemapRenderer>().sortingOrder = -100;

            FloorTypeData defaultFloor = db.DefaultFloor;
            for (int x = 0; x < FloorWidth; x++)
            for (int y = 0; y < FloorDepth; y++)
            {
                var cell = new Vector2Int(x, y);
                floor.SetTile((Vector3Int)cell, defaultFloor.GetTile(cell));
            }

            var furnitureGo = new GameObject("Muebles");
            furnitureGo.transform.SetParent(gridGo.transform, false);
            var wallsGo = new GameObject("Paredes");
            var customersGo = new GameObject("Clientes");
            var staffGo = new GameObject("Personal");

            var cursorGo = new GameObject("CursorCelda");
            var cursor = cursorGo.AddComponent<SpriteRenderer>();
            cursor.sprite = LoadSprite("CursorCelda");
            cursor.sortingOrder = -50;
            cursor.enabled = false;

            // ----- Cámara -----
            Camera cam = Camera.main;
            if (cam == null)
            {
                var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
                cam = camGo.AddComponent<Camera>();
            }
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.12f, 0.11f, 0.14f);
            Vector3 center = grid.CellToLocalInterpolated(new Vector3(FloorWidth / 2f, FloorDepth / 2f, 0));
            cam.transform.position = new Vector3(center.x, center.y + 0.5f, -10f);

            var ppc = cam.GetComponent<PixelPerfectCamera>();
            if (ppc == null) ppc = cam.gameObject.AddComponent<PixelPerfectCamera>();
            ppc.assetsPPU = PPU;
            ppc.refResolutionX = 640;
            ppc.refResolutionY = 360;
            if (cam.GetComponent<CameraController>() == null) cam.gameObject.AddComponent<CameraController>();

            // ----- Sistemas -----
            var systems = new GameObject("Sistemas");
            var gridManager = systems.AddComponent<GridManager>();
            var cafe = systems.AddComponent<CafeState>();
            var tasks = systems.AddComponent<TaskManager>();
            var staff = systems.AddComponent<StaffManager>();
            var spawner = systems.AddComponent<CustomerSpawner>();
            var wallRenderer = systems.AddComponent<WallRenderer>();
            var build = systems.AddComponent<BuildModeController>();
            var save = systems.AddComponent<SaveSystem>();
            var dayCycle = systems.AddComponent<DayCycle>();
            var bootstrap = systems.AddComponent<GameBootstrap>();

            Wire(gridManager, ("grid", grid), ("floorTilemap", floor), ("database", db),
                 ("entranceTile", AssetDatabase.LoadAssetAtPath<Tile>($"{TileFolder}/Entrada.asset")));
            var gmSo = new SerializedObject(gridManager);
            gmSo.FindProperty("entranceCell").vector2IntValue = EntranceCell;
            gmSo.ApplyModifiedPropertiesWithoutUndo();

            Wire(staff, ("grid", gridManager), ("employeePrefab", AssetDatabase.LoadAssetAtPath<Employee>(EmployeePrefabPath)),
                 ("staffParent", staffGo.transform));
            Wire(spawner, ("grid", gridManager), ("customerPrefab", AssetDatabase.LoadAssetAtPath<Customer>(CustomerPrefabPath)),
                 ("customersParent", customersGo.transform));
            Wire(wallRenderer, ("grid", gridManager), ("wallsParent", wallsGo.transform),
                 ("wallX", LoadSprite("Pared_x")), ("wallY", LoadSprite("Pared_y")),
                 ("lowX", LoadSprite("Pared_baja_x")), ("lowY", LoadSprite("Pared_baja_y")),
                 ("doorX", LoadSprite("Puerta_x")), ("doorY", LoadSprite("Puerta_y")),
                 ("lowDoorX", LoadSprite("Puerta_baja_x")), ("lowDoorY", LoadSprite("Puerta_baja_y")),
                 ("windowX", LoadSprite("Ventana_x")), ("windowY", LoadSprite("Ventana_y")));
            Wire(build, ("gridManager", gridManager), ("cam", cam), ("database", db), ("furnitureParent", furnitureGo.transform),
                 ("cellCursor", cursor), ("wallRenderer", wallRenderer));
            Wire(save, ("grid", gridManager), ("staff", staff), ("spawner", spawner), ("wallRenderer", wallRenderer),
                 ("furnitureParent", furnitureGo.transform));
            Wire(dayCycle, ("spawner", spawner), ("staff", staff), ("saveSystem", save));
            Wire(bootstrap, ("grid", gridManager), ("staff", staff), ("saveSystem", save), ("dayCycle", dayCycle));

            MancoffeeUiSetup.AddInterface(cam, gridManager, staff, spawner, build);

            EditorSceneManager.MarkSceneDirty(gridGo.scene);
            EditorSceneManager.SaveScene(gridGo.scene);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };

            Debug.Log("[Mancoffee] Escena armada en " + ScenePath);
        }

        // ------------------------------------------------------------------ utilidades de assets

        private static void EnsureFolders()
        {
            foreach (string folder in new[] { ArtFolder, FurnitureFolder, ProductFolder, FloorFolder, TileFolder, PrefabFolder, Root + "/Scenes" })
            {
                if (AssetDatabase.IsValidFolder(folder)) continue;
                string parent = Path.GetDirectoryName(folder)!.Replace("\\", "/");
                AssetDatabase.CreateFolder(parent, Path.GetFileName(folder));
            }
        }

        private static T LoadOrCreate<T>(string path) where T : ScriptableObject
        {
            var asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset != null) return asset;
            asset = ScriptableObject.CreateInstance<T>();
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Sprite LoadSprite(string name) => AssetDatabase.LoadAssetAtPath<Sprite>($"{ArtFolder}/{name}.png");

        private static void Wire(Object target, params (string property, Object value)[] references)
        {
            var so = new SerializedObject(target);
            foreach ((string property, Object value) in references)
            {
                SerializedProperty prop = so.FindProperty(property);
                if (prop == null)
                {
                    Debug.LogError($"[Mancoffee] {target.GetType().Name} no tiene el campo '{property}'");
                    continue;
                }
                prop.objectReferenceValue = value;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Tile CreateTile(string name, Texture2D texture)
        {
            Sprite sprite = SaveSprite($"{ArtFolder}/{name}.png", texture, new Vector2(0.5f, 0.5f));
            var tile = LoadOrCreate<Tile>($"{TileFolder}/{name}.asset");
            tile.sprite = sprite;
            EditorUtility.SetDirty(tile);
            return tile;
        }

        private static Sprite SaveBoxSprite(string fileName, int w, int d, int height, Color baseColor)
        {
            Texture2D tex = DrawBox(w, d, height, baseColor);
            // El pivote es la punta inferior del rombo de apoyo.
            return SaveSprite($"{ArtFolder}/{fileName}.png", tex, new Vector2(16f * d / tex.width, 0f));
        }

        private static Sprite SaveSprite(string assetPath, Texture2D tex, Vector2 pivot)
        {
            string fullPath = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, assetPath);
            File.WriteAllBytes(fullPath, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);

            var importer = (TextureImporter)AssetImporter.GetAtPath(assetPath);
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PPU;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;

            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();

            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        // ------------------------------------------------------------------ dibujo procedural

        private static Color Outline = new(0.22f, 0.2f, 0.22f);

        private static Texture2D NewTexture(int w, int h)
        {
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            tex.SetPixels32(new Color32[w * h]);
            return tex;
        }

        private static Vector2[] Diamond(Vector2 bottom, int w, int d, float lift)
        {
            var up = new Vector2(0, lift);
            return new[]
            {
                bottom + up,
                bottom + new Vector2(16 * w, 8 * w) + up,
                bottom + new Vector2(16 * w - 16 * d, 8 * w + 8 * d) + up,
                bottom + new Vector2(-16 * d, 8 * d) + up
            };
        }

        private static bool Inside(Vector2 p, Vector2[] poly)
        {
            bool inside = false;
            for (int i = 0, j = poly.Length - 1; i < poly.Length; j = i++)
            {
                if ((poly[i].y > p.y) != (poly[j].y > p.y) &&
                    p.x < (poly[j].x - poly[i].x) * (p.y - poly[i].y) / (poly[j].y - poly[i].y) + poly[i].x)
                    inside = !inside;
            }
            return inside;
        }

        private static Color Shade(Color c, float f) =>
            new(Mathf.Clamp01(c.r * f), Mathf.Clamp01(c.g * f), Mathf.Clamp01(c.b * f), 1f);

        private static Texture2D DrawFloorTile(Color color)
        {
            var tex = NewTexture(32, 16);
            Vector2[] poly = Diamond(new Vector2(16, 0), 1, 1, 0);
            Color edge = Shade(color, 0.88f);
            for (int x = 0; x < 32; x++)
            for (int y = 0; y < 16; y++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                if (!Inside(p, poly)) continue;
                // Solo se oscurece el borde superior: así cada línea entre celdas se dibuja una vez.
                tex.SetPixel(x, y, Inside(p + Vector2.up, poly) ? color : edge);
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D DrawCursorTile()
        {
            var tex = NewTexture(32, 16);
            Vector2[] poly = Diamond(new Vector2(16, 0), 1, 1, 0);
            for (int x = 0; x < 32; x++)
            for (int y = 0; y < 16; y++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                if (!Inside(p, poly)) continue;
                bool border = !Inside(p + Vector2.up, poly) || !Inside(p + Vector2.down, poly) ||
                              !Inside(p + Vector2.left, poly) || !Inside(p + Vector2.right, poly);
                tex.SetPixel(x, y, border ? Color.white : new Color(1, 1, 1, 0.18f));
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D DrawBox(int w, int d, int height, Color baseColor)
        {
            int width = 16 * (w + d);
            int texHeight = 8 * (w + d) + height;
            var tex = NewTexture(width, texHeight);

            var bottom = new Vector2(16 * d, 0);
            Vector2[] floor = Diamond(bottom, w, d, 0);
            Vector2[] top = Diamond(bottom, w, d, height);
            Vector2[] leftFace = { floor[3], floor[0], top[0], top[3] };
            Vector2[] rightFace = { floor[0], floor[1], top[1], top[0] };

            Color topC = Shade(baseColor, 1.15f), leftC = Shade(baseColor, 0.85f),
                  rightC = Shade(baseColor, 0.65f), outline = Shade(baseColor, 0.35f);

            bool Solid(Vector2 p) => Inside(p, top) || Inside(p, leftFace) || Inside(p, rightFace);

            for (int x = 0; x < width; x++)
            for (int y = 0; y < texHeight; y++)
            {
                var p = new Vector2(x + 0.5f, y + 0.5f);
                Color c;
                if (Inside(p, top)) c = topC;
                else if (Inside(p, leftFace)) c = leftC;
                else if (Inside(p, rightFace)) c = rightC;
                else continue;

                bool border = !Solid(p + Vector2.up) || !Solid(p + Vector2.down) ||
                              !Solid(p + Vector2.left) || !Solid(p + Vector2.right);
                tex.SetPixel(x, y, border ? outline : c);
            }
            tex.Apply();
            return tex;
        }

        /// <summary>Figura en escala de grises (el color lo pone el tinte). El empleado lleva delantal y gorra.</summary>
        private static Texture2D DrawPerson(bool employee)
        {
            const int w = 12, h = 22;
            var tex = NewTexture(w, h);

            bool Head(int x, int y) => (new Vector2(x + 0.5f, y + 0.5f) - new Vector2(6f, 17.5f)).sqrMagnitude <= 3.6f * 3.6f;
            bool Body(int x, int y) => x >= 2 && x <= 9 && y >= 4 && y <= 13 && !((x == 2 || x == 9) && y == 13);
            bool Legs(int x, int y) => y >= 0 && y <= 3 && (x == 3 || x == 4 || x == 7 || x == 8);
            bool Solid(int x, int y) => x >= 0 && y >= 0 && x < w && y < h && (Head(x, y) || Body(x, y) || Legs(x, y));

            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (!Solid(x, y)) continue;
                bool border = !Solid(x + 1, y) || !Solid(x - 1, y) || !Solid(x, y + 1) || !Solid(x, y - 1);
                Color c;
                if (border) c = Outline;
                else if (Head(x, y)) c = employee && y >= 19 ? new Color(0.3f, 0.3f, 0.32f) : new Color(1f, 0.95f, 0.9f);
                else if (Legs(x, y)) c = new Color(0.5f, 0.5f, 0.55f);
                else if (employee) c = x >= 4 && x <= 7 && y <= 11 ? Color.white : new Color(0.55f, 0.55f, 0.58f);
                else c = Color.white;
                tex.SetPixel(x, y, c);
            }
            tex.Apply();
            return tex;
        }

        /// <summary>
        /// Panel de pared parado sobre un borde. Se dibuja en la orientación Y (sube hacia la derecha)
        /// y se espeja para la X.
        /// </summary>
        private static Texture2D DrawWall(bool axisX, int height, WallKind kind)
        {
            const int w = 16;
            int h = 8 + height;
            var tex = NewTexture(w, h);

            Color face = axisX ? new Color(0.78f, 0.73f, 0.66f) : new Color(0.9f, 0.86f, 0.78f);
            Color cap = new(0.97f, 0.94f, 0.88f);
            Color outline = new(0.4f, 0.35f, 0.3f);
            Color frame = new(0.45f, 0.3f, 0.2f);
            Color glass = new(0.62f, 0.84f, 0.95f);
            bool tall = height > 10;
            float doorTop = tall ? height * 0.75f : height + 1f;

            bool InFace(int x, int y)
            {
                if (x < 0 || x >= w || y < 0 || y >= h) return false;
                float hb = (y + 0.5f) - (x + 0.5f) * 0.5f;
                return hb >= 0f && hb <= height;
            }

            bool IsOpening(int x, int y)
            {
                if (kind != WallKind.Puerta) return false;
                float t = (x + 0.5f) / w, hb = (y + 0.5f) - (x + 0.5f) * 0.5f;
                return t > 0.22f && t < 0.78f && hb < doorTop;
            }

            bool Solid(int x, int y) => InFace(x, y) && !IsOpening(x, y);

            for (int x = 0; x < w; x++)
            for (int y = 0; y < h; y++)
            {
                if (!Solid(x, y)) continue;
                float t = (x + 0.5f) / w;
                float hb = (y + 0.5f) - (x + 0.5f) * 0.5f;

                Color c = hb > height - 2 ? cap : face;
                if (kind == WallKind.Puerta && t > 0.16f && t < 0.84f && hb < doorTop + 2f) c = frame;
                if (kind == WallKind.Ventana && tall)
                {
                    bool inGlass = t > 0.22f && t < 0.78f && hb > height * 0.35f && hb < height * 0.75f;
                    bool inFrame = t > 0.16f && t < 0.84f && hb > height * 0.35f - 2f && hb < height * 0.75f + 2f;
                    if (inGlass) c = glass;
                    else if (inFrame) c = frame;
                }
                bool border = !Solid(x - 1, y) || !Solid(x + 1, y) || !Solid(x, y - 1) || !Solid(x, y + 1);
                if (border) c = outline;

                tex.SetPixel(axisX ? w - 1 - x : x, y, c);
            }
            tex.Apply();
            return tex;
        }

        private static Texture2D DrawPixel()
        {
            var tex = NewTexture(1, 1);
            tex.SetPixel(0, 0, Color.white);
            tex.Apply();
            return tex;
        }

        // ------------------------------------------------------------------ íconos por patrón

        private static Dictionary<char, Color> Palette() => new()
        {
            ['o'] = new Color(0.22f, 0.2f, 0.22f),
            ['w'] = Color.white,
            ['l'] = new Color(0.4f, 0.24f, 0.14f),
            ['y'] = new Color(0.93f, 0.72f, 0.32f),
            ['p'] = new Color(0.95f, 0.6f, 0.72f),
            ['c'] = new Color(0.96f, 0.88f, 0.7f),
            ['r'] = new Color(0.85f, 0.2f, 0.2f),
            ['g'] = new Color(0.2f, 0.62f, 0.3f),
        };

        /// <summary>Dibuja un patrón de texto: la primera fila es la de arriba, '.' es transparente.</summary>
        private static Texture2D DrawPattern(string[] rows, Dictionary<char, Color> palette)
        {
            int h = rows.Length, w = rows[0].Length;
            var tex = NewTexture(w, h);
            for (int r = 0; r < h; r++)
            for (int x = 0; x < w; x++)
                if (palette.TryGetValue(rows[r][x], out Color c)) tex.SetPixel(x, h - 1 - r, c);
            tex.Apply();
            return tex;
        }

        private static readonly string[] BubblePattern =
        {
            "..ooooooooo..",
            ".owwwwwwwwwo.",
            "owwwwwwwwwwwo",
            "owwwwwwwwwwwo",
            "owwwwwwwwwwwo",
            "owwwwwwwwwwwo",
            "owwwwwwwwwwwo",
            "owwwwwwwwwwwo",
            ".owwwwwwwwwo.",
            "..oowoooooo..",
            "...owo.......",
            "...oo........"
        };

        private static readonly string[] ExclamationPattern =
        {
            "....r....",
            "...rrr...",
            "...rrr...",
            "...rrr...",
            "....r....",
            "....r....",
            ".........",
            "....r....",
            "........."
        };

        private static readonly string[] DollarPattern =
        {
            "....g....",
            "..ggggg..",
            ".gg.g....",
            "..ggg....",
            "....ggg..",
            "....g.gg.",
            "..ggggg..",
            "....g....",
            "........."
        };

        private static readonly string[] CupPattern =
        {
            ".........",
            ".oooooo..",
            ".ollllo..",
            ".owwwwooo",
            ".owwwwo.o",
            ".owwwwooo",
            "..owwo...",
            "...oo....",
            "........."
        };

        private static readonly string[] CroissantPattern =
        {
            ".........",
            "...ooo...",
            "..oyyyo..",
            ".oyyoyyo.",
            "oyyo.oyyo",
            "oyo...oyo",
            ".o.....o.",
            ".........",
            "........."
        };

        private static readonly string[] CakePattern =
        {
            ".........",
            "....oo...",
            "...oppo..",
            "..oppppo.",
            ".occcccco",
            ".occcccco",
            ".occcccco",
            ".oooooooo",
            "........."
        };
    }
}
