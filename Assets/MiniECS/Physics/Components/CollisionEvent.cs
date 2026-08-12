using Unity.Collections;
using Unity.Mathematics;

namespace MiniECS.Physics
{
    public enum CollisionPhase : byte
    {
        Enter = 0,
        Stay = 1,
        Exit = 2
    }

    /// <summary>
    /// A single contact between two colliders, surfaced once per frame.
    /// Normal points from EntityA toward EntityB.
    /// </summary>
    public struct CollisionEvent
    {
        public int EntityA;
        public int EntityB;
        public float3 Normal;       // from A toward B
        public float Penetration;
        public int IsTrigger;       // 1 if at least one side is a trigger
        public CollisionPhase Phase;// Enter / Stay / Exit (filled on the main thread)
    }

    public delegate void CollisionEventHandler(NativeArray<CollisionEvent> events);

    /// <summary>
    /// Frame-coherent collision event buffer. CollisionSystem fills CollisionEvents.List
    /// every frame and invokes subscribers after Enter/Stay/Exit phases are resolved.
    /// </summary>
    public static class CollisionEvents
    {
        static NativeList<CollisionEvent> _events;
        static event CollisionEventHandler _subscribers;

        public static void Subscribe(CollisionEventHandler handler) { _subscribers += handler; }
        public static void Unsubscribe(CollisionEventHandler handler) { _subscribers -= handler; }

        public static NativeList<CollisionEvent> List
        {
            get
            {
                if (!_events.IsCreated)
                    _events = new NativeList<CollisionEvent>(64, Allocator.Persistent);
                return _events;
            }
        }

        internal static void Publish()
        {
            if (_events.IsCreated && _events.Length > 0)
                _subscribers?.Invoke(_events.AsArray());
        }

        internal static void ClearFrame()
        {
            if (_events.IsCreated) _events.Clear();
        }

        internal static void Dispose()
        {
            if (_events.IsCreated) _events.Dispose();
            _events = default;
        }
    }
}
