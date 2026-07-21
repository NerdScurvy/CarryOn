using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Newtonsoft.Json.Linq;
using CarryOn.Utility;
using CarryOn.Common.Models;

namespace CarryOn.Client.Logic.TransformTemplates
{
    /// <summary>
    /// Handles loading of transform template assets based on provided template codes. 
    /// It resolves the asset location, loads the JSON content, and validates the template code against the asset name to ensure consistency. 
    /// This class serves as a utility for managing transform template assets within the CarryOn mod, allowing for flexible referencing and organization 
    /// of templates in the game's asset system.
    /// </summary>
    public sealed class TransformTemplateLoader
    {
        private readonly ICoreClientAPI capi;

        public TransformTemplateLoader(ICoreClientAPI capi)
        {
            this.capi = capi;
        }

        /// <summary>
        /// Resolves the asset location for a given transform template code. 
        /// The method supports both domain-prefixed codes (e.g., "modid:template") and simple codes (e.g., "template"). 
        /// It constructs the asset path based on a predefined directory structure ("config/transformtemplates/") and appends the ".json" extension. 
        /// If the code is domain-prefixed, it uses the specified domain; otherwise, it defaults to the mod's own ID. 
        /// This allows for flexible referencing of transform template assets while maintaining a consistent organization within the game's asset system.
        /// </summary>
        /// <param name="assetCode"> The code of the transform template asset to resolve. </param>
        /// <returns> The resolved asset location. </returns>
        public AssetLocation? ResolveAssetLocation(string assetCode)
        {
            if (string.IsNullOrEmpty(assetCode)) return null;

            const string pathPrefix = "config/transformtemplates/";
            const string extension = ".json";

            var domain = CarryCodes.ModId;
            string assetPath;
            var parts = assetCode.Split(':');

            if (parts.Length == 2)
            {
                domain = parts[0].ToLowerInvariant();
                assetPath = pathPrefix + parts[1] + extension;
            }
            else
            {
                assetPath = pathPrefix + assetCode + extension;
            }

            return new AssetLocation(domain, assetPath);
        }

        /// <summary>
        /// Attempts to load the transform template JSON asset based on the provided asset code. 
        /// It resolves the asset location, checks for the existence of the asset, and parses its content into a JsonObject. 
        /// If any step fails (e.g., asset not found, empty content, invalid JSON), it returns false and outputs null for the asset location and template JSON object. 
        /// This method ensures that only valid and properly formatted transform template assets are loaded for further processing.
        /// </summary>
        /// <param name="assetCode"> The code of the transform template asset to load. </param>
        /// <param name="assetLocation"> Outputs the resolved asset location if the asset is found. </param>
        /// <param name="templateJsonObj"> Outputs the parsed JSON object if the asset is successfully loaded and parsed. </param>
        /// <returns> True if the asset is successfully loaded and parsed; otherwise, false. </returns>
        public bool TryLoadTemplateJson(
            string assetCode,
            out AssetLocation? assetLocation,
            out JsonObject? templateJsonObj)
        {
            assetLocation = ResolveAssetLocation(assetCode);
            templateJsonObj = null;

            if (assetLocation == null) return false;

            var asset = capi.Assets.TryGet(assetLocation);
            if (asset == null)
            {
                capi.Logger.Warning($"CarryOn: Transform template asset not found: {assetLocation}");
                return false;
            }

            var templateJson = asset.ToText();
            if (templateJson == null)
            {
                capi.Logger.Warning($"CarryOn: Transform template asset is empty: {assetLocation}");
                return false;
            }

            templateJsonObj = new JsonObject(JToken.Parse(templateJson));
            return true;
        }

        /// <summary>
        /// If the template JSON contains a "code" property, validates that it matches the asset name (without extension) to prevent confusion and ensure consistency.
        /// </summary>
        /// <param name="templateJsonObj">The JSON object representing the transform template.</param>
        /// <param name="assetLocation">The location of the asset being validated.</param>
        /// <returns>True if the template code matches the asset name or if no code is specified; otherwise, false.</returns>
        public bool ValidateTemplateCodeMatchesAssetName(JsonObject templateJsonObj, AssetLocation assetLocation)
        {
            if (!JsonHelper.TryGetString(templateJsonObj, "code", out var templateCode))
            {
                return true;
            }

            var asset = capi.Assets.TryGet(assetLocation);
            if (asset == null) return false;

            if (asset.Name.Replace(".json", "") == templateCode) return true;

            capi.Logger.Warning(
                $"CarryOn: Transform template asset code '{templateCode}' does not match asset name '{asset.Name}' in {assetLocation}");
            return false;
        }
    }
}
