using MiniECS.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class TestVatDirect : MonoBehaviour
{
    public VatAnimationData vatData;
    public int instanceCount = 100;
    public bool useRenderMeshIndirect = true;

    ComputeBuffer instanceBuffer;
    GraphicsBuffer argsBuffer;

    void Start()
    {
        if (vatData == null)
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("t:VatAnimationData");
            if (guids.Length > 0)
            {
                var path = UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]);
                vatData = UnityEditor.AssetDatabase.LoadAssetAtPath<VatAnimationData>(path);
            }
        }

        if (vatData == null || vatData.originalMesh == null || vatData.animationMaterial == null)
        {
            Debug.LogError("[TestVatDirect] Missing assets");
            enabled = false;
            return;
        }

        instanceBuffer = new ComputeBuffer(instanceCount, sizeof(float) * 20);
        var data = new InstanceData[instanceCount];
        int clipStart = vatData.clips != null && vatData.clips.Length > 0 ? vatData.clips[0].startFrame : 0;
        int clipFrameCount = vatData.clips != null && vatData.clips.Length > 0 ? vatData.clips[0].frameCount : Mathf.Max(1, vatData.totalFrames);
        for (int i = 0; i < instanceCount; i++)
        {
            data[i].transformMatrix = Matrix4x4.TRS(
                new Vector3(Random.Range(-10f, 10f), 0, Random.Range(-10f, 10f)),
                Quaternion.identity, Vector3.one * 0.5f
            );
            data[i].animIndex = 0;
            data[i].currentFrame = Random.Range(0f, clipFrameCount);
            data[i].clipStartFrame = clipStart;
            data[i].clipFrameCount = clipFrameCount;
        }
        instanceBuffer.SetData(data);

        var args = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        args[0].indexCountPerInstance = vatData.originalMesh.GetIndexCount(0);
        args[0].instanceCount = (uint)instanceCount;
        args[0].startIndex = vatData.originalMesh.GetIndexStart(0);
        args[0].baseVertexIndex = vatData.originalMesh.GetBaseVertex(0);
        args[0].startInstance = 0;
        argsBuffer = new GraphicsBuffer(
            GraphicsBuffer.Target.IndirectArguments,
            1,
            GraphicsBuffer.IndirectDrawIndexedArgs.size
        );
        argsBuffer.SetData(args);

        Debug.Log($"[TestVatDirect] Ready mesh={vatData.originalMesh.name} instances={instanceCount}");
    }

    void OnPostRender()
    {
        if (vatData == null) return;

        var mpb = new MaterialPropertyBlock();
        mpb.SetBuffer("_InstanceBuffer", instanceBuffer);
        mpb.SetTexture("_PositionVAT", vatData.positionTexture);
        mpb.SetTexture("_NormalVAT", vatData.normalTexture);
        mpb.SetFloat("_VATWidth", vatData.vertexCount);
        mpb.SetFloat("_VATHeight", vatData.totalFrames);

        if (useRenderMeshIndirect)
        {
            var rp = new RenderParams(vatData.animationMaterial)
            {
                worldBounds = new Bounds(Vector3.zero, Vector3.one * 100),
                matProps = mpb,
                layer = 0,
                shadowCastingMode = ShadowCastingMode.Off,
                receiveShadows = false
            };
            Graphics.RenderMeshIndirect(rp, vatData.originalMesh, argsBuffer);
            Debug.Log($"[TestVatDirect] RenderMeshIndirect {instanceCount}");
        }
        else
        {
            Graphics.DrawMeshInstancedIndirect(
                vatData.originalMesh, 0, vatData.animationMaterial,
                new Bounds(Vector3.zero, Vector3.one * 100),
                argsBuffer, 0, mpb,
                ShadowCastingMode.Off, false
            );
            Debug.Log($"[TestVatDirect] DrawMeshInstancedIndirect {instanceCount}");
        }
    }

    void OnDestroy()
    {
        instanceBuffer?.Release();
        argsBuffer?.Release();
    }
}
