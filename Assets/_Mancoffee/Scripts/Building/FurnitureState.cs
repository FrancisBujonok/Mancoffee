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
    /// También elige qué se dibuja: la versión sucia, la animación de reposo y la capa animada de trabajo o de alerta.
    /// </summary>
    [DisallowMultipleComponent]
    public class FurnitureState : MonoBehaviour
    {
        private PlacedFurniture furniture;
        private SpriteRenderer furnitureSprite;
        private SpriteRenderer animOverlay;
        private SpriteRenderer dishesOverlay;
        private SpriteRenderer steam;
        private readonly int[] dirty = new int[2];
        private float steamTime;
        private float idlePhase;

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

        /// <summary>Hay un empleado usándolo ahora (preparando o lavando).</summary>
        public bool InUse
        {
            get
            {
                foreach (Employee e in Employee.Active)
                    if (e != null && e.WorkingAt == furniture) return true;
                return false;
            }
        }

        public void Init(PlacedFurniture placed, SpriteRenderer sprite)
        {
            furniture = placed;
            furnitureSprite = sprite;
            idlePhase = Random.value * 10f;

            animOverlay = CreateChild("Animacion", 1);
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

        private bool HasDirtyArt => furniture != null && furniture.Data.dirtySprites != null && furniture.Data.dirtySprites.Length > 0;
        private bool HasAlertArt => furniture != null && furniture.Data.alertFrames != null && furniture.Data.alertFrames.Length > 0;

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

        /// <summary>Íconos sueltos: solo para muebles que no tienen dibujo propio del estado.</summary>
        private void Refresh()
        {
            if (dishesOverlay == null || furnitureSprite == null) return;
            GameDatabase db = Db;
            bool visible = furnitureSprite.enabled;

            Sprite dishSprite = db == null || HasDirtyArt ? null : DirtyCups > 0 ? db.dirtyCupIcon : DirtyPlates > 0 ? db.dirtyPlateIcon : null;
            dishesOverlay.enabled = dishSprite != null && visible;
            dishesOverlay.sprite = dishSprite;

            bool showSteam = NeedsPurge && !HasAlertArt && db != null && db.steamIcon != null && visible;
            steam.enabled = showSteam;
            steam.sprite = showSteam ? db.steamIcon : null;
        }

        private static Sprite Frame(Sprite[] frames, float fps, float time)
        {
            if (frames == null || frames.Length == 0) return null;
            int i = Mathf.FloorToInt(time * Mathf.Max(0.1f, fps)) % frames.Length;
            return frames[i];
        }

        private void LateUpdate()
        {
            if (furnitureSprite == null || furniture == null || furniture.Data == null) return;
            FurnitureData data = furniture.Data;

            // Si el mueble se oculta (lo están moviendo) se ocultan también sus capas.
            if (!furnitureSprite.enabled)
            {
                animOverlay.enabled = dishesOverlay.enabled = steam.enabled = false;
                return;
            }

            // Sprite principal: sucio > animación de reposo > el de la rotación.
            float t = Time.time + idlePhase;
            Sprite main = DirtyCount > 0 ? data.GetDirtySprite(furniture.Rotation) : null;
            if (main == null && data.idleFrames != null && data.idleFrames.Length > 0 && data.sprites.Length <= 1)
                main = Frame(data.idleFrames, data.idleFps, t);
            if (main == null) main = data.GetSprite(furniture.Rotation);
            if (furnitureSprite.sprite != main) furnitureSprite.sprite = main;

            // Capa animada: trabajando > pidiendo mantenimiento.
            Sprite overlay = null;
            if (data.workFrames != null && data.workFrames.Length > 0 && InUse) overlay = Frame(data.workFrames, data.workFps, Time.time);
            else if (NeedsPurge && HasAlertArt) overlay = Frame(data.alertFrames, data.alertFps, Time.time);
            animOverlay.enabled = overlay != null;
            animOverlay.sprite = overlay;
            if (overlay != null) animOverlay.transform.position = furnitureSprite.transform.position;

            // Íconos de respaldo.
            if (!dishesOverlay.enabled && !steam.enabled && (DirtyCount > 0 || NeedsPurge)) Refresh();
            Bounds b = furnitureSprite.bounds;
            if (dishesOverlay.enabled)
                dishesOverlay.transform.position = new Vector3(b.center.x, b.max.y - 3f / 32f, 0f);
            if (steam.enabled)
            {
                steamTime += Time.deltaTime;
                float bob = Mathf.Round(Mathf.Sin(steamTime * 4f) * 1.5f) / 32f;
                steam.transform.position = new Vector3(b.center.x, b.max.y + 2f / 32f + bob, 0f);
            }
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
