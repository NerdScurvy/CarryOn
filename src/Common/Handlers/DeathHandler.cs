using System;
using System.Linq;
using CarryOn.API.Common.Interfaces;
using CarryOn.API.Common.Models;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace CarryOn.Common.Handlers
{
    /// <summary>
    /// Handles player deaths and drops carried blocks if "deathPunishment" isn't set to "keep".
    /// </summary>
    public class DeathHandler : IDisposable
    {
        private readonly ICoreServerAPI api;
        private readonly ICarryManager carryManager;

        public DeathHandler(ICoreServerAPI api, ICarryManager carryManager)
        {
            this.api = api ?? throw new ArgumentNullException(nameof(api));
            this.carryManager = carryManager ?? throw new ArgumentNullException(nameof(carryManager));
            api.Event.PlayerDeath += OnPlayerDeath;
        }

        public void Dispose()
        {
            api.Event.PlayerDeath -= OnPlayerDeath;
        }
        
        /// <summary>
        /// <para>
        ///   Only drop carried blocks if "deathPunishment" isn't set to "keep".
        ///   This is how the vanilla game checks whether it should drop inventory contents.
        /// </para>
        /// <para>
        ///   NOTE: Taking damage will still drop blocks not carried on back.
        ///   See <see cref="Server.EntityBehaviorDropCarriedOnDamage"/> for that.
        /// </para>
        /// </summary>
        /// <param name="player"> The player that died. </param>
        /// <param name="source"> The source of the damage that caused the death. </param
        private void OnPlayerDeath(IPlayer player, DamageSource source)
        {
            if (player.Entity.Properties.Server?.Attributes?.GetBool("keepContents", false) != true)
                carryManager.DropCarried(player.Entity, Enum.GetValues(typeof(CarrySlot)).Cast<CarrySlot>());
        }
    }
}