using System;
using System.Collections.Generic;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Logic
{
    internal static class CarryOnConfigTreeSerializer
    {
        private const string KeyConfigVersion = "ConfigVersion";
        private const string KeyCarryables = "Carryables";
        private const string KeyCarryablesOnBack = "CarryablesOnBack";
        private const string KeyInteractables = "Interactables";
        private const string KeyCarryHungerRate = "CarryHungerRate";
        private const string KeyCarryWalkSpeed = "CarryWalkSpeed";
        private const string KeyDropCarriedOnDamage = "DropCarriedOnDamage";
        private const string KeyCarryOptions = "CarryOptions";
        private const string KeyCarryableFilters = "CarryableFilters";
        private const string KeyCarriedBlockEntity = "CarriedBlockEntity";
        private const string KeyDebuggingOptions = "DebuggingOptions";
        private const string KeyModifierOverrides = "ModifierOverrides";
        private const string KeyMultipliers = "Multipliers";
        private const string KeyGlobal = "Global";
        private const string KeyByBlockMaterial = "ByBlockMaterial";
        private const string KeyByBlockCode = "ByBlockCode";
        private const string KeyByBlockClass = "ByBlockClass";
        private const string KeySlotDefaults = "SlotDefaults";
        private const string KeyCarryableReport = "CarryableReport";
        private const string KeyEnabled = "Enabled";
        private const string KeyOutputToLog = "OutputToLog";
        private const string KeyFileFormat = "FileFormat";
        private const string KeyBlockFilters = "BlockFilters";
        private const string KeyReportMode = "ReportMode";
        private const string KeyMaterial = "Material";
        private const string KeyBackpackTypes = "BackpackTypes";

        public static ITreeAttribute ToTree(CarryOnConfig config)
        {
            var tree = new TreeAttribute();
            tree.SetInt(KeyConfigVersion, config.ConfigVersion ?? CurrentConfigVersion);

            tree[KeyCarryables] = TreeSerializer.ToTree(config.Carryables);
            tree[KeyCarryablesOnBack] = TreeSerializer.ToTree(config.CarryablesOnBack);
            tree[KeyInteractables] = TreeSerializer.ToTree(config.Interactables);
            tree[KeyCarryHungerRate] = ToCarryHungerRateTree(config.CarryHungerRate);
            tree[KeyCarryWalkSpeed] = ToCarryWalkSpeedTree(config.CarryWalkSpeed);
            tree[KeyDropCarriedOnDamage] = ToDropCarriedOnDamageTree(config.DropCarriedOnDamage);
            tree[KeyCarryOptions] = TreeSerializer.ToTree(config.CarryOptions);
            tree[KeyCarryableFilters] = TreeSerializer.ToTree(config.CarryablesFilters);
            tree[KeyCarriedBlockEntity] = TreeSerializer.ToTree(config.CarriedBlockEntity);
            tree[KeyDebuggingOptions] = ToDebuggingOptionsTree(config.DebuggingOptions);
            tree[KeyBackpackTypes] = ToBackpackTypesTree(config.BackpackTypes);

            return tree;
        }

        public static CarryOnConfig FromTree(ITreeAttribute tree)
        {
            var config = new CarryOnConfig();

            if (tree.HasAttribute(KeyConfigVersion))
                config.ConfigVersion = tree.GetInt(KeyConfigVersion);

            TreeSerializer.FromTree(tree[KeyCarryables] as ITreeAttribute, config.Carryables);
            TreeSerializer.FromTree(tree[KeyCarryablesOnBack] as ITreeAttribute, config.CarryablesOnBack);
            TreeSerializer.FromTree(tree[KeyInteractables] as ITreeAttribute, config.Interactables);
            config.CarryHungerRate = FromCarryHungerRateTree(tree[KeyCarryHungerRate] as ITreeAttribute);
            config.CarryWalkSpeed = FromCarryWalkSpeedTree(tree[KeyCarryWalkSpeed] as ITreeAttribute);
            config.DropCarriedOnDamage = FromDropCarriedOnDamageTree(tree[KeyDropCarriedOnDamage] as ITreeAttribute);
            TreeSerializer.FromTree(tree[KeyCarryOptions] as ITreeAttribute, config.CarryOptions);
            TreeSerializer.FromTree(tree[KeyCarryableFilters] as ITreeAttribute, config.CarryablesFilters);
            TreeSerializer.FromTree(tree[KeyCarriedBlockEntity] as ITreeAttribute, config.CarriedBlockEntity);
            FromDebuggingOptionsTree(tree[KeyDebuggingOptions] as ITreeAttribute, config.DebuggingOptions);

            // BackpackTypes has no [TreeValue] members, so it needs an explicit tree section. A missing
            // key intentionally keeps the property-initializer default; a present-but-empty section is a
            // user's explicit "no backpack compensation via mapping" and is preserved.
            var backpackTypes = FromBackpackTypesTree(tree[KeyBackpackTypes] as ITreeAttribute);
            if (backpackTypes != null)
            {
                config.BackpackTypes = backpackTypes;
                config.InvalidateBackpackCache();
            }

            config.EnsureSlotDefaults();

            return config;
        }

        private static ITreeAttribute ToCarryHungerRateTree(CarryHungerRateConfig config)
        {
            var tree = (TreeAttribute)TreeSerializer.ToTree(config);
            tree[KeyModifierOverrides] = ToModifierOverridesTree(config.ModifierOverrides);
            tree[KeyMultipliers] = ToMultipliersTree(config.Multipliers);
            return tree;
        }

        private static CarryHungerRateConfig FromCarryHungerRateTree(ITreeAttribute? tree)
        {
            var config = new CarryHungerRateConfig();
            if (tree == null) return config;

            TreeSerializer.FromTree(tree, config);
            config.ModifierOverrides = FromModifierOverridesTree(tree[KeyModifierOverrides] as ITreeAttribute);
            config.Multipliers = FromMultipliersTree(tree[KeyMultipliers] as ITreeAttribute);
            return config;
        }

        private static ITreeAttribute ToCarryWalkSpeedTree(CarryWalkSpeedConfig config)
        {
            var tree = (TreeAttribute)TreeSerializer.ToTree(config);
            tree[KeyModifierOverrides] = ToModifierOverridesTree(config.ModifierOverrides);
            tree[KeyMultipliers] = ToMultipliersTree(config.Multipliers);
            return tree;
        }

        private static CarryWalkSpeedConfig FromCarryWalkSpeedTree(ITreeAttribute? tree)
        {
            var config = new CarryWalkSpeedConfig();
            if (tree == null) return config;

            TreeSerializer.FromTree(tree, config);
            config.ModifierOverrides = FromModifierOverridesTree(tree[KeyModifierOverrides] as ITreeAttribute);
            config.Multipliers = FromMultipliersTree(tree[KeyMultipliers] as ITreeAttribute);
            return config;
        }

        private static ITreeAttribute ToMultipliersTree(ModifierMultipliersConfig? multipliers)
        {
            var tree = new TreeAttribute();
            multipliers ??= new ModifierMultipliersConfig();

            tree[KeyGlobal] = ToSlotValueTree(multipliers.Global);
            tree[KeyByBlockMaterial] = ToMaterialMapTree(multipliers.ByBlockMaterial);

            return tree;
        }

        private static ModifierMultipliersConfig FromMultipliersTree(ITreeAttribute? tree)
        {
            if (tree == null) return new ModifierMultipliersConfig();

            return new ModifierMultipliersConfig
            {
                Global = FromSlotValueTree(tree[KeyGlobal] as ITreeAttribute),
                ByBlockMaterial = FromMaterialMapTree(tree[KeyByBlockMaterial] as ITreeAttribute)
            };
        }

        private static ITreeAttribute ToDebuggingOptionsTree(DebuggingOptionsConfig config)
        {
            var tree = (TreeAttribute)TreeSerializer.ToTree(config);

            var reportTree = new TreeAttribute();
            reportTree.SetBool(KeyEnabled, config.CarryableReport.Enabled);
            reportTree.SetBool(KeyOutputToLog, config.CarryableReport.OutputToLog);
            reportTree.SetString(KeyFileFormat, config.CarryableReport.FileFormat.ToString());

            var filters = config.CarryableReport.BlockFilters;
            if (filters != null && filters.Length > 0)
                reportTree.SetStringArray(KeyBlockFilters, filters);

            reportTree.SetString(KeyReportMode, config.CarryableReport.ReportMode.ToString());

            tree[KeyCarryableReport] = reportTree;

            return tree;
        }

        private static void FromDebuggingOptionsTree(ITreeAttribute? tree, DebuggingOptionsConfig config)
        {
            if (tree == null) return;

            TreeSerializer.FromTree(tree, config);

            if (tree[KeyCarryableReport] is ITreeAttribute reportTree)
            {
                if (reportTree.HasAttribute(KeyEnabled))
                    config.CarryableReport.Enabled = reportTree.GetBool(KeyEnabled);
                if (reportTree.HasAttribute(KeyOutputToLog))
                    config.CarryableReport.OutputToLog = reportTree.GetBool(KeyOutputToLog);
                if (reportTree.HasAttribute(KeyFileFormat))
                {
                    var formatStr = reportTree.GetString(KeyFileFormat);
                    if (Enum.TryParse<ReportFileFormat>(formatStr, out var format))
                        config.CarryableReport.FileFormat = format;
                }
                if (reportTree.HasAttribute(KeyBlockFilters))
                    config.CarryableReport.BlockFilters = (reportTree as TreeAttribute)?.GetStringArray(KeyBlockFilters) ?? [];
                if (reportTree.HasAttribute(KeyReportMode))
                {
                    var modeStr = reportTree.GetString(KeyReportMode);
                    if (Enum.TryParse<ReportMode>(modeStr, out var mode))
                        config.CarryableReport.ReportMode = mode;
                }
            }
        }

        private static ITreeAttribute ToModifierOverridesTree(ModifierOverridesConfig overrides)
        {
            var tree = new TreeAttribute();
            overrides ??= new ModifierOverridesConfig();

            tree[KeyByBlockCode] = ToSpeedMapTree(overrides.ByBlockCode);
            tree[KeyByBlockClass] = ToSpeedMapTree(overrides.ByBlockClass);
            tree[KeySlotDefaults] = ToSlotValueTree(overrides.SlotDefaults);

            return tree;
        }

        private static ModifierOverridesConfig FromModifierOverridesTree(ITreeAttribute? tree)
        {
            if (tree == null)
            {
                return new ModifierOverridesConfig();
            }

            return new ModifierOverridesConfig
            {
                ByBlockCode = FromSpeedMapTree(tree[KeyByBlockCode] as ITreeAttribute),
                ByBlockClass = FromSpeedMapTree(tree[KeyByBlockClass] as ITreeAttribute),
                SlotDefaults = FromSlotValueTree(tree[KeySlotDefaults] as ITreeAttribute)
            };
        }

        private static ITreeAttribute ToSpeedMapTree(List<SlotModifierConfig> list)
        {
            var tree = new TreeAttribute();

            if (list == null)
            {
                return tree;
            }

            foreach (var entry in list)
            {
                if (string.IsNullOrWhiteSpace(entry.Key) || entry.IsEmpty)
                {
                    continue;
                }

                tree[entry.Key.Trim()] = ToSlotValueTree(entry);
            }

            return tree;
        }

        private static List<SlotModifierConfig> FromSpeedMapTree(ITreeAttribute? tree)
        {
            var list = new List<SlotModifierConfig>();

            if (tree is not TreeAttribute speedTree)
            {
                return list;
            }

            foreach (var key in speedTree.Keys)
            {
                if (string.IsNullOrWhiteSpace(key))
                {
                    continue;
                }

                var value = FromSlotValueTree(speedTree[key] as ITreeAttribute);
                var entry = new SlotModifierConfig
                {
                    Key = key.Trim(),
                    Hands = value.Hands,
                    Back = value.Back
                };

                if (entry.IsEmpty)
                {
                    continue;
                }

                list.Add(entry);
            }

            return list;
        }

        private static ITreeAttribute ToSlotValueTree(SlotValueConfig speed)
        {
            var tree = new TreeAttribute();
            speed ??= new SlotValueConfig();

            if (speed.Hands.HasValue)
            {
                tree.SetFloat(CarrySlot.Hands.ToString(), speed.Hands.Value);
            }

            if (speed.Back.HasValue)
            {
                tree.SetFloat(CarrySlot.Back.ToString(), speed.Back.Value);
            }

            return tree;
        }

        private static SlotValueConfig FromSlotValueTree(ITreeAttribute? tree)
        {
            var speed = new SlotValueConfig();

            if (tree == null)
            {
                return speed;
            }

            if (tree.HasAttribute(CarrySlot.Hands.ToString()))
            {
                speed.Hands = tree.GetFloat(CarrySlot.Hands.ToString());
            }

            if (tree.HasAttribute(CarrySlot.Back.ToString()))
            {
                speed.Back = tree.GetFloat(CarrySlot.Back.ToString());
            }

            return speed;
        }

        private static ITreeAttribute ToMaterialMapTree(List<SlotMaterialMultiplierConfig> list)
        {
            var tree = new TreeAttribute();

            if (list == null) return tree;

            for (int i = 0; i < list.Count; i++)
            {
                var entry = list[i];
                if (entry == null || entry.Material == null) continue;

                var item = new TreeAttribute();
                item.SetString(KeyMaterial, entry.Material.Value.ToString());
                if (entry.Hands.HasValue)
                    item.SetFloat(CarrySlot.Hands.ToString(), entry.Hands.Value);
                if (entry.Back.HasValue)
                    item.SetFloat(CarrySlot.Back.ToString(), entry.Back.Value);

                tree[i.ToString()] = item;
            }

            return tree;
        }

        private static List<SlotMaterialMultiplierConfig> FromMaterialMapTree(ITreeAttribute? tree)
        {
            var list = new List<SlotMaterialMultiplierConfig>();

            if (tree is not TreeAttribute materialTree) return list;

            foreach (var key in materialTree.Keys)
            {
                if (!int.TryParse(key, out _)) continue;

                var item = materialTree[key] as ITreeAttribute;
                if (item == null) continue;

                var materialStr = item.GetString(KeyMaterial);
                if (string.IsNullOrWhiteSpace(materialStr)) continue;

                if (!Enum.TryParse<EnumBlockMaterial>(materialStr, true, out var material)) continue;

                var entry = new SlotMaterialMultiplierConfig
                {
                    Material = material
                };

                if (item.HasAttribute(CarrySlot.Hands.ToString()))
                    entry.Hands = item.GetFloat(CarrySlot.Hands.ToString());
                if (item.HasAttribute(CarrySlot.Back.ToString()))
                    entry.Back = item.GetFloat(CarrySlot.Back.ToString());

                list.Add(entry);
            }

            return list;
        }

        private static ITreeAttribute ToBackpackTypesTree(IDictionary<string, string[]>? backpackTypes)
        {
            var tree = new TreeAttribute();
            if (backpackTypes == null) return tree;

            foreach (var entry in backpackTypes)
            {
                if (string.IsNullOrWhiteSpace(entry.Key)) continue;

                tree.SetStringArray(entry.Key.Trim(), entry.Value ?? []);
            }

            return tree;
        }

        private static IDictionary<string, string[]>? FromBackpackTypesTree(ITreeAttribute? tree)
        {
            // Null (key absent) means "keep the default"; an empty dictionary means the user
            // deliberately mapped no backpack codes.
            if (tree is not TreeAttribute backpackTree) return null;

            var result = new Dictionary<string, string[]>();
            foreach (var key in backpackTree.Keys)
            {
                if (string.IsNullOrWhiteSpace(key)) continue;

                result[key.Trim()] = backpackTree.GetStringArray(key) ?? [];
            }

            return result;
        }

        private static ITreeAttribute ToDropCarriedOnDamageTree(DropCarriedOnDamageConfig config)
        {
            return (TreeAttribute)TreeSerializer.ToTree(config);
        }

        private static DropCarriedOnDamageConfig FromDropCarriedOnDamageTree(ITreeAttribute? tree)
        {
            var config = new DropCarriedOnDamageConfig();
            if (tree != null)
            {
                TreeSerializer.FromTree(tree, config);
            }
            return config;
        }
    }
}
