using CoolHome.Utilities;
using Il2Cpp;
using MelonLoader;
using HarmonyLib;
using System.Collections;
using UnityEngine;
using Il2CppTLD.Placement;
using Il2CppTLD.Gear;

namespace CoolHome
{
    public class InteriorSpaceManager
    {
        static string ScriptName = "SCRIPT_Heating";

        public string? CurrentSpace;
        public string? CurrentSpaceMeta;
        public WarmingWalls? CurrentWalls;
        public GameObject? Instance;

        public AuroraManager? AuroraManager = null;

        public Dictionary<string, GameObject> TrackedSpaces = new Dictionary<string, GameObject>();
        public Dictionary<string, WarmingWalls> RegisteredHeaters = new Dictionary<string, WarmingWalls>();

        public void LoadData()
        {
            if (Instance is null) return;
            TrackedSpaces.Clear();
            RegisteredHeaters.Clear();

            SaveManager saveManager = CoolHome.saveManager;
            InteriorSpaceManagerProxy dataToLoad = saveManager.LoadSpaceManager();

            CurrentSpace = dataToLoad.CurrentSpace;
            if (AuroraManager is not null && dataToLoad.AuroraManager is not null) AuroraManager.LoadData(dataToLoad.AuroraManager);

            foreach (KeyValuePair<string, WarmingWallsProxy> trackedSpace in dataToLoad.TrackedSpaces)
            {
                GameObject go = new GameObject(trackedSpace.Key);
                go.transform.parent = Instance.transform;

                WarmingWalls ww = go.AddComponent<WarmingWalls>();
                ww.LoadData(trackedSpace.Value);
                TrackedSpaces.Add(trackedSpace.Key, go);

                if (trackedSpace.Key == CurrentSpace) CurrentWalls = ww;
            }

            foreach (KeyValuePair<string, string> registeredFire in dataToLoad.RegisteredFires)
            {
                if (TrackedSpaces[registeredFire.Value]) RegisteredHeaters.Add(
                    registeredFire.Key,
                    TrackedSpaces[registeredFire.Value].GetComponent<WarmingWalls>()
                );
            }
        }

        public void SaveData()
        {
            if (Instance is null) return;

            SaveManager saveManager = CoolHome.saveManager;
            InteriorSpaceManagerProxy dataToSave = new InteriorSpaceManagerProxy();

            dataToSave.CurrentSpace = CurrentSpace;
            if (AuroraManager is not null) dataToSave.AuroraManager = AuroraManager.SaveData();

            foreach (KeyValuePair<string, WarmingWalls> registeredFire in RegisteredHeaters)
            {
                string name = registeredFire.Value.gameObject.name;
                dataToSave.RegisteredFires.Add(registeredFire.Key, name);
            }

            foreach (KeyValuePair<string, GameObject> trackedSpace in TrackedSpaces)
            {
                WarmingWallsProxy ww = trackedSpace.Value.GetComponent<WarmingWalls>().SaveData();
                dataToSave.TrackedSpaces.Add(trackedSpace.Key, ww);
            }

            saveManager.SaveSpaceManager(dataToSave);
        }

        [HarmonyPatch(typeof(SaveGameSystem), nameof(SaveGameSystem.SaveSceneData))]
        internal class SaveGamePatch
        {
            static void Postfix()
            {
                CoolHome.spaceManager.SaveData();
            }
        }

        public void Init()
        {
            GameObject instance = GameObject.Find(ScriptName);
            if (instance is null)
            {
                instance = new GameObject(ScriptName);
                instance.AddComponent<Preserve>();
                instance.AddComponent<PlayerHeater>();
                AuroraManager = instance.AddComponent<AuroraManager>();
            }
            Instance = instance;
            LoadData();
        }

        public void Deinit()
        {
            if (Instance is null) return;
            Preserve p = Instance.GetComponent<Preserve>();
            p.Destroy();
            Instance = null;
        }

        public bool IsInitialized()
        {
            return Instance is not null;
        }

        public string? GetCurrentSpaceName()
        {
            return CurrentSpace;
        }

        public WarmingWalls? GetCurrentSpace()
        {
            if (CurrentWalls is not null) return CurrentWalls;
            if (CurrentSpace is not null && TrackedSpaces.ContainsKey(CurrentSpace))
            {
                GameObject go = TrackedSpaces[CurrentSpace];
                CurrentWalls = go.GetComponent<WarmingWalls>();
                if (CurrentWalls is not null) return CurrentWalls;
            }
            return null;
        }

        public void EnterIndoorScene(string sceneName)
        {
            CurrentSpace = sceneName;
            if (TrackedSpaces.ContainsKey(sceneName)) TrackedSpaces[sceneName].GetComponent<WarmingWalls>().RemoveShadowHeaters();
        }

        public void LeaveIndoorScene()
        {
            string? sceneName = CurrentSpace;
            WarmingWalls? ww = CurrentWalls;

            if (sceneName is null || ww is null) return;

            GearItem? itemInHands = GameManager.GetPlayerManagerComponent().m_ItemInHands;

            Fire[] allFires = GameObject.FindObjectsOfType<Fire>();
            foreach (Fire f in allFires)
            {
                if (f is null) continue;
                if (f.GetRemainingLifeTimeSeconds() < 1) continue;
                string pdid = f.GetComponent<ObjectGuid>().PDID;
                ww.AddShadowHeater("FIRE", pdid, HeatSourceControl.FIRE_POWER, f.GetRemainingLifeTimeSeconds());
            }

            FlareItem[] allFlares = GameObject.FindObjectsOfType<FlareItem>();
            foreach (FlareItem flare in allFlares)
            {
                if (flare is null) continue;
                if (!flare.IsBurning()) continue;
                if (itemInHands is not null && itemInHands == flare.m_GearItem) continue;
                string id = GetGearItemId(flare.m_GearItem);
                ww.AddShadowHeater("FLARE", id, HeatSourceControl.FLARE_POWER, flare.GetNormalizedBurnTimeLeft() * flare.GetModifiedBurnLifetimeMinutes() * 60);
            }

            TorchItem[] allTorches = GameObject.FindObjectsOfType<TorchItem>();
            foreach (TorchItem torch in allTorches)
            {
                if (torch is null) continue;
                if (!torch.IsBurning()) continue;
                if (itemInHands is not null && itemInHands == torch.m_GearItem) continue;
                string id = GetGearItemId(torch.m_GearItem);
                ww.AddShadowHeater("TORCH", id, HeatSourceControl.TORCH_POWER, (1 - torch.GetBurnProgress()) * torch.GetModifiedBurnLifetimeMinutes() * 60);
            }

            KeroseneLampItem[] allLamps = GameObject.FindObjectsOfType<KeroseneLampItem>();
            foreach (KeroseneLampItem lamp in allLamps)
            {
                if (lamp is null) continue;
                if (!lamp.IsOn()) continue;
                if (itemInHands is not null && itemInHands == lamp.m_GearItem) continue;
                string id = GetGearItemId(lamp.m_GearItem);
                ww.AddShadowHeater("LAMP", id, HeatSourceControl.LAMP_POWER, (lamp.m_CurrentFuelLiters / lamp.GetModifiedFuelBurnLitersPerHour()) * 3600);
            }

            CurrentSpace = null;
            CurrentWalls = null;
        }

        public void EnterOutdoorScene()
        {
            IndoorSpaceTrigger[] triggers = GameObject.FindObjectsOfType<IndoorSpaceTrigger>();
            foreach (IndoorSpaceTrigger ist in triggers)
            {
                string name = GetIndoorSpaceName(ist);
                if (TrackedSpaces.ContainsKey(name)) TrackedSpaces[name].GetComponent<WarmingWalls>().RemoveShadowHeaters();
            }
        }

        public void LeaveOutdoorScene()
        {
            IndoorSpaceTrigger[] triggers = GameObject.FindObjectsOfType<IndoorSpaceTrigger>();
            List<WarmingWalls> wallComponents = new List<WarmingWalls>();

            GearItem? itemInHands = GameManager.GetPlayerManagerComponent().m_ItemInHands;

            Dictionary<string, Fire> firesPresent = new Dictionary<string, Fire>();
            Fire[] allFires = GameObject.FindObjectsOfType<Fire>();
            foreach (Fire f in allFires)
            {
                if (f is null) continue;
                firesPresent[GetFireId(f.gameObject)] = f;
            }

            Dictionary<string, FlareItem> flaresPresent = new Dictionary<string, FlareItem>();
            FlareItem[] allFlares = GameObject.FindObjectsOfType<FlareItem>();
            foreach (FlareItem flare in allFlares)
            {
                if (flare is null) continue;
                flaresPresent[GetGearItemId(flare.m_GearItem)] = flare;
            }

            Dictionary<string, TorchItem> torchesPresent = new Dictionary<string, TorchItem>();
            TorchItem[] allTorches = GameObject.FindObjectsOfType<TorchItem>();
            foreach (TorchItem torch in allTorches)
            {
                if (torch is null) continue;
                torchesPresent[GetGearItemId(torch.m_GearItem)] = torch;
            }

            Dictionary<string, KeroseneLampItem> lampsPresent = new Dictionary<string, KeroseneLampItem>();
            KeroseneLampItem[] allLamps = GameObject.FindObjectsOfType<KeroseneLampItem>();
            foreach (KeroseneLampItem lamp in allLamps)
            {
                if (lamp is null) continue;
                lampsPresent[GetGearItemId(lamp.m_GearItem)] = lamp;
            }

            foreach (IndoorSpaceTrigger ist in triggers)
            {
                string name = GetIndoorSpaceName(ist);
                WarmingWalls? ww = TrackedSpaces.ContainsKey(name) && TrackedSpaces[name] is not null ? TrackedSpaces[name].GetComponent<WarmingWalls>() : null;
                if (ww is not null) wallComponents.Add(ww);
            }

            foreach (KeyValuePair<string, WarmingWalls> entry in RegisteredHeaters)
            {
                if (!wallComponents.Contains(entry.Value)) continue;
                if (firesPresent.ContainsKey(entry.Key))
                {
                    Fire f = firesPresent[entry.Key];
                    if (f.GetRemainingLifeTimeSeconds() < 1) continue;
                    string pdid = f.GetComponent<ObjectGuid>().PDID;
                    entry.Value.AddShadowHeater("FIRE", pdid, HeatSourceControl.FIRE_POWER, f.GetRemainingLifeTimeSeconds());
                    continue;
                }
                if (flaresPresent.ContainsKey(entry.Key))
                {
                    FlareItem flare = flaresPresent[entry.Key];
                    if (itemInHands is not null && itemInHands == flare.m_GearItem) continue;
                    entry.Value.AddShadowHeater("FLARE", GetGearItemId(flare.m_GearItem), HeatSourceControl.FLARE_POWER, flare.GetNormalizedBurnTimeLeft() * flare.GetModifiedBurnLifetimeMinutes() * 60);
                    continue;
                }
                if (torchesPresent.ContainsKey(entry.Key))
                {
                    TorchItem torch = torchesPresent[entry.Key];
                    if (itemInHands is not null && itemInHands == torch.m_GearItem) continue;
                    entry.Value.AddShadowHeater("TORCH", GetGearItemId(torch.m_GearItem), HeatSourceControl.TORCH_POWER, (1 - torch.GetBurnProgress()) * torch.GetModifiedBurnLifetimeMinutes() * 60);
                    continue;
                }
                if (lampsPresent.ContainsKey(entry.Key))
                {
                    KeroseneLampItem lamp = lampsPresent[entry.Key];
                    if (itemInHands is not null && itemInHands == lamp.m_GearItem) continue;
                    entry.Value.AddShadowHeater("LAMP", GetGearItemId(lamp.m_GearItem), HeatSourceControl.LAMP_POWER, (lamp.m_CurrentFuelLiters / lamp.GetModifiedFuelBurnLitersPerHour()) * 3600);
                }
            }

            CurrentSpace = null;
            CurrentWalls = null;
        }

        public string GetIndoorSpaceName(IndoorSpaceTrigger ist)
        {
            ObjectGuid id = ist.GetComponent<ObjectGuid>();
            return id.PDID;
        }

        public string GetCarSpaceName(VehicleDoor door)
        {
            GameObject parent = door.transform.parent.gameObject;
            return parent.transform.position.ToString();
        }

        public void EnterIndoorSpace(IndoorSpaceTrigger ist)
        {
            string name = GetIndoorSpaceName(ist);
            CurrentSpace = name;
            Melon<CoolHome>.Logger.Msg("Entering space named " + name);
        }

        [HarmonyPatch(typeof(IndoorSpaceTrigger), nameof(IndoorSpaceTrigger.OnTriggerEnter))]
        internal class IndoorSpaceTriggerOnEnterPatch
        {
            static void Postfix(IndoorSpaceTrigger __instance)
            {
                __instance.m_UseOutdoorTemperature = false;
                CoolHome.spaceManager.EnterIndoorSpace(__instance);
            }
        }

        public void Leave()
        {
            CurrentSpace = null;
            CurrentSpaceMeta = null;
            CurrentWalls = null;
        }

        [HarmonyPatch(typeof(IndoorSpaceTrigger), nameof(IndoorSpaceTrigger.OnTriggerExit))]
        internal class IndoorSpaceTriggerOnExitPatch
        {
            static void Postfix(IndoorSpaceTrigger __instance)
            {
                CoolHome.spaceManager.Leave();
            }
        }

        public WarmingWalls? CreateNewSpace()
        {
            if (CurrentSpace is null) return null;

            GameObject go = new GameObject();
            go.name = CurrentSpace;
            WarmingWalls ww = go.AddComponent<WarmingWalls>();
            if (Instance is null) CoolHome.spaceManager.Init();
            if (Instance is not null) go.transform.parent = Instance.transform;

            CurrentWalls = ww;
            CurrentWalls.Name = CurrentSpace;
            TrackedSpaces[CurrentSpace] = go;
            ww.Profile = CoolHome.LoadSceneConfig(CurrentSpaceMeta is not null ? CurrentSpaceMeta : CurrentSpace);
            ww.OnCreate();

            return ww;
        }

        public bool HasRegisteredHeaters(WarmingWalls ww)
        {
            return RegisteredHeaters.ContainsValue(ww);
        }

        public void RemoveIrrelevantSpace(WarmingWalls ww)
        {
            TrackedSpaces.Remove(ww.Name);
        }

        static string GetFireId(GameObject fire)
        {
            ObjectGuid og = fire.GetComponent<ObjectGuid>();

            if (og != null && og.PDID != null)
            {
                return og.PDID;
            }

            //SafehouseCustomization+ Patch
            MelonLogger.Msg($"ObjectGUID or PDID is null. Attempting to patch.");
            og = fire.GetOrAddComponent<ObjectGuid>();

            //Generate seed from position
            Vector3 v = fire.transform.position;
            int seed = Mathf.CeilToInt(v.x * v.z + v.y * 10000f);

            //Generate GUID from seed
            var r = new System.Random(seed);
            var guid = new byte[16];
            r.NextBytes(guid);
            Guid newGuid = new Guid(guid);

			PdidTable.RuntimeAddOrReplace(og, newGuid.ToString());

			MelonLogger.Msg($"Added GUID {og.PDID} to object {fire.name} at {fire.transform.position}");

            return og.PDID;
        }

        public static string GetGearItemId(GearItem gi)
        {
            return gi.m_InstanceID.ToString();
        }

        public void RegisterFire(Fire fire)
        {
            if (GetCurrentSpaceName() is null) return;
            WarmingWalls? ww = GetCurrentSpace();
            if (ww is null)
            {
                ww = CreateNewSpace();
            }
            string id = GetFireId(fire.gameObject);
            if (!RegisteredHeaters.ContainsKey(id)) RegisteredHeaters[id] = ww!;
        }

        public void RegisterGearItemHeater(GearItem heater)
        {
            if (GetCurrentSpaceName() is null) return;
            WarmingWalls? ww = GetCurrentSpace();
            if (ww is null)
            {
                ww = CreateNewSpace();
            }
            string id = GetGearItemId(heater);
            if (!RegisteredHeaters.ContainsKey(id)) RegisteredHeaters[id] = ww!;
        }

        public void UnregisterHeater(string id)
        {
            if (RegisteredHeaters.ContainsKey(id)) RegisteredHeaters.Remove(id);
        }



        [HarmonyPatch(typeof(Fire), nameof(Fire.TurnOn))]
        internal class FireRegisterPatch
        {
            static void Postfix(Fire __instance)
            {
                CoolHome.spaceManager.RegisterFire(__instance);
            }
        }

        [HarmonyPatch(typeof(Fire), nameof(Fire.TurnOff))]
        internal class FireUnregisterPatch
        {
            static void Postfix(Fire __instance)
            {
                string id = GetFireId(__instance.gameObject);
                CoolHome.spaceManager.UnregisterHeater(id);
            }
        }

        static void TryRegisterGearItem(GearItem gi)
        {
            if (gi.m_FlareItem is not null && gi.m_FlareItem.IsBurning())
            {
                CoolHome.spaceManager.RegisterGearItemHeater(gi);
                return;
            }

            if (gi.m_TorchItem is not null && gi.m_TorchItem.IsBurning())
            {
                CoolHome.spaceManager.RegisterGearItemHeater(gi);
                return;
            }

            if (gi.m_KeroseneLampItem is not null && gi.m_KeroseneLampItem.IsOn())
            {
                CoolHome.spaceManager.RegisterGearItemHeater(gi);
            }
        }

        [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.Throw))]
        internal class ThrownGearItemRegisterPatch
        {
            static void Postfix(GearItem gi)
            {
                InteriorSpaceManager.TryRegisterGearItem(gi);
            }
        }

        [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.Drop))]
        internal class DropGearItemRegisterPatch
        {
            static void Postfix(GameObject go)
            {
                GearItem gi = go.GetComponent<GearItem>();
                InteriorSpaceManager.TryRegisterGearItem(gi);
            }
        }

        [HarmonyPatch(typeof(FlareItem), nameof(FlareItem.BurnOut))]
        internal class FlareBurningOutPatch
        {
            static void Postfix(FlareItem __instance)
            {
                string id = InteriorSpaceManager.GetGearItemId(__instance.m_GearItem);
                CoolHome.spaceManager.UnregisterHeater(id);
            }
        }

        [HarmonyPatch(typeof(TorchItem), nameof(TorchItem.Extinguish))]
        internal class TorchItemBurningOutPatch
        {
            static void Postfix(TorchItem __instance)
            {
                string id = InteriorSpaceManager.GetGearItemId((__instance.m_GearItem));
                CoolHome.spaceManager.UnregisterHeater(id);
            }
        }

        public GameObject? ObjectToPlace;

        [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.StartPlaceMesh), new Type[] { typeof(GameObject), typeof(float), typeof(PlaceMeshFlags), typeof(PlaceMeshRules) })]
        internal class RememberObjectToPlacePatch
        {
            static void Postfix(GameObject objectToPlace)
            {
                CoolHome.spaceManager.ObjectToPlace = objectToPlace;
            }
        }

        [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.ExitMeshPlacement))]
        internal class ForgetObjectToPlacePatch
        {
            static void Postfix()
            {
                if (CoolHome.spaceManager.ObjectToPlace is not null) CoolHome.spaceManager.ObjectToPlace = null;
            }
        }

        [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.PlaceMeshInWorld))]
        internal class PlaceObjectToPlacePatch
        {
            static void Postfix()
            {
                if (CoolHome.spaceManager.ObjectToPlace is not null)
                {
                    GearItem? gi = CoolHome.spaceManager.ObjectToPlace.GetComponent<GearItem>();
                    if (gi is null) return;
                    InteriorSpaceManager.TryRegisterGearItem(gi);
                }
            }
        }

        [HarmonyPatch(typeof(PlayerManager), nameof(PlayerManager.ProcessPickupItemInteraction))]
        internal class UnregisterObjectPickedUpPatch
        {
            static void Postfix(GearItem item)
            {
                if (item.m_FlareItem is null && item.m_TorchItem is null && item.m_KeroseneLampItem is null) return;
                string id = InteriorSpaceManager.GetGearItemId(item);
                CoolHome.spaceManager.UnregisterHeater(id);
            }
        }

        public WarmingWalls? GetSpaceAssociatedWithFire(GameObject fire)
        {
            string id = GetFireId(fire);
            if (RegisteredHeaters.ContainsKey(id)) return RegisteredHeaters[id];
            return null;
        }

        public WarmingWalls? GetSpaceAssociatedWithGearItem(GearItem gi)
        {
            string id = GetGearItemId(gi);
            if (RegisteredHeaters.ContainsKey(id)) return RegisteredHeaters[id];
            return null;
        }

        [HarmonyPatch(typeof(HeatSource), nameof(HeatSource.Update))]
        internal class HeatSourceUpdatePatch
        {
            static void Prefix(HeatSource __instance)
            {
                Fire? fire = __instance.gameObject.GetComponent<Fire>();
                if (fire is null) return;
                if (__instance.m_TempIncrease < 1) return;

                WarmingWalls? ww = CoolHome.spaceManager.GetSpaceAssociatedWithFire(__instance.gameObject);
                if (ww is null) return;

                if (CoolHome.settings.UseTemperatureBasedFires)
                {
                    float power = HeatSourceControl.FIRE_TEMP_POWER_PER_C * __instance.m_TempIncrease;
                    ww.Heat(power);
                } else
                {
                    float powerCoefficient = __instance.m_TempIncrease / __instance.m_MaxTempIncrease;
                    float power = HeatSourceControl.FIRE_POWER;
                    ww.Heat(power * powerCoefficient);
                }
            }
        }

        [HarmonyPatch(typeof(FlareItem), nameof(FlareItem.Update))]
        internal class FlareItemUpdatePatch
        {
            static void Prefix(FlareItem __instance)
            {
                if (!__instance.IsBurning()) return;

                WarmingWalls? ww = CoolHome.spaceManager.GetSpaceAssociatedWithGearItem(__instance.m_GearItem);
                if (ww is null) return;

                ww.Heat(1000);
            }
        }

        [HarmonyPatch(typeof(TorchItem), nameof(TorchItem.Update))]
        internal class TorchItemUpdatePatch
        {
            static void Prefix(TorchItem __instance)
            {
                if (!__instance.IsBurning()) return;

                WarmingWalls? ww = CoolHome.spaceManager.GetSpaceAssociatedWithGearItem(__instance.m_GearItem);
                if (ww is null) return;

                ww.Heat(800);
            }
        }

        [HarmonyPatch(typeof(KeroseneLampItem), nameof(KeroseneLampItem.Update))]
        internal class KeroseneLampItemUpdatePatch
        {
            static void Prefix(KeroseneLampItem __instance)
            {
                if (!__instance.IsOn()) return;

                WarmingWalls? ww = CoolHome.spaceManager.GetSpaceAssociatedWithGearItem(__instance.m_GearItem);
                if (ww is null) return;

                ww.Heat(400);
            }
        }

        [HarmonyPatch(typeof(PlayerInVehicle), nameof(PlayerInVehicle.EnterVehicle))]
        internal class PlayerInVehicleEnterPatch
        {
            static void Postfix(PlayerInVehicle __instance)
            {
                VehicleDoor door = __instance.m_VehicleDoorUsed;
                string name = CoolHome.spaceManager.GetCarSpaceName(door);
                CoolHome.spaceManager.CurrentSpace = name;
                string parentName = door.transform.parent.gameObject.name.ToLowerInvariant();
                if (parentName.Contains("plane")) CoolHome.spaceManager.CurrentSpaceMeta = "Plane";
                else CoolHome.spaceManager.CurrentSpaceMeta = "Truck";
            }
        }

        [HarmonyPatch(typeof(PlayerInVehicle), nameof(PlayerInVehicle.ExitVehicle))]
        internal class PlayerInVehicleExitPatch
        {
            static void Postfix()
            {
                CoolHome.spaceManager.Leave();
            }
        }
    }
}
