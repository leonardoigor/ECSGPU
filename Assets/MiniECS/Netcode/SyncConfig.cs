using Unity.Netcode;

namespace MiniECS.Netcode
{
    public static class SyncConfig
    {
        public const string MessageName = "MiniEcsSync";

        public static float SyncInterval = 0.1f;

        public static NetworkDelivery Delivery = NetworkDelivery.ReliableFragmentedSequenced;
    }
}
