using System;
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
    public class PlantContainerTransformGroupResolver : ICarriedTransformGroupResolver
    {
        public string ResolverCode => "plant-container";

        private static readonly Regex SaplingTypeRegex = new("^sapling-(?<wood>.+)-(?:free|snow)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex FlowerTypeRegex = new("^flower-(?<flower>.+)-(?:free|snow)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex MushroomTypeRegex = new("^mushroom-(?<mushroom>[^-]+)-", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex FernTypeRegex = new("^fern-(?<fern>[^-]+)(?:-(?:free|snow))?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex CrotonTypeRegex = new("^flower-croton-(?<croton>(?:small|medium)-.+)$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex CactusFamilyRegex = new("^(?<family>[^-]*cactus)(?:-(?<variant>.+))?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex TallPlantTypeRegex = new("^(?<plant>tallgrass)(?:-.*)?$", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        public int Priority => 0;

        public bool TryResolve(ICoreAPI api, CarriedBlock carried, string baseGroup, out CarriedGroupResolution resolution)
        {
            resolution = null;

            if (api?.World == null || carried?.Block == null || string.IsNullOrEmpty(baseGroup))
            {
                return false;
            }

            var containerSlots = TransformGroupResolverHelper.GetContainerSlots(carried);

            if (containerSlots == null || containerSlots.Count == 0)
            {
                return false;
            }

            var plantedGroup = "planted";

            var primaryGroup = $"{baseGroup}-{plantedGroup}";
            var result = new CarriedGroupResolution();
            result.PrimaryGroupCandidates.Add(primaryGroup);

            var plantItemStack = containerSlots.GetItemstack("0");
            if (plantItemStack == null)
            {
                resolution = result;
                return true;
            }

            if (plantItemStack.Class == EnumItemClass.Block)
            {
                var plantBlock = api.World.GetBlock(plantItemStack.Id);
                if (plantBlock != null)
                {
                    if (plantBlock.Class == "BlockSapling")
                    {
                        PrependPrimaryCandidate(result, primaryGroup + "-sapling");

                        var saplingType = ExtractSaplingType(plantBlock.Code?.Path);

                        if (!string.IsNullOrEmpty(saplingType))
                        {
                            PrependPrimaryCandidate(result, primaryGroup + "-sapling-" + saplingType);
                        }

                        var saplingGroup = plantedGroup + "-sapling";
                        if (!string.IsNullOrEmpty(saplingType))
                        {
                            result.AdditionalGroupCandidates.Add(new CarriedGroupCandidateSet
                            {
                                Groups = { saplingGroup + "-" + saplingType }
                            });
                        }
                    }
                    else if (plantBlock.Class == "BlockPlant" && IsCrotonCode(plantBlock.Code?.Path))
                    {
                        PrependPrimaryCandidate(result, primaryGroup + "-croton");

                        var crotonType = ExtractCrotonType(plantBlock.Code?.Path);

                        if (!string.IsNullOrEmpty(crotonType))
                        {
                            PrependPrimaryCandidate(result, primaryGroup + "-croton-" + crotonType);
                        }

                        var crotonGroup = plantedGroup + "-croton";

                        var groupCandidates = new CarriedGroupCandidateSet
                        {
                            AssetTypeIfUnset = CarriedGroupAssetType.Item,
                            AssetNameIfUnset = string.IsNullOrEmpty(crotonType)
                                ? null
                                : "carryon:croton-" + crotonType
                        };

                        if (!string.IsNullOrEmpty(crotonType))
                        {
                            groupCandidates.Groups.Add(crotonGroup + "-" + crotonType);
                        }

                        groupCandidates.Groups.Add(crotonGroup);
                        result.AdditionalGroupCandidates.Add(groupCandidates);
                    }
                    else if (IsCactusCode(plantBlock.Code?.Path))
                    {
                        PrependPrimaryCandidate(result, primaryGroup + "-cactus");

                        var cactusFamily = ExtractCactusFamily(plantBlock.Code?.Path);

                        if (!string.IsNullOrEmpty(cactusFamily))
                        {
                            PrependPrimaryCandidate(result, primaryGroup + "-cactus-" + cactusFamily);
                        }

                        var cactusGroup = plantedGroup + "-cactus";

                        var groupCandidates = new CarriedGroupCandidateSet
                        {
                            AssetTypeIfUnset = CarriedGroupAssetType.Item,
                            AssetNameIfUnset = string.IsNullOrEmpty(cactusFamily)
                                ? null
                                : "carryon:cactus-" + cactusFamily + (IsLargePlantContainer(carried.Block) ? "-large" : string.Empty)
                        };

                        if (!string.IsNullOrEmpty(cactusFamily))
                        {
                            groupCandidates.Groups.Add(cactusGroup + "-" + cactusFamily);
                        }

                        groupCandidates.Groups.Add(cactusGroup);
                        result.AdditionalGroupCandidates.Add(groupCandidates);
                    }
                    else if (plantBlock.Class == "BlockPlant" && IsTallFernCode(plantBlock.Code?.Path))
                    {
                        const string tallFernType = "tallfern";

                        PrependPrimaryCandidate(result, primaryGroup + "-fern");
                        PrependPrimaryCandidate(result, primaryGroup + "-fern-" + tallFernType);

                        var fernGroup = plantedGroup + "-fern";
                        var useLargeFernVariant = IsLargePlantContainer(carried.Block);

                        var groupCandidates = new CarriedGroupCandidateSet
                        {
                            AssetTypeIfUnset = CarriedGroupAssetType.Item,
                            AssetNameIfUnset = "carryon:" + tallFernType + (useLargeFernVariant ? "-large" : string.Empty)
                        };

                        groupCandidates.Groups.Add(fernGroup + "-" + tallFernType);
                        groupCandidates.Groups.Add(fernGroup);
                        result.AdditionalGroupCandidates.Add(groupCandidates);
                    }
                    else if (plantBlock.Class is "BlockLupine" or "BlockPlant")
                    {
                        var flowerType = ExtractFlowerType(plantBlock.Code?.Path);
                        var flowerGroup = plantedGroup + "-flower";

                        if (!string.IsNullOrEmpty(flowerType))
                        {
                            PrependPrimaryCandidate(result, primaryGroup + "-flower");

                            if (plantBlock.Class == "BlockLupine")
                            {
                                PrependPrimaryCandidate(result, primaryGroup + "-lupine");
                                PrependPrimaryCandidate(result, primaryGroup + "-lupine-" + flowerType);
                            }
                            else
                            {
                                PrependPrimaryCandidate(result, primaryGroup + "-flower-" + flowerType);
                            }
                        }

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
                        result.AdditionalGroupCandidates.Add(groupCandidates);
                    }
                    else if (plantBlock.Class == "BlockMushroom")
                    {
                        PrependPrimaryCandidate(result, primaryGroup + "-mushroom");

                        var mushroomType = ExtractMushroomType(plantBlock.Code?.Path);
                        var mushroomGroup = plantedGroup + "-mushroom";
                        var mushroomCode = plantBlock.Code?.Path;
                        var useLargeMushroomVariant = IsLargePlantContainer(carried.Block);

                        if (!string.IsNullOrEmpty(mushroomType))
                        {
                            PrependPrimaryCandidate(result, primaryGroup + "-mushroom-" + mushroomType);
                        }

                        var groupCandidates = new CarriedGroupCandidateSet
                        {
                            AssetTypeIfUnset = CarriedGroupAssetType.Item,
                            AssetNameIfUnset = string.IsNullOrEmpty(mushroomCode)
                                ? null
                                : "carryon:" + mushroomCode + (useLargeMushroomVariant ? "-large" : string.Empty)
                        };

                        if (!string.IsNullOrEmpty(mushroomType))
                        {
                            groupCandidates.Groups.Add(mushroomGroup + "-" + mushroomType);
                        }

                        groupCandidates.Groups.Add(mushroomGroup);
                        result.AdditionalGroupCandidates.Add(groupCandidates);
                    }
                    else if (plantBlock.Class == "BlockFern")
                    {
                        PrependPrimaryCandidate(result, primaryGroup + "-fern");

                        var fernType = ExtractFernType(plantBlock.Code?.Path);
                        var fernGroup = plantedGroup + "-fern";
                        var useLargeFernVariant = IsLargePlantContainer(carried.Block);

                        if (!string.IsNullOrEmpty(fernType))
                        {
                            PrependPrimaryCandidate(result, primaryGroup + "-fern-" + fernType);
                        }


                        var groupCandidates = new CarriedGroupCandidateSet
                        {
                            AssetTypeIfUnset = CarriedGroupAssetType.Item,
                            AssetNameIfUnset = string.IsNullOrEmpty(fernType)
                                ? null
                                : "carryon:fern-" + fernType + (useLargeFernVariant ? "-large" : string.Empty)
                        };

                        if (!string.IsNullOrEmpty(fernType))
                        {
                            groupCandidates.Groups.Add(fernGroup + "-" + fernType);
                        }

                        groupCandidates.Groups.Add(fernGroup);
                        result.AdditionalGroupCandidates.Add(groupCandidates);
                    }
                }
            }
            else if (plantItemStack.Class == EnumItemClass.Item)
            {
                var plantItem = api.World.GetItem(plantItemStack.Id);
                // Check if item is an instance of ItemCattailRoot class
                if (plantItem is not null && plantItem.GetType().Name == "ItemCattailRoot")
                {
                    // Strip root suffix to get base reed type for transform group naming
                    var plantCodePath = plantItem?.Code?.Path;
                    var baseType = plantCodePath.EndsWith("root", StringComparison.Ordinal)
                        ? plantCodePath.Substring(0, plantCodePath.Length - 4)
                        : plantCodePath;

                    var reedGroup = plantedGroup + "-reed";
                    var typeGroup = reedGroup + "-" + baseType;

                    // Primary root fallback chain (no -large suffix in group names)
                    PrependPrimaryCandidate(result, primaryGroup + "-reed-" + baseType);
                    PrependPrimaryCandidate(result, primaryGroup + "-reed");

                    // Additional transform groups, with legacy fallback for small templates
                    var candidates = new CarriedGroupCandidateSet();
                    candidates.Groups.Add(typeGroup);                     // planted-reed-cattail
                    result.AdditionalGroupCandidates.Add(candidates);
                }
            }

            if (result.AdditionalGroupCandidates.Count > 0)
            {
                result.EnableVertexWarpForAdditionalTransforms = true;
            }

            resolution = result;
            return true;
        }

        private string ExtractSaplingType(string codePath)
        {
            if (string.IsNullOrEmpty(codePath)) return null;

            var match = SaplingTypeRegex.Match(codePath);
            return match.Success ? match.Groups["wood"].Value : null;
        }

        private string ExtractFlowerType(string codePath)
        {
            if (string.IsNullOrEmpty(codePath)) return null;

            var match = FlowerTypeRegex.Match(codePath);
            if (match.Success) return match.Groups["flower"].Value;

            match = TallPlantTypeRegex.Match(codePath);
            return match.Success ? match.Groups["plant"].Value : null;
        }

        private string ExtractMushroomType(string codePath)
        {
            if (string.IsNullOrEmpty(codePath)) return null;

            var match = MushroomTypeRegex.Match(codePath);
            return match.Success ? match.Groups["mushroom"].Value : null;
        }

        private string ExtractFernType(string codePath)
        {
            if (string.IsNullOrEmpty(codePath)) return null;

            var match = FernTypeRegex.Match(codePath);
            return match.Success ? match.Groups["fern"].Value : null;
        }

        private string ExtractCrotonType(string codePath)
        {
            if (string.IsNullOrEmpty(codePath)) return null;
            var match = CrotonTypeRegex.Match(codePath);
            return match.Success ? match.Groups["croton"].Value : null;
        }

        private string ExtractCactusFamily(string codePath)
        {
            if (string.IsNullOrEmpty(codePath)) return null;
            var match = CactusFamilyRegex.Match(codePath);
            return match.Success ? match.Groups["family"].Value : null;
        }

        private static bool IsCrotonCode(string codePath)
        {
            return !string.IsNullOrEmpty(codePath)
            && codePath.StartsWith("flower-croton-", StringComparison.Ordinal);
        }

        private bool IsCactusCode(string codePath)
        {
            return !string.IsNullOrEmpty(codePath)
                && codePath.IndexOf("cactus", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private bool IsTallFernCode(string codePath)
        {
            return !string.IsNullOrEmpty(codePath)
                && codePath.StartsWith("tallfern", StringComparison.Ordinal);
        }

        private void PrependPrimaryCandidate(CarriedGroupResolution result, string group)
        {
            if (result == null || string.IsNullOrEmpty(group))
            {
                return;
            }

            if (result.PrimaryGroupCandidates.Contains(group))
            {
                return;
            }

            result.PrimaryGroupCandidates.Insert(0, group);
        }

        private bool IsLargePlantContainer(Block containerBlock)
        {
            if (containerBlock == null)
            {
                return false;
            }

            var size = containerBlock.Attributes?["plantContainerSize"]?.AsString(null);
            if (!string.IsNullOrEmpty(size))
            {
                return size.Equals("large", StringComparison.OrdinalIgnoreCase);
            }

            var codePath = containerBlock.Code?.Path;
            if (string.IsNullOrEmpty(codePath))
            {
                return false;
            }

            if (codePath.IndexOf("flowerpot", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            return codePath.IndexOf("planter", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public string GetCacheSignature(ICoreAPI api, CarriedBlock carried, string baseGroup, CarriedGroupResolution resolution)
        {
            var slots = TransformGroupResolverHelper.GetContainerSlots(carried);
            var plantStack = slots?.GetItemstack("0");
            var isLarge = IsLargePlantContainer(carried?.Block) ? "1" : "0";

            if (plantStack == null)
            {
                return "slot0=empty|large=" + isLarge;
            }

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
