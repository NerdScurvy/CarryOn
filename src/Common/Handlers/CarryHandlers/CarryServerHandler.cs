using System;
using System.Collections.Generic;
using System.Linq;
using CarryOn.Common.Behaviors;
using CarryOn.Common.Entities;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Logic;
using CarryOn.Common.Models;
using CarryOn.Common.Network;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using static CarryOn.Common.Models.CarryCodes;

namespace CarryOn.Common.Handlers.CarryHandlers
{
    internal class CarryServerHandler
    {
        private readonly ICarryManager carryManager;
        private readonly IConfigProvider configProvider;
        private ICoreServerAPI? api;
        private TransferLogic? transferLogic;

        internal bool BackSlotEnabled => configProvider.Config.CarryOptions?.BackSlotEnabled ?? false;
        internal bool OpenGroundStorageBagWhileCarrying => configProvider.Config.CarryOptions?.OpenGroundStorageBagWhileCarrying ?? true;

        private const int WrittenCellsMaxCount = 64;
        private const int WrittenCellsMaxLeash = 12;

        internal CarryServerHandler(ICarryManager carryManager, IConfigProvider configProvider)
        {
            this.carryManager = carryManager;
            this.configProvider = configProvider;
        }

        internal void Init(ICoreServerAPI api, TransferLogic transferLogic)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.transferLogic = transferLogic ?? throw new ArgumentNullException(nameof(transferLogic));
        }

        private ICoreServerAPI RequireApi([System.Runtime.CompilerServices.CallerMemberName] string? callerName = null)
        {
            var a = api;
            if (a == null)
            {
                var msg = callerName != null
                    ? $"{callerName} requires a server context."
                    : "ServerApi is not initialized.";
                throw new InvalidOperationException(msg);
            }
            return a;
        }

        // ------------------------------
        //  Server side message handlers
        // ------------------------------

        internal void OnInteractMessage(IServerPlayer player, InteractMessage message)
        {
            var world = player.Entity.World;
            var block = world.BlockAccessor.GetBlock(message.Position);

            if (block?.HasBlockBehavior<BlockBehaviorCarryableInteract>() == true)
            {
                var behavior = block.GetBehavior<BlockBehaviorCarryableInteract>();

                if (RefuseOutOfReachInteract(player, message.Position)) return;

                if (behavior.CanInteract(player))
                {
                    var blockSelection = message.Selection;
                    blockSelection.Block = block;
                    if (block.OnBlockInteractStart(world, player, blockSelection)
                        && block is IMultiBlockInteract)
                    {
                        // Multiblock gate-style blocks (e.g. Medieval Architecture's BlockGate,
                        // drawbridge, portcullis and gate-style trapdoors) commit their open/close
                        // only when the interaction stops, so the release must be dispatched too.
                        // Vanilla doors/trapdoors open on Start and ignore Stop, keeping this
                        // no-op for them.
                        block.OnBlockInteractStop(1f, world, player, blockSelection);
                    }
                }
            }
            else if (OpenGroundStorageBagWhileCarrying && block is BlockGroundStorage)
            {
                if (RefuseOutOfReachInteract(player, message.Position)) return;

                var blockSelection = message.Selection;
                blockSelection.Block = block;
                var beg = world.BlockAccessor.GetBlockEntity(message.Position)
                    as BlockEntityGroundStorage;
                if (beg.IsGroundStorageHeldBagTargeted(blockSelection))
                {
                    block.OnBlockInteractStart(world, player, blockSelection);
                }
            }
        }

        /// <summary>
        /// Reach gate for the interact path. Returns true when the interaction was refused and the
        /// caller must stop.
        /// <para>
        /// Rejection is MarkBlockDirty plus an error, not FailCarryAction, for two reasons. The
        /// client's <c>CarryInteractionStateMachine.ContinueInteractAction</c> (:275-279) already
        /// called <c>OnBlockInteractStart</c> locally - and while carrying, <c>selection.Block</c> is
        /// the gate's own behavior, not CarryOn's, because
        /// <c>BlockBehaviorCarryableInteract</c> is an <c>IConditionalBlockBehavior</c> that never
        /// overrides <c>OnBlockInteractStart</c> and only gates through <c>CanInteract</c>. So the
        /// gate is already open on the client by the time the server can refuse, and an
        /// error-only rejection would leave the phantom until some unrelated resync - the same class
        /// of desync ReconcileSignCells exists to prevent. MarkBlockDirty re-sends the block's
        /// server-truth variant and closes it again.
        /// </para>
        /// <para>
        /// FailCarryAction is the wrong tool: <c>CarryCodes.cs:168-170</c> defines only
        /// <c>PickUpFailed</c> and <c>PlaceDownFailed</c> as default fallback codes, so there is no
        /// <c>interact-failed</c> default to pass and inventing one would add a fallback code the
        /// client has never rendered; and nothing about the carry changed on this path, so the
        /// carried-revision bump and hotbar lock would be bookkeeping for a state transition that
        /// never happened. The only client-visible prediction to repair is the block toggle.
        /// </para>
        /// <para>
        /// The gate sits inside the branch that has already established the target is something this
        /// handler acts on, not at the top of <see cref="OnInteractMessage"/>. At the top it would
        /// fire for a spoofed interact naming any block anywhere and dirty-mark that attacker-chosen
        /// cell - handing a client an unbounded "mark any far block dirty" primitive over a channel
        /// that otherwise only ever carries CarryOn's own interactions. It also costs nothing here
        /// to place it later: the block read above is a chunk lookup the handler already performed
        /// unconditionally before this change.
        /// </para>
        /// </summary>
        private bool RefuseOutOfReachInteract(IServerPlayer player, BlockPos target)
        {
            if (CarryReach.IsWithinReach(player.Entity, target, GetMaxInteractionDistance())) return false;

            player.Entity.World.BlockAccessor.MarkBlockDirty(target);
            CarryErrorHelper.SendError(player, FailureCodes.BlockOutOfReach);
            return true;
        }

        internal void OnPickUpMessage(IServerPlayer player, PickUpMessage message)
        {
            if (message == null || message.Position == null)
            {
                FailCarryAction(player, message?.Position, FailureCodes.InvalidData, FailureCodes.PickUpFailed);
                return;
            }

            if (message.Slot != CarrySlot.Hands || !player.Entity.CanInteract(requireEmptyHanded: true))
            {
                FailCarryAction(player, message.Position, FailureCodes.CannotInteract, FailureCodes.PickUpFailed);
                return;
            }

            // Reach gate. Ahead of the sign scan and the pickup attempt, so a target the client
            // had no business naming costs no world reads at all. Creative is exempt, and the
            // metric is the actor's server-side eye to the cell centre - see CarryReach.
            if (!CarryReach.IsWithinReach(player.Entity, message.Position, GetMaxInteractionDistance()))
            {
                FailCarryAction(player, message.Position, FailureCodes.BlockOutOfReach, FailureCodes.PickUpFailed);
                return;
            }

            // Pickup reconciliation. The client predicts a pickup optimistically and its
            // local path has no permission filter, so it cleared every attached wall sign of
            // the footprint before this message existed. The whole-footprint claim gate and
            // the per-sign claim filter are server-only, so the server can only answer after
            // the fact. The scan is taken BEFORE the attempt, while the parent block is
            // still in the world (after a successful pickup its cell is air and the footprint
            // would scan empty).
            var signCandidates = ScanPickUpSignCandidates(player, message);

            string failureCode = FailureCodes.Ignore;
            if (carryManager.TryPickUp(
                player.Entity,
                message.Position,
                message.Slot,
                ref failureCode,
                checkIsCarryable: true,
                playSound: true,
                captureAttachedSigns: message.CaptureAttachedWallSigns))
            {
                // Success: the parent and every permitted sign are gone, and the client's own
                // prediction matches that. A candidate cell that is STILL occupied is a sign
                // the claim filter left behind that the client cleared anyway - the one cell
                // the server never wrote, so nothing else would ever tell the client.
                ReconcileSignCells(player, signCandidates, onlyCellsStillOccupied: true);
                return;
            }

            // Failure: nothing was removed server-side, so every candidate cell the client
            // cleared is a phantom (a sign is not part of the parent's multiblock, so
            // FailCarryAction's single MarkBlockDirty does not cover it).
            ReconcileSignCells(player, signCandidates, onlyCellsStillOccupied: false);
            FailCarryAction(player, message.Position, failureCode, FailureCodes.PickUpFailed);
        }

        /// <summary>
        /// The wall signs the client's optimistic path was entitled to clear for this
        /// message, or an empty list when the pickup could not have removed any (the same
        /// effective predicate CarryPickupService uses to capture: config
        /// CarryAttachedWallSigns AND the message's own flag).
        /// </summary>
        private List<CarrySignScan.Candidate> ScanPickUpSignCandidates(IServerPlayer player, PickUpMessage message)
        {
            if (configProvider.Config.CarryOptions?.CarryAttachedWallSigns != true) return [];
            if (!message.CaptureAttachedWallSigns) return [];

            var world = player.Entity.World;
            var block = world.BlockAccessor.GetBlock(message.Position);
            return CarrySignScan.Scan(world, message.Position, block);
        }

        /// <summary>
        /// Dirts each candidate sign cell on both vanilla channels: ExchangeBlock with
        /// the server-truth block id and a BlockEntities entry (a client that created a
        /// phantom sign block entity needs the removal/data marker a bare
        /// MarkBlockDirty does not deliver - README Decision 12).
        /// </summary>
        private static void ReconcileSignCells(
            IServerPlayer player,
            List<CarrySignScan.Candidate> candidates,
            bool onlyCellsStillOccupied)
        {
            if (candidates.Count == 0) return;

            var blockAccessor = player.Entity.World.BlockAccessor;
            foreach (var candidate in candidates)
            {
                if (onlyCellsStillOccupied && blockAccessor.GetBlock(candidate.SignPos).Id == 0) continue;
                blockAccessor.MarkBlockDirty(candidate.SignPos);
                blockAccessor.MarkBlockEntityDirty(candidate.SignPos);
            }
        }

        internal void OnPickUpGroundStorageBagMessage(IServerPlayer player, PickUpGroundStorageBagMessage message)
        {
            if (message == null || message.Position == null || message.HitPosition == null)
            {
                FailCarryAction(player, null, FailureCodes.InvalidData, FailureCodes.PickUpFailed);
                return;
            }

            if (!player.Entity.CanInteract(requireEmptyHanded: true)
                || carryManager.GetCarried(player.Entity, CarrySlot.Hands) != null)
            {
                FailCarryAction(player, message.Position, FailureCodes.AlreadyCarrying, FailureCodes.PickUpFailed);
                return;
            }

            var selection = message.Selection;
            selection.Block = player.Entity.World.BlockAccessor.GetBlock(selection.Position);

            string failureCode = FailureCodes.Ignore;
            if (carryManager.TryPickUpGroundStorageBag(
                player.Entity,
                selection,
                ref failureCode,
                GetMaxInteractionDistance()))
            {
                return;
            }

            FailCarryAction(player, message.Position, failureCode, FailureCodes.PickUpFailed);
        }

        internal void OnBagToBackpackMessage(IServerPlayer player, BagToBackpackMessage message)
        {
            if (message == null) return;

            if (!player.Entity.CanInteract(requireEmptyHanded: true))
            {
                CarryErrorHelper.SendError(player, FailureCodes.CannotInteract, "cannot-swap-back");
                return;
            }

            string failureCode = FailureCodes.Ignore;
            if (carryManager.TryStoreBagInBackpack(player.Entity, ref failureCode))
            {
                return;
            }

            CarryErrorHelper.SendError(player, failureCode);
        }

        internal void OnPlaceDownMessage(IServerPlayer player, PlaceDownMessage message)
        {
            if (message == null || message.Position == null || message.HitPosition == null || message.PlacedAt == null || message.Selection == null)
            {
                FailCarryAction(player, message?.Position, FailureCodes.InvalidData, FailureCodes.PlaceDownFailed);
                return;
            }

            if (!IsWrittenCellsValid(player, message))
            {
                FailCarryAction(player, message.Position, FailureCodes.InvalidData, FailureCodes.PlaceDownFailed);
                return;
            }

            // Reach gate, deliberately after the written-cell leash: the rejection below
            // reconciles message.WrittenCells, and that client-supplied array is only bounded
            // because IsWrittenCellsValid has already run. Reach first would hand an unbounded
            // attacker-chosen array to the reconcile.
            //
            // message.Position and message.Selection.Position are the same cell by construction,
            // but the server can also write the two cells it offsets from that one - the
            // face-adjacent cell and the cell above - so all three are gated. Offsets are taken
            // on copies because BlockPos.Offset mutates in place.
            BlockPos target = message.Position;
            BlockPos[] reachableCells =
            {
                target,
                target.Copy().Offset(message.Selection!.Face),
                target.UpCopy()
            };
            foreach (var cell in reachableCells)
            {
                if (CarryReach.IsWithinReach(player.Entity, cell, GetMaxInteractionDistance())) continue;

                ReconcilePredictedCells(player, message.WrittenCells);
                FailCarryAction(player, target, FailureCodes.BlockOutOfReach, FailureCodes.PlaceDownFailed);
                return;
            }

            if (!player.Entity.CanInteract(requireEmptyHanded: message.Slot != CarrySlot.Hands))
            {
                FailCarryAction(player, message.Position, FailureCodes.CannotInteract, FailureCodes.PlaceDownFailed);
                return;
            }

            string failureCode = FailureCodes.Ignore;
            if (carryManager.TryPlaceDownAt(
                player,
                message.Slot,
                message.Selection!,
                out var placedAt,
                ref failureCode))
            {
                if (placedAt != message.PlacedAt)
                {
                    if (message.WrittenCells is { Length: > 0 })
                    {
                        ReconcilePredictedCells(player, message.WrittenCells);
                    }
                    else
                    {
                        player.Entity.World.BlockAccessor.MarkBlockDirty(message.PlacedAt);
                        player.Entity.World.BlockAccessor.MarkBlockEntityDirty(message.PlacedAt);
                    }
                }
                return;
            }

            ReconcilePredictedCells(player, message.WrittenCells);
            FailCarryAction(player, message.Position, failureCode, FailureCodes.PlaceDownFailed);
        }

        private static void ReconcilePredictedCells(IServerPlayer player, BlockPos[]? cells)
        {
            if (cells == null) return;
            foreach (var cell in cells)
            {
                if (cell != null)
                {
                    player.Entity.World.BlockAccessor.MarkBlockDirty(cell);
                    player.Entity.World.BlockAccessor.MarkBlockEntityDirty(cell);
                }
            }
        }

        private static bool IsWrittenCellsValid(IServerPlayer player, PlaceDownMessage message)
        {
            if (message.WrittenCells is not { Length: > 0 }) return true;
            if (message.WrittenCells.Length > WrittenCellsMaxCount) return false;

            // The leash is intersected over **two** anchors, and the actor is the one that
            // matters.
            //
            // `message.PlacedAt` was the original anchor and is still enforced: it keeps
            // the dirty set clustered around where the client thinks the placement
            // happened, which is what ReconcilePredictedCells assumes when it marks those
            // cells dirty afterwards.
            //
            // The actor anchor is the security property. `message.Position` is client-supplied,
            // so anchoring there means a client can name any cell it likes as the origin and then
            // ask the server to dirty-mark cells anywhere in the world - bounded by the count cap
            // (64) and by nothing else, because the distance between the origin and the far cells
            // is whatever the client chose. The reach gate above now bounds that cell to the
            // actor's reach, but a radius is not an origin: a client naming the cell 6 blocks away
            // is still naming an origin of its own choosing, and this leash's job is to keep the
            // dirty set inside the actor's own leash no matter what the message claims.
            // `player.Entity.Pos` is the server's own record of where the actor is and is the only
            // origin here the client does not control.
            //
            // Intersecting both means the dirty set is inside the leash of the actor no
            // matter what `PlacedAt` claims, which is what keeps the 64-cell count cap
            // meaningful: without the actor anchor, the traffic cost is
            // `count x cell distance` and that product is unbounded.
            //
            // Ordering constraint: this runs *before* TryPlaceDownAt, so the server's own
            // `placedAt` does not exist yet and the second anchor can only be the
            // client-claimed one. A post-placement re-verify against the server's
            // `placedAt` is a worthwhile follow-up; this is not it.
            var actorPos = player.Entity.Pos.AsBlockPos;

            foreach (var cell in message.WrittenCells)
            {
                if (cell == null) return false;

                int fromActor = Math.Abs(cell.X - actorPos.X)
                    + Math.Abs(cell.Y - actorPos.Y)
                    + Math.Abs(cell.Z - actorPos.Z);
                if (fromActor > WrittenCellsMaxLeash) return false;

                int fromTarget = Math.Abs(cell.X - message.PlacedAt.X)
                    + Math.Abs(cell.Y - message.PlacedAt.Y)
                    + Math.Abs(cell.Z - message.PlacedAt.Z);
                if (fromTarget > WrittenCellsMaxLeash) return false;
            }

            return true;
        }

        internal void OnSwapSlotsMessage(IServerPlayer player, SwapSlotsMessage message)
        {
            if ((message.First != message.Second)
                && (message.First == CarrySlot.Back || message.Second == CarrySlot.Back)
                && player.Entity.CanInteract(requireEmptyHanded: true))
            {
                var handsOccupied = carryManager.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands);
                if (handsOccupied && !BackSlotEnabled)
                {
                    carryManager.TouchCarriedAttributes(player.Entity);
                    return;
                }

                if (carryManager.SwapCarried(player.Entity, message.First, message.Second))
                {
                    api?.World.PlaySoundAt(new AssetLocation(CarryCodes.SoundPaths.Throw), player.Entity);
                    carryManager.TouchCarriedAttributes(player.Entity);
                }
            }
        }

        internal void OnAttachMessage(IServerPlayer player, AttachMessage message)
        {
            string failureCode = FailureCodes.Ignore;
            if (TrySlotAction(player, message.TargetEntityId, message.SlotIndex, attach: true, ref failureCode))
            {
                return;
            }

            SendAttachDetachFailure(player, failureCode);
        }

        internal void OnDetachMessage(IServerPlayer player, DetachMessage message)
        {
            string failureCode = FailureCodes.Ignore;
            if (TrySlotAction(player, message.TargetEntityId, message.SlotIndex, attach: false, ref failureCode))
            {
                return;
            }

            SendAttachDetachFailure(player, failureCode);
        }

        private bool TrySlotAction(IServerPlayer player, long targetEntityId, int slotIndex, bool attach, ref string failureCode)
        {
            var a = RequireApi();

            var entity = a.World.GetEntityById(targetEntityId);
            if (entity == null)
            {
                failureCode = FailureCodes.EntityNotFound;
                return false;
            }

            var slotHandler = entity.GetBehavior<EntityBehaviorCarryable>()?.SlotHandler;
            if (slotHandler == null)
            {
                failureCode = FailureCodes.AttachableNotFound;
                return false;
            }

            if (!attach && HasOpenMountedBagInventory(a, player, entity, slotIndex))
            {
                failureCode = FailureCodes.SlotInventoryOpen;
                return false;
            }

            return attach
                ? slotHandler.TryAttach(entity, slotIndex, player, a, out failureCode)
                : slotHandler.TryDetach(entity, slotIndex, player, a, out failureCode);
        }

        private static bool HasOpenMountedBagInventory(ICoreServerAPI api, IServerPlayer player, Entity entity, int slotIndex)
        {
            var inventoryName = $"{MountedBagInventoryPrefix}-{slotIndex}-{entity.EntityId}";
            return CarryInventoryGuard.HasOpenInventory(api, player, id => id.StartsWith(inventoryName));
        }

        internal void OnPutMessage(IServerPlayer player, PutMessage message)
        {
            var a = RequireApi();

            if (message == null)
            {
                a.Logger.Error("OnPutMessage: Received null message");
                return;
            }

            if (transferLogic == null)
            {
                a.Logger.Error("OnPutMessage: TransferLogic is not initialized");
                return;
            }

            if (!transferLogic.TryPutCarryable(player, message, out string failureCode))
            {
                SendTransferFailure(player, failureCode);
            }
        }

        internal void OnTakeMessage(IServerPlayer player, TakeMessage message)
        {
            var a = RequireApi();

            if (transferLogic == null)
            {
                a.Logger.Error("OnTakeMessage: TransferLogic is not initialized");
                return;
            }

            if (!transferLogic.TryTakeCarryable(player, message, out string failureCode))
            {
                SendTransferFailure(player, failureCode);
            }
        }

        internal void OnDismountMessage(IServerPlayer player, DismountMessage message)
        {
            player.Entity.TryUnmount();

            player.Entity.World.GetEntityById(message.EntityId)?
                .GetBehavior<EntityBehaviorCreatureCarrier>()?
                .Seats?.FirstOrDefault(s => s.SeatId == message.SeatId)?
                .Controls?.StopAllMovement();
        }

        internal void OnPickupEntityMessage(IServerPlayer player, PickupEntityMessage message)
        {
            var entity = player.Entity.World.GetEntityById(message.EntityId);
            if (entity == null) return;

            var behavior = entity.GetBehavior<EntityBehaviorCarryable>();
            if (behavior == null || !behavior.IsCarryInteractable) return;

            if (!player.Entity.CanInteract(requireEmptyHanded: true))
            {
                CarryErrorHelper.SendError(player, FailureCodes.CannotInteract);
                return;
            }

            // Eye anchor and creative exemption both come from CarryReach; the code stays
            // EntityOutOfReach because the target here is an entity, not a block cell.
            if (entity.Pos == null
                || !CarryReach.IsWithinReach(player.Entity, entity.Pos.XYZ, GetMaxInteractionDistance()))
            {
                CarryErrorHelper.SendError(player, FailureCodes.EntityOutOfReach);
                return;
            }

            if (behavior.Handler == null)
            {
                CarryErrorHelper.SendError(player, FailureCodes.EntityHandlerNotConfigured);
                return;
            }
            TryPickupFromEntityGeneric(player, entity, behavior.Handler);
        }

        // ------------------------------
        //  Both side event handlers
        // ------------------------------

        internal EnumHandling OnBeforeActiveSlotChanged(EntityAgent entity)
        {
            return CarrySlotGuard.GuardHandsCarried(carryManager, entity);
        }

        // ------------------------------
        //  Server side event handlers
        // ------------------------------

        internal void OnServerEntitySpawn(Entity entity)
        {
            if (entity is EntityPlayer) return;

            foreach (var carried in carryManager.GetAllCarried(entity))
                carryManager.SetCarried(entity, carried, carried.Slot);
        }

        internal void OnServerPlayerNowPlaying(IServerPlayer player)
        {
            foreach (var carried in carryManager.GetAllCarried(player.Entity))
                carryManager.SetCarried(player.Entity, carried, carried.Slot);
        }

        // ------------------------------
        //  Helpers
        // ------------------------------

        private void FailCarryAction(IServerPlayer player, BlockPos? pos, string failureCode, string defaultCode)
        {
            _ = RequireApi();

            if (pos != null) player.Entity.World.BlockAccessor.MarkBlockDirty(pos);
            carryManager.TouchCarriedAttributes(player.Entity);
            player.Entity.WatchedAttributes.MarkPathDirty($"stats/{Stats.WalkSpeed}");
            carryManager.LockHotbarSlots(player);

            if (failureCode == FailureCodes.AlreadyReported)
            {
                // The permission gate already told the player - vanilla's claim message,
                // triggered by the gate's single re-invoke on the denied cell. Sending this
                // action's generic message as well would be a second toast for one refusal.
                // Deliberately checked after the side effects above, which still have to
                // run: the failed action still dirties the block, relocks the hotbar and
                // touches the carried attributes.
                return;
            }

            if (!string.IsNullOrEmpty(failureCode) && failureCode != FailureCodes.Ignore)
            {
                CarryErrorHelper.SendError(player, failureCode, $"{defaultCode}-{failureCode}");
            }
            else
            {
                CarryErrorHelper.SendError(player, defaultCode, defaultCode);
            }
        }

        private int GetMaxInteractionDistance()
            => configProvider.Config.CarryOptions?.MaxInteractionDistance ?? Defaults.MaxInteractionDistance;

        private static void SendAttachDetachFailure(IServerPlayer player, string? failureCode)
        {
            if (failureCode == null || failureCode == FailureCodes.Ignore)
            {
                return;
            }

            CarryErrorHelper.SendError(player, failureCode);
        }

        private static void SendTransferFailure(IServerPlayer player, string? failureCode)
        {
            if (string.IsNullOrEmpty(failureCode)
                || failureCode == FailureCodes.Ignore
                || failureCode == FailureCodes.Stop
                || failureCode == FailureCodes.Default
                || failureCode == FailureCodes.Internal)
            {
                return;
            }

            CarryErrorHelper.SendError(player, failureCode);
        }

        private bool TryPickupFromEntityGeneric(IServerPlayer player, Entity entity, ICarryableEntityPickupHandler handler)
        {
            var entityApi = entity.Api;
            if (entityApi == null) return false;

            if (carryManager.IsCarrySlotOccupied(player.Entity, CarrySlot.Hands))
            {
                CarryErrorHelper.SendError(player, FailureCodes.AlreadyCarrying, "pick-up-already-carrying");
                return false;
            }

            if (!handler.CanPickup(player.WorldData.CurrentGameMode, player.PlayerUID, entity, entityApi))
            {
                CarryErrorHelper.SendError(player, FailureCodes.NotOwner, "pickup-not-owner");
                return false;
            }

            var carriedBlock = handler.SerializeToCarriedBlock(entity, CarrySlot.Hands, entityApi);
            if (carriedBlock == null)
            {
                // A null result is only acceptable for a genuinely data-less tree. A present-but-
                // unreadable tree must never be treated as "nothing there": the entity stays alive
                // and the player is told, otherwise a failed read would silently delete the carried
                // block from the world.
                if (entity is EntityCarriedBlock ecb && ecb.CarriedBlockTree != null)
                {
                    // Creative players may adopt the untouched raw tree into a carry slot so the
                    // content stays recoverable once the source mod is re-enabled.
                    if (player.WorldData.CurrentGameMode == EnumGameMode.Creative
                        && carryManager.SetCarriedRaw(player.Entity, ecb.CarriedBlockTree, CarrySlot.Hands))
                    {
                        handler.OnPickedUp(entity, player, null, entityApi);
                        return true;
                    }

                    entityApi.World.Logger.Warning(
                        $"carryon: carried block entity {entity.EntityId} has unreadable carried data; protecting it from pickup and removal.");
                    CarryErrorHelper.SendError(player, FailureCodes.CarriedBlockUnreadable, "carried-block-unreadable");
                }

                return false;
            }

            carryManager.SetCarried(player.Entity, carriedBlock, CarrySlot.Hands);
            handler.OnPickedUp(entity, player, carriedBlock, entityApi);
            return true;
        }
    }
}
