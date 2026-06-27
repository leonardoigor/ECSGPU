using System.Collections.Generic;
using UnityEngine;

namespace MiniECS.Rendering
{
    public static class ModelRegistry
    {
        static readonly List<Mesh> meshes = new();
        static readonly List<Material> materials = new();
        static readonly Dictionary<int, int> lookup = new();
        static Shader instancingShader;

        static int Hash(Mesh mesh, Material material)
        {
            int hash = 17;
            hash = hash * 31 + (mesh != null ? mesh.GetInstanceID() : 0);
            hash = hash * 31 + (material != null ? material.GetInstanceID() : 0);
            return hash;
        }

        public static int Register(Mesh mesh, Material material)
        {
            int key = Hash(mesh, material);
            if (lookup.TryGetValue(key, out int id))
                return id;

            id = meshes.Count;
            meshes.Add(mesh);
            materials.Add(EnsureCompatible(material));
            lookup[key] = id;
            return id;
        }

        static Material EnsureCompatible(Material material)
        {
            if (material != null && material.HasProperty(Shader.PropertyToID("_MatrixBuffer")))
                return material;

            if (instancingShader == null)
                instancingShader = Shader.Find("Custom/IndirectInstancing");

            var clone = new Material(instancingShader);
            if (material != null)
                clone.CopyPropertiesFromMaterial(material);
            return clone;
        }

        public static Mesh GetMesh(int id) => meshes[id];
        public static Material GetMaterial(int id) => materials[id];
        public static int Count => meshes.Count;
    }
}
