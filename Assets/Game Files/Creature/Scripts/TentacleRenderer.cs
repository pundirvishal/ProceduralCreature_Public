using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

[RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
public class TentacleRendererBatched : MonoBehaviour
{
    [Header("Visual Settings - Tentacle Body")]
    [SerializeField] private Gradient colour = default;
    [SerializeField] private Material material; // Shared Material for both tentacle and points
    [SerializeField] private float startWidth = 0.15f;
    [Tooltip("Adjust this if you see splitting at the tip with alpha clipping. A slightly larger value can help.")]
    [SerializeField] private float endWidth = 0.05f;

    [Header("Mesh Type")]
    [SerializeField] private bool useCircularCrossSection = false;
    private int radialSegments = 2;

    private int widthSubdivisions = 2;
    private int lengthSubdivisions = 4;

    [Header("Sorting Settings")]
    [SerializeField] private string sortingLayerName = "Default";
    [SerializeField] private int baseSortingOrder = 5;
    [SerializeField] private int grippingSortingBonus = -2;
    [SerializeField] private int attackingSortingBonus = 5;
    [SerializeField] private bool useDynamicSorting = true;

    [Header("Points Settings")]
    [Tooltip("Assign your sprite texture here. This texture will be used by the shared material.")]
    [SerializeField] private Texture2D embeddedSpriteTexture;
    [SerializeField] private int pointsCount = 5;
    [SerializeField] private float pointsWidth = 0.5f;
    [SerializeField] private float pointsHeight = 0.5f;
    [SerializeField] private float pointsOffset = 0.01f;

    private LogicalTentacle logic;
    private MeshRenderer meshRenderer;
    private Mesh mesh;
    private readonly List<Vector3> verts = new();
    private readonly List<int> tris = new();
    private readonly List<Color> cols = new();
    private readonly List<Vector2> uvs = new();
    private readonly List<Vector2> uv1s = new();

    private bool isInitialized = false;
    // OPTIMIZATION: Re-use this array to prevent garbage allocation every frame.
    private Vector3[] lastPositions = null;

    private readonly List<Vector3> smoothPath = new();
    private readonly List<Vector3> smoothNormals = new();
    private readonly List<float> smoothProgress = new();
    private TentacleBatchMaker _batcher;

    public void GetGeneratedMeshData(out List<Vector3> outVerts, out List<int> outTris, out List<Color> outCols, out List<Vector2> outUvs0, out List<Vector2> outUvs1)
    {
        outVerts = this.verts;
        outTris = this.tris;
        outCols = this.cols;
        outUvs0 = this.uvs;
        outUvs1 = this.uv1s;
    }

    public void Initialize(LogicalTentacle t, TentacleBatchMaker batcher)
    {
        logic = t;
        this._batcher = batcher;

        meshRenderer = GetComponent<MeshRenderer>();
        mesh = GetComponent<MeshFilter>().mesh;
        if (material != null)
            meshRenderer.material = material;
        meshRenderer.enabled = false;
        SetupSorting();
        transform.localPosition = Vector3.zero;
        transform.localRotation = Quaternion.identity;
        isInitialized = true;
    }

    private void SetupSorting()
    {
        meshRenderer.sortingLayerName = sortingLayerName;
        meshRenderer.sortingOrder = baseSortingOrder;
    }

    private void FixedUpdate()
    {
        if (!isInitialized || logic?.Points == null || logic.Points.Length < 2)
        {
            if (verts.Count > 0)
            {
                verts.Clear();
                uv1s.Clear();
                mesh.Clear();
                _batcher?.SetDirty();
            }
            return;
        }

        if (!HasPositionsChanged()) return;

        _batcher?.SetDirty();
        logic.CompleteSimulation();
        if (useDynamicSorting) UpdateDynamicSorting();

        GenerateCombinedMesh();

        // OPTIMIZATION: Copy positions into the existing array to avoid GC.
        UpdateLastPositions();
    }

    private bool HasPositionsChanged()
    {
        var points = logic.Points;
        if (lastPositions == null || lastPositions.Length != points.Length) return true;
        for (int i = 0; i < points.Length; i++)
        {
            // Use sqrMagnitude for a faster check (avoids sqrt).
            if (((Vector3)points[i].position - lastPositions[i]).sqrMagnitude > 0.0001f)
                return true;
        }
        return false;
    }

    // OPTIMIZATION: This method updates the cached positions without creating a new array.
    private void UpdateLastPositions()
    {
        var points = logic.Points;
        if (lastPositions == null || lastPositions.Length != points.Length)
        {
            lastPositions = new Vector3[points.Length];
        }
        for (int i = 0; i < points.Length; i++)
        {
            lastPositions[i] = points[i].position;
        }
    }

    private void UpdateDynamicSorting()
    {
        int newSortingOrder = baseSortingOrder;
        if (logic.isAttacking) newSortingOrder += attackingSortingBonus;
        else if (logic.IsGripping) newSortingOrder += grippingSortingBonus;

        if (meshRenderer.sortingOrder != newSortingOrder)
            meshRenderer.sortingOrder = newSortingOrder;
    }

    private void GenerateCombinedMesh()
    {
        verts.Clear(); tris.Clear(); cols.Clear(); uvs.Clear(); uv1s.Clear();
        smoothPath.Clear(); smoothNormals.Clear(); smoothProgress.Clear();

        var points = logic.Points;
        var pointCount = points.Length;
        if (pointCount < 2) return;

        for (int i = 0; i < pointCount - 1; i++)
        {
            Vector3 p0 = points[Mathf.Max(i - 1, 0)].position;
            Vector3 p1 = points[i].position;
            Vector3 p2 = points[Mathf.Min(i + 1, pointCount - 1)].position;
            Vector3 p3 = points[Mathf.Min(i + 2, pointCount - 1)].position;
            Vector3 segmentTangent = (p2 - p1).normalized;
            if (segmentTangent.sqrMagnitude < 0.0001f)
                segmentTangent = (i > 0 ? (p1 - p0).normalized : (p3 - p2).normalized);
            Vector3 segmentNormal = new Vector3(-segmentTangent.y, segmentTangent.x, 0);

            for (int j = 0; j < lengthSubdivisions; j++)
            {
                float t = (float)j / lengthSubdivisions;
                Vector3 newPoint = GetCatmullRomPosition(t, p0, p1, p2, p3);
                smoothPath.Add(newPoint);
                smoothNormals.Add(segmentNormal);
                float globalT = ((float)i + t) / (pointCount - 1);
                smoothProgress.Add(globalT);
            }
        }
        smoothPath.Add(points[pointCount - 1].position);
        smoothProgress.Add(1f);
        if (smoothNormals.Count > 0)
            smoothNormals.Add(smoothNormals[smoothNormals.Count - 1]);
        else
        {
            Vector3 p0 = points[pointCount - 2].position;
            Vector3 p1 = points[pointCount - 1].position;
            Vector3 segmentTangent = (p1 - p0).normalized;
            smoothNormals.Add(new Vector3(-segmentTangent.y, segmentTangent.x, 0));
        }

        if (useCircularCrossSection) BuildCircularMesh(smoothPath, smoothNormals, smoothProgress);
        else BuildRibbonMesh(smoothPath, smoothNormals, smoothProgress);

        AddPointsToCombinedMesh();

        mesh.Clear();
        if (verts.Count > 0)
        {
            mesh.SetVertices(verts);
            mesh.SetColors(cols);
            mesh.SetUVs(0, uvs);
            mesh.SetUVs(1, uv1s);
            mesh.SetTriangles(tris, 0, true);
            mesh.RecalculateBounds();
        }
    }

    private void BuildRibbonMesh(List<Vector3> path, List<Vector3> normals, List<float> progress)
    {
        int currentVertexBaseIndex = verts.Count;
        if (path.Count != normals.Count) return;
        for (int i = 0; i < path.Count; i++)
        {
            float currentProgress = progress[i];
            float width = Mathf.Lerp(startWidth, endWidth, currentProgress) * 0.5f;
            Color color = colour.Evaluate(currentProgress);
            Vector3 normal = normals[i];
            for (int j = 0; j <= widthSubdivisions; j++)
            {
                float widthT = (float)j / widthSubdivisions;
                float widthOffset = Mathf.Lerp(-width, width, widthT);
                verts.Add(path[i] + normal * widthOffset);
                cols.Add(color);
                uvs.Add(new Vector2(widthT, currentProgress));
                uv1s.Add(Vector2.zero);
            }
        }
        for (int i = 0; i < path.Count - 1; i++)
        {
            int ringOffset = widthSubdivisions + 1;
            int root = i * ringOffset + currentVertexBaseIndex;
            for (int j = 0; j < widthSubdivisions; j++)
            {
                tris.Add(root + j);
                tris.Add(root + j + 1);
                tris.Add(root + j + ringOffset);
                tris.Add(root + j + 1);
                tris.Add(root + j + 1 + ringOffset);
                tris.Add(root + j + ringOffset);
            }
        }
    }

    private void BuildCircularMesh(List<Vector3> path, List<Vector3> normals, List<float> progress)
    {
        int currentVertexBaseIndex = verts.Count;
        if (path.Count != normals.Count) return;
        for (int i = 0; i < path.Count; i++)
        {
            float currentProgress = progress[i];
            float radius = Mathf.Lerp(startWidth, endWidth, currentProgress) * 0.5f;
            Color color = colour.Evaluate(currentProgress);
            Vector3 normal = normals[i];
            Vector3 tangent = Vector3.Cross(normal, Vector3.forward);
            for (int j = 0; j < radialSegments; j++)
            {
                float angle = (float)j / radialSegments * 2 * Mathf.PI;
                Vector3 offset = (normal * Mathf.Cos(angle) + tangent * Mathf.Sin(angle)) * radius;
                verts.Add(path[i] + offset);
                cols.Add(color);
                uvs.Add(new Vector2((float)j / radialSegments, currentProgress));
                uv1s.Add(Vector2.zero);
            }
        }
        for (int i = 0; i < path.Count - 1; i++)
        {
            int root = i * radialSegments + currentVertexBaseIndex;
            for (int j = 0; j < radialSegments; j++)
            {
                int next = (j + 1) % radialSegments;
                tris.Add(root + j);
                tris.Add(root + j + radialSegments);
                tris.Add(root + next);
                tris.Add(root + next);
                tris.Add(root + j + radialSegments);
                tris.Add(root + next + radialSegments);
            }
        }
    }

    private void AddPointsToCombinedMesh()
    {
        if (embeddedSpriteTexture == null || pointsCount <= 0 || smoothPath.Count < 2) return;

        int currentVertexBaseIndex = verts.Count;
        int actualPointsCount = Mathf.Min(pointsCount, smoothPath.Count - 1);
        if (actualPointsCount <= 0) return;

        for (int i = 0; i < actualPointsCount; i++)
        {
            float t = (float)(i + 1) / actualPointsCount;
            int pathIndex = Mathf.Clamp(Mathf.FloorToInt(t * (smoothPath.Count - 1)), 1, smoothPath.Count - 1);
            Vector3 pointPosition = smoothPath[pathIndex];
            Vector3 tentacleNormal = smoothNormals[pathIndex];
            float pointProgress = smoothProgress[pathIndex];
            Vector3 pointUp = tentacleNormal;
            Vector3 pointRight = new Vector3(pointUp.y, -pointUp.x, 0); // Correct cross product with (0,0,-1)
            Vector3 center = pointPosition + pointUp * pointsOffset;
            float halfWidth = pointsWidth * 0.5f;
            float halfHeight = pointsHeight * 0.5f;
            Vector3 v0 = center - pointRight * halfWidth - pointUp * halfHeight;
            Vector3 v1 = center + pointRight * halfWidth - pointUp * halfHeight;
            Vector3 v2 = center - pointRight * halfWidth + pointUp * halfHeight;
            Vector3 v3 = center + pointRight * halfWidth + pointUp * halfHeight;
            verts.Add(v0); verts.Add(v1); verts.Add(v2); verts.Add(v3);

            Color pointColor = colour.Evaluate(pointProgress);
            cols.Add(pointColor); cols.Add(pointColor); cols.Add(pointColor); cols.Add(pointColor);

            uvs.Add(new Vector2(0, 0)); uvs.Add(new Vector2(1, 0)); uvs.Add(new Vector2(0, 1)); uvs.Add(new Vector2(1, 1));

            var pointFlag = new Vector2(1.0f, 0.0f);
            uv1s.Add(pointFlag); uv1s.Add(pointFlag); uv1s.Add(pointFlag); uv1s.Add(pointFlag);

            tris.Add(currentVertexBaseIndex + 0); tris.Add(currentVertexBaseIndex + 2); tris.Add(currentVertexBaseIndex + 1);
            tris.Add(currentVertexBaseIndex + 1); tris.Add(currentVertexBaseIndex + 2); tris.Add(currentVertexBaseIndex + 3);
            currentVertexBaseIndex += 4;
        }
    }

    private static Vector3 GetCatmullRomPosition(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        return 0.5f * (
            (2f * p1) +
            (-p0 + p2) * t +
            (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t +
            (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t);
    }
}