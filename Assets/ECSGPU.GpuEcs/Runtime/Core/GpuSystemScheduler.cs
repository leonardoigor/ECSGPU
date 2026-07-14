using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ECSGPU.GpuEcs
{
    public class GpuSystemScheduler : IDisposable
    {
        struct SystemEntry
        {
            public IGpuSystem System;
            public ComputeShader Shader;
            public GpuSystemAttribute Attr;
        }

        readonly List<SystemEntry> _systems = new List<SystemEntry>();
        readonly Dictionary<Type, GpuSystemAttribute> _systemAttributes = new Dictionary<Type, GpuSystemAttribute>();

        public int SystemCount => _systems.Count;

        public void Register<T>(ComputeShader shader) where T : struct, IGpuSystem
        {
            Type type = typeof(T);
            var attr = type.GetCustomAttribute<GpuSystemAttribute>();
            if (attr == null)
            {
                Debug.LogError($"[GpuSystemScheduler] {type.Name} is missing [GpuSystem] attribute.");
                return;
            }

            var system = default(T);
            system.OnCreate(null);

            _systems.Add(new SystemEntry
            {
                System = system,
                Shader = shader,
                Attr = attr
            });

            _systemAttributes[type] = attr;
        }

        public void Register(Type systemType, ComputeShader shader)
        {
            var attr = systemType.GetCustomAttribute<GpuSystemAttribute>();
            if (attr == null)
            {
                Debug.LogError($"[GpuSystemScheduler] {systemType.Name} is missing [GpuSystem] attribute.");
                return;
            }

            var system = (IGpuSystem)Activator.CreateInstance(systemType);
            system.OnCreate(null);

            _systems.Add(new SystemEntry
            {
                System = system,
                Shader = shader,
                Attr = attr
            });

            _systemAttributes[systemType] = attr;
        }

        public void RegisterAll(ComputeShader shader)
        {
            var assembly = Assembly.GetExecutingAssembly();
            var systemTypes = assembly.GetTypes();

            foreach (var type in systemTypes)
            {
                if (!typeof(IGpuSystem).IsAssignableFrom(type) || type.IsInterface || type.IsAbstract)
                    continue;

                if (type.GetCustomAttribute<GpuSystemAttribute>() == null)
                    continue;

                Register(type, shader);
            }
        }

        public void RegisterAllFromAssembly(Assembly assembly, ComputeShader defaultShader)
        {
            var systemTypes = assembly.GetTypes();

            foreach (var type in systemTypes)
            {
                if (!typeof(IGpuSystem).IsAssignableFrom(type) || type.IsInterface || type.IsAbstract)
                    continue;

                var attr = type.GetCustomAttribute<GpuSystemAttribute>();
                if (attr == null)
                    continue;

                Register(type, defaultShader);
            }
        }

        public void Update(float deltaTime, GpuWorld world)
        {
            for (int i = 0; i < _systems.Count; i++)
            {
                var entry = _systems[i];
                entry.System.OnUpdate(world, deltaTime);
            }
        }

        public void Dispatch<T>(GpuWorld world, int entityCount) where T : struct, IGpuSystem
        {
            Type type = typeof(T);
            for (int i = 0; i < _systems.Count; i++)
            {
                if (_systems[i].System.GetType() != type)
                    continue;

                var entry = _systems[i];
                int kernel = entry.Shader.FindKernel(entry.Attr.KernelName);

                int groupsX = (entityCount + entry.Attr.ThreadX - 1) / entry.Attr.ThreadX;
                int groupsY = (entityCount > 0 && entry.Attr.ThreadY > 1) ? (entityCount + entry.Attr.ThreadY - 1) / entry.Attr.ThreadY : 1;
                int groupsZ = 1;

                entry.Shader.Dispatch(kernel, groupsX, groupsY, groupsZ);
                break;
            }
        }

        public void Dispatch(Type systemType, GpuWorld world, int entityCount)
        {
            for (int i = 0; i < _systems.Count; i++)
            {
                if (_systems[i].System.GetType() != systemType)
                    continue;

                var entry = _systems[i];
                int kernel = entry.Shader.FindKernel(entry.Attr.KernelName);

                int groupsX = (entityCount + entry.Attr.ThreadX - 1) / entry.Attr.ThreadX;
                int groupsY = 1;
                int groupsZ = 1;

                entry.Shader.Dispatch(kernel, groupsX, groupsY, groupsZ);
                break;
            }
        }

        public GpuSystemAttribute GetAttribute<T>() where T : struct, IGpuSystem
        {
            _systemAttributes.TryGetValue(typeof(T), out var attr);
            return attr;
        }

        public GpuSystemAttribute GetAttribute(Type systemType)
        {
            _systemAttributes.TryGetValue(systemType, out var attr);
            return attr;
        }

        public void Dispose()
        {
            for (int i = 0; i < _systems.Count; i++)
            {
                _systems[i].System.OnDestroy();
            }
            _systems.Clear();
            _systemAttributes.Clear();
        }
    }
}
