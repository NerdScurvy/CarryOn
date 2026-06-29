using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;

namespace CarryOn.Client.Logic.TransformGroupResolvers
{

    /// <summary>
    /// Transform group resolver for plant containers. 
    /// It handles blocks that are plant containers or flower pots and generates candidate transform groups based on the type of plant contained within.
    /// </summary>
    public class PlantContainerTransformGroupResolver : IRootTransformGroupResolver, IAttachmentTransformGroupResolver
    {
        public string ResolverCode => "plant-container";

        private static readonly Regex SaplingTypeRegex = new("^sapling-(?<wood>.+)-(?:free|snow)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex FlowerTypeRegex = new("^flower-(?<flower>.+)-(?:free|snow)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex MushroomTypeRegex = new("^mushroom-(?<mushroom>[^-]+)-", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex FernTypeRegex = new("^fern-(?<fern>[^-]+)(?:-(?:free|snow))?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex CrotonTypeRegex = new("^flower-croton-(?<croton>(?:small|medium)-.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex CactusFamilyRegex = new("^(?<family>[^-]*cactus)(?:-(?<variant>.+))?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex TallPlantTypeRegex = new("^(?<plant>tallgrass)(?:-.*)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        bool IRootTransformGroupResolver.TryResolve(ICoreAPI api, CarriedBlock carried, string baseGroup, out IList<string>? candidates)
        {
            candidates = null;

            if (api?.World == null || carried?.Block == null || string.IsNullOrEmpty(baseGroup))
                return false;

            var containerSlots = BlockUtils.GetContainerSlots(carried);
            if (containerSlots == null || containerSlots.Count == 0)
                return false;

            var plantedGroup = "planted";
            var primaryGroup = $"{baseGroup}-{plantedGroup}";

            var list = new List<string> { primaryGroup };

            var plantItemStack = containerSlots.GetItemstack("0");
            if (plantItemStack != null && plantItemStack.Class == EnumItemClass.Block)
            {
                var plantBlock = api.World.GetBlock(plantItemStack.Id);
                if (plantBlock != null)
                    PopulatePrimaryCandidates(list, api, carried, plantBlock, primaryGroup, plantedGroup);
            }
            else if (plantItemStack != null && plantItemStack.Class == EnumItemClass.Item)
            {
                var plantItem = api.World.GetItem(plantItemStack.Id);
                if (plantItem is not null && plantItem.GetType().Name == "ItemCattailRoot")
                    PopulateCattailPrimaryCandidates(list, plantItem, primaryGroup, plantedGroup);
            }

            candidates = list;
            return true;
        }

        bool IAttachmentTransformGroupResolver.TryResolve(ICoreAPI api, CarriedBlock carried, string baseGroup, out AttachmentResolveResult? result)
        {
            result = null;

            if (api?.World == null || carried?.Block == null || string.IsNullOrEmpty(baseGroup))
                return false;

            var containerSlots = BlockUtils.GetContainerSlots(carried);
            if (containerSlots == null || containerSlots.Count == 0)
                return false;

            var plantedGroup = "planted";
            var resolveResult = new AttachmentResolveResult();

            var plantItemStack = containerSlots.GetItemstack("0");
            if (plantItemStack == null)
                return false;

            if (plantItemStack.Class == EnumItemClass.Block)
            {
                var plantBlock = api.World.GetBlock(plantItemStack.Id);
                if (plantBlock != null)
                    PopulateAttachmentCandidates(resolveResult, api, carried, plantBlock, plantedGroup);
            }
            else if (plantItemStack.Class == EnumItemClass.Item)
            {
                var plantItem = api.World.GetItem(plantItemStack.Id);
                if (plantItem is not null && plantItem.GetType().Name == "ItemCattailRoot")
                    PopulateCattailAttachmentCandidates(resolveResult, plantItem, plantedGroup);
            }

            if (resolveResult.Candidates.Count > 0)
                resolveResult.EnableVertexWarp = true;

            result = resolveResult;
            return true;
        }

        private void PopulatePrimaryCandidates(IList<string> candidates, ICoreAPI api, CarriedBlock carried, Block plantBlock, string primaryGroup, string plantedGroup)
        {
            if (plantBlock.Class == "BlockSapling")
            {
                var saplingType = ExtractSaplingType(plantBlock.Code?.Path);
                candidates.Add(primaryGroup + "-sapling");
                if (!string.IsNullOrEmpty(saplingType))
                    candidates.Add(primaryGroup + "-sapling-" + saplingType);
            }
            else if (plantBlock.Class == "BlockPlant" && IsCrotonCode(plantBlock.Code?.Path))
            {
                var crotonType = ExtractCrotonType(plantBlock.Code?.Path);
                candidates.Add(primaryGroup + "-croton");
                if (!string.IsNullOrEmpty(crotonType))
                    candidates.Add(primaryGroup + "-croton-" + crotonType);
            }
            else if (IsCactusCode(plantBlock.Code?.Path))
            {
                var cactusFamily = ExtractCactusFamily(plantBlock.Code?.Path);
                candidates.Add(primaryGroup + "-cactus");
                if (!string.IsNullOrEmpty(cactusFamily))
                    candidates.Add(primaryGroup + "-cactus-" + cactusFamily);
            }
            else if (plantBlock.Class == "BlockPlant" && IsTallFernCode(plantBlock.Code?.Path))
            {
                candidates.Add(primaryGroup + "-fern");
                candidates.Add(primaryGroup + "-fern-tallfern");
            }
            else if (plantBlock.Class is "BlockLupine" or "BlockPlant")
            {
                var flowerType = ExtractFlowerType(plantBlock.Code?.Path);
                if (!string.IsNullOrEmpty(flowerType))
                {
                    candidates.Add(primaryGroup + "-flower");
                    if (plantBlock.Class == "BlockLupine")
                    {
                        candidates.Add(primaryGroup + "-lupine");
                        candidates.Add(primaryGroup + "-lupine-" + flowerType);
                    }
                    else
                    {
                        candidates.Add(primaryGroup + "-flower-" + flowerType);
                    }
                }
            }
            else if (plantBlock.Class == "BlockMushroom")
            {
                var mushroomType = ExtractMushroomType(plantBlock.Code?.Path);
                candidates.Add(primaryGroup + "-mushroom");
                if (!string.IsNullOrEmpty(mushroomType))
                    candidates.Add(primaryGroup + "-mushroom-" + mushroomType);
            }
            else if (plantBlock.Class == "BlockFern")
            {
                var fernType = ExtractFernType(plantBlock.Code?.Path);
                candidates.Add(primaryGroup + "-fern");
                if (!string.IsNullOrEmpty(fernType))
                    candidates.Add(primaryGroup + "-fern-" + fernType);
            }
        }

        private void PopulateCattailPrimaryCandidates(IList<string> candidates, Item plantItem, string primaryGroup, string plantedGroup)
        {
            var plantCodePath = plantItem.Code?.Path ?? string.Empty;
            var baseType = plantCodePath.EndsWith("root", StringComparison.Ordinal)
                ? plantCodePath.Substring(0, plantCodePath.Length - 4)
                : plantCodePath;

            candidates.Add(primaryGroup + "-reed-" + baseType);
            candidates.Add(primaryGroup + "-reed");
        }

        private void PopulateAttachmentCandidates(AttachmentResolveResult result, ICoreAPI api, CarriedBlock carried, Block plantBlock, string plantedGroup)
        {
            if (plantBlock.Class == "BlockSapling")
                AddSaplingCandidates(result, plantBlock, plantedGroup);
            else if (plantBlock.Class == "BlockPlant" && IsCrotonCode(plantBlock.Code?.Path))
                AddCrotonCandidates(result, plantBlock, plantedGroup);
            else if (IsCactusCode(plantBlock.Code?.Path))
                AddCactusCandidates(result, api, carried, plantBlock, plantedGroup);
            else if (plantBlock.Class == "BlockPlant" && IsTallFernCode(plantBlock.Code?.Path))
                AddTallFernCandidates(result, carried, plantedGroup);
            else if (plantBlock.Class is "BlockLupine" or "BlockPlant")
                AddFlowerOrLupineCandidates(result, plantBlock, plantedGroup);
            else if (plantBlock.Class == "BlockMushroom")
                AddMushroomCandidates(result, plantBlock, carried, plantedGroup);
            else if (plantBlock.Class == "BlockFern")
                AddFernCandidates(result, plantBlock, carried, plantedGroup);
        }

        private void AddSaplingCandidates(AttachmentResolveResult result, Block plantBlock, string plantedGroup)
        {
            var saplingType = ExtractSaplingType(plantBlock.Code?.Path);
            var saplingGroup = plantedGroup + "-sapling";
            if (!string.IsNullOrEmpty(saplingType))
            {
                result.Candidates.Add(new CarriedGroupCandidateSet
                {
                    Groups = { saplingGroup + "-" + saplingType }
                });
            }
        }

        private void AddCrotonCandidates(AttachmentResolveResult result, Block plantBlock, string plantedGroup)
        {
            var crotonType = ExtractCrotonType(plantBlock.Code?.Path);
            var crotonGroup = plantedGroup + "-croton";

            var groupCandidates = new CarriedGroupCandidateSet
            {
                AssetTypeIfUnset = CarriedGroupAssetType.Item,
                AssetNameIfUnset = string.IsNullOrEmpty(crotonType)
                    ? null
                    : "carryon:croton-" + crotonType
            };

            if (!string.IsNullOrEmpty(crotonType))
                groupCandidates.Groups.Add(crotonGroup + "-" + crotonType);

            groupCandidates.Groups.Add(crotonGroup);
            result.Candidates.Add(groupCandidates);
        }

        private void AddCactusCandidates(AttachmentResolveResult result, ICoreAPI api, CarriedBlock carried, Block plantBlock, string plantedGroup)
        {
            var cactusFamily = ExtractCactusFamily(plantBlock.Code?.Path);
            var cactusGroup = plantedGroup + "-cactus";

            var groupCandidates = new CarriedGroupCandidateSet
            {
                AssetTypeIfUnset = CarriedGroupAssetType.Item,
                AssetNameIfUnset = string.IsNullOrEmpty(cactusFamily)
                    ? null
                    : "carryon:cactus-" + cactusFamily + (IsLargePlantContainer(carried.Block) ? "-large" : string.Empty)
            };

            if (!string.IsNullOrEmpty(cactusFamily))
                groupCandidates.Groups.Add(cactusGroup + "-" + cactusFamily);

            groupCandidates.Groups.Add(cactusGroup);
            result.Candidates.Add(groupCandidates);
        }

        private void AddTallFernCandidates(AttachmentResolveResult result, CarriedBlock carried, string plantedGroup)
        {
            const string tallFernType = "tallfern";
            var fernGroup = plantedGroup + "-fern";
            var useLargeFernVariant = IsLargePlantContainer(carried.Block);

            var groupCandidates = new CarriedGroupCandidateSet
            {
                AssetTypeIfUnset = CarriedGroupAssetType.Item,
                AssetNameIfUnset = "carryon:" + tallFernType + (useLargeFernVariant ? "-large" : string.Empty)
            };

            groupCandidates.Groups.Add(fernGroup + "-" + tallFernType);
            groupCandidates.Groups.Add(fernGroup);
            result.Candidates.Add(groupCandidates);
        }

        private void AddFlowerOrLupineCandidates(AttachmentResolveResult result, Block plantBlock, string plantedGroup)
        {
            var flowerType = ExtractFlowerType(plantBlock.Code?.Path);
            var flowerGroup = plantedGroup + "-flower";

            var groupCandidates = new CarriedGroupCandidateSet
            {
                AssetTypeIfUnset = CarriedGroupAssetType.Item,
                AssetNameIfUnset = string.IsNullOrEmpty(plantBlock.Code?.Path) ? null : "carryon:" + plantBlock.Code.Path
            };

            if (!string.IsNullOrEmpty(flowerType))
            {
                groupCandidates.Groups.Add(plantedGroup + "-" + flowerType);
                groupCandidates.Groups.Add(flowerGroup + "-" + flowerType);
            }

            groupCandidates.Groups.Add(flowerGroup);
            result.Candidates.Add(groupCandidates);
        }

        private void AddMushroomCandidates(AttachmentResolveResult result, Block plantBlock, CarriedBlock carried, string plantedGroup)
        {
            var mushroomType = ExtractMushroomType(plantBlock.Code?.Path);
            var mushroomGroup = plantedGroup + "-mushroom";
            var mushroomCode = plantBlock.Code?.Path;
            var useLargeMushroomVariant = IsLargePlantContainer(carried.Block);

            var groupCandidates = new CarriedGroupCandidateSet
            {
                AssetTypeIfUnset = CarriedGroupAssetType.Item,
                AssetNameIfUnset = string.IsNullOrEmpty(mushroomCode)
                    ? null
                    : "carryon:" + mushroomCode + (useLargeMushroomVariant ? "-large" : string.Empty)
            };

            if (!string.IsNullOrEmpty(mushroomType))
                groupCandidates.Groups.Add(mushroomGroup + "-" + mushroomType);

            groupCandidates.Groups.Add(mushroomGroup);
            result.Candidates.Add(groupCandidates);
        }

        private void AddFernCandidates(AttachmentResolveResult result, Block plantBlock, CarriedBlock carried, string plantedGroup)
        {
            var fernType = ExtractFernType(plantBlock.Code?.Path);
            var fernGroup = plantedGroup + "-fern";
            var useLargeFernVariant = IsLargePlantContainer(carried.Block);

            var groupCandidates = new CarriedGroupCandidateSet
            {
                AssetTypeIfUnset = CarriedGroupAssetType.Item,
                AssetNameIfUnset = string.IsNullOrEmpty(fernType)
                    ? null
                    : "carryon:fern-" + fernType + (useLargeFernVariant ? "-large" : string.Empty)
            };

            if (!string.IsNullOrEmpty(fernType))
                groupCandidates.Groups.Add(fernGroup + "-" + fernType);

            groupCandidates.Groups.Add(fernGroup);
            result.Candidates.Add(groupCandidates);
        }

        private void PopulateCattailAttachmentCandidates(AttachmentResolveResult result, Item plantItem, string plantedGroup)
        {
            var plantCodePath = plantItem.Code?.Path ?? string.Empty;
            var baseType = plantCodePath.EndsWith("root", StringComparison.Ordinal)
                ? plantCodePath.Substring(0, plantCodePath.Length - 4)
                : plantCodePath;

            var reedGroup = plantedGroup + "-reed";
            var typeGroup = reedGroup + "-" + baseType;

            var candidates = new CarriedGroupCandidateSet();
            candidates.Groups.Add(typeGroup);
            result.Candidates.Add(candidates);
        }

        private string? ExtractSaplingType(string? codePath)
        {
            if (string.IsNullOrEmpty(codePath)) return null;

            var match = SaplingTypeRegex.Match(codePath);
            return match.Success ? match.Groups["wood"].Value : null;
        }

        private string? ExtractFlowerType(string? codePath)
        {
            if (string.IsNullOrEmpty(codePath)) return null;

            var match = FlowerTypeRegex.Match(codePath);
            if (match.Success) return match.Groups["flower"].Value;

            match = TallPlantTypeRegex.Match(codePath);
            return match.Success ? match.Groups["plant"].Value : null;
        }

        private string? ExtractMushroomType(string? codePath)
        {
            if (string.IsNullOrEmpty(codePath)) return null;

            var match = MushroomTypeRegex.Match(codePath);
            return match.Success ? match.Groups["mushroom"].Value : null;
        }

        private string? ExtractFernType(string? codePath)
        {
            if (string.IsNullOrEmpty(codePath)) return null;

            var match = FernTypeRegex.Match(codePath);
            return match.Success ? match.Groups["fern"].Value : null;
        }

        private string? ExtractCrotonType(string? codePath)
        {
            if (string.IsNullOrEmpty(codePath)) return null;
            var match = CrotonTypeRegex.Match(codePath);
            return match.Success ? match.Groups["croton"].Value : null;
        }

        private string? ExtractCactusFamily(string? codePath)
        {
            if (string.IsNullOrEmpty(codePath)) return null;
            var match = CactusFamilyRegex.Match(codePath);
            return match.Success ? match.Groups["family"].Value : null;
        }

        private static bool IsCrotonCode(string? codePath)
        {
            return !string.IsNullOrEmpty(codePath)
            && codePath.StartsWith("flower-croton-", StringComparison.Ordinal);
        }

        private bool IsCactusCode(string? codePath)
        {
            return !string.IsNullOrEmpty(codePath)
                && codePath.IndexOf("cactus", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsTallFernCode(string? codePath)
        {
            return !string.IsNullOrEmpty(codePath)
                && codePath.StartsWith("tallfern", StringComparison.Ordinal);
        }

        private bool IsLargePlantContainer(Block? containerBlock)
        {
            if (containerBlock == null)
                return false;

            var size = containerBlock.Attributes?["plantContainerSize"]?.AsString(null);
            if (!string.IsNullOrEmpty(size))
                return size.Equals("large", StringComparison.OrdinalIgnoreCase);

            var codePath = containerBlock.Code?.Path;
            if (string.IsNullOrEmpty(codePath))
                return false;

            if (codePath.IndexOf("flowerpot", StringComparison.OrdinalIgnoreCase) >= 0)
                return false;

            return codePath.IndexOf("planter", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public string? GetCacheSignature(ICoreAPI api, CarriedBlock carried, string baseGroup)
        {
            var slots = BlockUtils.GetContainerSlots(carried);
            var plantStack = slots?.GetItemstack("0");
            var isLarge = IsLargePlantContainer(carried?.Block) ? "1" : "0";

            if (plantStack == null)
                return "slot0=empty|large=" + isLarge;

            return string.Concat(
                "slot0:",
                ((int)plantStack.Class).ToString(),
                ":",
                plantStack.Id.ToString(),
                "|large=",
                isLarge);
        }
    }
}
