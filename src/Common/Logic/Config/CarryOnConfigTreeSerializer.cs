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
        public static ITreeAttribute ToTree(CarryOnConfig config)
        {
            var tree = new TreeAttribute();
            tree.SetInt("ConfigVersion", config.ConfigVersion ?? CurrentConfigVersion);

            tree["Carryables"] = TreeSerializer.ToTree(config.Carryables);
            tree["CarryablesOnBack"] = TreeSerializer.ToTree(config.CarryablesOnBack);
            tree["Interactables"] = TreeSerializer.ToTree(config.Interactables);
            tree["CarryHungerRate"] = ToCarryHungerRateTree(config.CarryHungerRate);
            tree["CarryWalkSpeed"] = ToCarryWalkSpeedTree(config.CarryWalkSpeed);
            tree["DropCarriedOnDamage"] = ToDropCarriedOnDamageTree(config.DropCarriedOnDamage);
            tree["CarryOptions"] = TreeSerializer.ToTree(config.CarryOptions);
            tree["CarryableFilters"] = TreeSerializer.ToTree(config.CarryablesFilters);
            tree["CarriedBlockEntity"] = TreeSerializer.ToTree(config.CarriedBlockEntity);
            tree["DebuggingOptions"] = ToDebuggingOptionsTree(config.DebuggingOptions);

            return tree;
        }

        public static CarryOnConfig FromTree(ITreeAttribute tree)
        {
            var config = new CarryOnConfig();

            if (tree.HasAttribute("ConfigVersion"))
                config.ConfigVersion = tree.GetInt("ConfigVersion");

            TreeSerializer.FromTree(tree["Carryables"] as ITreeAttribute, config.Carryables);
            TreeSerializer.FromTree(tree["CarryablesOnBack"] as ITreeAttribute, config.CarryablesOnBack);
            TreeSerializer.FromTree(tree["Interactables"] as ITreeAttribute, config.Interactables);
            config.CarryHungerRate = FromCarryHungerRateTree(tree["CarryHungerRate"] as ITreeAttribute);
            config.CarryWalkSpeed = FromCarryWalkSpeedTree(tree["CarryWalkSpeed"] as ITreeAttribute);
            config.DropCarriedOnDamage = FromDropCarriedOnDamageTree(tree["DropCarriedOnDamage"] as ITreeAttribute);
            TreeSerializer.FromTree(tree["CarryOptions"] as ITreeAttribute, config.CarryOptions);
            TreeSerializer.FromTree(tree["CarryableFilters"] as ITreeAttribute, config.CarryablesFilters);
            TreeSerializer.FromTree(tree["CarriedBlockEntity"] as ITreeAttribute, config.CarriedBlockEntity);
            FromDebuggingOptionsTree(tree["DebuggingOptions"] as ITreeAttribute, config.DebuggingOptions);

            return config;
        }

        private static ITreeAttribute ToCarryHungerRateTree(CarryHungerRateConfig config)
        {
            var tree = (TreeAttribute)TreeSerializer.ToTree(config);
            tree["ModifierOverrides"] = ToModifierOverridesTree(config.ModifierOverrides);
            tree["Multipliers"] = ToMultipliersTree(config.Multipliers);
            return tree;
        }

        private static CarryHungerRateConfig FromCarryHungerRateTree(ITreeAttribute? tree)
        {
            var config = new CarryHungerRateConfig();
            if (tree == null) return config;

            TreeSerializer.FromTree(tree, config);
            config.ModifierOverrides = FromModifierOverridesTree(tree["ModifierOverrides"] as ITreeAttribute);
            config.Multipliers = FromMultipliersTree(tree["Multipliers"] as ITreeAttribute);
            CarryOnConfig.PopulateSlotDefaults(config.ModifierOverrides?.SlotDefaults, CarryCodes.Defaults.HungerRateModifier);
            return config;
        }

        private static ITreeAttribute ToCarryWalkSpeedTree(CarryWalkSpeedConfig config)
        {
            var tree = (TreeAttribute)TreeSerializer.ToTree(config);
            tree["ModifierOverrides"] = ToModifierOverridesTree(config.ModifierOverrides);
            tree["Multipliers"] = ToMultipliersTree(config.Multipliers);
            return tree;
        }

        private static CarryWalkSpeedConfig FromCarryWalkSpeedTree(ITreeAttribute? tree)
        {
            var config = new CarryWalkSpeedConfig();
            if (tree == null) return config;

            TreeSerializer.FromTree(tree, config);
            config.ModifierOverrides = FromModifierOverridesTree(tree["ModifierOverrides"] as ITreeAttribute);
            config.Multipliers = FromMultipliersTree(tree["Multipliers"] as ITreeAttribute);
            CarryOnConfig.PopulateSlotDefaults(config.ModifierOverrides?.SlotDefaults, CarryCodes.Defaults.WalkSpeedModifier);
            return config;
        }

        private static ITreeAttribute ToMultipliersTree(ModifierMultipliersConfig? multipliers)
        {
            var tree = new TreeAttribute();
            multipliers ??= new ModifierMultipliersConfig();

            tree["Global"] = ToSlotValueTree(multipliers.Global);
            tree["ByBlockMaterial"] = ToMaterialMapTree(multipliers.ByBlockMaterial);

            return tree;
        }

        private static ModifierMultipliersConfig FromMultipliersTree(ITreeAttribute? tree)
        {
            if (tree == null) return new ModifierMultipliersConfig();

            return new ModifierMultipliersConfig
            {
                Global = FromSlotValueTree(tree["Global"] as ITreeAttribute),
                ByBlockMaterial = FromMaterialMapTree(tree["ByBlockMaterial"] as ITreeAttribute)
            };
        }

        private static ITreeAttribute ToDebuggingOptionsTree(DebuggingOptionsConfig config)
        {
            var tree = (TreeAttribute)TreeSerializer.ToTree(config);

            var reportTree = new TreeAttribute();
            reportTree.SetBool("Enabled", config.CarryableReport.Enabled);
            reportTree.SetBool("OutputToLog", config.CarryableReport.OutputToLog);
            reportTree.SetString("FileFormat", config.CarryableReport.FileFormat.ToString());

            var filters = config.CarryableReport.BlockFilters;
            if (filters != null && filters.Length > 0)
                reportTree.SetStringArray("BlockFilters", filters);

            reportTree.SetString("ReportMode", config.CarryableReport.ReportMode.ToString());

            tree["CarryableReport"] = reportTree;

            return tree;
        }

        private static void FromDebuggingOptionsTree(ITreeAttribute? tree, DebuggingOptionsConfig config)
        {
            if (tree == null) return;

            TreeSerializer.FromTree(tree, config);

            if (tree["CarryableReport"] is ITreeAttribute reportTree)
            {
                if (reportTree.HasAttribute("Enabled"))
                    config.CarryableReport.Enabled = reportTree.GetBool("Enabled");
                if (reportTree.HasAttribute("OutputToLog"))
                    config.CarryableReport.OutputToLog = reportTree.GetBool("OutputToLog");
                if (reportTree.HasAttribute("FileFormat"))
                {
                    var formatStr = reportTree.GetString("FileFormat");
                    if (Enum.TryParse<ReportFileFormat>(formatStr, out var format))
                        config.CarryableReport.FileFormat = format;
                }
                if (reportTree.HasAttribute("BlockFilters"))
                    config.CarryableReport.BlockFilters = (reportTree as TreeAttribute)?.GetStringArray("BlockFilters") ?? [];
                if (reportTree.HasAttribute("ReportMode"))
                {
                    var modeStr = reportTree.GetString("ReportMode");
                    if (Enum.TryParse<ReportMode>(modeStr, out var mode))
                        config.CarryableReport.ReportMode = mode;
                }
            }
        }

        private static ITreeAttribute ToModifierOverridesTree(ModifierOverridesConfig overrides)
        {
            var tree = new TreeAttribute();
            overrides ??= new ModifierOverridesConfig();

            tree["ByBlockCode"] = ToSpeedMapTree(overrides.ByBlockCode);
            tree["ByBlockClass"] = ToSpeedMapTree(overrides.ByBlockClass);
            tree["SlotDefaults"] = ToSlotValueTree(overrides.SlotDefaults);

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
                ByBlockCode = FromSpeedMapTree(tree["ByBlockCode"] as ITreeAttribute),
                ByBlockClass = FromSpeedMapTree(tree["ByBlockClass"] as ITreeAttribute),
                SlotDefaults = FromSlotValueTree(tree["SlotDefaults"] as ITreeAttribute)
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
                item.SetString("Material", entry.Material.Value.ToString());
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

                var materialStr = item.GetString("Material");
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
