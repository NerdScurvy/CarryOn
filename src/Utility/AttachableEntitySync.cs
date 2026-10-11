using Vintagestory.API.Common.Entities;

namespace CarryOn.Utility
{
    /// <summary>
    /// Sync helpers for entities with attachable inventories. After any mutation of the attachment
    /// state the entity must be flagged as shape-modified and its chunk marked modified so the
    /// updated state is re-synced to clients. The pair is valid on both the client and the server
    /// (server-side re-sync is what keeps the renderer on other players correct).
    /// </summary>
    public static class AttachableEntitySync
    {
        /// <summary>
        /// Marks the entity as shape-modified and its chunk as modified.
        /// </summary>
        public static void MarkDirty(Entity entity)
        {
            entity.MarkShapeModified();
            entity.World.BlockAccessor.GetChunkAtBlockPos(entity.Pos.AsBlockPos)?.MarkModified();
        }
    }
}
