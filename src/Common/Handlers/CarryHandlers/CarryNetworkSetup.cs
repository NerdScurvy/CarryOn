using System;
using CarryOn.Common.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Server;

namespace CarryOn.Common.Handlers.CarryHandlers
{
    internal static class CarryNetworkSetup
    {
        internal static IClientNetworkChannel RegisterClient(IClientNetworkChannel channel)
        {
            ArgumentNullException.ThrowIfNull(channel);

            return channel
                .RegisterMessageType<InteractMessage>()
                .RegisterMessageType<LockSlotsMessage>()
                .RegisterMessageType<PickUpMessage>()
                .RegisterMessageType<PlaceDownMessage>()
                .RegisterMessageType<SwapSlotsMessage>()
                .RegisterMessageType<AttachMessage>()
                .RegisterMessageType<DetachMessage>()
                .RegisterMessageType<PutMessage>()
                .RegisterMessageType<TakeMessage>()
                .RegisterMessageType<DismountMessage>()
                .RegisterMessageType<ConfigSyncMessage>()
                .RegisterMessageType<PickupEntityMessage>();
        }

        internal static IServerNetworkChannel RegisterServer(IServerNetworkChannel channel)
        {
            ArgumentNullException.ThrowIfNull(channel);

            return channel
                .RegisterMessageType<InteractMessage>()
                .RegisterMessageType<LockSlotsMessage>()
                .RegisterMessageType<PickUpMessage>()
                .RegisterMessageType<PlaceDownMessage>()
                .RegisterMessageType<SwapSlotsMessage>()
                .RegisterMessageType<AttachMessage>()
                .RegisterMessageType<DetachMessage>()
                .RegisterMessageType<PutMessage>()
                .RegisterMessageType<TakeMessage>()
                .RegisterMessageType<DismountMessage>()
                .RegisterMessageType<ConfigSyncMessage>()
                .RegisterMessageType<PickupEntityMessage>();
        }
    }
}
