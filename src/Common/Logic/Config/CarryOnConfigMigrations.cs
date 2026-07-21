using System;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Newtonsoft.Json.Linq;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Logic
{
    internal static class CarryOnConfigMigrations
    {
        private static readonly Action<CarryOnConfig>[] Migrations =
        [
            MigrateV1ToV2,
            MigrateV2ToV3,
            MigrateV3ToV4,
            MigrateV4ToV5,
        ];

        public static void Upgrade(CarryOnConfig config)
        {
            var startVersion = config.ConfigVersion ?? 0;

            try
            {
                for (int i = startVersion; i < Migrations.Length; i++)
                {
                    Migrations[i](config);
                }

                config.ConfigVersion = CurrentConfigVersion;
            }
            catch
            {
                throw;
            }
        }

        /// <summary>Migrates from v1 (legacy flat config) to v2 (structured config).</summary>
        private static void MigrateV1ToV2(CarryOnConfig config)
        {
            if (config.ConfigVersion != null) return;

            if (config.Legacy != null)
            {
                config.Carryables.Anvil = config.Legacy.TryGetBool("AnvilEnabled", config.Carryables.Anvil);
                config.Carryables.Barrel = config.Legacy.TryGetBool("BarrelEnabled", config.Carryables.Barrel);
                config.Carryables.Bookshelf = config.Legacy.TryGetBool("BookshelfEnabled", config.Carryables.Bookshelf);
                config.Carryables.BunchOCandles = config.Legacy.TryGetBool("BunchOCandlesEnabled", config.Carryables.BunchOCandles);
                config.Carryables.Chandelier = config.Legacy.TryGetBool("ChandelierEnabled", config.Carryables.Chandelier);
                config.Carryables.ChestTrunk = config.Legacy.TryGetBool("ChestTrunkEnabled", config.Carryables.ChestTrunk);
                config.Carryables.Chest = config.Legacy.TryGetBool("ChestEnabled", config.Carryables.Chest);
                config.Carryables.Clutter = config.Legacy.TryGetBool("ClutterEnabled", config.Carryables.Clutter);
                config.Carryables.Crate = config.Legacy.TryGetBool("CrateEnabled", config.Carryables.Crate);
                config.Carryables.DisplayCase = config.Legacy.TryGetBool("DisplayCaseEnabled", config.Carryables.DisplayCase);
                config.Carryables.Flowerpot = config.Legacy.TryGetBool("FlowerpotEnabled", config.Carryables.Flowerpot);
                config.Carryables.Forge = config.Legacy.TryGetBool("ForgeEnabled", config.Carryables.Forge);
                config.Carryables.Henbox = config.Legacy.TryGetBool("HenboxEnabled", config.Carryables.Henbox);
                config.Carryables.LogWithResin = config.Legacy.TryGetBool("LogWithResinEnabled", config.Carryables.LogWithResin);
                config.Carryables.LootVessel = config.Legacy.TryGetBool("LootVesselEnabled", config.Carryables.LootVessel);
                config.Carryables.MoldRack = config.Legacy.TryGetBool("MoldRackEnabled", config.Carryables.MoldRack);
                config.Carryables.Mold = config.Legacy.TryGetBool("MoldsEnabled", config.Carryables.Mold);
                config.Carryables.Oven = config.Legacy.TryGetBool("OvenEnabled", config.Carryables.Oven);
                config.Carryables.Planter = config.Legacy.TryGetBool("PlanterEnabled", config.Carryables.Planter);
                config.Carryables.Quern = config.Legacy.TryGetBool("QuernEnabled", config.Carryables.Quern);
                config.Carryables.ReedChest = config.Legacy.TryGetBool("ReedBasketEnabled", config.Carryables.ReedChest);
                config.Carryables.Shelf = config.Legacy.TryGetBool("ShelfEnabled", config.Carryables.Shelf);
                config.Carryables.Sign = config.Legacy.TryGetBool("SignEnabled", config.Carryables.Sign);
                config.Carryables.StorageVessel = config.Legacy.TryGetBool("StorageVesselEnabled", config.Carryables.StorageVessel);
                config.Carryables.ToolRack = config.Legacy.TryGetBool("ToolRackEnabled", config.Carryables.ToolRack);
                config.Carryables.TorchHolder = config.Legacy.TryGetBool("TorchHolderEnabled", config.Carryables.TorchHolder);
                config.Carryables.Resonator = config.Legacy.TryGetBool("ResonatorEnabled", config.Carryables.Resonator);

                config.Interactables.Door = config.Legacy.TryGetBool("InteractDoorEnabled", config.Interactables.Door);
                config.Interactables.Storage = config.Legacy.TryGetBool("InteractStorageEnabled", config.Interactables.Storage);

                config.CarryOptions.BackSlotEnabled = config.Legacy.TryGetBool("BackSlotEnabled", config.CarryOptions.BackSlotEnabled);
                config.CarryablesOnBack.ChestTrunk = config.Legacy.TryGetBool("AllowChestTrunksOnBack", config.CarryablesOnBack.ChestTrunk);
                config.CarryOptions.AllowHighCapacityStorageOnBack = config.Legacy.TryGetBool("AllowLargeChestsOnBack", config.CarryOptions.AllowHighCapacityStorageOnBack);
                config.CarryablesOnBack.Crate = config.Legacy.TryGetBool("AllowCratesOnBack", config.CarryablesOnBack.Crate);
                config.CarryOptions.RemoveInteractDelayWhileCarrying = config.Legacy.TryGetBool("RemoveInteractDelayWhileCarrying", config.CarryOptions.RemoveInteractDelayWhileCarrying);
                config.CarryOptions.InteractSpeedMultiplier = config.Legacy.TryGetFloat("InteractSpeedMultiplier", config.CarryOptions.InteractSpeedMultiplier);

                config.DebuggingOptions.LoggingEnabled = config.Legacy.TryGetBool("LoggingEnabled", config.DebuggingOptions.LoggingEnabled);
                config.DebuggingOptions.DisableHarmonyPatch = !config.Legacy.TryGetBool("HarmonyPatchEnabled", !config.DebuggingOptions.DisableHarmonyPatch);

                config.CarryablesFilters.AutoMatchIgnoreMods = config.Legacy.TryGetStringArray("AutoMatchIgnoreMods", config.CarryablesFilters.AutoMatchIgnoreMods);
                config.CarryablesFilters.AllowedShapeOnlyMatches = config.Legacy.TryGetStringArray("AllowedShapeOnlyMatches", config.CarryablesFilters.AllowedShapeOnlyMatches);
                config.CarryablesFilters.RemoveBaseCarryableBehaviour = config.Legacy.TryGetStringArray("RemoveBaseCarryableBehaviour", config.CarryablesFilters.RemoveBaseCarryableBehaviour);
                config.CarryablesFilters.RemoveCarryableBehaviour = config.Legacy.TryGetStringArray("RemoveCarryableBehaviour", config.CarryablesFilters.RemoveCarryableBehaviour);
            }
        }

        /// <summary>Migrates from v2 to v3 — back-slot fields moved from CarryOptions to CarryablesOnBack.</summary>
        private static void MigrateV2ToV3(CarryOnConfig config)
        {
            if (config.ConfigVersion != 2) return;

            if (config.CarryOptions?.Legacy != null)
            {
                if (config.CarryOptions.Legacy.ContainsKey("AllowLargeChestsOnBack"))
                {
                    config.CarryOptions.AllowHighCapacityStorageOnBack = config.CarryOptions.Legacy.TryGetBool("AllowLargeChestsOnBack", config.CarryOptions.AllowHighCapacityStorageOnBack);
                }
                if (config.CarryOptions.Legacy.ContainsKey("AllowChestTrunksOnBack"))
                {
                    config.CarryablesOnBack.ChestTrunk = config.CarryOptions.Legacy.TryGetBool("AllowChestTrunksOnBack", config.CarryablesOnBack.ChestTrunk);
                }
                if (config.CarryOptions.Legacy.ContainsKey("AllowCratesOnBack"))
                {
                    config.CarryablesOnBack.Crate = config.CarryOptions.Legacy.TryGetBool("AllowCratesOnBack", config.CarryablesOnBack.Crate);
                }
            }
        }

        /// <summary>Migrates from v3 to v4 — walk-speed settings moved from CarryOptions to CarryWalkSpeed.</summary>
        private static void MigrateV3ToV4(CarryOnConfig config)
        {
            if (config.ConfigVersion != 3) return;

            if (config.CarryOptions?.Legacy != null)
            {
                config.CarryWalkSpeed.HandsEnabled = !config.CarryOptions.Legacy.TryGetBool("IgnoreCarrySpeedPenalty", false);
                config.CarryWalkSpeed.BackEnabled = !config.CarryOptions.Legacy.TryGetBool("IgnoreCarrySpeedPenalty", false);
                config.CarryWalkSpeed.HandsAllowSprint = config.CarryOptions.Legacy.TryGetBool("AllowSprintWhileCarrying", false);
                config.CarryWalkSpeed.BackAllowSprint = config.CarryOptions.Legacy.TryGetBool("AllowSprintWhileCarrying", true);

                if (config.CarryOptions.Legacy.TryGetValue("WalkSpeedOverrides", out var overridesToken)
                    && overridesToken is JObject overridesObj)
                {
                    var overrides = new ModifierOverridesConfig();

                    if (overridesObj["ByBlockCode"] is JObject byBlockCode)
                    {
                        foreach (var entry in byBlockCode.Properties())
                        {
                            if (entry.Value is JObject slotConfig)
                            {
                                overrides.ByBlockCode.Add(new SlotModifierConfig
                                {
                                    Key = entry.Name,
                                    Hands = slotConfig.Value<float?>("Hands"),
                                    Back = slotConfig.Value<float?>("Back")
                                });
                            }
                            else if (entry.Value is JValue val && val.Type == JTokenType.Float)
                            {
                                overrides.ByBlockCode.Add(new SlotModifierConfig
                                {
                                    Key = entry.Name,
                                    Hands = (float?)val,
                                    Back = (float?)val
                                });
                            }
                        }
                    }

                    if (overridesObj["ByBlockClass"] is JObject byBlockClass)
                    {
                        foreach (var entry in byBlockClass.Properties())
                        {
                            if (entry.Value is JObject slotConfig)
                            {
                                overrides.ByBlockClass.Add(new SlotModifierConfig
                                {
                                    Key = entry.Name,
                                    Hands = slotConfig.Value<float?>("Hands"),
                                    Back = slotConfig.Value<float?>("Back")
                                });
                            }
                            else if (entry.Value is JValue val && val.Type == JTokenType.Float)
                            {
                                overrides.ByBlockClass.Add(new SlotModifierConfig
                                {
                                    Key = entry.Name,
                                    Hands = (float?)val,
                                    Back = (float?)val
                                });
                            }
                        }
                    }

                    if (overridesObj["SlotDefaults"] is JObject slotDefaults)
                    {
                        overrides.SlotDefaults = new SlotModifierConfig
                        {
                            Hands = slotDefaults.Value<float?>("Hands"),
                            Back = slotDefaults.Value<float?>("Back")
                        };
                    }

                    config.CarryWalkSpeed.ModifierOverrides = overrides;
                    CarryOnConfig.PopulateSlotDefaults(config.CarryWalkSpeed.ModifierOverrides?.SlotDefaults, CarryCodes.Defaults.WalkSpeedModifier);
                }
            }
        }

        /// <summary>Migrates from v4 to v5 — no-op (multipliers and report defaults).</summary>
        private static void MigrateV4ToV5(CarryOnConfig config)
        {
            if (config.ConfigVersion != 4) return;
        }
    }
}
