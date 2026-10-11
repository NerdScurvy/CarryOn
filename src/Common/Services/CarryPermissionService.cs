using System;
using System.Collections.Generic;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Interfaces;
using CarryOn.Common.Models;
using CarryOn.Utility;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Common.Services
{
    internal sealed class CarryPermissionService
    {
        private readonly ICarryManager carryManager;
        private readonly IConfigProvider configProvider;

        public CarryPermissionService(ICarryManager carryManager, IConfigProvider configProvider)
        {
            this.carryManager = carryManager;
            this.configProvider = configProvider;
        }

        /// <summary>
        /// Silent multi-cell permission scan over a block footprint: returns the first
        /// cell the entity lacks permission for, with every per-cell gate (events,
        /// reinforcement, creative mode, claims) applied without error messages. The
        /// caller emits the standard claim message exactly once by re-invoking
        /// <see cref="HasPermissionAt(Entity, BlockPos, bool)"/> with
        /// <c>showErrorMessage: true</c> on the returned cell, so multi-cell denials never
        /// spam one message per cell.
        /// </summary>
        public bool TryGetFirstDeniedCell(Entity entity, IEnumerable<BlockPos> footprint, out BlockPos? deniedCell)
            => TryGetFirstDeniedCell(entity, footprint, out deniedCell, out _);

        /// <inheritdoc cref="TryGetFirstDeniedCell(Entity, IEnumerable{BlockPos}, out BlockPos?)"/>
        /// <param name="reason">
        /// Why the returned cell was refused, so the caller can report the actual reason
        /// instead of a generic one. <see cref="PermissionDenialReason.None"/> when nothing
        /// was denied.
        /// </param>
        public bool TryGetFirstDeniedCell(Entity entity, IEnumerable<BlockPos> footprint, out BlockPos? deniedCell, out PermissionDenialReason reason)
        {
            ArgumentNullException.ThrowIfNull(entity);
            ArgumentNullException.ThrowIfNull(footprint);

            foreach (var cell in footprint)
            {
                if (!HasPermissionAt(entity, cell, out var cellReason, showErrorMessage: false))
                {
                    deniedCell = cell;
                    reason = cellReason;
                    return true;
                }
            }

            deniedCell = null;
            reason = PermissionDenialReason.None;
            return false;
        }

        public bool HasPermissionAt(Entity entity, BlockPos pos, bool showErrorMessage = true)
            => HasPermissionAt(entity, pos, out _, showErrorMessage);

        /// <inheritdoc cref="HasPermissionAt(Entity, BlockPos, bool)"/>
        /// <param name="reason">
        /// Why the cell was refused, or <see cref="PermissionDenialReason.None"/> when it
        /// was permitted. Every exit below names its reason, including the ones that send
        /// no message - which is the point: reinforcement refuses before the claim check
        /// and returns without saying anything, which is why a reinforced cell used to
        /// reach the player as a bare "no permission".
        /// </param>
        public bool HasPermissionAt(Entity entity, BlockPos pos, out PermissionDenialReason reason, bool showErrorMessage = true)
        {
            var isReinforced = entity.Api.ModLoader.GetModSystem<ModSystemBlockReinforcement>()?.IsReinforced(pos) ?? false;
            if (entity is EntityPlayer playerEntity)
            {
                var result = entity.World.GetCarryEvents().TriggerCheckPermissionAt(playerEntity, pos, isReinforced);
                if (result.HasValue)
                {
                    reason = result.Value ? PermissionDenialReason.None : PermissionDenialReason.Event;
                    return result.Value;
                }

                var isCreative = playerEntity.Player.WorldData.CurrentGameMode == EnumGameMode.Creative;
                if (!isCreative && isReinforced)
                {
                    reason = PermissionDenialReason.Reinforced;
                    return false;
                }

                // Client-side best-effort: Claims.TestAccess/TryAccess always return Granted on client,
                // so iterate Claims.All for a zero-traffic early warning
                if (entity.Api.Side == EnumAppSide.Client)
                {
                    var allowed = HasPermissionAtClient(playerEntity.Player, pos);
                    reason = allowed ? PermissionDenialReason.None : PermissionDenialReason.Claim;
                    return allowed;
                }

                bool granted;
                if (showErrorMessage)
                    granted = entity.World.Claims.TryAccess(playerEntity.Player, pos, EnumBlockAccessFlags.BuildOrBreak);
                else
                    granted = entity.World.Claims.TestAccess(playerEntity.Player, pos, EnumBlockAccessFlags.BuildOrBreak) == EnumWorldAccessResponse.Granted;

                reason = granted ? PermissionDenialReason.None : PermissionDenialReason.Claim;
                return granted;
            }
            else
            {
                // Deliberate, accepted behaviour - see the summary for the full rationale.
                //
                // The claim gate is keyed on identity: TriggerCheckPermissionAt needs an
                // IPlayer, Claims.TryAccess/TestAccess need one, and a non-player entity
                // (a dropped item, a projectile, a mod-owned entity) has no claim identity
                // to ask about. So "deny unless permitted" cannot be evaluated here at all -
                // there is nothing to look up, and inventing a permissive default would let
                // every claim-protected block be rebuilt by any non-player actor.
                //
                // The concrete exposure: an attachable-creature drop (a tamed creature
                // detaching into a carried item and reattaching elsewhere) can land inside a
                // foreign claim and rebuild the block there, because the reinform branch
                // accepts whenever the cell is not reinforced. Claims do not stop it.
                //
                // Accepted as-is: tightening this needs a non-player
                // identity to evaluate claims against, which the engine's claim API does not
                // provide. The honest fix is a reach check on the placement origin, which is
                // tracked as a known limitation - see docs/known-issues.md.
                reason = isReinforced ? PermissionDenialReason.Reinforced : PermissionDenialReason.None;
                return !isReinforced;
            }
        }

        private bool HasPermissionAtClient(IPlayer player, BlockPos pos)
        {
            if (configProvider?.Config?.CarryOptions?.ClientSidePermissionCheck != true)
                return true;

            var claims = player.Entity?.World?.Claims?.All;
            if (claims == null)
                return true;

            foreach (var claim in claims)
            {
                if (claim.PositionInside(pos))
                {
                    if (claim.TestPlayerAccess(player, EnumBlockAccessFlags.BuildOrBreak) == EnumPlayerAccessResult.Denied)
                        return false;
                }
            }

            return true;
        }
    }
}
