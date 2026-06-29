using System;
using System.Collections.Generic;
using CarryOn.API.Common.Models;
using CarryOn.Common.Logic;
using CarryOn.Utility;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using static CarryOn.API.Common.Models.CarryCode;

namespace CarryOn.Common.Entities
{
    public class EntityCarriedBlock : Entity
    {
        internal static CarriedBlockEntityConfig? Config { private get; set; }

        private bool showParticles = true;
        private float despawnAfterDays = 14f;
        private PickupAccess pickupAccess = PickupAccess.OwnerFirst;
        private float gracePeriodSeconds = 300f;

        public override void Initialize(EntityProperties properties, ICoreAPI api, long InChunkIndex3D)
        {
            base.Initialize(properties, api, InChunkIndex3D);

            var cfg = Config;
            if (cfg == null) return;

            this.pickupAccess = cfg.PickupAccess;
            this.gracePeriodSeconds = cfg.GracePeriodSeconds;

            if (Properties != null)
                Properties.SelectionBoxSize = new Vec2f(cfg.Scale, cfg.Scale);

            if (api.Side == EnumAppSide.Client)
            {
                this.showParticles = cfg.ShowParticles;

                if (Properties?.Client != null)
                    Properties.Client.Size = cfg.Scale;
            }

            if (api.Side == EnumAppSide.Server)
                this.despawnAfterDays = cfg.DespawnAfterDays;
        }

        public override void OnGameTick(float dt)
        {
            base.OnGameTick(dt);

            if (Api is ICoreClientAPI capi)
            {
                if (this.showParticles && capi.World.Rand.NextDouble() < 0.3)
                {
                    capi.World.SpawnParticles(new SimpleParticleProperties
                    {
                        MinPos = Pos.XYZ,
                        Color = ColorUtil.ToRgba(255, 200, 150, 50),
                        MinSize = 0.1f,
                        MaxSize = 0.15f,
                        MinVelocity = new Vec3f(-0.05f, 0.1f, -0.05f),
                        AddVelocity = new Vec3f(0.1f, 0.3f, 0.1f),
                        MinQuantity = 1,
                        LifeLength = 1.5f,
                        WithTerrainCollision = false,
                        LightEmission = ColorUtil.ToRgba(255, 200, 150, 50)
                    });
                }
                return;
            }

            if (this.despawnAfterDays > 0 && this.Api.World.Calendar.TotalDays - this.DropTimeDays >= this.despawnAfterDays)
                this.Die(EnumDespawnReason.Expire, null);
        }

        public override string GetName()
        {
            var blockName = GetBlockName();
            return LocalizationHelper.GetLang("dropped-block", blockName ?? "?");
        }

        public override string GetInfoText()
        {
            var lines = new List<string>();

            var ownerUid = this.OwnerUid;
            if (ownerUid != null)
            {
                var ownerName = Api?.World?.PlayerByUid(ownerUid)?.PlayerName;
                if (ownerName == null && Api is ICoreClientAPI capi
                    && capi.World.Player?.WorldData?.CurrentGameMode != EnumGameMode.Creative)
                {
                    ownerName = LocalizationHelper.GetLang("offline-player");
                }
                lines.Add(LocalizationHelper.GetLang("dropped-by", ownerName ?? ownerUid));
            }

            var elapsedSeconds = TimeSpan.FromTicks(DateTime.UtcNow.Ticks - this.DropTimeRealTicks).TotalSeconds;
            lines.Add(LocalizationHelper.GetLang("dropped-ago", FormatDuration(elapsedSeconds)));

            if (this.pickupAccess == PickupAccess.OwnerFirst && this.gracePeriodSeconds > 0)
            {
                var remaining = Math.Max(0, this.gracePeriodSeconds - (float)elapsedSeconds);
                if (remaining > 0)
                    lines.Add(LocalizationHelper.GetLang("grace-period-left", FormatDuration(remaining)));
            }

            if (this.despawnAfterDays > 0 && Api?.World?.Calendar != null)
            {
                var remainingDays = this.despawnAfterDays - (Api.World.Calendar.TotalDays - this.DropTimeDays);
                if (remainingDays > 0)
                    lines.Add(LocalizationHelper.GetLang("despawn-in", Math.Max(0.1, remainingDays).ToString("F1")));
            }

            return string.Join("\n", lines);
        }

        private string? GetBlockName()
        {
            var tree = this.CarriedBlockTree;
            if (tree == null) return null;
            var stack = tree.GetItemstack(AttributeKey.CarriedBlock.Stack);
            if (stack == null) return null;
            if (stack.Block == null)
                stack.ResolveBlockOrItem(Api?.World);
            return stack.GetName();
        }

        private static string FormatDuration(double totalSeconds)
        {
            if (totalSeconds < 60)
                return LocalizationHelper.GetLang("duration-seconds", (int)totalSeconds);
            if (totalSeconds < 3600)
                return LocalizationHelper.GetLang("duration-minutes", (int)(totalSeconds / 60));
            if (totalSeconds < 86400)
                return LocalizationHelper.GetLang("duration-hours", (int)(totalSeconds / 3600));
            return LocalizationHelper.GetLang("duration-days", (int)(totalSeconds / 86400));
        }

        private static ItemStack[]? handsfreeStacks;
        private static ItemStack[]? nohandsfreeStacks;

        public override WorldInteraction[] GetInteractionHelp(IClientWorldAccessor world, EntitySelection es, IClientPlayer player)
        {
            var canPickup = CarriedBlockAccessPolicy.CanPickup(
                player.WorldData.CurrentGameMode,
                player.PlayerUID,
                this.OwnerUid,
                this.pickupAccess,
                this.gracePeriodSeconds,
                this.DropTimeRealTicks);

            if (!canPickup) return [];

            handsfreeStacks ??= [new ItemStack(world.GetItem(new AssetLocation("carryon:icon-handsfree")))];
            nohandsfreeStacks ??= [new ItemStack(world.GetItem(new AssetLocation("carryon:icon-nohandsfree")))];

            bool canDoCarryAction = player.Entity?.CanDoCarryAction(requireEmptyHanded: true) == true;
            var itemstacks = canDoCarryAction ? handsfreeStacks : nohandsfreeStacks;

            return [new WorldInteraction
            {
                ActionLangCode = CarryOnCode("entityhelp-pickup-carriedblock"),
                MouseButton = EnumMouseButton.Right,
                RequireFreeHand = true,
                Itemstacks = itemstacks
            }];
        }

        /// <summary>
        /// Pickup is handled through CarryInteractionStateMachine with a progress circle.
        /// The default OnInteract is intentionally empty to prevent instant pickup.
        /// </summary>
        public override void OnInteract(EntityAgent byEntity, ItemSlot slot,
            Vec3d hitPosition, EnumInteractMode mode)
        {
        }

        // -- Data stored in WatchedAttributes for automatic server->client sync --

        public string? OwnerUid
            => this.WatchedAttributes.GetString(AttributeKey.CarriedBlock.OwnerUid, null);

        public double DropTimeDays
            => this.WatchedAttributes.GetDouble(AttributeKey.CarriedBlock.DropTime, 0);

        public long DropTimeRealTicks
            => this.WatchedAttributes.GetLong(AttributeKey.CarriedBlock.DropTimeRealTicks, 0);

        public ITreeAttribute? CarriedBlockTree
            => this.WatchedAttributes[AttributeKey.CarriedBlock.WatchedTree] as ITreeAttribute;

        public void SetCarriedBlockData(ITreeAttribute carriedTree, string ownerUid, double dropTimeDays, long dropTimeRealTicks)
        {
            this.WatchedAttributes[AttributeKey.CarriedBlock.WatchedTree] = carriedTree;
            this.WatchedAttributes.SetString(AttributeKey.CarriedBlock.OwnerUid, ownerUid);
            this.WatchedAttributes.SetDouble(AttributeKey.CarriedBlock.DropTime, dropTimeDays);
            this.WatchedAttributes.SetLong(AttributeKey.CarriedBlock.DropTimeRealTicks, dropTimeRealTicks);
            this.WatchedAttributes.MarkAllDirty();
        }
    }
}
