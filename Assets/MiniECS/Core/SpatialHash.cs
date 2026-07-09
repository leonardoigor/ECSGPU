using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace MiniECS
{
    public struct SpatialHash : IDisposable
    {
        public float ChunkSize;

        NativeArray<int> _buckets;
        NativeArray<int> _bucketKeys;
        int _bucketMask;

        NativeArray<int> _chunkKeys;
        NativeArray<int> _chunkHead;
        NativeArray<int> _chunkCounts;
        int _chunkCount;
        int _maxChunks;

        NativeArray<int> _entityChunk;
        NativeArray<int> _entityNext;
        NativeArray<int> _entityPrev;
        int _maxEntities;

        public readonly bool IsCreated => _buckets.IsCreated;
        public readonly int MaxEntities => _maxEntities;
        public readonly int MaxChunks => _maxChunks;
        public readonly int BucketCount => _buckets.IsCreated ? _buckets.Length : 0;

        public static int PackKey(int x, int z) => (x & 0xFFFF) | ((z & 0xFFFF) << 16);

        public static int KeyFromPosition(float x, float z, float chunkSize)
        {
            int cx = (int)math.floor(x / chunkSize);
            int cz = (int)math.floor(z / chunkSize);
            if (cx < -32768 || cx > 32767 || cz < -32768 || cz > 32767)
            {
                Debug.LogError($"[SpatialHash] Chunk coordinate out of range: ({cx}, {cz})");
                cx = math.clamp(cx, -32768, 32767);
                cz = math.clamp(cz, -32768, 32767);
            }
            return PackKey(cx, cz);
        }

        public SpatialHash(float chunkSize, int maxEntities, int maxChunks, int bucketCount)
        {
            ChunkSize = chunkSize;
            _maxEntities = maxEntities;
            _maxChunks = maxChunks;

            bucketCount = math.ceilpow2(bucketCount);
            _bucketMask = bucketCount - 1;

            _buckets = new NativeArray<int>(bucketCount, Allocator.Persistent);
            _bucketKeys = new NativeArray<int>(bucketCount, Allocator.Persistent);
            _chunkKeys = new NativeArray<int>(maxChunks, Allocator.Persistent);
            _chunkHead = new NativeArray<int>(maxChunks, Allocator.Persistent);
            _chunkCounts = new NativeArray<int>(maxChunks, Allocator.Persistent);
            _entityChunk = new NativeArray<int>(maxEntities + 1, Allocator.Persistent);
            _entityNext = new NativeArray<int>(maxEntities + 1, Allocator.Persistent);
            _entityPrev = new NativeArray<int>(maxEntities + 1, Allocator.Persistent);
            _chunkCount = 0;

            for (int i = 0; i < bucketCount; i++)
                _buckets[i] = 0;

            for (int i = 0; i <= maxEntities; i++)
            {
                _entityChunk[i] = -1;
                _entityNext[i] = -1;
                _entityPrev[i] = -1;
            }

            for (int i = 0; i < maxChunks; i++)
                _chunkHead[i] = -1;
        }

        public void Dispose()
        {
            if (_buckets.IsCreated) _buckets.Dispose();
            if (_bucketKeys.IsCreated) _bucketKeys.Dispose();
            if (_chunkKeys.IsCreated) _chunkKeys.Dispose();
            if (_chunkHead.IsCreated) _chunkHead.Dispose();
            if (_chunkCounts.IsCreated) _chunkCounts.Dispose();
            if (_entityChunk.IsCreated) _entityChunk.Dispose();
            if (_entityNext.IsCreated) _entityNext.Dispose();
            if (_entityPrev.IsCreated) _entityPrev.Dispose();
        }

        int FindBucketSlot(int key, out bool exists)
        {
            int hash = key * 73856093;
            int idx = hash & _bucketMask;
            int start = idx;
            do
            {
                if (_buckets[idx] == 0) { exists = false; return idx; }
                if (_bucketKeys[idx] == key) { exists = true; return idx; }
                idx = (idx + 1) & _bucketMask;
            } while (idx != start);
            exists = false;
            return -1;
        }

        public int FindChunk(int key)
        {
            int hash = key * 73856093;
            int idx = hash & _bucketMask;
            int start = idx;
            do
            {
                if (_buckets[idx] == 0) return -1;
                if (_bucketKeys[idx] == key) return _buckets[idx] - 1;
                idx = (idx + 1) & _bucketMask;
            } while (idx != start);
            return -1;
        }

        public int GetOrCreateChunk(int key)
        {
            bool exists;
            int slot = FindBucketSlot(key, out exists);
            if (slot < 0) return -1;

            if (exists)
                return _buckets[slot] - 1;

            if (_chunkCount >= _maxChunks)
            {
                Debug.LogError("[SpatialHash] Max chunks reached!");
                return -1;
            }

            int ci = _chunkCount;
            _chunkCount++;
            _buckets[slot] = ci + 1;
            _bucketKeys[slot] = key;
            _chunkKeys[ci] = key;
            _chunkHead[ci] = -1;
            _chunkCounts[ci] = 0;
            return ci;
        }

        public void InsertEntity(int entityId, Vector3 position)
        {
            if (entityId < 0 || entityId > _maxEntities)
            {
                Debug.LogError($"[SpatialHash] Entity ID {entityId} out of range (max {_maxEntities})");
                return;
            }

            int key = KeyFromPosition(position.x, position.z, ChunkSize);
            int ci = GetOrCreateChunk(key);
            if (ci < 0) return;

            int oldChunk = _entityChunk[entityId];
            if (oldChunk >= 0)
                RemoveFromChunk(entityId, oldChunk);

            AddToChunk(entityId, ci);
        }

        public void MoveEntity(int entityId, Vector3 newPosition)
        {
            InsertEntity(entityId, newPosition);
        }

        public void RemoveEntity(int entityId)
        {
            if (entityId < 0 || entityId > _maxEntities) return;
            int oldChunk = _entityChunk[entityId];
            if (oldChunk >= 0)
                RemoveFromChunk(entityId, oldChunk);
        }

        void AddToChunk(int entityId, int ci)
        {
            int head = _chunkHead[ci];
            _entityChunk[entityId] = ci;
            _entityNext[entityId] = head;
            _entityPrev[entityId] = -1;

            if (head >= 0)
                _entityPrev[head] = entityId;

            _chunkHead[ci] = entityId;
            _chunkCounts[ci]++;
        }

        void RemoveFromChunk(int entityId, int ci)
        {
            int prev = _entityPrev[entityId];
            int next = _entityNext[entityId];

            if (prev >= 0)
                _entityNext[prev] = next;
            else
                _chunkHead[ci] = next;

            if (next >= 0)
                _entityPrev[next] = prev;

            _entityChunk[entityId] = -1;
            _entityNext[entityId] = -1;
            _entityPrev[entityId] = -1;
            _chunkCounts[ci]--;
        }

        public int CollectEntitiesInRadius(float centerX, float centerZ, float radius, NativeList<int> results)
        {
            int count = 0;
            int minCx = (int)math.floor((centerX - radius) / ChunkSize);
            int maxCx = (int)math.floor((centerX + radius) / ChunkSize);
            int minCz = (int)math.floor((centerZ - radius) / ChunkSize);
            int maxCz = (int)math.floor((centerZ + radius) / ChunkSize);

            for (int cz = minCz; cz <= maxCz; cz++)
            {
                for (int cx = minCx; cx <= maxCx; cx++)
                {
                    int key = PackKey(cx, cz);
                    int ci = FindChunk(key);
                    if (ci < 0) continue;

                    int eid = _chunkHead[ci];
                    while (eid >= 0)
                    {
                        results.Add(eid);
                        count++;
                        eid = _entityNext[eid];
                    }
                }
            }
            return count;
        }

        public int GetEntityChunk(int entityId) => _entityChunk[entityId];

        public int ChunkCount => _chunkCount;
    }
}
