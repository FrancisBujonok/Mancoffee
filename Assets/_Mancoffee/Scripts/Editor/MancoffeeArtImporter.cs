using System;
using System.Collections.Generic;
using System.IO;
using Mancoffee.Data;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Mancoffee.EditorTools
{
    /// <summary>
    /// Aplica el arte dibujado (estilo A) sobre los datos del juego: muebles con sus estados y animaciones,
    /// pisos e íconos. Lee Art/EstiloA/manifest.json, que dice el pivote de cada PNG y qué va en cada mueble.
    /// Se puede ejecutar las veces que haga falta; "1. Generar arte y datos" también lo llama al final.
    /// </summary>
    public static class MancoffeeArtImporter
    {
        private const int PPU = 32;
        private const string Root = "Assets/_Mancoffee";
        public const string Folder = Root + "/Art/EstiloA";
        private const string TileFolder = Root + "/Tiles";
        private const string DatabasePath = Root + "/Data/BaseDeDatos.asset";

        [Serializable] private class FileEntry { public string name; public float px; public float py; }

        [Serializable]
        private class FurnitureEntry
        {
            public string id;
            public string[] sprites, dirty, idle, work, alert;
            public float idleFps, workFps, alertFps;
        }

        [Serializable] private class FloorEntry { public string id, a, b; }
        [Serializable] private class IconEntry { public string dirtyCup, dirtyPlate, steam; }

        [Serializable]
        private class Manifest
        {
            public FileEntry[] files;
            public FurnitureEntry[] furniture;
            public FloorEntry[] floors;
            public IconEntry icons;
        }

        [MenuItem("Mancoffee/6. Importar arte estilo A")]
        public static void Import()
        {
            if (!ApplyIfPresent())
                Debug.LogWarning($"[Mancoffee] No encontré {Folder}/manifest.json: no hay arte para importar.");
        }

        /// <summary>Devuelve falso si no hay arte en la carpeta.</summary>
        public static bool ApplyIfPresent()
        {
            string manifestPath = Path.Combine(Directory.GetParent(Application.dataPath)!.FullName, Folder, "manifest.json");
            if (!File.Exists(manifestPath)) return false;

            AssetDatabase.Refresh();
            var manifest = JsonUtility.FromJson<Manifest>(File.ReadAllText(manifestPath));
            var sprites = new Dictionary<string, Sprite>();

            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (FileEntry f in manifest.files) ConfigureImporter($"{Folder}/{f.name}.png", new Vector2(f.px, f.py));
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            foreach (FileEntry f in manifest.files)
                sprites[f.name] = AssetDatabase.LoadAssetAtPath<Sprite>($"{Folder}/{f.name}.png");

            Sprite[] List(string[] names)
            {
                if (names == null) return new Sprite[0];
                var result = new List<Sprite>();
                foreach (string n in names)
                    if (sprites.TryGetValue(n, out Sprite s) && s != null) result.Add(s);
                return result.ToArray();
            }

            // Muebles
            var byId = new Dictionary<string, FurnitureData>();
            foreach (string guid in AssetDatabase.FindAssets("t:FurnitureData"))
            {
                var data = AssetDatabase.LoadAssetAtPath<FurnitureData>(AssetDatabase.GUIDToAssetPath(guid));
                if (data != null && !string.IsNullOrEmpty(data.id)) byId[data.id] = data;
            }
            int applied = 0;
            foreach (FurnitureEntry e in manifest.furniture)
            {
                if (!byId.TryGetValue(e.id, out FurnitureData data)) continue;
                Sprite[] main = List(e.sprites);
                if (main.Length == 0) continue;
                data.sprites = main;
                data.dirtySprites = List(e.dirty);
                data.idleFrames = List(e.idle);
                data.idleFps = e.idleFps > 0 ? e.idleFps : 4f;
                data.workFrames = List(e.work);
                data.workFps = e.workFps > 0 ? e.workFps : 6f;
                data.alertFrames = List(e.alert);
                data.alertFps = e.alertFps > 0 ? e.alertFps : 5f;
                EditorUtility.SetDirty(data);
                applied++;
            }

            // Pisos: se cambia el sprite de los tiles que ya usan los datos y la escena.
            foreach (FloorEntry f in manifest.floors)
            {
                SetTileSprite($"{TileFolder}/Suelo_{f.id}_A.asset", sprites.GetValueOrDefault(f.a));
                SetTileSprite($"{TileFolder}/Suelo_{f.id}_B.asset", sprites.GetValueOrDefault(f.b));
            }

            // Íconos
            var db = AssetDatabase.LoadAssetAtPath<GameDatabase>(DatabasePath);
            if (db != null && manifest.icons != null)
            {
                db.dirtyCupIcon = sprites.GetValueOrDefault(manifest.icons.dirtyCup) ?? db.dirtyCupIcon;
                db.dirtyPlateIcon = sprites.GetValueOrDefault(manifest.icons.dirtyPlate) ?? db.dirtyPlateIcon;
                db.steamIcon = sprites.GetValueOrDefault(manifest.icons.steam) ?? db.steamIcon;
                EditorUtility.SetDirty(db);
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"[Mancoffee] Arte estilo A aplicado: {applied} muebles, {manifest.floors.Length} pisos.");
            return true;
        }

        private static void SetTileSprite(string path, Sprite sprite)
        {
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null || sprite == null) return;
            tile.sprite = sprite;
            EditorUtility.SetDirty(tile);
        }

        private static void ConfigureImporter(string assetPath, Vector2 pivot)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer) return;
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            bool same = importer.textureType == TextureImporterType.Sprite &&
                        importer.spriteImportMode == SpriteImportMode.Single &&
                        Mathf.Approximately(importer.spritePixelsPerUnit, PPU) &&
                        importer.filterMode == FilterMode.Point &&
                        importer.textureCompression == TextureImporterCompression.Uncompressed &&
                        !importer.mipmapEnabled &&
                        settings.spriteAlignment == (int)SpriteAlignment.Custom &&
                        settings.spriteMeshType == SpriteMeshType.FullRect &&
                        (settings.spritePivot - pivot).sqrMagnitude < 1e-8f;
            if (same) return;

            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = PPU;
            importer.filterMode = FilterMode.Point;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.mipmapEnabled = false;
            importer.alphaIsTransparency = true;
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = pivot;
            settings.spriteMeshType = SpriteMeshType.FullRect;
            importer.SetTextureSettings(settings);
            importer.SaveAndReimport();
        }
    }
}
