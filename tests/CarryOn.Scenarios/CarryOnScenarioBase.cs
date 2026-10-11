using System;
using System.Threading.Tasks;
using Atlas.Api;
using Atlas.XUnit;
using CarryOn;
using CarryOn.API.Common.Interfaces;
using CarryOn.Common.Models;
using Vintagestory.API.Common;
using Xunit;

namespace CarryOn.Scenarios
{
    /// <summary>Shared setup for CarryOn Atlas scenarios.</summary>
    public abstract class CarryOnScenarioBase : AtlasScenarioBase
    {
        /// <summary>Resolves the server-side CarryOn mod system.</summary>
        protected CarrySystem Mod => World.Api.ModLoader.GetModSystem<CarrySystem>()!;

        /// <summary>The public carry manager seam the scenarios drive.</summary>
        protected ICarryManager Carry => Mod.CarryManager!;

        /// <summary>The mod network channel name (the carryon mod id).</summary>
        protected string Channel => CarryCodes.ModId;

        /// <summary>Driver wrapping carry actions for a joined test player.</summary>
        protected CarryActions Actions => new CarryActions(World.Api.World, Carry);

        /// <summary>
        /// Joins a test player in the suite's default game mode: SURVIVAL.
        /// Atlas joins players in creative by default, and behavior diverges
        /// there - vanilla land claims do not restrict creative players,
        /// CarryOn lets creative bypass reinforcement and dropped-carried-
        /// block ownership, and lethal damage is nullified - so a row that
        /// only passes in creative proves nothing about the survival behavior
        /// players actually experience. Rows that deliberately exercise a
        /// creative bypass pass <see cref="EnumGameMode.Creative"/> explicitly.
        /// </summary>
        protected async Task<ITestPlayer> JoinPlayer(
            string name,
            EnumGameMode gameMode = EnumGameMode.Survival)
        {
            var player = await World.JoinPlayer(name);
            var joinedAs = player.Player.WorldData.CurrentGameMode;
            player.Player.WorldData.CurrentGameMode = gameMode;
            Assert.True(
                player.Player.WorldData.CurrentGameMode == gameMode,
                $"Game-mode forcing failed for '{name}' (joined as {joinedAs}, wanted {gameMode})");
            return player;
        }

        /// <summary>
        /// Puts an already-joined player into <paramref name="languageCode"/> for the rest
        /// of the scenario, so a row can assert that server-authored text reaches the
        /// player in the player's language rather than in the server's.
        /// <para>
        /// <c>IServerPlayer.LanguageCode</c> is deliberately read-only, but the concrete
        /// <c>ServerPlayer</c> behind it carries the settable property the join handshake
        /// populates - casting to it is the seam. Fails loudly if the engine stops handing
        /// out <c>ServerPlayer</c> instances, so the row can never quietly assert against
        /// the server's own locale.
        /// </para>
        /// </summary>
        protected static void UseLanguage(ITestPlayer player, string languageCode)
        {
            if (player.Player is not Vintagestory.Server.ServerPlayer serverPlayer)
            {
                throw new InvalidOperationException(
                    "Expected the joined player's backing type to be Vintagestory.Server.ServerPlayer, "
                    + $"got {player.Player.GetType().FullName}. The engine changed - update UseLanguage "
                    + "before trusting any row that calls it.");
            }

            serverPlayer.LanguageCode = languageCode;
            Assert.Equal(languageCode, player.Player.LanguageCode);
        }

        /// <summary>
        /// Seeds the "another player has it open" premise by pushing
        /// <paramref name="inventory"/> through the same server call vanilla's
        /// GUI-open makes, so <c>InventoryManager.OpenedInventories</c> contains
        /// exactly the entry the production guard scans.
        /// <para>
        /// Shared by both suites (this file is linked into
        /// <c>CarryOn.Scenarios.ExampleMods</c>) rather than duplicated per class, so
        /// the premise-seeding helper has one definition: a second copy could drift
        /// from the first and the guard's reject rows would stop meaning anything.
        /// </para>
        /// </summary>
        protected static void OpenFor(ITestPlayer player, IInventory inventory)
        {
            player.Player.InventoryManager.OpenInventory(inventory);
        }
    }
}