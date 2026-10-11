using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Common.Models;
using CarryOn.API.Event;
using CarryOn.Common.Behaviors;
using CarryOn.Server.Logic;
using CarryOn.Utility;
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
    public class CarryManager : ICarryManager, IConfigProvider
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

        private readonly HashSet<string> registeredTransferCodes
            = new(StringComparer.OrdinalIgnoreCase);

        private readonly CodeRegistry<ICarryableBlockSlotHandler> transferHandlers = new(CarryCodes.ModId);

        /// <inheritdoc/>
        public void RegisterTransferBehavior(string modId, string behaviorCode)
        {
            if (string.IsNullOrWhiteSpace(behaviorCode))
                throw new ArgumentException("Behavior code cannot be null or empty.",
                    nameof(behaviorCode));

            registeredTransferCodes.Add(behaviorCode);
        }

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateTransferBehaviors()
            => registeredTransferCodes;

        /// <inheritdoc/>
        public void RegisterTransferHandler(string handlerCode, ICarryableBlockSlotHandler handler)
            => transferHandlers.Register(handlerCode, handler);

        /// <inheritdoc/>
        public bool TryGetTransferHandler(string handlerCode, out ICarryableBlockSlotHandler? handler)
            => transferHandlers.TryGet(handlerCode, out handler);

        /// <inheritdoc/>
        public bool IsTransferHandlerRegistered(string handlerCode)
            => transferHandlers.Contains(handlerCode);

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateTransferHandlers()
            => transferHandlers.EnumerateCodes();

        public CarryOnConfig Config => ConfigProvider.Config;

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
        public void RegisterEntityTransformGroupResolver(string modId, IEntityTransformGroupResolver resolver)
            => ResolverRegistry.RegisterEntity(modId, resolver);

        /// <inheritdoc/>
        public bool TryGetEntityTransformGroupResolver(string resolverCode, out IEntityTransformGroupResolver? resolver)
            => ResolverRegistry.TryGetEntityResolver(resolverCode, out resolver);

        /// <inheritdoc/>
        public bool UnregisterEntityTransformGroupResolver(IEntityTransformGroupResolver resolver)
            => ResolverRegistry.UnregisterEntity(resolver);

        /// <inheritdoc/>
        public IEnumerable<IEntityTransformGroupResolver> EnumerateEntityTransformGroupResolvers()
            => ResolverRegistry.EnumerateEntityResolvers();

        /// <inheritdoc/>
        public string ResolveEntityTransformGroupOverride(EntityAgent entity, CarrySlot carrySlot, string baseGroup)
        {
            if (entity == null || string.IsNullOrEmpty(baseGroup))
                return baseGroup;

            var resolvers = ResolverRegistry.EnumerateEntityResolvers();
            if (resolvers.Count == 0)
                return baseGroup;

            foreach (var resolver in resolvers)
            {
                try
                {
                    if (resolver.TryResolve(Api, entity, carrySlot, baseGroup, out var group)
                        && !string.IsNullOrEmpty(group))
                    {
                        return group;
                    }
                }
                catch (Exception ex)
                {
                    CarryEvents.OnEventHandlerError?.Invoke(ex);
                }
            }

            return baseGroup;
        }

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
        public CarriedBlockReadResult GetCarriedReadResult(Entity entity, CarrySlot slot, out ITreeAttribute? rawTree)
        {
            return Services.State.GetCarriedReadResult(entity, slot, out rawTree);
        }

        /// <inheritdoc/>
        public bool IsCarrySlotOccupied(Entity entity, CarrySlot slot)
        {
            return Services.State.IsCarrySlotOccupied(entity, slot);
        }

        /// <inheritdoc/>
        public CarriedBlock? GetRenderableCarried(Entity entity, CarrySlot slot)
        {
            return Services.State.GetRenderableCarried(entity, slot);
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
        public bool SetCarriedRaw(Entity entity, ITreeAttribute? rawTree, CarrySlot slot)
        {
            return Services.State.SetCarriedRaw(entity, rawTree, slot);
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
        public bool TryPickUpGroundStorageBag(
            Entity entity,
            BlockSelection selection,
            ref string failureCode,
            int? maxInteractionDistance = null,
            bool playSound = true)
        {
            return Services.Pickup.TryPickUpGroundStorageBag(entity, selection, ref failureCode, maxInteractionDistance, playSound);
        }

        /// <inheritdoc/>
        public bool TryStoreBagInBackpack(Entity entity, ref string failureCode, bool playSound = true)
        {
            return Services.Pickup.TryStoreBagInBackpack(entity, ref failureCode, playSound);
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
        /// Internal - prefer <see cref="LockHotbarSlots"/> for public API usage.
        /// </summary>
        internal void SendLockSlotsMessage(EntityPlayer player)
        {
            Services.State.SendLockSlotsMessage(player);
        }

        /// <summary>
        /// Configures the server network channel used to send hotbar lock-state packets.
        /// </summary>
        /// <param name="serverChannel">The server network channel.</param>
        internal void ConfigureServerChannel(IServerNetworkChannel serverChannel)
        {
            Services.ConfigureServerChannel(serverChannel);
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
        public void RegisterEventHandler<T>() where T : ICarryEventHandler, new()
        {
            Services.EventBootstrapper.RegisterEventHandler<T>();
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

        // ── Entity handler registry ──

        private readonly CodeRegistry<ICarryableEntityPickupHandler> entityHandlers = new(CarryCodes.ModId);

        /// <inheritdoc/>
        public void RegisterEntityPickupHandler(string handlerCode, ICarryableEntityPickupHandler handler)
            => entityHandlers.Register(handlerCode, handler);

        /// <inheritdoc/>
        public bool TryGetEntityPickupHandler(string handlerCode, out ICarryableEntityPickupHandler? handler)
            => entityHandlers.TryGet(handlerCode, out handler);

        /// <inheritdoc/>
        public bool IsEntityPickupHandlerRegistered(string handlerCode)
            => entityHandlers.Contains(handlerCode);

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateEntityPickupHandlers()
            => entityHandlers.EnumerateCodes();

        // ── Spawner factory registry ──

        private readonly CodeRegistry<ICarryEntitySpawnerFactory> spawnerFactories = new(CarryCodes.ModId);

        /// <inheritdoc/>
        public void RegisterEntitySpawnerFactory(string factoryCode, ICarryEntitySpawnerFactory factory)
            => spawnerFactories.Register(factoryCode, factory);

        /// <inheritdoc/>
        public bool TryGetEntitySpawnerFactory(string factoryCode, out ICarryEntitySpawnerFactory? factory)
            => spawnerFactories.TryGet(factoryCode, out factory);

        /// <inheritdoc/>
        public bool IsEntitySpawnerFactoryRegistered(string factoryCode)
            => spawnerFactories.Contains(factoryCode);

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateEntitySpawnerFactories()
            => spawnerFactories.EnumerateCodes();

        // ── Slot handler registry ──

        private readonly CodeRegistry<ICarryableEntitySlotHandler> slotHandlers = new(CarryCodes.ModId);

        /// <inheritdoc/>
        public void RegisterSlotHandler(string handlerCode, ICarryableEntitySlotHandler handler)
            => slotHandlers.Register(handlerCode, handler);

        /// <inheritdoc/>
        public bool TryGetSlotHandler(string handlerCode, out ICarryableEntitySlotHandler? handler)
            => slotHandlers.TryGet(handlerCode, out handler);

        /// <inheritdoc/>
        public bool IsSlotHandlerRegistered(string handlerCode)
            => slotHandlers.Contains(handlerCode);

        /// <inheritdoc/>
        public IEnumerable<string> EnumerateSlotHandlers()
            => slotHandlers.EnumerateCodes();
    }
}