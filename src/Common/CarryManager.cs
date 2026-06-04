using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.API.Event;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Services;
using CarryOn.Server.Logic;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;


namespace CarryOn.API.Common
{
    public class CarryManager : ICarryManager
    {

        public ICoreAPI Api { get; private set; }

        public CarrySystem CarrySystem { get; private set; }

        public CarryEvents CarryEvents => CarrySystem.CarryEvents;

        private readonly List<RegisteredTransformGroupResolver> transformGroupResolvers = new();
        private readonly Dictionary<string, RegisteredTransformGroupResolver> transformGroupResolversByCode = new(StringComparer.OrdinalIgnoreCase);
        internal CarryManagerServices Services { get; }

        public CarryManager(ICoreAPI api, CarrySystem carrySystem)
        {
            CarrySystem = carrySystem ?? throw new ArgumentNullException(nameof(carrySystem));
            Api = api ?? throw new ArgumentNullException(nameof(api));
            Services = new CarryManagerServices(Api, CarrySystem, this);
        }

        public CarryOnConfig GetConfig()
        {
            return CarrySystem.Config;
        }

        /// <summary>
        /// Registers a transform group resolver to determine the transform group for carried blocks.
        /// </summary>
        /// <param name="modId">Owning mod id for this resolver registration.</param>
        /// <param name="resolver"> The transform group resolver to register. </param>
        /// <exception cref="ArgumentNullException"></exception>
        public void RegisterTransformGroupResolver(string modId, ICarriedTransformGroupResolver resolver)
        {
            if (string.IsNullOrWhiteSpace(modId))
            {
                throw new ArgumentException("Mod id cannot be null or empty.", nameof(modId));
            }

            ArgumentNullException.ThrowIfNull(resolver);
            if (string.IsNullOrWhiteSpace(resolver.ResolverCode))
            {
                throw new ArgumentException("Resolver code cannot be null or empty.", nameof(resolver));
            }

            var canonicalCode = ToCanonicalResolverCode(modId, resolver.ResolverCode);
            if (canonicalCode == null)
            {
                throw new ArgumentException("Resolver code cannot be null or empty after trimming.", nameof(resolver));
            }

            if (transformGroupResolversByCode.TryGetValue(canonicalCode, out var existing))
            {
                if (!ReferenceEquals(existing.Resolver, resolver))
                {
                    throw new InvalidOperationException(
                        $"A transform group resolver is already registered for code '{canonicalCode}' by mod '{existing.ModId}'.");
                }

                return;
            }

            var registration = new RegisteredTransformGroupResolver
            {
                ModId = modId.Trim(),
                ResolverCode = canonicalCode,
                Resolver = resolver
            };

            transformGroupResolvers.Add(registration);
            transformGroupResolversByCode[canonicalCode] = registration;
        }

        /// <summary>
        /// Attempts to get a registered transform group resolver by code.
        /// </summary>
        /// <param name="resolverCode">The resolver code to look up.</param>
        /// <param name="resolver">The resolver instance when found.</param>
        /// <returns>True if a matching resolver was found; otherwise false.</returns>
        public bool TryGetTransformGroupResolver(string resolverCode, out ICarriedTransformGroupResolver? resolver)
        {
            resolver = null;
            if (string.IsNullOrWhiteSpace(resolverCode))
            {
                return false;
            }

            var canonicalCode = ToCanonicalLookupCode(resolverCode);
            if (canonicalCode == null)
            {
                return false;
            }
            
            if (!transformGroupResolversByCode.TryGetValue(canonicalCode, out var registration))
            {
                return false;
            }

            resolver = registration.Resolver;
            return true;
        }

        /// <summary>
        /// Attempts to get resolver registration metadata by code.
        /// </summary>
        /// <param name="resolverCode">The resolver code to look up.</param>
        /// <param name="registration">The resolver registration when found.</param>
        /// <returns>True if a matching registration was found; otherwise false.</returns>
        public bool TryGetTransformGroupResolverRegistration(string resolverCode, out RegisteredTransformGroupResolver? registration)
        {
            registration = null;
            if (string.IsNullOrWhiteSpace(resolverCode))
            {
                return false;
            }

            var canonicalCode = ToCanonicalLookupCode(resolverCode);
            if (canonicalCode == null)
            {
                return false;
            }

            var found = transformGroupResolversByCode.TryGetValue(canonicalCode, out var foundReg);
            registration = foundReg;
            return found;
        }

        /// <summary>
        /// Unregisters a transform group resolver.
        /// </summary>
        /// <param name="resolver"> The transform group resolver to unregister. </param>
        /// <returns> True if the resolver was successfully unregistered, otherwise false. </returns>
        public bool UnregisterTransformGroupResolver(ICarriedTransformGroupResolver resolver)
        {
            if (resolver == null) return false;

            RegisteredTransformGroupResolver? registration = null;
            for (var i = 0; i < transformGroupResolvers.Count; i++)
            {
                if (ReferenceEquals(transformGroupResolvers[i].Resolver, resolver))
                {
                    registration = transformGroupResolvers[i];
                    transformGroupResolvers.RemoveAt(i);
                    break;
                }
            }

            if (registration == null)
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(registration.ResolverCode)
                && transformGroupResolversByCode.TryGetValue(registration.ResolverCode, out var existing)
                && ReferenceEquals(existing.Resolver, resolver))
            {
                transformGroupResolversByCode.Remove(registration.ResolverCode);
            }

            return true;
        }

        /// <summary>
        /// Gets the list of registered transform group resolvers.
        /// </summary>
        /// <returns> A read-only list of registered transform group resolvers. </returns>
        public IReadOnlyList<RegisteredTransformGroupResolver> GetTransformGroupResolvers()
        {
            return transformGroupResolvers;
        }

        private static string? ToCanonicalLookupCode(string resolverCode)
        {
            var trimmed = resolverCode?.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                return null;
            }

            return trimmed.IndexOf(':') >= 0
                ? trimmed
                : $"{CarryCode.ModId}:{trimmed}";
        }

        private static string? ToCanonicalResolverCode(string modId, string resolverCode)
        {
            var normalizedModId = modId?.Trim();
            var normalizedCode = resolverCode?.Trim();

            return normalizedCode?.IndexOf(':') >= 0
                ? normalizedCode
                : $"{normalizedModId}:{normalizedCode}";
        }


        /// <summary>
        /// Gets all carried blocks for the specified entity.
        /// </summary>
        /// <param name="entity"> The entity whose carried blocks are being retrieved. </param>
        /// <returns> An enumerable of all carried blocks for the specified entity. </returns>
        public IEnumerable<CarriedBlock> GetAllCarried(Entity entity)
        {
            return Services.State.GetAllCarried(entity);
        }

        /// <summary>
        /// Gets the CarriedBlock carried by the entity in the specified carry slot.
        /// </summary>
        /// <param name="entity"> The entity whose carried block is being retrieved. </param>
        /// <param name="slot"> The carry slot from which to retrieve the carried block. </param>
        /// <returns> The CarriedBlock in the specified carry slot, or null if none exists. </returns>
        /// <exception cref="ArgumentNullException"></exception>
        public CarriedBlock? GetCarried(Entity entity, CarrySlot slot)
        {
            return Services.State.GetCarried(entity, slot);
        }


        /// <summary>
        /// Sets the CarriedBlock for the entity in the specified carry slot.
        /// </summary>
        /// <param name="entity"> The entity for which to set the carried block. </param>
        /// <param name="carriedBlock"> The CarriedBlock to set for the entity. </param>
        public void SetCarried(Entity entity, CarriedBlock carriedBlock) => Services.State.SetCarried(entity, carriedBlock);

        /// <summary>
        /// Sets the CarriedBlock for the entity in the specified carry slot.
        /// </summary>
        /// <param name="entity"> The entity for which to set the carried block. </param>
        /// <param name="carriedBlock"> The CarriedBlock to set for the entity. </param>
        /// <param name="markDirty"> Whether to mark the entity's carried attributes as dirty. </param>
        public void SetCarried(Entity entity, CarriedBlock carriedBlock, bool markDirty = true)
        {
            Services.State.SetCarried(entity, carriedBlock, markDirty);
        }

        /// <summary>
        /// Sets the CarriedBlock for the entity in the specified carry slot.
        /// </summary>
        /// <param name="entity"> The entity for which to set the carried block. </param>
        /// <param name="slot"> The carry slot in which to set the carried block. </param>
        /// <param name="stack"> The ItemStack to set for the carried block. </param>
        /// <param name="blockEntityData"> The block entity data to set for the carried block. </param>
        public void SetCarried(Entity entity, CarrySlot slot, ItemStack stack, ITreeAttribute? blockEntityData)
              => Services.State.SetCarried(entity, slot, stack, blockEntityData);

        /// <summary>
        /// Sets the CarriedBlock for the entity in the specified carry slot.
        /// </summary>
        /// <param name="entity"> The entity for which to set the carried block. </param>
        /// <param name="slot"> The carry slot in which to set the carried block. </param>
        /// <param name="stack"> The ItemStack to set for the carried block. </param>
        /// <param name="blockEntityData"> The block entity data to set for the carried block. </param>
        /// <param name="markDirty"> Whether to mark the entity's carried attributes as dirty. </param>
        /// <exception cref="ArgumentNullException"></exception>
        public void SetCarried(Entity entity, CarrySlot slot, ItemStack stack, ITreeAttribute? blockEntityData, bool markDirty = true)
        {
            Services.State.SetCarried(entity, slot, stack, blockEntityData, markDirty);
        }


        /// <summary>
        /// Removes the CarriedBlock from the entity in the specified carry slot.
        /// </summary>
        /// <param name="entity"> The entity from which to remove the carried block. </param>
        /// <param name="slot"> The carry slot from which to remove the carried block. </param>
        /// <exception cref="ArgumentNullException"></exception>
        public void RemoveCarried(Entity entity, CarrySlot slot) => Services.State.RemoveCarried(entity, slot);


        /// <summary>
        /// Removes the CarriedBlock from the entity in the specified carry slot.
        /// </summary>
        /// <param name="entity"> The entity from which to remove the carried block. </param>
        /// <param name="slot"> The carry slot from which to remove the carried block. </param>
        /// <param name="markDirty"> Whether to mark the entity's carried attributes as dirty. </param>
        /// <exception cref="ArgumentNullException"></exception>
        public void RemoveCarried(Entity entity, CarrySlot slot, bool markDirty = true)
        {
            Services.State.RemoveCarried(entity, slot, markDirty);
        }

        /// <summary>
        ///   Attempts to swap the <see cref="CarriedBlock"/>s currently carried in the
        ///   entity's <paramref name="first"/> and <paramref name="second"/> slots.
        /// </summary>
        /// <param name="entity"> The entity whose carried blocks are being swapped. </param>
        /// <param name="first"> The first carry slot. </param>
        /// <param name="second"> The second carry slot. </param>
        /// <exception cref="ArgumentNullException"> Thrown if entity is null. </exception>
        public bool SwapCarried(Entity entity, CarrySlot first, CarrySlot second)
        {
            return Services.State.SwapCarried(entity, first, second);
        }

        /// <summary>
        /// Gets a CarriedBlock from the world at the specified position and slot.
        /// The block is removed from the world.
        /// </summary>
        /// <param name="pos"> The position of the block in the world. </param>
        /// <param name="slot"> The carry slot. </param>
        /// <param name="checkIsCarryable">If <c>true</c>, checks if the block is carryable in a particular slot.</param>
        /// <returns> The CarriedBlock from the world, or null if none exists. </returns>
        public CarriedBlock GetCarriedFromWorld(BlockPos pos, CarrySlot slot, bool checkIsCarryable = false)
        {
            return Services.Placement.GetCarriedFromWorld(pos, slot, checkIsCarryable)!;
        }


        /// <summary>
        /// Gets a CarriedBlock from the world at the specified position and slot.
        /// The block is removed from the world.
        /// </summary>
        /// <param name="entity"> The entity attempting to pick up the block. </param>
        /// <param name="pos"> The position of the block in the world. </param>
        /// <param name="slot"> The carry slot. </param>
        /// <param name="checkIsCarryable">If <c>true</c>, checks if the block is carryable in a particular slot.</param>
        /// <returns> The CarriedBlock from the world, or null if none exists. </returns>
        public CarriedBlock? GetCarriedFromWorld(Entity entity, BlockPos pos, CarrySlot slot, ref string failureCode, bool checkIsCarryable = false)
        {
            return Services.Placement.GetCarriedFromWorld(entity, pos, slot, ref failureCode, checkIsCarryable);
        }

        /// <summary>
        /// Restores the block at a specified position with the entity data from the carried block.
        /// </summary>
        /// <param name="world"> The world in which the block exists. </param>
        /// <param name="carriedBlock"> The carried block containing the entity data. </param>
        /// <param name="pos"> The position of the block in the world. </param>
        /// <param name="dropped">Signal block was dropped to any delegates</param>
        public void RestoreBlockEntityData(IWorldAccessor world, CarriedBlock carriedBlock, BlockPos pos, bool dropped = false)
        {
            Services.Placement.RestoreBlockEntityData(world, carriedBlock, pos, dropped);
        }




        /// <summary>
        /// Attempts to pick up a block from the specified position.
        /// </summary>
        /// <param name="entity"> The entity attempting to pick up the block. </param>
        /// <param name="pos"> The position of the block in the world. </param>
        /// <param name="slot"> The carry slot. </param>
        /// <param name="failureCode"> The failure code to be set if pickup fails. </param>
        /// <param name="checkIsCarryable"> Whether to check if the block is carryable. </param>
        /// <param name="playSound"> Whether to play a sound when picking up the block. </param>
        /// <returns> True if the block was successfully picked up, false otherwise. </returns>
        public bool TryPickUp(
            Entity entity,
            BlockPos pos,
            CarrySlot slot,
            ref string failureCode,
            bool checkIsCarryable = true,
            bool playSound = true)
        {
            return Services.Placement.TryPickUp(entity, pos, slot, ref failureCode, checkIsCarryable, playSound);
        }


        /// <summary>
        /// Attempts to pick up a block from the specified position.
        /// </summary>
        /// <param name="entity"> The entity attempting to pick up the block. </param>
        /// <param name="pos"> The position of the block in the world. </param>
        /// <param name="slot"> The carry slot. </param>
        /// <param name="checkIsCarryable"> Whether to check if the block is carryable. </param>
        /// <param name="playSound"> Whether to play a sound when picking up the block. </param>
        /// <returns> True if the block was successfully picked up, false otherwise. </returns>
        public bool TryPickUp(Entity entity, BlockPos pos, CarrySlot slot, bool checkIsCarryable = true, bool playSound = true)
        {
            return Services.Placement.TryPickUp(entity, pos, slot, checkIsCarryable, playSound);
        }

        /// <summary>
        /// Tries to place the carriedBlock in the world, removing from entity if successful
        /// </summary>
        /// <param name="entity"> The entity attempting to place the block. </param>
        /// <param name="carriedBlock"> The carried block to be placed. </param>
        /// <param name="selection"> The block selection indicating where to place the block. </param>
        /// <param name="dropped">If <c>true</c>, the block is set in world instead of placed, bypassing some checks.</param>
        /// <param name="playSound"> Whether to play a sound when placing the block. </param>
        /// <returns> True if the block was successfully placed, false otherwise. </returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool TryPlaceDown(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, bool dropped = false, bool playSound = true)
        {
            return Services.Placement.TryPlaceDown(entity, carriedBlock, selection, dropped, playSound);
        }

        /// <summary>
        /// Tries to place the carriedBlock in the world, removing from entity if successful
        /// </summary>
        /// <param name="entity"> The entity attempting to place the block. </param>
        /// <param name="carriedBlock"> The carried block to be placed. </param>
        /// <param name="selection"> The block selection indicating where to place the block. </param>
        /// <param name="failureCode"> The failure code to be set if placement fails. </param>
        /// <param name="dropped">If <c>true</c>, the block is set in world instead of placed, bypassing some checks.</param>
        /// <param name="playSound"> Whether to play a sound when placing the block. </param>
        /// <returns> True if the block was successfully placed, false otherwise. </returns>
        /// <exception cref="ArgumentNullException"></exception>
        public bool TryPlaceDown(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, ref string failureCode, bool dropped = false, bool playSound = true)
        {
            return Services.Placement.TryPlaceDown(entity, carriedBlock, selection, ref failureCode, dropped, playSound);
        }

        /// <summary>
        /// Attempts to place the carried block at the specified position.
        /// </summary>
        /// <param name="player"> The player attempting to place the block. </param>
        /// <param name="carrySlot"> The carry slot to place from. </param>
        /// <param name="selection"> The block selection indicating where to place the block. </param>
        /// <param name="placedAt"> Position of where the block was placed. It may have replaced the selected block. </param>
        /// <returns> True if the block was successfully placed, false otherwise. </returns>
        public bool TryPlaceDownAt(IPlayer player, CarrySlot carrySlot, BlockSelection selection, out BlockPos? placedAt)
        {
            return Services.Placement.TryPlaceDownAt(player, carrySlot, selection, out placedAt);
        }

        /// <summary>
        /// Tries to place the carried block at the specified position.
        /// </summary>
        /// <param name="player"> The player attempting to place the block. </param>
        /// <param name="carrySlot"> The carry slot to place from. </param>
        /// <param name="selection"> The block selection indicating where to place the block. </param>
        /// <param name="placedAt"> Position of where the block was placed. It may have replaced the selected block. </param>
        /// <param name="failureCode"> A reference to a string that will contain the failure code if the placement fails. </param>
        /// <returns> True if the block was successfully placed, false otherwise. </returns>
        public bool TryPlaceDownAt(IPlayer player, CarrySlot carrySlot, BlockSelection selection, out BlockPos? placedAt, ref string failureCode)
        {
            return Services.Placement.TryPlaceDownAt(player, carrySlot, selection, out placedAt, ref failureCode);
        }

        /// <summary>
        /// Tries to attach the carried block in player hands to an entity attachment slot.
        /// </summary>
        /// <param name="player"> The player attempting to attach. </param>
        /// <param name="targetEntityId"> The target entity id. </param>
        /// <param name="slotIndex"> The attachment selection box slot index. </param>
        /// <param name="playSound"> Whether to play the attach sound. </param>
        /// <returns> True if the block was successfully attached; otherwise false. </returns>
        public bool TryAttach(IServerPlayer player, long targetEntityId, int slotIndex, bool playSound = true)
        {
            return Services.Attachment.TryAttach(player, targetEntityId, slotIndex, playSound);
        }

        /// <summary>
        /// Tries to attach the carried block in player hands to an entity attachment slot.
        /// </summary>
        /// <param name="player"> The player attempting to attach. </param>
        /// <param name="targetEntityId"> The target entity id. </param>
        /// <param name="slotIndex"> The attachment selection box slot index. </param>
        /// <param name="failureCode"> The failure code to be set if attaching fails. </param>
        /// <param name="playSound"> Whether to play the attach sound. </param>
        /// <returns> True if the block was successfully attached; otherwise false. </returns>
        public bool TryAttach(IServerPlayer player, long targetEntityId, int slotIndex, ref string failureCode, bool playSound = true)
        {
            return Services.Attachment.TryAttach(player, targetEntityId, slotIndex, ref failureCode, playSound);
        }

        /// <summary>
        /// Tries to detach a carryable block from an entity attachment slot to player hands.
        /// </summary>
        /// <param name="player"> The player attempting to detach. </param>
        /// <param name="targetEntityId"> The target entity id. </param>
        /// <param name="slotIndex"> The attachment selection box slot index. </param>
        /// <param name="playSound"> Whether to play the detach sound. </param>
        /// <returns> True if the block was successfully detached; otherwise false. </returns>
        public bool TryDetach(IServerPlayer player, long targetEntityId, int slotIndex, bool playSound = true)
        {
            return Services.Attachment.TryDetach(player, targetEntityId, slotIndex, playSound);
        }

        /// <summary>
        /// Tries to detach a carryable block from an entity attachment slot to player hands.
        /// </summary>
        /// <param name="player"> The player attempting to detach. </param>
        /// <param name="targetEntityId"> The target entity id. </param>
        /// <param name="slotIndex"> The attachment selection box slot index. </param>
        /// <param name="failureCode"> The failure code to be set if detaching fails. </param>
        /// <param name="playSound"> Whether to play the detach sound. </param>
        /// <returns> True if the block was successfully detached; otherwise false. </returns>
        public bool TryDetach(IServerPlayer player, long targetEntityId, int slotIndex, ref string failureCode, bool playSound = true)
        {
            return Services.Attachment.TryDetach(player, targetEntityId, slotIndex, ref failureCode, playSound);
        }

        /// <summary>
        /// Checks if the entity has permission to carry the block at the specified position.
        /// Delegates to the placement service.
        /// </summary>
        /// <param name="entity"> The entity attempting to carry the block. </param>
        /// <param name="pos"> The position of the block. </param>
        /// <returns> True if the entity has permission to carry the block, false otherwise. </returns>
        public bool HasPermissionToCarry(Entity entity, BlockPos pos)
        {
            return Services.Placement.HasPermissionToCarry(entity, pos);
        }

        /// <summary>
        /// Sends a message to the player to lock the hotbar slots.
        /// </summary>
        /// <param name="player"> The player to whom the message will be sent. </param>
        public void LockHotbarSlots(IServerPlayer player)
        {
            Services.State.LockHotbarSlots(player);
        }

        /// <summary>
        /// Sends a message to the player to lock the hotbar slots.
        /// </summary>
        /// <param name="player"> The player to whom the message will be sent. </param>
        internal void SendLockSlotsMessage(EntityPlayer player)
        {
            Services.State.SendLockSlotsMessage(player);
        }

        /// <summary>
        /// Drops the carried blocks from specified slots on the entity.
        /// </summary>
        /// <param name="entity"> The entity from which to drop the carried blocks. </param>
        /// <param name="slots"> The carried slots to drop. </param>
        /// <param name="range"> The radius to check for placement. </param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void DropCarried(Entity entity, IEnumerable<CarrySlot> slots, int range = 4)
        {
            Services.Drop.DropCarried(entity, slots, range);
        }

        /// <summary>
        /// Drops the block from the specified carriedBlock.
        /// </summary>
        /// <param name="entity"> The entity attempting to carry the block. </param>
        /// <param name="carriedBlock"> The carried block to drop. </param>
        /// <param name="range"> The radius to check for placement. </param>
        /// <param name="blockPlacer"> The block placer to use for placing the block. </param>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public void DropCarriedBlock(Entity entity, CarriedBlock carriedBlock, int range = 4, BlockPlacer? blockPlacer = null)
        {
            Services.Drop.DropCarriedBlock(entity, carriedBlock, range, blockPlacer);
        }

        /// <summary>
        /// Drops a carried block as an item.
        /// All items in the carried block's inventory will be dropped if applicable.
        /// </summary>
        /// <param name="carriedBlock"> The carried block to drop as an item. </param>
        /// <param name="centerBlock"> The position where the block is being dropped. </param>
        /// <param name="player"> The player associated with the drop, if any. </param>
        /// <param name="entity"> The entity from which the block is being dropped. </param>
        public void DropBlockAsItem(CarriedBlock carriedBlock, BlockPos centerBlock, IServerPlayer player, Entity entity)
        {
            Services.Drop.DropBlockAsItem(carriedBlock, centerBlock, player, entity);
        }

        /// <summary>
        /// Increments the carried-state revision and marks the carried root dirty if necessary.
        /// Client side only returns the current revision, while server side increments and returns the new revision.
        /// </summary>
        /// <param name="entity"> The entity whose carried attributes are being touched. </param>
        /// <returns> The new revision number. </returns>
        /// <exception cref="ArgumentNullException"> Thrown if entity is null. </exception>
        public int TouchCarriedAttributes(Entity entity)
        {
            return Services.State.TouchCarriedAttributes(entity);
        }

        /// <summary>
        /// Gets the current carried-state revision for the entity. This can be used to check if the carried state has changed since the last time it was checked.
        /// </summary>
        /// <param name="entity"> The entity whose carried attributes are being queried. </param>
        /// <returns> The current carried-state revision. </returns>
        /// <exception cref="ArgumentNullException"> Thrown if entity is null. </exception>
        public int GetCarriedRevision(Entity entity)
        {
            return Services.State.GetCarriedRevision(entity);
        }


        /// <summary>
        /// Initializes carry events discovered by the event bootstrap service.
        /// </summary>
        /// <param name="api"> The core API instance. </param>
        public void InitEvents(ICoreAPI api)
        {
            Services.EventBootstrapper.InitEvents(api);
        }

        /// <summary>
        /// Checks if the block is carryable in the specified slot.
        /// </summary>
        /// <param name="block"> The block to check. </param>
        /// <param name="slot"> The slot to check. </param>
        /// <returns> True if the block is carryable in the specified slot, otherwise false. </returns>
        public bool IsCarryable(Block block, CarrySlot slot)
            => block.GetBehavior<BlockBehaviorCarryable>()?.Slots?[slot] != null;

        /// <summary>
        /// Checks if the block is carryable.
        /// </summary>
        /// <param name="block"> The block to check. </param>
        /// <returns> True if the block is carryable, otherwise false. </returns>
        public bool IsCarryable(Block block)
            => block.HasBehavior<BlockBehaviorCarryable>();

        /// <summary>
        /// Checks if the entity can interact with a block while carrying in hands.
        /// </summary>
        /// <param name="block"> The block to check. </param>
        /// <returns> True if the entity can interact with the block while carrying, otherwise false. </returns>
        public bool CanInteractWhileCarrying(Block block)
            => block.HasBehavior<BlockBehaviorCarryableInteract>();
    }
}