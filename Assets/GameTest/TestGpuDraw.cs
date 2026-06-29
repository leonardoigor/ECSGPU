using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(Camera))]
public class TestGpuDraw : MonoBehaviour
{
    void OnPostRender()
    {
        var mesh = Resources.GetBuiltinResource<Mesh>("Cube.fbx");
        if (mesh == null) return;

        var shader = Shader.Find("Custom/IndirectInstancing");
        if (shader == null) return;
        var mat = new Material(shader);

        var buf = new ComputeBuffer(1, sizeof(float) * 16);
        buf.SetData(new Matrix4x4[] {
            Matrix4x4.TRS(new Vector3(0, 1, 5), Quaternion.identity, Vector3.one)
        });

        var args = new GraphicsBuffer(
            GraphicsBuffer.Target.IndirectArguments,
            1,
            GraphicsBuffer.IndirectDrawIndexedArgs.size
        );
        var drawArgs = new GraphicsBuffer.IndirectDrawIndexedArgs[1];
        drawArgs[0].indexCountPerInstance = mesh.GetIndexCount(0);
        drawArgs[0].instanceCount = 1;
        drawArgs[0].startIndex = mesh.GetIndexStart(0);
        drawArgs[0].baseVertexIndex = mesh.GetBaseVertex(0);
        drawArgs[0].startInstance = 0;
        args.SetData(drawArgs);

        var mpb = new MaterialPropertyBlock();
        mpb.SetBuffer("_MatrixBuffer", buf);

        var rp = new RenderParams(mat)
        {
            worldBounds = new Bounds(Vector3.zero, Vector3.one * 100),
            matProps = mpb,
            layer = 0,
            shadowCastingMode = ShadowCastingMode.Off,
            receiveShadows = false
        };

        Graphics.RenderMeshIndirect(rp, mesh, args);

        Debug.Log("[TestGpuDraw] drew with RenderMeshIndirect");
    }
}
