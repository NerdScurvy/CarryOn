using System.Collections.Generic;
using System.Linq;
using CarryOn.Client.Models;
using CarryOn.Common.Behaviors;
using Vintagestory.API.Client;
using Vintagestory.API.Datastructures;

namespace CarryOn.Client.Logic.TransformTemplates
{
    /// <summary>
    /// Manages the loading, parsing, and resolution of transform templates for carryable blocks. 
    /// It provides functionality to load transform template assets based on specified codes, parse the transform groups defined within those assets, 
    /// and resolve/flatten the transform groups for use in rendering or other logic. The manager maintains a mapping of template codes to their 
    /// corresponding transform groups and utilizes helper classes (TransformTemplateLoader, TransformTemplateParser, TransformTemplateResolver) to 
    /// handle specific aspects of the loading and resolution process. This class serves as a central point for managing transform templates within 
    /// the CarryOn mod's client-side logic.
    /// </summary>
    public sealed class TransformTemplateManager
    {
        private readonly ICoreClientAPI capi;
        private readonly TransformTemplateLoader loader;
        private readonly TransformTemplateParser parser;
        private readonly TransformTemplateResolver resolver;

        // templateCode -> (groupName -> groupDefinition)
        private Dictionary<string, Dictionary<string, TransformGroup?>> templatesByCode
            = new Dictionary<string, Dictionary<string, TransformGroup?>>();

        public TransformTemplateManager(ICoreClientAPI capi)
        {
            this.capi = capi;
            loader = new TransformTemplateLoader(capi);
            parser = new TransformTemplateParser(capi);
            resolver = new TransformTemplateResolver(capi);
        }

        /// <summary>
        /// Loads the transform templates based on the provided list of asset codes. For each code, it attempts to load the corresponding JSON asset, 
        /// validates its content, and parses the transform groups defined within it.
        /// </summary>
        /// <param name="assetCodes">The list of asset codes to load.</param>
        public void LoadTemplates(IList<string> assetCodes)
        {
            this.templatesByCode = new Dictionary<string, Dictionary<string, TransformGroup?>>();

            if (assetCodes == null) return;

            foreach (var code in assetCodes)
            {
                if (string.IsNullOrWhiteSpace(code)) continue;

                if (!loader.TryLoadTemplateJson(code, out var assetLocation, out var templateJsonObj))
                {
                    continue;
                }

                if (templateJsonObj == null || assetLocation == null)
                {
                    continue;
                }

                if (!loader.ValidateTemplateCodeMatchesAssetName(templateJsonObj, assetLocation))
                {
                    continue;
                }

                if (!parser.TryParseTransformGroups(templateJsonObj, out var groups) || groups == null)
                {
                    capi.Logger.Warning($"CarryOn: Transform template asset is missing 'transformGroups' section in {assetLocation}");
                    continue;
                }

                this.templatesByCode[code.ToLowerInvariant()] = groups;
            }
        }

        /// <summary>
        /// Resolves and flattens the transform groups based on the provided template codes and optional local transform groups. 
        /// It uses the TransformTemplateResolver to handle the resolution and flattening process, which includes merging groups from multiple templates and applying any local overrides. 
        /// The result is a dictionary mapping group names to their corresponding array of TransformSettings, ready for use in rendering or other logic that requires the resolved transform groups.
        /// </summary>
        /// <param name="templateCodes"> The list of template codes to resolve. </param>
        /// <param name="localTransformGroups"> Optional local transform groups to override or extend the templates. </param>
        /// <returns> A dictionary mapping group names to their corresponding array of TransformSettings. </returns>
        public Dictionary<string, TransformSettings[]> ResolveAndFlattenTransformGroups(
            IList<string> templateCodes,
            Dictionary<string, TransformGroup?>? localTransformGroups = null)
        {
            return resolver.ResolveAndFlatten(this.templatesByCode, templateCodes, localTransformGroups);
        }

        /// <summary>
        /// Attempts to parse the transform groups from the provided JSON object. It checks for the existence of the "transformGroups" section and parses each group definition into a TransformGroup object. 
        /// The result is a dictionary mapping group names to their corresponding TransformGroup instances. 
        /// This method is used to extract transform group definitions from the JSON content of transform template assets.
        /// </summary>
        /// <param name="settingsJson"> The JSON object containing the transform groups. </param>
        /// <param name="groups"> Outputs the parsed transform groups if successful. </param>
        /// <returns> True if the transform groups were successfully parsed; otherwise, false. </returns>
        public bool TryParseTransformGroups(JsonObject settingsJson, out Dictionary<string, TransformGroup?>? groups)
            => parser.TryParseTransformGroups(settingsJson, out groups);

        public static TransformTemplateManager InitializeFromBlocks(ICoreClientAPI api)
        {
            var carryableWithTemplates = api.World.Blocks
                .Where(b => b.GetBehavior<BlockBehaviorCarryable>()?.TransformTemplates != null)
                .ToList();

            var transformTemplateCodes = carryableWithTemplates
                .SelectMany(b => b.GetBehavior<BlockBehaviorCarryable>().TransformTemplates)
                .ToHashSet();

            var manager = new TransformTemplateManager(api);
            manager.LoadTemplates([.. transformTemplateCodes]);

            var carryablesToResolve = api.World.Blocks
                .Where(b => b.GetBehavior<BlockBehaviorCarryable>() != null &&
                            (b.GetBehavior<BlockBehaviorCarryable>().TransformTemplates != null || b.GetBehavior<BlockBehaviorCarryable>().HasLocalTransformGroups))
                .ToList();

            foreach (var block in carryablesToResolve)
            {
                block.GetBehavior<BlockBehaviorCarryable>().ResolveTransformGroups(manager);
            }

            return manager;
        }

    }
}