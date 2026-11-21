using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using Eremite.Buildings;
using Eremite.Buildings.UI;
using Eremite.Controller;
using Eremite.Model;
using Eremite.Services;
using Eremite.View;
using Eremite.View.UI;
using HarmonyLib;
using UnityEngine;

namespace CampsUpgrade
{
    /// <summary>
    /// Gives the gatherer camps and fishing huts the same upgrades as the Woodcutters Camp.
    ///
    /// Three things are needed for that:
    ///   1. Data: copy the woodcutters' upgrade levels onto the other buildings (CopyUpgradesToTargets).
    ///   2. Text: the copied perks get their own names and descriptions, so the UI reads correctly (CustomTexts).
    ///   3. UI:   the gatherer/fishing panels have no upgrades tab, so copy the camp panel's (AddUpgradesUi).
    /// </summary>
    // [HarmonyPatch] on the class tells Harmony (the runtime patching library) that this class contains patch methods.
    // Harmony rewrites game methods at runtime so ours runs before/after them, without editing the game's DLL.
    // [BepInPlugin] gives BepInEx (the mod loader) the mod's id, name and version; BaseUnityPlugin makes it a Unity
    // MonoBehaviour, so Unity creates it at startup and calls Awake().
    [HarmonyPatch]
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        private static ConfigEntry<bool> DebugLog;

        // The building whose upgrades are copied to all the others.
        private const string SourceBuildingName = "Woodcutters Camp";

        // The buildings that receive those upgrades (the model "Name", not the display name).
        // The Woodcutters Camp is a CampModel; the gatherer huts and fishing huts are other model classes
        // (GathererHutModel, FishingHutModel). All three extend UpgradableBuildingModel, which is what holds "levels".
        // Some buildings have a separate "Primitive ..." model with its own name, so both names are listed.
        private static readonly string[] TargetBuildingNames =
        {
            "Forager's Camp",
            "Primitive Forager's Camp",
            "Herbalist's Camp",
            "Primitive Herbalist's Camp",
            "Trapper's Camp",
            "Primitive Trapper's Camp",
            "Harvester Camp",
            "Stonecutter's Camp",
            "Fishing Hut",
            "Primitive Fishing Hut",
        };

        // One yes/no setting per building (the woodcutters and every target), default yes.
        // The config file is BepInEx/config/<GUID>.cfg and is created on the first run.
        private static readonly Dictionary<string, ConfigEntry<bool>> UpgradeEnabled =
            new Dictionary<string, ConfigEntry<bool>>();

        // Unity calls Awake() once when the plugin object is created, before the game's own startup finishes.
        // Logger and Config are provided by BaseUnityPlugin.
        private void Awake()
        {
            Log = Logger;

            DebugLog = Config.Bind(
                "DebugLog",
                "DebugLog",
                false,
                "If true, add the gi logs to bepinex log window");

            BindConfigs();
            // Scans this class for [HarmonyPatch] methods and applies them all.
            Harmony.CreateAndPatchAll(typeof(Plugin));
            Log.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded");
        }

        private static void DebugInfo(string message)
        {
            if (DebugLog.Value) Log.LogInfo(message);
        }

        private void BindConfigs()
        {
            foreach (var name in new[] { SourceBuildingName }.Concat(TargetBuildingNames))
            {
                UpgradeEnabled[name] = Config.Bind(
                    "Buildings",
                    ConfigKey(name),
                    true,
                    "If true, this building can be upgraded. Takes effect the next time the game starts. This is for the small version of the building as well");
            }
        }

        // BepInEx rejects ' in config keys, so "Forager's Camp" is shown as "Foragers Camp" in the config file.
        private static string ConfigKey(string name) => name.Replace("'", "");

        private static bool IsUpgradeEnabled(string name)
        {
            return !UpgradeEnabled.TryGetValue(name, out var entry) || entry.Value;
        }

        #region Woodcutters Camp

        // The game only enables camp upgrades when the CampsUpgradesActivationModel effect has set
        // EffectsState.campsUpgradesActive (global, affects ALL camps). UpgradableBuilding.HasUpgrades
        // calls CampModel.AreUpgradesUnlockd(), so we force it true for the woodcutters camp only.
        //
        // Harmony postfix: runs right after the original method. Special parameter names are filled in by Harmony:
        //   __instance = the object the method was called on (the CampModel)
        //   __result   = the original return value; "ref" lets us change what the caller receives
        [HarmonyPatch(typeof(CampModel), nameof(CampModel.AreUpgradesUnlockd))]
        [HarmonyPostfix]
        private static void AllowWoodcutterUpgrades(CampModel __instance, ref bool __result)
        {
            // AreUpgradesUnlockd is a virtual method on UpgradableBuildingModel, and CampModel overrides it. This patch
            // targets the CampModel override, so it only sees camps (GathererHutModel and FishingHutModel do not
            // override it). Matching on the name then picks out the woodcutters among the camps.
            if (!__result && __instance.Name.Contains("Woodcutter") && IsUpgradeEnabled(SourceBuildingName))
                __result = true;
        }

        #endregion

        #region Copying the upgrade data

        // Text for our custom LocaText keys (key -> English text), filled while the perks are cloned.
        private static readonly Dictionary<string, string> CustomTexts = new Dictionary<string, string>();

        private static bool upgradesCopied;

        // Runs once the game has finished loading, before any match. All building models exist by then.
        // OnServicesReady is not public, so it is targeted by its name as a string (nameof would not compile).
        [HarmonyPatch(typeof(MainController), "OnServicesReady")]
        [HarmonyPostfix]
        private static void OnServicesReady()
        {
            try { CopyUpgradesToTargets(); }
            catch (Exception e) { Log.LogError($"{PluginInfo.PLUGIN_NAME} copying upgrades failed: {e}"); }
        }

        // Lets the game find the text for our custom keys.
        // The game's UI looks text up by a key (LocaText.key) through TextsService. Our keys are not in the game's
        // text tables, so after the normal lookup we replace the result whenever the key is one of ours.
        [HarmonyPatch(typeof(TextsService), nameof(TextsService.GetLocaText))]
        [HarmonyPostfix]
        private static void CustomLocaText(string key, ref string __result)
        {
            if (key != null && CustomTexts.TryGetValue(key, out var text))
                __result = text;
        }

        private static void CopyUpgradesToTargets()
        {
            if (upgradesCopied) return;
            upgradesCopied = true;

            // One pass over the game's buildings instead of one scan per target.
            var upgradable = new Dictionary<string, UpgradableBuildingModel>();
            foreach (var b in Serviceable.Settings.Buildings.OfType<UpgradableBuildingModel>())
            {
                if (!upgradable.ContainsKey(b.Name))
                    upgradable.Add(b.Name, b);
            }

            if (!upgradable.TryGetValue(SourceBuildingName, out var model) ||
                !(model is CampModel source) || source.levels == null)
            {
                Log.LogWarning($"{PluginInfo.PLUGIN_NAME} upgrades not copied: '{SourceBuildingName}' not found");
                return;
            }

            // Built once and shared: every target building gets the same levels array (and so the same perk objects).
            var levels = CreateLevelsFrom(source);

            foreach (var name in TargetBuildingNames)
            {
                if (!IsUpgradeEnabled(name))
                {
                    DebugInfo($"Upgrades for '{name}' are disabled in the config, skipped.");
                    continue;
                }

                if (!upgradable.TryGetValue(name, out var target))
                {
                    Log.LogWarning($"{PluginInfo.PLUGIN_NAME} building '{name}' not found, skipped");
                    continue;
                }

                target.levels = levels;
                DebugInfo($"{target.Name} now has {levels.Length} upgrade levels copied from {source.Name}.");
            }
        }

        // Same goods cost as the source, but with our own copies of its perks.
        private static BuildingLevelModel[] CreateLevelsFrom(UpgradableBuildingModel source)
        {
            var levels = new BuildingLevelModel[source.levels.Length];

            for (int levelIndex = 0; levelIndex < levels.Length; levelIndex++)
            {
                var sourceLevel = source.levels[levelIndex];
                int level = levelIndex;

                levels[level] = new BuildingLevelModel
                {
                    requiredGoods = sourceLevel.requiredGoods,
                    options = sourceLevel.options
                        .Select((perk, perkIndex) => ClonePerk(perk, level, perkIndex))
                        .ToArray(),
                };
            }

            return levels;
        }

        // The woodcutters' perks carry woodcutter wording, so we clone each perk and give the clone neutral text.
        // (The clone does the same thing as the original; only the name and description differ.)
        //
        // Perks are Unity ScriptableObjects (data assets loaded with the game). Object.Instantiate makes an independent
        // in-memory copy, so changing the copy's text does not touch the woodcutters' original perk.
        private static BuildingPerkModel ClonePerk(BuildingPerkModel original, int level, int perkIndex)
        {
            var clone = UnityEngine.Object.Instantiate(original);
            // "name" is Unity's built-in object name (used in the editor/debugging); "Name" used elsewhere is the game model's own property.
            clone.name = $"{original.Name} (copy)";

            GetPerkText(original, out var displayName, out var description);
            // Unique key per level/perk slot (e.g. CampsUpgrade_L0_1_Name); CustomLocaText above resolves these.
            var nameKey = $"CampsUpgrade_L{level}_{perkIndex}_Name";
            var descriptionKey = $"CampsUpgrade_L{level}_{perkIndex}_Desc";
            CustomTexts[nameKey] = displayName;
            CustomTexts[descriptionKey] = description;

            clone.displayName = new LocaText { key = nameKey };
            clone.description = new LocaText { key = descriptionKey };
            clone.formatDescription = false; // use our description as is instead of the original's formatted one
            return clone;
        }

        // Placeholder wording. The numbers come from the woodcutters' perks (speed +25%, capacity 3, extra production 33%).
        // Picks the text by the perk's concrete C# type (pattern-matching switch; "case X name" also casts to X so we
        // can read its fields, "case X _" just checks the type).
        private static void GetPerkText(BuildingPerkModel perk, out string displayName, out string description)
        {
            switch (perk)
            {
                case VillagerPerkBuildingPerkModel _:
                    displayName = "Worker Speed";
                    description = "Workers work 25% faster.";
                    break;

                case WorkersExtraCapacityBuildingPerkModel capacity:
                    displayName = "Worker Capacity";
                    description = $"Workers carry {capacity.amount} more goods per trip.";
                    break;

                case ExtraProductionChanceBuildingPerkModel extraProduction:
                    displayName = "Extra Production Chance";
                    description = $"{(int)Math.Round(extraProduction.amount * 100)}% chance to produce extra goods.";
                    break;

                case AutomatonBuildingPerkModel automaton:
                    displayName = "Storage Automaton";
                    description = $"Adds {automaton.amount} hauling cart to this building. That will take the good back to the warehouse.";
                    break;

                default:
                    displayName = perk.Name;
                    description = perk.Name;
                    break;
            }
        }

        #endregion

        #region Upgrades UI

        // The gatherer and fishing panels have no upgrades tab or upgrades panel, so we copy the camp panel's.
        // The panels are scene singletons that only exist once a match has loaded, so this runs from
        // GameController.StartGame (after the match's services are loaded, before the player can click anything).

        // Unity UI vocabulary used below:
        //   Transform  = a GameObject's position in the scene hierarchy (parent/children). Every UI element has one.
        //   Find("A/B")= looks up a child by its hierarchy path, relative to that transform; returns null if missing.
        //   Instantiate(x, parent) = clones x (and all its children and components) and attaches the clone under parent.
        //   SiblingIndex = order among the parent's children; for UI layouts it sets the order the elements appear in.
        //   GetComponent<T>() = gets the script/component of type T attached to that GameObject.
        // The path strings ("Content/TabsPanel/Upgrades" etc.) are the names in the game's UI prefabs, so they can
        // break if the game renames them (hence the null checks and warning log).

        // The upgrades tab we added to each panel.
        private static readonly Dictionary<ProductionBuildingPanel, AnimatedTab> UpgradeTabs =
            new Dictionary<ProductionBuildingPanel, AnimatedTab>();

        [HarmonyPatch(typeof(GameController), "StartGame")]
        [HarmonyPostfix]
        private static void OnGameStart()
        {
            try
            {   
                
                // Three separate building panels exist, matching the three model classes:
                //   CampPanel        - camps such as the Woodcutters Camp; has the upgrades tab we copy from
                //   GathererHutPanel - gatherer huts; no upgrades tab, so we add one
                //   FishingHutPanel  - fishing huts; no upgrades tab, so we add one
                // Gatherer and fishing huts use different panels, so each needs its own call.
                AddUpgradesUi(GathererHutPanel.Instance);
                AddUpgradesUi(FishingHutPanel.Instance);
            }
            catch (Exception e) { Log.LogError($"{PluginInfo.PLUGIN_NAME} adding upgrades UI failed: {e}"); }
        }

        // Show the upgrades tab only for buildings that have upgrades (same as CampPanel does for camps).
        [HarmonyPatch(typeof(GathererHutPanel), nameof(GathererHutPanel.Show), new[] { typeof(GathererHut) })]
        [HarmonyPostfix]
        private static void GathererHutPanel_Show_Postfix(GathererHutPanel __instance, GathererHut gathererHut)
        {
            UpdateUpgradesTab(__instance, gathererHut);
        }

        [HarmonyPatch(typeof(FishingHutPanel), nameof(FishingHutPanel.Show), new[] { typeof(FishingHut) })]
        [HarmonyPostfix]
        private static void FishingHutPanel_Show_Postfix(FishingHutPanel __instance, FishingHut hut)
        {
            UpdateUpgradesTab(__instance, hut);
        }

        // The second argument of [HarmonyPatch] lists the parameter types, to pick the right overload of Show.
        // SwitchTab(tab, bool) shows or hides that tab.
        private static void UpdateUpgradesTab(ProductionBuildingPanel panel, Building building)
        {
            if (UpgradeTabs.TryGetValue(panel, out var tab))
                panel.tabsPanel.SwitchTab(tab, building.HasUpgrades);
        }

        private static void AddUpgradesUi(ProductionBuildingPanel panel)
        {
            if (panel == null || UpgradeTabs.ContainsKey(panel)) return;

            // Find the camp panel's upgrades tab and panel (to copy) and the places to put the copies.
            var campPanel = CampPanel.Instance;
            var campTab = campPanel != null ? campPanel.transform.Find("Content/TabsPanel/Upgrades") : null;
            var campUpgradesPanel = campPanel != null ? campPanel.transform.Find("Content/UpgradesPanel") : null;
            var content = panel.transform.Find("Content");
            var tabsParent = content != null ? content.Find("TabsPanel") : null;
            if (campTab == null || campUpgradesPanel == null || tabsParent == null)
            {
                Log.LogWarning($"{PluginInfo.PLUGIN_NAME} upgrades UI not added to {panel.GetType().Name}: " +
                               $"campPanel={campPanel != null} campTab={campTab != null} " +
                               $"campUpgradesPanel={campUpgradesPanel != null} tabsParent={tabsParent != null}");
                return;
            }

            // The upgrades panel goes right after the construction panel, like in the camp panel.
            var upgradesPanel = UnityEngine.Object.Instantiate(campUpgradesPanel, content);
            upgradesPanel.name = campUpgradesPanel.name;
            var constructionPanel = content.Find("ConstructionPanel");
            upgradesPanel.SetSiblingIndex(constructionPanel != null ? constructionPanel.GetSiblingIndex() + 1 : content.childCount - 1);

            // The tab goes second, after the main tab. It must reveal our copy of the panel, not the camp's.
            var tabObject = UnityEngine.Object.Instantiate(campTab, tabsParent);
            tabObject.name = campTab.name;
            tabObject.SetSiblingIndex(1);
            var tab = tabObject.GetComponent<AnimatedTab>();
            // The cloned tab still points at the camp panel's upgrades panel (references survive the clone),
            // so repoint it at our copy. toEnable is the list of things the tab turns on when clicked.
            tab.toEnable = new[] { upgradesPanel.GetComponent<ActivationLock>() };

            // Register the tab with the panel's tab bar and the panel with the building panel.
            // tabs is an array, so build a list, insert, and assign a new array back.
            var tabs = panel.tabsPanel.tabs.ToList();
            tabs.Insert(1, tab);
            panel.tabsPanel.tabs = tabs.ToArray();
            // SetUp wires the tab's click to the tab bar's handler (the clone's was wired to the camp panel's bar).
            tab.SetUp(panel.tabsPanel.OnTabClicked);

            panel.upgradesPanel = upgradesPanel.GetComponent<BuildingUpgradesPanel>();
            UpgradeTabs[panel] = tab;
            DebugInfo($"Added upgrades tab and panel to {panel.GetType().Name}.");
        }

        #endregion
    }
}
