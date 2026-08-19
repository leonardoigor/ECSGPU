using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.Netcode;
using UnityEngine;

namespace MiniECS.Netcode
{
    public class EcsNetworkManager : MonoBehaviour
    {
        public const string ClientRequestMessageName = "MiniEcsClientRequest";
        float _timer;

        bool _initialized;
        bool _isServer;

        public bool IsInitialized => _initialized;
        public bool IsServer => _isServer;

        void OnEnable()
        {
            TryInitialize();
        }

        void OnDisable()
        {
            if (_initialized)
            {
                if (!_isServer)
                {
                    try
                    {
                        NetworkManager.Singleton.CustomMessagingManager
                            .UnregisterNamedMessageHandler(SyncConfig.MessageName);
                    }
                    catch { }
                }
                else
                {
                    try
                    {
                        NetworkManager.Singleton.CustomMessagingManager
                            .UnregisterNamedMessageHandler(ClientRequestMessageName);
                    }
                    catch { }
                }
                _initialized = false;
            }
        }

        public void TryInitialize()
        {
            if (_initialized) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                return;

            _isServer = NetworkManager.Singleton.IsServer;
            ComponentSerializer.Initialize();

            if (ComponentSerializer.SyncTypeCount == 0)
            {
                Debug.LogWarning("[EcsNetworkManager] No [Sync] component types found. Nothing to replicate.");
                _initialized = true;
                return;
            }

            Debug.Log($"[EcsNetworkManager] Initialized with {ComponentSerializer.SyncTypeCount} sync type(s). " +
                      $"Mode: {(_isServer ? "SERVER" : "CLIENT")}");

            if (!_isServer)
            {
                NetworkManager.Singleton.CustomMessagingManager
                    .RegisterNamedMessageHandler(SyncConfig.MessageName, OnMessageReceived);
            }
            else
            {
                NetworkManager.Singleton.CustomMessagingManager
                    .RegisterNamedMessageHandler(ClientRequestMessageName, OnClientRequestReceived);
            }

            _initialized = true;
        }

        void Update()
        {
            if (!_initialized)
            {
                TryInitialize();
                return;
            }
            if (!_isServer) return;

            _timer += Time.deltaTime;
            if (_timer < SyncConfig.SyncInterval) return;
            _timer = 0f;

            SendSnapshot();
        }

        // ── Server: Send Full Snapshot ───────────────────────────────

        void SendSnapshot()
        {
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                return;

            using var writer = new FastBufferWriter(1024, Unity.Collections.Allocator.Temp,
                ushort.MaxValue);

            var syncIds = CollectSyncEntityIds();

            writer.WriteByteSafe(0); // message type: FullSnapshot
            writer.WriteValueSafe(syncIds.Count);

            foreach (int eid in syncIds)
                ComponentSerializer.WriteEntity(writer, eid);

            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessageToAll(SyncConfig.MessageName, writer,
                    SyncConfig.Delivery);
        }

        // ── Client: Send Entity Request to Server ──────────────────────

        public void SendRequestToServer(Entity entity)
        {
            if (_isServer) return;
            if (NetworkManager.Singleton == null || !NetworkManager.Singleton.IsListening)
                return;

            using var writer = new FastBufferWriter(512, Unity.Collections.Allocator.Temp,
                ushort.MaxValue);

            writer.WriteByteSafe(1); // message type: ClientRequest
            ComponentSerializer.WriteEntity(writer, entity.Id);

            var serverId = NetworkManager.ServerClientId;
            NetworkManager.Singleton.CustomMessagingManager
                .SendNamedMessage(ClientRequestMessageName, serverId, writer,
                    SyncConfig.Delivery);
        }

        // ── Server: Handle Incoming Client Request ─────────────────────

        void OnClientRequestReceived(ulong clientId, FastBufferReader reader)
        {
            try
            {
                reader.ReadByteSafe(out _); // message type: ClientRequest

                var (_, components) = ComponentSerializer.ReadEntity(reader);

                var entity = EntityManager.CreateEntity();

                MethodInfo addGeneric = null;
                foreach (var (typeIndex, data) in components)
                {
                    var type = ComponentSerializer.IndexToType[typeIndex];
                    if (addGeneric == null)
                    {
                        addGeneric = typeof(EntityManager).GetMethod("Add",
                            BindingFlags.Public | BindingFlags.Static);
                    }
                    var genericAdd = addGeneric.MakeGenericMethod(type);
                    genericAdd.Invoke(null, new object[] { entity, data });
                }

                string username = string.Empty;
                if (EntityManager.Has<MatchRequestComponent>(entity.Id))
                {
                    username = EntityManager.Pool<MatchRequestComponent>().Get(entity.Id).Username;
                    EntityManager.RemoveComponent<MatchRequestComponent>(entity.Id);
                }

                EntityManager.Add(entity, new PlayerInfoComponent
                {
                    ClientId = clientId,
                    Username = username
                });

                EntityManager.Add(entity, new MatchStatusComponent
                {
                    ClientId = clientId,
                    Status = MatchStatus.Searching
                });

                Debug.Log($"[EcsNetworkManager] Player '{username}' (client {clientId}) joined matchmaking " +
                          $"(entity {entity.Id}).");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EcsNetworkManager] Error processing client request: {ex}");
            }
        }

        static List<int> CollectSyncEntityIds()
        {
            var result = new List<int>();
            var seen = new HashSet<int>();

            foreach (var kvp in EntityManager.GetAllPools())
            {
                if (!ComponentSerializer.TypeToIndex.ContainsKey(kvp.Key)) continue;

                foreach (int id in kvp.Value.GetEntities())
                {
                    if (seen.Add(id))
                        result.Add(id);
                }
            }

            return result;
        }

        // ── Client: Handle Incoming Message ──────────────────────────

        void OnMessageReceived(ulong clientId, FastBufferReader reader)
        {
            try
            {
                reader.ReadByteSafe(out _); // message type: FullSnapshot
                reader.ReadValueSafe(out int entityCount);

                var incomingIds = new HashSet<int>();

                for (int i = 0; i < entityCount; i++)
                {
                    var (serverId, components) = ComponentSerializer.ReadEntity(reader);
                    incomingIds.Add(serverId);
                    ApplyServerState(serverId, components);
                }

                CleanupRemoved(incomingIds);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[EcsNetworkManager] Error processing sync message: {ex}");
            }
        }

        void ApplyServerState(int serverId, List<(short typeIndex, object data)> components)
        {
            Entity local = SyncEntityRegistry.GetLocal(serverId);
            bool isNew = local.Id == 0;

            if (isNew)
            {
                local = EntityManager.CreateEntity();
                SyncEntityRegistry.SetMapping(serverId, local);
            }

            var incomingTypes = new HashSet<Type>();
            MethodInfo addGeneric = null;

            foreach (var (typeIndex, data) in components)
            {
                var type = ComponentSerializer.IndexToType[typeIndex];
                incomingTypes.Add(type);

                var pool = EntityManager.GetPool(type);

                if (pool.Has(local.Id))
                {
                    int idx = pool.GetIndex(local.Id);
                    pool.GetComponents().SetValue(data, idx);
                }
                else
                {
                    if (addGeneric == null)
                    {
                        addGeneric = typeof(EntityManager).GetMethod("Add",
                            BindingFlags.Public | BindingFlags.Static);
                    }
                    var genericAdd = addGeneric.MakeGenericMethod(type);
                    genericAdd.Invoke(null, new object[] { local, data });
                }
            }

            if (!isNew)
            {
                foreach (var kvp in ComponentSerializer.TypeToIndex)
                {
                    var type = kvp.Key;
                    if (incomingTypes.Contains(type)) continue;

                    var pool = EntityManager.GetPool(type);
                    if (pool.Has(local.Id))
                        RemoveComponentFromPool(pool, type, local.Id);
                }
            }
        }

        static void RemoveComponentFromPool(IComponentPool pool, Type type, int localId)
        {
            var removeMethod = typeof(EntityManager).GetMethod("RemoveComponent",
                BindingFlags.Public | BindingFlags.Static);
            if (removeMethod != null)
            {
                var generic = removeMethod.MakeGenericMethod(type);
                generic.Invoke(null, new object[] { localId });
            }
        }

        void CleanupRemoved(HashSet<int> incomingIds)
        {
            var toRemove = new List<int>();

            foreach (var kvp in SyncEntityRegistry.All)
            {
                if (!incomingIds.Contains(kvp.Key))
                    toRemove.Add(kvp.Key);
            }

            foreach (int serverId in toRemove)
            {
                var local = SyncEntityRegistry.GetLocal(serverId);
                if (local.Id == 0) continue;

                foreach (var kvp in ComponentSerializer.TypeToIndex)
                {
                    var type = kvp.Key;
                    var pool = EntityManager.GetPool(type);
                    if (pool.Has(local.Id))
                        RemoveComponentFromPool(pool, type, local.Id);
                }

                SyncEntityRegistry.Remove(serverId);
            }
        }
    }
}
