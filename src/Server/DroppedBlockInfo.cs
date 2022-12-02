using System;
using System.IO;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace CarryOn.Server
{
    public class DroppedBlockInfo
    {
        public DateTime DroppedDateTime { get; set; }
        public string OwnerUID { get; set; }

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

        public static void Create(BlockPos pos, IPlayer player)
        {
            ICoreAPI api = player.Entity.Api;
            var fileLocation = GetFileLocation(pos, api);

            var droppedBlockInfo = new DroppedBlockInfo()
            {
                DroppedDateTime = DateTime.Now,
                OwnerUID = player.PlayerUID
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