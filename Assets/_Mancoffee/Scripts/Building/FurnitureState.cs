using Mancoffee.Core;
using Mancoffee.Data;
using Mancoffee.GridSystem;
using Mancoffee.Staff;
using UnityEngine;

namespace Mancoffee.Building
{
    /// <summary>
    /// Lo que le pasa a un mueble mientras se usa: la vajilla sucia que queda en una mesa o en la bacha,
    /// y el desgaste de las máquinas que necesitan purga (largan vapor y trabajan a mitad de velocidad).
    /// </summary>
    [DisallowMultipleComponent]
    public class FurnitureState : MonoBehaviour
    {
        private PlacedFurniture furniture;
        private SpriteRenderer furnitureSprite;
        private SpriteRenderer dishesOverlay;
        private SpriteRenderer steam;
        private readonly int[] dirty = new int[2];
        private float steamTime;

        public PlacedFurniture Furniture => furniture;
        public int Uses { get; private set; }
        public int MaintenanceEvery => furniture != null && furniture.Data != null ? furniture.Data.maintenanceEvery : 0;
        public bool NeedsMaintenance => MaintenanceEvery > 0;
        public bool NeedsPurge => NeedsMaintenance && Uses >= MaintenanceEvery;

        /// <summary>Multiplica la velocidad de preparación (0,5 con la máquina sin purgar).</summary>
        public float SpeedFactor => NeedsPurge ? 0.5f : 1f;

        public bool IsSink => furniture != null && furniture.Data.isSink;
        public bool IsTable => furniture != null && furniture.Data.category == FurnitureCategory.Mesa;
        public int DirtyCups => dirty[(int)DishKind.Taza];
        public int DirtyPlates => dirty[(int)DishKind.Plato];
        public int DirtyCount => dirty[0] + dirty[1];

        public void Init(PlacedFurniture placed, SpriteRenderer sprite)
        {
            furniture = placed;
            furnitureSprite = sprite;

            dishesOverlay = CreateChild("Vajilla", 2);
            steam = CreateChild("Vapor", 3);
            Refresh();
        }

        private SpriteRenderer CreateChild(string childName, int order)
        {
            var go = new GameObject(childName);
            go.transform.SetParent(transform, false);
            var sr = go.AddComponent<SpriteRenderer>();
            sr.sortingOrder = order;
            sr.enabled = false;
            return sr;
        }

        private static GameDatabase Db => GridManager.Instance != null ? GridManager.Instance.Database : null;

        // =========================== Máquinas ===========================

        /// <summary>Se terminó de preparar algo acá.</summary>
        public void AddUse()
        {
            if (!NeedsMaintenance) return;
            Uses++;
            if (Uses == MaintenanceEvery)
            {
                CafeState.Instance?.Notify($"La {furniture.Data.displayName.ToLowerInvariant()} larga vapor: prepara a mitad de velocidad hasta que alguien la purgue", NoteKind.Malo);
                TaskManager.Instance?.RequestFurniture(TaskType.Purgar, furniture);
            }
            Refresh();
        }

        public void Purge()
        {
            Uses = 0;
            Refresh();
        }

        // =========================== Vajilla ===========================

        /// <summary>Deja vajilla sucia (en una mesa, o en la bacha) y pide la tarea que corresponde.</summary>
        public void AddDirty(DishKind kind, int amount, bool requestTask = true)
        {
            if (amount <= 0) return;
            dirty[(int)kind] += amount;
            Refresh();
            if (!requestTask || TaskManager.Instance == null) return;
            if (IsSink) TaskManager.Instance.RequestFurniture(TaskType.Lavar, furniture);
            else TaskManager.Instance.RequestFurniture(TaskType.LevantarVajilla, furniture);
        }

        /// <summary>Se lleva toda la vajilla sucia de acá. Devuelve (tazas, platos).</summary>
        public (int cups, int plates) TakeAllDirty()
        {
            var result = (dirty[0], dirty[1]);
            dirty[0] = dirty[1] = 0;
            Refresh();
            return result;
        }

        /// <summary>Al cargar una partida.</summary>
        public void LoadState(int uses, int cups, int plates)
        {
            Uses = Mathf.Max(0, uses);
            dirty[0] = Mathf.Max(0, cups);
            dirty[1] = Mathf.Max(0, plates);
            Refresh();
            if (TaskManager.Instance == null) return;
            if (NeedsPurge) TaskManager.Instance.RequestFurniture(TaskType.Purgar, furniture);
            if (DirtyCount > 0)
                TaskManager.Instance.RequestFurniture(IsSink ? TaskType.Lavar : TaskType.LevantarVajilla, furniture);
        }

        // =========================== Visual ===========================

        private void Refresh()
        {
            if (dishesOverlay == null || furnitureSprite == null) return;
            GameDatabase db = Db;
            Bounds b = furnitureSprite.bounds;

            Sprite dishSprite = db == null ? null : DirtyCups > 0 ? db.dirtyCupIcon : DirtyPlates > 0 ? db.dirtyPlateIcon : null;
            dishesOverlay.enabled = dishSprite != null && furnitureSprite.enabled;
            dishesOverlay.sprite = dishSprite;
            dishesOverlay.transform.position = new Vector3(b.center.x, b.max.y - 3f / 32f, 0f);

            bool showSteam = NeedsPurge && db != null && db.steamIcon != null;
            steam.enabled = showSteam;
            steam.sprite = showSteam ? db.steamIcon : null;
            steam.transform.position = new Vector3(b.center.x, b.max.y + 2f / 32f, 0f);
        }

        private void Update()
        {
            if (steam == null || !steam.enabled) return;
            steamTime += Time.deltaTime;
            Bounds b = furnitureSprite.bounds;
            float bob = Mathf.Round(Mathf.Sin(steamTime * 4f) * 1.5f) / 32f;
            steam.transform.position = new Vector3(b.center.x, b.max.y + 2f / 32f + bob, 0f);
        }

        private void LateUpdate()
        {
            // Si el mueble se oculta (lo están moviendo) la vajilla y el vapor también.
            if (furnitureSprite == null) return;
            if (!furnitureSprite.enabled && (dishesOverlay.enabled || steam.enabled))
            {
                dishesOverlay.enabled = false;
                steam.enabled = false;
            }
            else if (furnitureSprite.enabled && !dishesOverlay.enabled && DirtyCount > 0) Refresh();
            else if (furnitureSprite.enabled && dishesOverlay.enabled) dishesOverlay.transform.position =
                new Vector3(furnitureSprite.bounds.center.x, furnitureSprite.bounds.max.y - 3f / 32f, 0f);
        }

        private void OnDestroy()
        {
            // Si se vende el mueble con vajilla encima, la vajilla se recupera limpia.
            Inventory inv = Inventory.Instance;
            if (inv == null || inv.IsLoading || DirtyCount == 0) return;
            inv.ReturnClean(DishKind.Taza, dirty[0]);
            inv.ReturnClean(DishKind.Plato, dirty[1]);
        }
    }
}
