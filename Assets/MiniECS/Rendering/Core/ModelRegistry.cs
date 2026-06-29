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
        static readonly List<VatAnimationData> perModelVat = new();

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
            perModelVat.Add(null);
            lookup[key] = id;
            return id;
        }

        public static int Register(Mesh mesh, Material material, VatAnimationData vatData)
        {
            int key = Hash(mesh, material);
            if (lookup.TryGetValue(key, out int id))
            {
                perModelVat[id] = vatData;
                return id;
            }

            id = meshes.Count;
            meshes.Add(mesh);
            materials.Add(EnsureCompatible(material));
            perModelVat.Add(vatData);
            lookup[key] = id;
            return id;
        }

        static Material EnsureCompatible(Material material)
        {
            if (material != null && (material.HasProperty(Shader.PropertyToID("_PositionVAT")) || material.HasProperty(Shader.PropertyToID("_InstanceBuffer")) || material.HasProperty(Shader.PropertyToID("_MatrixBuffer"))))
            {
                material.enableInstancing = true;
                return material;
            }

            if (instancingShader == null)
                instancingShader = Shader.Find("Custom/IndirectInstancing");

            var clone = new Material(instancingShader);
            if (material != null)
                clone.CopyPropertiesFromMaterial(material);
            clone.enableInstancing = true;
            return clone;
        }

        public static VatAnimationData GetVatData(int modelId)
        {
            if (modelId < 0 || modelId >= perModelVat.Count)
                return null;
            return perModelVat[modelId];
        }

        public static void Clear()
        {
            meshes.Clear();
            materials.Clear();
            lookup.Clear();
            perModelVat.Clear();
            instancingShader = null;
        }

        public static int Count => meshes.Count;

        public static Mesh GetMesh(int id) => meshes[id];
        public static Material GetMaterial(int id) => materials[id];
    }
}

