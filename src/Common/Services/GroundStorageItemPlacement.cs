using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Common.Services
{
    /// <summary>
    /// Server-authoritative placement of ground-storable item stacks (e.g. a bag picked up
    /// from ground storage) into ground storage: depositing into an existing storage or
    /// creating a new one, replicating the vanilla
    /// <see cref="CollectibleBehaviorGroundStorable.Interact"/> pipeline without its
    /// player-hand prerequisites. Success is always verified by the storage actually holding
    /// the stack afterwards - vanilla's own signals are unreliable:
    /// <see cref="BlockEntityGroundStorage"/> deposits return <c>true</c> even when the
    /// transfer moved nothing, and the create pipeline deposits through the player's real
    /// hotbar slot, whose transfer rules differ per game mode (creative deposits a clone and
    /// never consumes the slot; survival can refuse the transfer outright, after which
    /// vanilla self-deletes the still-empty storage). The create core therefore replicates
    /// only the placement half of <see cref="BlockGroundStorage.CreateStorage"/> and
    /// deposits the carried stack directly, exactly like the deposit path.
    /// </summary>
    internal sealed class GroundStorageItemPlacement(ICoreAPI api, ICarryManager carryManager)
    {
        /// <summary>
        /// Places an item-class carried stack at the given player selection, mirroring
        /// <see cref="CollectibleBehaviorGroundStorable.Interact"/>: an existing ground
        /// storage at the selected cell (or directly above it) receives the stack; otherwise
        /// a new storage is created above an upward click that can attach it. Returns the
        /// cell the stack ended up in via <paramref name="placedAt"/>.
        /// </summary>
        public bool TryPlaceAtSelection(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, out BlockPos? placedAt)
        {
            placedAt = null;

            // Server-authoritative: the client predicts only the anchor cell (via
            // BlockUtils.GetGroundStoragePlacementPosition) and never mutates the world, so
            // no client-side ghost storage can appear.
            if (api.Side == EnumAppSide.Client) return false;

            var bagProps = GetGroundStorageProps(carriedBlock.ItemStack);
            if (bagProps == null) return false;

            var world = api.World;

            var storage = world.BlockAccessor.GetBlockEntity(selection.Position) as BlockEntityGroundStorage
                ?? world.BlockAccessor.GetBlockEntity(selection.Position.UpCopy(1)) as BlockEntityGroundStorage;

            if (storage != null)
            {
                if (!TryDeposit(entity, storage, carriedBlock, selection)) return false;
                placedAt = storage.Pos.Copy();
                return true;
            }

            // New-storage branch, mirroring the vanilla Interact gate: only an upward click
            // on a block that can attach the storage, with a replaceable cell above it.
            if (selection.Face != BlockFacing.UP) return false;

            var groundStorageBlock = world.BlockAccessor.GetBlock(new AssetLocation("groundstorage")) as BlockGroundStorage;
            var clickedBlock = world.BlockAccessor.GetBlock(selection.Position);
            if (groundStorageBlock == null
                || !clickedBlock.CanAttachBlockAt(world.BlockAccessor, groundStorageBlock, selection.Position, BlockFacing.UP, null))
                return false;

            var cell = selection.Position.AddCopy(selection.Face);
            if (world.BlockAccessor.GetBlock(cell).Replaceable < 6000) return false;
            if (!carryManager.HasPermissionAt(entity, cell, showErrorMessage: false)) return false;

            if (!TryCreateWithSelection(entity, carriedBlock, selection, bagProps)) return false;

            placedAt = cell;
            return true;
        }

        /// <summary>
        /// Deposits an item-class carried stack into an existing ground storage, replicating
        /// the server side of vanilla <c>OnPlayerInteractStart</c>/<c>putOrGetItemSingle</c>:
        /// storage-properties refresh, layout compatibility, then a transfer into the clicked
        /// slot (or a free slot of the layout's usable set). An occupied slot never receives
        /// the stack - vanilla's occupied-slot branch takes from the storage instead, which a
        /// place-down must never do. Stacking/Messy12 storages aggregate a matching stack
        /// type into their single slot, refusing at capacity.
        /// </summary>
        /// <param name="selection">The player's click on the storage block; null selects the
        /// first free usable slot instead of the clicked one (drop path).</param>
        public bool TryDeposit(Entity entity, BlockEntityGroundStorage storage, CarriedBlock carriedBlock, BlockSelection? selection)
        {
            var stack = carriedBlock.ItemStack;
            var bagProps = GetGroundStorageProps(stack);
            if (stack == null || bagProps == null) return false;

            if (!carryManager.HasPermissionAt(entity, storage.Pos, showErrorMessage: false)) return false;

            var player = (entity as EntityPlayer)?.Player;
            if (player != null && !BlockBehaviorReinforcable.AllowRightClickPickup(api.World, storage.Pos, player))
                return false;

            var wasEmpty = storage.Inventory.Empty;

            // Mirrors OnPlayerInteractStart: derive/refresh the storage's layout properties
            // before the deposit - an empty storage adopts the incoming stack's layout.
            storage.DetermineStorageProperties(new DummySlot(stack));

            if (!wasEmpty)
            {
                // Layout gate from putOrGetItemSingle: layouts must match, except that a
                // Messy12 stack may join a Quadrants storage.
                var storageLayout = storage.StorageProps?.Layout;
                var bagLayout = bagProps.Layout;
                if (storageLayout != bagLayout
                    && !(storageLayout == EnumGroundStorageLayout.Quadrants && bagLayout == EnumGroundStorageLayout.Messy12))
                    return false;
            }

            var targetSlot = ResolveDepositSlot(storage, stack, selection);
            if (targetSlot == null) return false;

            // DummySlot source so the carried stack itself is never mutated by the transfer;
            // success is verified by the transfer actually moving items.
            var moved = new DummySlot(stack.Clone()).TryPutInto(api.World, targetSlot, stack.StackSize);
            if (moved <= 0) return false;

            storage.LightUpdate(targetSlot.Itemstack);
            storage.MarkDirty(true);
            SoundHelper.PlaySound(api, storage.StorageProps?.PlaceRemoveSound, storage.Pos, entity as EntityPlayer, dualCall: false);
            return true;
        }

        /// <summary>
        /// Creates a new ground storage holding an item-class carried stack at
        /// <paramref name="pos"/>, for the drop pipeline where no player selection exists.
        /// Builds synthetic selections for the two gestures vanilla ground storage supports -
        /// the ground top face first, then each horizontal face so wall-attached storages
        /// (WallHalves) can find a wall - and routes each through the create core.
        /// </summary>
        public bool TryCreate(Entity entity, CarriedBlock carriedBlock, BlockPos pos)
        {
            var bagProps = GetGroundStorageProps(carriedBlock.ItemStack);
            if (bagProps == null) return false;

            var world = api.World;
            var groundStorageBlock = world.BlockAccessor.GetBlock(new AssetLocation("groundstorage")) as BlockGroundStorage;
            if (groundStorageBlock == null) return false;

            var playerEntity = entity as EntityPlayer;
            var player = playerEntity != null ? world.PlayerByUid(playerEntity.PlayerUID) : null;
            if (playerEntity == null || player == null) return false;

            foreach (var (clicked, face) in GetPlacementFaces(pos))
            {
                var cell = clicked.AddCopy(face);
                if (!CanCreateStorageAt(cell, groundStorageBlock)) continue;
                if (!carryManager.HasPermissionAt(entity, cell, showErrorMessage: false)) continue;

                var blockSel = new BlockSelection
                {
                    Position = clicked,
                    Face = face,
                    HitPosition = new Vec3d(0.5, 0.5, 0.5),
                };

                if (TryCreateStorage(playerEntity, player, groundStorageBlock, carriedBlock, bagProps, blockSel, cell))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a new ground storage holding an item-class carried stack using the
        /// player's real selection (the placement cell is the selection's position offset by
        /// its face), mirroring the create branch of vanilla
        /// <see cref="CollectibleBehaviorGroundStorable.Interact"/>. The caller has already
        /// verified the attach, replaceability and permission gates.
        /// </summary>
        public bool TryCreateWithSelection(Entity entity, CarriedBlock carriedBlock, BlockSelection selection, GroundStorageProperties bagProps)
        {
            var world = api.World;
            var groundStorageBlock = world.BlockAccessor.GetBlock(new AssetLocation("groundstorage")) as BlockGroundStorage;
            if (groundStorageBlock == null) return false;

            var playerEntity = entity as EntityPlayer;
            var player = playerEntity != null ? world.PlayerByUid(playerEntity.PlayerUID) : null;
            if (playerEntity == null || player == null) return false;

            var cell = selection.Position.AddCopy(selection.Face);
            return TryCreateStorage(playerEntity, player, groundStorageBlock, carriedBlock, bagProps, selection.Clone(), cell);
        }

        /// <summary>
        /// Shared create core, replicating the placement half of vanilla
        /// <see cref="BlockGroundStorage.CreateStorage"/> - height cap, ground attach,
        /// WallHalves wall-attach resolution with horizontal fallback, mesh angle, block
        /// placement, block entity orientation and player unstick - then depositing the
        /// carried stack itself through the same mode-independent transfer
        /// <see cref="TryDeposit"/> uses. Vanilla's deposit (routed through the player's
        /// real hotbar slot) is bypassed entirely, so no hotbar injection, control forcing
        /// or remainder handling is needed.
        /// </summary>
        private bool TryCreateStorage(
            EntityPlayer playerEntity,
            IPlayer player,
            BlockGroundStorage groundStorageBlock,
            CarriedBlock carriedBlock,
            GroundStorageProperties bagProps,
            BlockSelection blockSel,
            BlockPos cell)
        {
            var world = api.World;
            var blockAccessor = world.BlockAccessor;
            var stack = carriedBlock.ItemStack!;

            if (cell.Y >= blockAccessor.MapSizeY) return false;

            // Callers pre-verify this; kept as the authoritative vanilla gate.
            var ground = cell.DownCopy(1);
            if (!blockAccessor.GetBlock(ground).CanAttachBlockAt(blockAccessor, groundStorageBlock, ground, BlockFacing.UP, null))
                return false;

            // Mesh angle from the click geometry, exactly as vanilla computes it.
            var anglePos = blockSel.DidOffset ? blockSel.Position.AddCopy(blockSel.Face!.Opposite) : blockSel.Position;
            var hit = blockSel.HitPosition ?? new Vec3d(0.5, 0.5, 0.5);
            var dX = playerEntity.Pos.X - (anglePos.X + hit.X);
            var dZ = playerEntity.Pos.Z - (anglePos.Z + hit.Z);
            var quarterTurn = (float)Math.PI / 2f;
            var meshAngle = (float)(int)Math.Round(Math.Atan2(dX, dZ) / quarterTurn) * quarterTurn;

            BlockFacing? attachFace = null;
            if (bagProps.Layout == EnumGroundStorageLayout.WallHalves)
            {
                // Wall attach: try the suggested orientation, then any horizontal, both at
                // the wall cell the storage would hang from.
                attachFace = Block.SuggestedHVOrientation(player, blockSel)[0];
                if (!CanWallAttach(cell.AddCopy(attachFace).Up(bagProps.WallOffY - 1), groundStorageBlock, attachFace))
                {
                    attachFace = null;
                    foreach (var face in BlockFacing.HORIZONTALS)
                    {
                        if (!CanWallAttach(cell.AddCopy(face).Up(bagProps.WallOffY - 1), groundStorageBlock, face)) continue;
                        attachFace = face;
                        break;
                    }
                }

                if (attachFace == null) return false;
                meshAngle = (float)Math.Atan2(attachFace.Normali.X, attachFace.Normali.Z);
            }

            blockAccessor.SetBlock(groundStorageBlock.Id, cell);

            var storage = blockAccessor.GetBlockEntity(cell) as BlockEntityGroundStorage;
            if (storage == null)
            {
                blockAccessor.SetBlock(0, cell);
                return false;
            }

            storage.MeshAngle = meshAngle;
            storage.AttachFace = attachFace;

            // Deposit the carried stack directly: an empty storage adopts its properties,
            // then the full stack goes into the clicked slot - the same resolution the
            // deposit path uses, so a Halves/WallHalves/Quadrants storage is filled where
            // the player aims (vanilla's create flow deposits via GetSlotAt too, with
            // MeshAngle and StorageProps already set). DummySlot source so no player hand
            // is involved and the carried stack itself is never mutated.
            storage.DetermineStorageProperties(new DummySlot(stack));
            var targetSlot = ResolveDepositSlot(storage, stack, blockSel) ?? storage.Inventory[0];
            var moved = new DummySlot(stack.Clone()).TryPutInto(world, targetSlot, stack.StackSize);
            if (moved <= 0)
            {
                blockAccessor.SetBlock(0, cell);
                blockAccessor.TriggerNeighbourBlockUpdate(cell);
                return false;
            }

            storage.LightUpdate(targetSlot.Itemstack);
            storage.MarkDirty(true);
            SoundHelper.PlaySound(api, storage.StorageProps?.PlaceRemoveSound, storage.Pos, playerEntity, dualCall: false);

            // Player unstick, mirroring vanilla: lift the player out of the fresh storage's
            // collision box.
            var collisionBoxes = groundStorageBlock.GetCollisionBoxes(blockAccessor, cell);
            if (collisionBoxes != null && collisionBoxes.Length > 0
                && CollisionTester.AabbIntersect(collisionBoxes[0], cell.X, cell.Y, cell.Z, playerEntity.SelectionBox, playerEntity.Pos.XYZ))
            {
                playerEntity.Pos.Y += collisionBoxes[0].Y2;
            }

            return true;
        }

        /// <summary>
        /// Whether the block at <paramref name="wallPos"/> can hold the storage attached at
        /// the given face - vanilla's wall-attach test for WallHalves creation.
        /// </summary>
        private bool CanWallAttach(BlockPos wallPos, Block groundStorageBlock, BlockFacing face)
        {
            var blockAccessor = api.World.BlockAccessor;
            return blockAccessor.GetBlock(wallPos).CanAttachBlockAt(blockAccessor, groundStorageBlock, wallPos, face.Opposite, null);
        }

        /// <summary>
        /// Resolves the storage slot a deposit goes into: the clicked slot (vanilla's
        /// hit-position mapping via <see cref="BlockEntityGroundStorage.GetSlotAt"/> when it
        /// is free, otherwise the first free slot of the layout's usable set. Stacking and
        /// Messy12 storages aggregate a matching stack into their single slot instead, up to
        /// capacity. Returns null when the storage cannot take the stack.
        /// </summary>
        private ItemSlot? ResolveDepositSlot(BlockEntityGroundStorage storage, ItemStack stack, BlockSelection? selection)
        {
            var inventory = storage.Inventory;
            if (inventory.Empty)
            {
                // A fresh-empty storage takes the stack into the clicked slot - vanilla's
                // GetSlotAt mapping is mesh-rotation aware, so a quadrant/half storage is
                // filled where the player aims; the first slot only when no click position
                // exists (drop path).
                if (selection?.HitPosition != null && storage.GetSlotAt(selection) is { } target)
                    return target;
                return inventory[0];
            }

            var layout = storage.StorageProps?.Layout;

            if (layout is EnumGroundStorageLayout.Stacking or EnumGroundStorageLayout.Messy12)
            {
                var slot = inventory[0];
                if (slot.Empty) return slot;
                if (storage.TotalStackSize >= storage.Capacity) return null;
                return slot.Itemstack?.Equals(api.World, stack, GlobalConstants.IgnoredStackAttributes) == true
                    ? slot
                    : null;
            }

            var usable = layout == null ? 0 : storage.UsableSlots(layout.Value);
            if (usable <= 0) return null;

            if (selection?.HitPosition != null && storage.GetSlotAt(selection) is { Empty: true } clicked)
                return clicked;

            for (var i = 0; i < usable && i < inventory.Count; i++)
            {
                if (inventory[i].Empty) return inventory[i];
            }

            return null;
        }

        /// <summary>
        /// The vanilla CreateStorage gates for a placement cell: the block below must
        /// support attaching the storage, and the cell itself must be replaceable.
        /// </summary>
        private bool CanCreateStorageAt(BlockPos cell, Block groundStorageBlock)
        {
            var blockAccessor = api.World.BlockAccessor;
            var ground = cell.DownCopy(1);

            if (!blockAccessor.GetBlock(ground).CanAttachBlockAt(blockAccessor, groundStorageBlock, ground, BlockFacing.UP, null))
                return false;

            return blockAccessor.GetBlock(cell).Replaceable >= 6000;
        }

        /// <summary>
        /// The block-face selections whose placement cell is <paramref name="pos"/>: the
        /// block below clicked on its top face, plus each horizontal neighbor clicked through
        /// its facing. Mirrors the two placement gestures vanilla ground storage supports.
        /// </summary>
        private static IEnumerable<(BlockPos Clicked, BlockFacing Face)> GetPlacementFaces(BlockPos pos)
        {
            yield return (pos.DownCopy(1), BlockFacing.UP);

            foreach (var face in BlockFacing.HORIZONTALS)
            {
                yield return (pos.AddCopy(-face.Normali.X, 0, -face.Normali.Z), face);
            }
        }

        private static GroundStorageProperties? GetGroundStorageProps(ItemStack? stack)
            => stack?.Collectible?.GetBehavior<CollectibleBehaviorGroundStorable>()?.StorageProps;
    }
}
