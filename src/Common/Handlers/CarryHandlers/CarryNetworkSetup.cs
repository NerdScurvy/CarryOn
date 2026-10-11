using System;
using CarryOn.Common.Network;
using Vintagestory.API.Client;
using Vintagestory.API.Server;

namespace CarryOn.Common.Handlers.CarryHandlers
{
    internal static class CarryNetworkSetup
    {
        private static readonly Type[] CarryMessageTypes =
        [
            typeof(InteractMessage),
            typeof(LockSlotsMessage),
            typeof(PickUpMessage),
            typeof(PickUpGroundStorageBagMessage),
            typeof(BagToBackpackMessage),
            typeof(PlaceDownMessage),
            typeof(SwapSlotsMessage),
            typeof(AttachMessage),
            typeof(DetachMessage),
            typeof(PutMessage),
            typeof(TakeMessage),
            typeof(DismountMessage),
            typeof(ConfigSyncMessage),
            typeof(CarryabilityStateMessage),
            typeof(PickupEntityMessage)
        ];

        internal static IClientNetworkChannel RegisterClient(IClientNetworkChannel channel)
        {
            ArgumentNullException.ThrowIfNull(channel);

            foreach (var type in CarryMessageTypes)
                channel.RegisterMessageType(type);

            return channel;
        }

        internal static IServerNetworkChannel RegisterServer(IServerNetworkChannel channel)
        {
            ArgumentNullException.ThrowIfNull(channel);

            foreach (var type in CarryMessageTypes)
                channel.RegisterMessageType(type);

            return channel;
        }
    }
}
