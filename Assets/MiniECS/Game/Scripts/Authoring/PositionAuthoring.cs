using UnityEngine;
using MiniECS;

namespace Game.Authoring
{
    public class PositionAuthoring : MonoBehaviour
    {
        void Awake()
        {
            var entity = EntityManager.CreateEntity();
            EntityManager.Add(entity, new Game.Components.Position { Value = transform.position });
            EntityManager.Add(entity, new Game.Components.TransformRef { Value = transform });
            EntityManager.Add(entity, new Game.Components.Velocity { Value = Vector3.zero });
        }
    }
}
