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
    [HarmonyPatch]
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public static ManualLogSource Log;
        private Harmony harmony;

        // The building whose upgrades are copied to all the others.
        private const string SourceBuildingName = "Woodcutters Camp";

        // The buildings that receive those upgrades (the model "Name", not the display name).
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

        private void Awake()
        {
            Log = Logger;
            BindConfigs();
            harmony = Harmony.CreateAndPatchAll(typeof(Plugin));
            Log.LogInfo($"{PluginInfo.PLUGIN_NAME} v{PluginInfo.PLUGIN_VERSION} loaded");
        }

        private void BindConfigs()
        {
            foreach (var name in new[] { SourceBuildingName }.Concat(TargetBuildingNames))
            {
                UpgradeEnabled[name] = Config.Bind(
                    "Buildings",
                    ConfigKey(name),
                    true,
                    "If true, this building can be upgraded. Takes effect the next time the game starts.");
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
        [HarmonyPatch(typeof(CampModel), nameof(CampModel.AreUpgradesUnlockd))]
        [HarmonyPostfix]
        private static void AllowWoodcutterUpgrades(CampModel __instance, ref bool __result)
        {
            if (!__result && __instance.Name.Contains("Woodcutter") && IsUpgradeEnabled(SourceBuildingName))
                __result = true;
        }

        #endregion

        #region Copying the upgrade data

        // Text for our custom LocaText keys (key -> English text), filled while the perks are cloned.
        private static readonly Dictionary<string, string> CustomTexts = new Dictionary<string, string>();

        private static bool upgradesCopied;

        // Runs once the game has finished loading, before any match. All building models exist by then.
        [HarmonyPatch(typeof(MainController), "OnServicesReady")]
        [HarmonyPostfix]
        private static void OnServicesReady()
        {
            try { CopyUpgradesToTargets(); }
            catch (Exception e) { Log.LogError($"{PluginInfo.PLUGIN_NAME} copying upgrades failed: {e}"); }
        }

        // Lets the game find the text for our custom keys.
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

            var allBuildings = Serviceable.Settings.Buildings;

            // DEBUG: every upgradable building the game has, to check the TargetBuildingNames against.
            foreach (var b in allBuildings.OfType<UpgradableBuildingModel>())
                Log.LogInfo($"[Debug] upgradable model: Name='{b.Name}' type={b.GetType().Name} levels={b.levels?.Length}");

            var source = allBuildings.OfType<CampModel>().FirstOrDefault(b => b.Name == SourceBuildingName);
            if (source?.levels == null)
            {
                Log.LogWarning($"{PluginInfo.PLUGIN_NAME} upgrades not copied: '{SourceBuildingName}' not found");
                return;
            }

            var levels = CreateLevelsFrom(source);

            foreach (var name in TargetBuildingNames)
            {
                if (!IsUpgradeEnabled(name))
                {
                    Log.LogInfo($"{PluginInfo.PLUGIN_NAME} upgrades for '{name}' are disabled in the config, skipped");
                    continue;
                }

                var target = allBuildings.OfType<UpgradableBuildingModel>().FirstOrDefault(b => b.Name == name);
                if (target == null)
                {
                    Log.LogWarning($"{PluginInfo.PLUGIN_NAME} building '{name}' not found, skipped");
                    continue;
                }

                target.levels = levels;
                Log.LogInfo($"{PluginInfo.PLUGIN_NAME} {target.Name} now has {levels.Length} upgrade levels copied from {source.Name}");
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
        private static BuildingPerkModel ClonePerk(BuildingPerkModel original, int level, int perkIndex)
        {
            var clone = UnityEngine.Object.Instantiate(original);
            clone.name = $"{original.Name} (copy)";

            GetPerkText(original, out var displayName, out var description);
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
                    description = $"Adds {automaton.amount} automaton to this building.";
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

        // The upgrades tab we added to each panel.
        private static readonly Dictionary<ProductionBuildingPanel, AnimatedTab> UpgradeTabs =
            new Dictionary<ProductionBuildingPanel, AnimatedTab>();

        [HarmonyPatch(typeof(GameController), "StartGame")]
        [HarmonyPostfix]
        private static void OnGameStart()
        {
            try
            {
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

        private static void UpdateUpgradesTab(ProductionBuildingPanel panel, Building building)
        {
            var hasTab = UpgradeTabs.TryGetValue(panel, out var tab);
            // DEBUG: why a building does or does not show the tab.
            Log.LogInfo($"[Debug] panel shown: building='{building.BuildingModel.Name}' hasUpgrades={building.HasUpgrades} " +
                        $"levels={(building.BuildingModel as UpgradableBuildingModel)?.levels?.Length} tabAdded={hasTab}");
            if (hasTab)
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
            tab.toEnable = new[] { upgradesPanel.GetComponent<ActivationLock>() };

            // Register the tab with the panel's tab bar and the panel with the building panel.
            var tabs = panel.tabsPanel.tabs.ToList();
            tabs.Insert(1, tab);
            panel.tabsPanel.tabs = tabs.ToArray();
            tab.SetUp(panel.tabsPanel.OnTabClicked);

            panel.upgradesPanel = upgradesPanel.GetComponent<BuildingUpgradesPanel>();
            UpgradeTabs[panel] = tab;
            Log.LogInfo($"{PluginInfo.PLUGIN_NAME} added upgrades tab and panel to {panel.GetType().Name}");
        }

        #endregion
    }
}
