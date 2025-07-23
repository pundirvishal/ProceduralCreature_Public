using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TentacleBatchMaker : MonoBehaviour
{
    private List<TentacleRendererBatched> tentacleRenderers;
    private Mesh combinedMesh;
    private MeshFilter meshFilter;
    private readonly List<Vector3> combinedVerts = new();
    private readonly List<int> combinedTris = new();
    private readonly List<Color> combinedCols = new();
    private readonly List<Vector2> combinedUvs = new();
    private readonly List<Vector2> combinedUv1s = new();
    private bool _isDirty = true;

    public void SetDirty()
    {
        _isDirty = true;
    }

    private void Awake()
    {
        meshFilter = GetComponent<MeshFilter>();
        combinedMesh = new Mesh
        {
            name = "CombinedTentacleMesh",
            indexFormat = IndexFormat.UInt32
        };
        meshFilter.mesh = combinedMesh;
    }

    private void LateUpdate() // Changed from FixedUpdate to ensure it runs after tentacle physics and rendering updates.
    {
        if (tentacleRenderers == null || tentacleRenderers.Count == 0 || !_isDirty) return;

        CombineAllTentacleMeshes();
        _isDirty = false;
    }

    private void CombineAllTentacleMeshes()
    {
        combinedVerts.Clear();
        combinedTris.Clear();
        combinedCols.Clear();
        combinedUvs.Clear();
        combinedUv1s.Clear();
        int vertexOffset = 0;

        // OPTIMIZATION: Cache the world-to-local matrix. This is much faster than transform.InverseTransformPoint per vertex.
        Matrix4x4 worldToLocalMatrix = transform.worldToLocalMatrix;

        foreach (var tentacle in tentacleRenderers)
        {
            if (tentacle == null) continue;
            tentacle.GetGeneratedMeshData(out var worldSpaceVerts, out var singleTris, out var singleCols, out var singleUvs0, out var singleUvs1);
            if (worldSpaceVerts.Count == 0) continue;

            // OPTIMIZATION: Use the cached matrix to transform all vertices.
            foreach (Vector3 worldVert in worldSpaceVerts)
                combinedVerts.Add(worldToLocalMatrix.MultiplyPoint3x4(worldVert));

            combinedCols.AddRange(singleCols);
            combinedUvs.AddRange(singleUvs0);
            combinedUv1s.AddRange(singleUvs1);
            foreach (var tri in singleTris)
                combinedTris.Add(tri + vertexOffset);
            vertexOffset += worldSpaceVerts.Count;
        }

        combinedMesh.Clear();
        if (combinedVerts.Count > 0)
        {
            combinedMesh.SetVertices(combinedVerts);
            combinedMesh.SetColors(combinedCols);
            combinedMesh.SetUVs(0, combinedUvs);
            combinedMesh.SetUVs(1, combinedUv1s);
            combinedMesh.SetTriangles(combinedTris, 0, true);
            combinedMesh.RecalculateBounds();
            // Optional: You might not need to recalculate normals if your shader doesn't use them or they are calculated in the shader.
            // combinedMesh.RecalculateNormals(); 
        }
    }

    public void RefreshTentacleList(List<TentacleRendererBatched> newList)
    {
        tentacleRenderers = newList;
        SetDirty();
    }
}