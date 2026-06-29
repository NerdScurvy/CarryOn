using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.API.Event;
using CarryOn.Common.Behaviors;
using CarryOn.Server.Logic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;


namespace CarryOn.Common.Services
{
    /// <summary>
    /// Default implementation of <see cref="ICarryManager"/>. Delegates all operations
    /// to the underlying service portfolio via <see cref="CarryManagerServices"/>.
    /// </summary>
    public class CarryManager : ICarryManager
    {

        public ICoreAPI Api { get; private set; }

        public IConfigProvider ConfigProvider { get; private set; }

        public CarryEvents CarryEvents { get; private set; }

        internal CarryManagerServices Services { get; }
        private readonly CarryResolverRegistry ResolverRegistry = new();

        public CarryManager(ICoreAPI api, IConfigProvider configProvider, CarryEvents carryEvents)
        {
            ConfigProvider = configProvider ?? throw new ArgumentNullException(nameof(configProvider));
            CarryEvents = carryEvents ?? throw new ArgumentNullException(nameof(carryEvents));
            Api = api ?? throw new ArgumentNullException(nameof(api));
            Services = new CarryManagerServices(Api, ConfigProvider, this);
        }

        public CarryOnConfig? Config => ConfigProvider?.Config;

        /// <inheritdoc/>
        public void RegisterRootTransformGroupResolver(string modId, IRootTransformGroupResolver resolver)
            => ResolverRegistry.RegisterRoot(modId, resolver);

        /// <inheritdoc/>
        public bool TryGetRootTransformGroupResolver(string resolverCode, out IRootTransformGroupResolver? resolver)
            => ResolverRegistry.TryGetRootResolver(resolverCode, out resolver);

        /// <inheritdoc/>
        public void RegisterAttachmentTransformGroupResolver(string modId, IAttachmentTransformGroupResolver resolver)
            => ResolverRegistry.RegisterAttachment(modId, resolver);

        /// <inheritdoc/>
        public bool TryGetAttachmentTransformGroupResolver(string resolverCode, out IAttachmentTransformGroupResolver? resolver)
            => ResolverRegistry.TryGetAttachmentResolver(resolverCode, out resolver);

        /// <inheritdoc/>
        public bool UnregisterRootTransformGroupResolver(IRootTransformGroupResolver resolver)
            => ResolverRegistry.UnregisterRoot(resolver);

        /// <inheritdoc/>
        public bool UnregisterAttachmentTransformGroupResolver(IAttachmentTransformGroupResolver resolver)
            => ResolverRegistry.UnregisterAttachment(resolver);

        /// <inheritdoc/>
        public IEnumerable<CarriedBlock> GetAllCarried(Entity entity)
        {
            return Services.State.GetAllCarried(entity);
        }

        /// <inheritdoc/>
        public CarriedBlock? GetCarried(Entity entity, CarrySlot slot)
        {
            return Services.State.GetCarried(entity, slot);
        }

        /// <inheritdoc/>
        public void SetCarried(Entity entity, CarriedBlock carriedBlock, CarrySlot? overrideSlot = null, bool markDirty = true)
        {
            Services.State.SetCarried(entity, carriedBlock, overrideSlot, markDirty);
        }

        /// <inheritdoc/>
        public void RemoveCarried(Entity entity, CarrySlot slot, bool markDirty = true)
        {
            Services.State.RemoveCarried(entity, slot, markDirty);
        }

        /// <inheritdoc/>
        public bool SwapCarried(Entity entity, CarrySlot first, CarrySlot second)
        {
            return Services.State.SwapCarried(entity, first, second);
        }

        /// <inheritdoc/>
        public CarriedBlock? GetCarriedFromWorld(BlockPos pos, CarrySlot slot, bool checkIsCarryable = false)
        {
            return Services.Pickup.GetCarriedFromWorld(pos, slot, checkIsCarryable);
        }

        /// <inheritdoc/>
        public CarriedBlock? GetCarriedFromWorld(Entity entity, BlockPos pos, CarrySlot slot, ref string failureCode, bool checkIsCarryable = false)
        {
            return Services.Pickup.GetCarriedFromWorld(entity, pos, slot, ref failureCode, checkIsCarryable);
        }

        /// <inheritdoc/>
        public void RestoreBlockEntityData(IWorldAccessor world, CarriedBlock carriedBlock, BlockPos pos, bool dropped = false)
        {
            Services.Placement.RestoreBlockEntityData(world, carriedBlock, pos, dropped);
        }

        /// <inheritdoc/>
        public bool TryPickUp(
            Entity entity,
            BlockPos pos,
            CarrySlot slot,
            ref string failureCode,
            bool checkIsCarryable = true,
            bool playSound = true,
            bool? captureAttachedSigns = null)
        {
            return Services.Pickup.TryPickUp(entity, pos, slot, ref failureCode, checkIsCarryable, playSound, captureAttachedSigns);
        }

        /// <inheritdoc/>
        public bool TryPlaceDown(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, ref string failureCode, bool dropped = false, bool playSound = true)
        {
            return Services.Placement.TryPlaceDown(entity, carriedBlock, selection, ref failureCode, dropped, playSound);
        }

        /// <inheritdoc/>
        public bool TryPlaceDownAt(IPlayer player, CarrySlot carrySlot, BlockSelection selection, out BlockPos? placedAt, ref string failureCode)
        {
            return Services.Placement.TryPlaceDownAt(player, carrySlot, selection, out placedAt, ref failureCode);
        }

        /// <inheritdoc/>
        public bool TryAttach(IServerPlayer player, long targetEntityId, int slotIndex, ref string failureCode, bool playSound = true)
        {
            return Services.Attachment.TryAttach(player, targetEntityId, slotIndex, ref failureCode, playSound);
        }

        /// <inheritdoc/>
        public bool TryDetach(IServerPlayer player, long targetEntityId, int slotIndex, ref string failureCode, bool playSound = true)
        {
            return Services.Attachment.TryDetach(player, targetEntityId, slotIndex, ref failureCode, playSound);
        }

        /// <inheritdoc/>
        public bool HasPermissionAt(Entity entity, BlockPos pos, bool showErrorMessage = true)
        {
            return Services.Permission.HasPermissionAt(entity, pos, showErrorMessage);
        }

        /// <inheritdoc/>
        public void LockHotbarSlots(IServerPlayer player)
        {
            Services.State.LockHotbarSlots(player);
        }

        /// <summary>
        /// Sends a lock-slots network message to the specified player entity.
        /// Internal — prefer <see cref="LockHotbarSlots"/> for public API usage.
        /// </summary>
        internal void SendLockSlotsMessage(EntityPlayer player)
        {
            Services.State.SendLockSlotsMessage(player);
        }

        /// <inheritdoc/>
        public void DropCarried(Entity entity, IEnumerable<CarrySlot> slots, int range = 4)
        {
            Services.Drop.DropCarried(entity, slots, range);
        }

        /// <inheritdoc/>
        public void DropCarriedBlock(Entity entity, CarriedBlock carriedBlock, int range = 4)
        {
            Services.Drop.DropCarriedBlock(entity, carriedBlock, range, null);
        }

        /// <summary>
        /// Drops a carried block with an explicit block placer. Class-only overload not on the interface;
        /// the interface exposes <see cref="DropCarriedBlock(Entity, CarriedBlock, int)"/> without the
        /// <paramref name="blockPlacer"/> parameter.
        /// </summary>
        public void DropCarriedBlock(Entity entity, CarriedBlock carriedBlock, int range, BlockPlacer? blockPlacer)
        {
            Services.Drop.DropCarriedBlock(entity, carriedBlock, range, blockPlacer);
        }

        /// <inheritdoc/>
        public void DropBlockAsEntityOrItem(CarriedBlock carriedBlock, BlockPos centerBlock, IServerPlayer player, Entity entity)
        {
            Services.Drop.DropBlockAsEntityOrItem(carriedBlock, centerBlock, player, entity);
        }

        /// <inheritdoc/>
        public int TouchCarriedAttributes(Entity entity)
        {
            return Services.State.TouchCarriedAttributes(entity);
        }

        /// <inheritdoc/>
        public int GetCarriedRevision(Entity entity)
        {
            return Services.State.GetCarriedRevision(entity);
        }

        /// <inheritdoc/>
        public void InitEvents(ICoreAPI api)
        {
            Services.EventBootstrapper.InitEvents(api);
        }

        /// <inheritdoc/>
        public bool IsCarryable(Block block, CarrySlot slot)
            => block.GetBehavior<BlockBehaviorCarryable>()?.Slots?[slot] != null;

        /// <inheritdoc/>
        public bool IsCarryable(Block block)
            => block.HasBehavior<BlockBehaviorCarryable>();

        /// <inheritdoc/>
        public bool CanInteractWhileCarrying(Block block)
            => block.HasBehavior<BlockBehaviorCarryableInteract>();
    }
}