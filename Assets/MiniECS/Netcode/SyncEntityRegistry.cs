using System.Collections.Generic;

namespace MiniECS.Netcode
{
    public static class SyncEntityRegistry
    {
        static readonly Dictionary<int, Entity> _serverToLocal = new();
        static readonly HashSet<int> _managedLocalIds = new();

        public static IEnumerable<KeyValuePair<int, Entity>> All => _serverToLocal;

        public static Entity GetLocal(int serverId)
        {
            return _serverToLocal.TryGetValue(serverId, out var e) ? e : default;
        }

        public static void SetMapping(int serverId, Entity localEntity)
        {
            _serverToLocal[serverId] = localEntity;
            _managedLocalIds.Add(localEntity.Id);
        }

        public static bool Remove(int serverId)
        {
            if (_serverToLocal.TryGetValue(serverId, out var local))
            {
                _serverToLocal.Remove(serverId);
                _managedLocalIds.Remove(local.Id);
                return true;
            }
            return false;
        }

        public static void Clear()
        {
            _serverToLocal.Clear();
            _managedLocalIds.Clear();
        }
    }
}
