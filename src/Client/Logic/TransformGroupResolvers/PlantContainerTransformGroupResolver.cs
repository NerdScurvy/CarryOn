using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.GameContent;

namespace CarryOn.Client.Logic.TransformGroupResolvers
{

    /// <summary>
    /// Transform group resolver for plant containers. 
    /// It handles blocks that are plant containers or flower pots and generates candidate transform groups based on the type of plant contained within.
    /// </summary>
    public class PlantContainerTransformGroupResolver : IRootTransformGroupResolver, IAttachmentTransformGroupResolver
    {
        public string ResolverCode => "plant-container";

        private static readonly Regex SaplingTypeRegex = new("^sapling-(?<wood>.+)-(?:free|snow)$", RegexOptions.Compiled);
        private static readonly Regex FlowerTypeRegex = new("^flower-(?<flower>.+)-(?:free|snow)$", RegexOptions.Compiled);
        private static readonly Regex MushroomTypeRegex = new("^mushroom-(?<mushroom>[^-]+)-", RegexOptions.Compiled);
        private static readonly Regex FernTypeRegex = new("^fern-(?<fern>[^-]+)(?:-(?:free|snow))?$", RegexOptions.Compiled);
        private static readonly Regex CrotonTypeRegex = new("^flower-croton-(?<croton>(?:small|medium)-.+)$", RegexOptions.Compiled);
        private static readonly Regex CactusFamilyRegex = new("^(?<family>[^-]*cactus)(?:-(?<variant>.+))?$", RegexOptions.Compiled);
        private static readonly Regex TallPlantTypeRegex = new("^(?<plant>tallgrass)(?:-.*)?$", RegexOptions.Compiled);

        bool IRootTransformGroupResolver.TryResolve(ICoreAPI api, CarriedBlock carried, string baseGroup, out IReadOnlyList<string>? candidates)
        {
            candidates = null;

            if (api?.World == null || carried?.Block == null || string.IsNullOrEmpty(baseGroup))
                return false;

            var containerSlots = BlockUtils.GetContainerSlots(carried);
            if (containerSlots == null || containerSlots.Count == 0)
                return false;

            var plantedGroup = "planted";
            var primaryGroup = $"{baseGroup}-{plantedGroup}";

            var list = new List<string>();

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
                if (plantItem is ItemCattailRoot)
                    PopulateCattailPrimaryCandidates(list, plantItem, primaryGroup, plantedGroup);
            }

            list.Add(primaryGroup);

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
            var candidates = new List<CarriedGroupCandidateSet>();

            var plantItemStack = containerSlots.GetItemstack("0");
            if (plantItemStack == null)
                return false;

            if (plantItemStack.Class == EnumItemClass.Block)
            {
                var plantBlock = api.World.GetBlock(plantItemStack.Id);
                if (plantBlock != null)
                    PopulateAttachmentCandidates(candidates, api, carried, plantBlock, plantedGroup);
            }
            else if (plantItemStack.Class == EnumItemClass.Item)
            {
                var plantItem = api.World.GetItem(plantItemStack.Id);
                if (plantItem is ItemCattailRoot)
                    PopulateCattailAttachmentCandidates(candidates, plantItem, plantedGroup);
            }

            if (candidates.Count == 0)
                return false;

            var resolveResult = new AttachmentResolveResult(candidates);
            resolveResult.EnableVertexWarp = true;

            result = resolveResult;
            return true;
        }

        private void PopulatePrimaryCandidates(IList<string> candidates, ICoreAPI api, CarriedBlock carried, Block plantBlock, string primaryGroup, string plantedGroup)
        {
            if (plantBlock.Class == "BlockSapling")
            {
                var saplingType = ExtractSaplingType(plantBlock.Code?.Path);
                if (!string.IsNullOrEmpty(saplingType))
                    candidates.Add(primaryGroup + "-sapling-" + saplingType);
                candidates.Add(primaryGroup + "-sapling");
            }
            else if (plantBlock.Class == "BlockPlant" && IsCrotonCode(plantBlock.Code?.Path))
            {
                var crotonType = ExtractCrotonType(plantBlock.Code?.Path);
                if (!string.IsNullOrEmpty(crotonType))
                    candidates.Add(primaryGroup + "-croton-" + crotonType);
                candidates.Add(primaryGroup + "-croton");
            }
            else if (IsCactusCode(plantBlock.Code?.Path))
            {
                var cactusFamily = ExtractCactusFamily(plantBlock.Code?.Path);
                if (!string.IsNullOrEmpty(cactusFamily))
                    candidates.Add(primaryGroup + "-cactus-" + cactusFamily);
                candidates.Add(primaryGroup + "-cactus");
            }
            else if (plantBlock.Class == "BlockPlant" && IsTallFernCode(plantBlock.Code?.Path))
            {
                candidates.Add(primaryGroup + "-fern-tallfern");
                candidates.Add(primaryGroup + "-fern");
            }
            else if (plantBlock.Class is "BlockLupine" or "BlockPlant")
            {
                var flowerType = ExtractFlowerType(plantBlock.Code?.Path);
                if (!string.IsNullOrEmpty(flowerType))
                {
                    if (plantBlock.Class == "BlockLupine")
                    {
                        candidates.Add(primaryGroup + "-lupine-" + flowerType);
                        candidates.Add(primaryGroup + "-lupine");
                    }
                    else
                    {
                        candidates.Add(primaryGroup + "-flower-" + flowerType);
                    }
                    candidates.Add(primaryGroup + "-flower");
                }
            }
            else if (plantBlock.Class == "BlockMushroom")
            {
                var mushroomType = ExtractMushroomType(plantBlock.Code?.Path);
                if (!string.IsNullOrEmpty(mushroomType))
                    candidates.Add(primaryGroup + "-mushroom-" + mushroomType);
                candidates.Add(primaryGroup + "-mushroom");
            }
            else if (plantBlock.Class == "BlockFern")
            {
                var fernType = ExtractFernType(plantBlock.Code?.Path);
                if (!string.IsNullOrEmpty(fernType))
                    candidates.Add(primaryGroup + "-fern-" + fernType);
                candidates.Add(primaryGroup + "-fern");
            }
        }

        private void PopulateCattailPrimaryCandidates(IList<string> candidates, Item plantItem, string primaryGroup, string plantedGroup)
        {
            var plantCodePath = plantItem.Code?.Path ?? string.Empty;
            var baseType = plantCodePath.EndsWith("root", StringComparison.Ordinal)
                ? plantCodePath.Substring(0, plantCodePath.Length - 4)
                : plantCodePath;

            candidates.Add(primaryGroup + "-reed");
            candidates.Add(primaryGroup + "-reed-" + baseType);
        }

        private void PopulateAttachmentCandidates(List<CarriedGroupCandidateSet> candidates, ICoreAPI api, CarriedBlock carried, Block plantBlock, string plantedGroup)
        {
            CarriedGroupCandidateSet? candidate = null;

            if (plantBlock.Class == "BlockSapling")
                candidate = CreateSaplingCandidate(plantBlock, plantedGroup);
            else if (plantBlock.Class == "BlockPlant" && IsCrotonCode(plantBlock.Code?.Path))
                candidate = CreateCrotonCandidate(plantBlock, plantedGroup);
            else if (IsCactusCode(plantBlock.Code?.Path))
                candidate = CreateCactusCandidate(api, carried, plantBlock, plantedGroup);
            else if (plantBlock.Class == "BlockPlant" && IsTallFernCode(plantBlock.Code?.Path))
                candidate = CreateTallFernCandidate(carried, plantedGroup);
            else if (plantBlock.Class is "BlockLupine" or "BlockPlant")
                candidate = CreateFlowerOrLupineCandidate(plantBlock, plantedGroup);
            else if (plantBlock.Class == "BlockMushroom")
                candidate = CreateMushroomCandidate(plantBlock, carried, plantedGroup);
            else if (plantBlock.Class == "BlockFern")
                candidate = CreateFernCandidate(plantBlock, carried, plantedGroup);

            if (candidate != null)
                candidates.Add(candidate);
        }

        private CarriedGroupCandidateSet? CreateSaplingCandidate(Block plantBlock, string plantedGroup)
        {
            var saplingType = ExtractSaplingType(plantBlock.Code?.Path);
            var saplingGroup = plantedGroup + "-sapling";
            if (string.IsNullOrEmpty(saplingType))
                return null;

            return new CarriedGroupCandidateSet(new[] { saplingGroup + "-" + saplingType });
        }

        private CarriedGroupCandidateSet CreateCrotonCandidate(Block plantBlock, string plantedGroup)
        {
            var crotonType = ExtractCrotonType(plantBlock.Code?.Path);
            var crotonGroup = plantedGroup + "-croton";

            var groups = new List<string>();
            if (!string.IsNullOrEmpty(crotonType))
                groups.Add(crotonGroup + "-" + crotonType);

            groups.Add(crotonGroup);

            return new CarriedGroupCandidateSet(groups)
            {
                AssetTypeIfUnset = CarriedGroupAssetType.Item,
                AssetNameIfUnset = string.IsNullOrEmpty(crotonType)
                    ? null
                    : "carryon:croton-" + crotonType
            };
        }

        private CarriedGroupCandidateSet CreateCactusCandidate(ICoreAPI api, CarriedBlock carried, Block plantBlock, string plantedGroup)
        {
            var cactusFamily = ExtractCactusFamily(plantBlock.Code?.Path);
            var cactusGroup = plantedGroup + "-cactus";

            var groups = new List<string>();
            if (!string.IsNullOrEmpty(cactusFamily))
                groups.Add(cactusGroup + "-" + cactusFamily);

            groups.Add(cactusGroup);

            return new CarriedGroupCandidateSet(groups)
            {
                AssetTypeIfUnset = CarriedGroupAssetType.Item,
                AssetNameIfUnset = string.IsNullOrEmpty(cactusFamily)
                    ? null
                    : "carryon:cactus-" + cactusFamily + (IsLargePlantContainer(carried.Block) ? "-large" : string.Empty)
            };
        }

        private CarriedGroupCandidateSet CreateTallFernCandidate(CarriedBlock carried, string plantedGroup)
        {
            const string tallFernType = "tallfern";
            var fernGroup = plantedGroup + "-fern";
            var useLargeFernVariant = IsLargePlantContainer(carried.Block);

            return new CarriedGroupCandidateSet(new[] { fernGroup + "-" + tallFernType, fernGroup })
            {
                AssetTypeIfUnset = CarriedGroupAssetType.Item,
                AssetNameIfUnset = "carryon:" + tallFernType + (useLargeFernVariant ? "-large" : string.Empty)
            };
        }

        private CarriedGroupCandidateSet CreateFlowerOrLupineCandidate(Block plantBlock, string plantedGroup)
        {
            var flowerType = ExtractFlowerType(plantBlock.Code?.Path);
            var flowerGroup = plantedGroup + "-flower";

            var groups = new List<string>();
            if (!string.IsNullOrEmpty(flowerType))
            {
                groups.Add(plantedGroup + "-" + flowerType);
                groups.Add(flowerGroup + "-" + flowerType);
            }

            groups.Add(flowerGroup);

            return new CarriedGroupCandidateSet(groups)
            {
                AssetTypeIfUnset = CarriedGroupAssetType.Item,
                AssetNameIfUnset = string.IsNullOrEmpty(plantBlock.Code?.Path) ? null : "carryon:" + plantBlock.Code.Path
            };
        }

        private CarriedGroupCandidateSet CreateMushroomCandidate(Block plantBlock, CarriedBlock carried, string plantedGroup)
        {
            var mushroomType = ExtractMushroomType(plantBlock.Code?.Path);
            var mushroomGroup = plantedGroup + "-mushroom";
            var mushroomCode = plantBlock.Code?.Path;
            var useLargeMushroomVariant = IsLargePlantContainer(carried.Block);

            var groups = new List<string>();
            if (!string.IsNullOrEmpty(mushroomType))
                groups.Add(mushroomGroup + "-" + mushroomType);

            groups.Add(mushroomGroup);

            return new CarriedGroupCandidateSet(groups)
            {
                AssetTypeIfUnset = CarriedGroupAssetType.Item,
                AssetNameIfUnset = string.IsNullOrEmpty(mushroomCode)
                    ? null
                    : "carryon:" + mushroomCode + (useLargeMushroomVariant ? "-large" : string.Empty)
            };
        }

        private CarriedGroupCandidateSet CreateFernCandidate(Block plantBlock, CarriedBlock carried, string plantedGroup)
        {
            var fernType = ExtractFernType(plantBlock.Code?.Path);
            var fernGroup = plantedGroup + "-fern";
            var useLargeFernVariant = IsLargePlantContainer(carried.Block);

            var groups = new List<string>();
            if (!string.IsNullOrEmpty(fernType))
                groups.Add(fernGroup + "-" + fernType);

            groups.Add(fernGroup);

            return new CarriedGroupCandidateSet(groups)
            {
                AssetTypeIfUnset = CarriedGroupAssetType.Item,
                AssetNameIfUnset = string.IsNullOrEmpty(fernType)
                    ? null
                    : "carryon:fern-" + fernType + (useLargeFernVariant ? "-large" : string.Empty)
            };
        }

        private void PopulateCattailAttachmentCandidates(List<CarriedGroupCandidateSet> candidates, Item plantItem, string plantedGroup)
        {
            var plantCodePath = plantItem.Code?.Path ?? string.Empty;
            var baseType = plantCodePath.EndsWith("root", StringComparison.Ordinal)
                ? plantCodePath.Substring(0, plantCodePath.Length - 4)
                : plantCodePath;

            var reedGroup = plantedGroup + "-reed";
            var typeGroup = reedGroup + "-" + baseType;

            candidates.Add(new CarriedGroupCandidateSet(new[] { typeGroup }));
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

        private static bool IsCactusCode(string? codePath)
        {
            return !string.IsNullOrEmpty(codePath)
                && codePath.IndexOf("cactus", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsTallFernCode(string? codePath)
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
