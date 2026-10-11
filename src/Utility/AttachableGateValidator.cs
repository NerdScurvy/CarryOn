using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace CarryOn.Utility
{
    /// <summary>
    /// Shared gate checks for what may be attached to an attachable entity.
    /// Used by <see cref="CarryOn.Common.Behaviors.EntityBehaviorAttachableCarryable"/> on attach
    /// and by the server recovery sweep to strip broken attachments from already-corrupted entities.
    /// </summary>
    public static class AttachableGateValidator
    {
        /// <summary>
        /// Mirrors the vanilla gate: the collectible must provide an <see cref="IAttachableToEntity"/>
        /// and that interface must accept attachment for this entity/itemstack.
        /// </summary>
        public static bool IsAttachable(Entity entity, ItemStack itemstack)
        {
            var attachableToEntity = IAttachableToEntity.FromCollectible(itemstack.Collectible);
            return attachableToEntity != null && attachableToEntity.IsAttachable(entity, itemstack);
        }

        /// <summary>
        /// Blocks "retrieveOnly" generic typed containers (collapsed chests) from attaching to
        /// entities. Their per-variant texture data can break entity tesselation.
        /// </summary>
        public static bool IsRetrieveOnlyStorage(ItemStack itemstack)
        {
            var block = itemstack.Block as BlockGenericTypedContainer;
            if (block == null) return false;

            var type = itemstack.Attributes?.GetString(CarryCodes.AttributeKeys.Type)
                ?? block.Attributes?["defaultType"]?.AsString("normal-generic")
                ?? "normal-generic";
            return block.Attributes?["retrieveOnly"]?[type]?.AsBool(false) == true;
        }
    }
}
