using System;
using System.Collections.Generic;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CarryOn.Server
{
    public class DroppedBlockInfo
    {
        public DateTime DroppedDateTime { get; set; }
        public string OwnerUID { get; set; }
        public string OwnerName { get; set; }
        public string BlockCode { get; set; }
        public string Teleport { get; set; }

        public BlockPos Position { get; set; }

        public List<string> Inventory { get; set; }

        private static string GetFileLocation(BlockPos pos, ICoreAPI api)
        {
            var localPath = Path.Combine("ModData", api.World.SavegameIdentifier, CarrySystem.ModId);
            var path = api.GetOrCreateDataPath(localPath);
            return Path.Combine(path, $"dropped-{pos.X}.{pos.Y}.{pos.Z}");
        }
        public static DroppedBlockInfo Get(BlockPos pos, IPlayer player)
        {
            ICoreAPI api = player.Entity.Api;
            var fileLocation = GetFileLocation(pos, api);

            if (File.Exists(fileLocation))
            {
                try
                {
                    var content = File.ReadAllText(fileLocation);
                    return JsonUtil.FromString<DroppedBlockInfo>(content);
                }
                catch (Exception e)
                {
                    api.World.Logger.Error($"Failed loading file '{fileLocation}' with error '{e}'!");
                }
            }
            return null;
        }

        public static void Create(BlockPos pos, IPlayer player, ITreeAttribute blockEntityData)
        {
            ICoreAPI api = player.Entity.Api;
            var fileLocation = GetFileLocation(pos, api);

            var blockAccessor = api.World.BlockAccessor;

            var block = blockAccessor.GetBlock(pos);
            var slotItems = new List<string>();
            if (blockEntityData["inventory"] is TreeAttribute inventory)
            {
                foreach (var slot in inventory.GetTreeAttribute("slots"))
                {
                    if(slot.Value is ItemstackAttribute itemstack){
                        slotItems.Add(itemstack.value.ToString());
                    }
                }
            }

            var droppedBlockInfo = new DroppedBlockInfo()
            {
                DroppedDateTime = DateTime.Now,
                OwnerUID = player.PlayerUID,
                OwnerName = player.PlayerName,
                BlockCode = block.Code.ToString(),
                Position = pos,
                Inventory = slotItems,
                Teleport = $"/tp ={pos.X} ={pos.Y} ={pos.Z}"
            };

            try
            {
                var content = JsonUtil.ToString(droppedBlockInfo);
                File.WriteAllText(fileLocation, content);
                api.World.Logger.Debug($"Created file '{fileLocation}'");
            }
            catch (Exception e)
            {
                api.World.Logger.Error($"Failed saving file '{fileLocation}' with error '{e}'!");
            }
        }

        public static void Remove(BlockPos pos, IPlayer player)
        {
            Remove(pos, player.Entity.Api);
        }

        public static void Remove(BlockPos pos, ICoreAPI api)
        {
            var fileLocation = GetFileLocation(pos, api);
            if (File.Exists(fileLocation))
            {
                try
                {
                    File.Delete(fileLocation);
                    api.World.Logger.Debug($"Removed file '{fileLocation}'");
                }
                catch (Exception e)
                {
                    api.World.Logger.Error($"Failed to delete file '{fileLocation}' with error '{e}'!");
                }
            }
        }
    }
}