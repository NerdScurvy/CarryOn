using System;
using System.Reflection;
using CarryOn.Common.Network;
using Vintagestory.API.Server;

namespace CarryOn.Scenarios
{
    /// <summary>
    /// Invokes the internal server-side mod-channel handlers with the public message
    /// records, mirroring what the network layer does when a real client sends the
    /// corresponding message on the carryon channel. Test-side reflection only: zero
    /// production source changes. Resolution fails fast with the drifted symbol named,
    /// so a production rename breaks loudly instead of silently.
    /// </summary>
    public static class HandlerInvoke
    {
        private const BindingFlags InstancePrivate = BindingFlags.Instance | BindingFlags.NonPublic;

        private static FieldInfo? serverHandlerField;
        private static bool resolved;
        private static MethodInfo? pickUpMethod;
        private static MethodInfo? placeDownMethod;
        private static MethodInfo? interactMethod;
        private static MethodInfo? attachMethod;
        private static MethodInfo? detachMethod;
        private static MethodInfo? putMethod;
        private static MethodInfo? takeMethod;
        private static MethodInfo? pickupEntityMethod;

        /// <summary>
        /// Drives CarryServerHandler.OnPickUpMessage - the path a real client's
        /// PickUpMessage takes on the server.
        /// </summary>
        public static void PickUp(CarrySystem mod, IServerPlayer player, PickUpMessage message)
        {
            ResolveMethods(mod);
            pickUpMethod!.Invoke(ServerHandler(mod), new object[] { player, message });
        }

        /// <summary>
        /// Drives CarryServerHandler.OnPlaceDownMessage - the path a real client's
        /// PlaceDownMessage (including its WrittenCells claim) takes on the server.
        /// </summary>
        public static void PlaceDown(CarrySystem mod, IServerPlayer player, PlaceDownMessage message)
        {
            ResolveMethods(mod);
            placeDownMethod!.Invoke(ServerHandler(mod), new object[] { player, message });
        }

        /// <summary>
        /// Drives CarryServerHandler.OnInteractMessage - the path a real client's
        /// InteractMessage takes on the server (interact-while-carrying with a
        /// carryableinteract block, plus the multiblock OnBlockInteractStop commit).
        /// </summary>
        public static void Interact(CarrySystem mod, IServerPlayer player, InteractMessage message)
        {
            ResolveMethods(mod);
            interactMethod!.Invoke(ServerHandler(mod), new object[] { player, message });
        }

        /// <summary>
        /// Drives CarryServerHandler.OnAttachMessage - the path a real client's
        /// AttachMessage takes on the server (attaching a carried block to an
        /// entity attachment slot).
        /// </summary>
        public static void Attach(CarrySystem mod, IServerPlayer player, AttachMessage message)
        {
            ResolveMethods(mod);
            attachMethod!.Invoke(ServerHandler(mod), new object[] { player, message });
        }

        /// <summary>
        /// Drives CarryServerHandler.OnDetachMessage - the path a real client's
        /// DetachMessage takes on the server (detaching a carried block from an
        /// entity attachment slot through CarryOn's own detach).
        /// </summary>
        public static void Detach(CarrySystem mod, IServerPlayer player, DetachMessage message)
        {
            ResolveMethods(mod);
            detachMethod!.Invoke(ServerHandler(mod), new object[] { player, message });
        }

        /// <summary>
        /// Drives CarryServerHandler.OnPutMessage - the path a real client's
        /// PutMessage takes on the server (putting a carried item into a block
        /// entity slot via carry transfer).
        /// </summary>
        public static void Put(CarrySystem mod, IServerPlayer player, PutMessage message)
        {
            ResolveMethods(mod);
            putMethod!.Invoke(ServerHandler(mod), new object[] { player, message });
        }

        /// <summary>
        /// Drives CarryServerHandler.OnTakeMessage - the path a real client's
        /// TakeMessage takes on the server (taking a carried block from a block
        /// entity slot via carry transfer).
        /// </summary>
        public static void Take(CarrySystem mod, IServerPlayer player, TakeMessage message)
        {
            ResolveMethods(mod);
            takeMethod!.Invoke(ServerHandler(mod), new object[] { player, message });
        }

        /// <summary>
        /// Drives CarryServerHandler.OnPickupEntityMessage - the path a real client's
        /// PickupEntityMessage takes on the server (picking up a dropped carried
        /// block entity through its registered entity-pickup handler).
        /// </summary>
        public static void PickupEntity(CarrySystem mod, IServerPlayer player, PickupEntityMessage message)
        {
            ResolveMethods(mod);
            pickupEntityMethod!.Invoke(ServerHandler(mod), new object[] { player, message });
        }

        private static object ServerHandler(CarrySystem mod)
        {
            var carryHandler = mod.CarryHandler
                ?? throw new InvalidOperationException(
                    "CarrySystem.CarryHandler is null - CarryOnLib not staged or not initialized.");

            if (serverHandlerField!.GetValue(carryHandler) is not { } handler)
            {
                throw new InvalidOperationException(
                    "CarryHandler.serverHandler is null - InitServer has not run (channel setup incomplete).");
            }

            return handler;
        }

        private static void ResolveMethods(CarrySystem mod)
        {
            if (resolved) return;

            var carryHandler = mod.CarryHandler
                ?? throw new InvalidOperationException(
                    "CarrySystem.CarryHandler is null - CarryOnLib not staged or not initialized.");

            // Resolve from the runtime type of the actual handler object so lookups always
            // hit the assembly the game loaded, not whichever copy the test compiled against.
            var carryHandlerType = carryHandler.GetType();
            serverHandlerField = carryHandlerType.GetField("serverHandler", InstancePrivate)
                ?? throw new InvalidOperationException(
                    "CarryHandler.serverHandler field not found - production internals changed; " +
                    "update HandlerInvoke (expected private field 'serverHandler' on CarryHandler).");

            var serverHandler = serverHandlerField.GetValue(carryHandler)
                ?? throw new InvalidOperationException(
                    "CarryHandler.serverHandler is null - InitServer has not run.");

            var serverHandlerType = serverHandler.GetType();
            pickUpMethod = RequireMethod<PickUpMessage>(serverHandlerType, "OnPickUpMessage");
            placeDownMethod = RequireMethod<PlaceDownMessage>(serverHandlerType, "OnPlaceDownMessage");
            interactMethod = RequireMethod<InteractMessage>(serverHandlerType, "OnInteractMessage");
            attachMethod = RequireMethod<AttachMessage>(serverHandlerType, "OnAttachMessage");
            detachMethod = RequireMethod<DetachMessage>(serverHandlerType, "OnDetachMessage");
            putMethod = RequireMethod<PutMessage>(serverHandlerType, "OnPutMessage");
            takeMethod = RequireMethod<TakeMessage>(serverHandlerType, "OnTakeMessage");
            pickupEntityMethod = RequireMethod<PickupEntityMessage>(serverHandlerType, "OnPickupEntityMessage");
            resolved = true;
        }

        private static MethodInfo RequireMethod<TMessage>(Type handlerType, string name)
            => handlerType.GetMethod(name, InstancePrivate)
                ?? throw new InvalidOperationException(
                    $"CarryServerHandler.{name} not found - production internals changed; " +
                    $"update HandlerInvoke (expected internal method {name}(IServerPlayer, {typeof(TMessage).Name})).");
    }
}
