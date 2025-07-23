using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using System.Runtime.InteropServices;

public class TentacleGpuRenderer : MonoBehaviour
{
    public static TentacleGpuRenderer Instance { get; private set; }

    [Header("Rendering Assets")]
    [SerializeField] private Material instancedTentacleMaterial;

    [Header("Mesh & Buffer Settings")]
    [Tooltip("The number of simulation points that define the tentacle's spine. Must match 'Segments' in your LogicalTentacle.")]
    [SerializeField] private int pointsPerTentacle = 26;
    [Tooltip("The total number of visual segments along the length of each tentacle. Higher values result in smoother curves.")]
    [SerializeField] private int totalMeshSegments = 100; // Number of slices along the tentacle length

    [Header("Cross-Section Type")]
    [Tooltip("If true, tentacles will have a circular cross-section. If false, they will be flat ribbons.")]
    [SerializeField] private bool useCircularCrossSection = true; // NEW CHECKBOX PARAMETER
    [Tooltip("Number of vertices around the circular cross-section. Only used if 'Use Circular Cross-Section' is true.")]
    [SerializeField] private int radialSegments = 8; // Number of vertices around the circle (e.g., 8 for octagonal, 16 for smoother)
    [Tooltip("Number of width subdivisions for ribbon cross-section. Only used if 'Use Circular Cross-Section' is false. (2 means 2 sides)")]
    [SerializeField] private int ribbonWidthSubdivisions = 2; // Typically 2 for a flat ribbon (two vertices per slice)


    [Tooltip("The maximum number of tentacles the system can render. This pre-allocates memory.")]
    [SerializeField] private int maxTentacles = 200;

    // IMPORTANT: Ensure this struct matches the one in your LogicalTentacle script exactly.
    // public struct TentaclePoint { public Vector2 position; public Vector2 prevPosition; }

    private Mesh _baseMesh;
    private ComputeBuffer _pointsBuffer;
    private ComputeBuffer _argsBuffer;
    private readonly uint[] _args = { 0, 0, 0, 0, 0 };
    private readonly List<LogicalTentacle> _activeTentacles = new List<LogicalTentacle>();
    private List<TentaclePoint> _allPointsData;
    private bool _isInitialized = false;

    private void Awake()
    {
        if (Instance != null && Instance != this) Destroy(gameObject);
        else
        {
            Instance = this;
            Debug.Log("TentacleGpuRenderer AWAKE: Instance has been set.", this);
            Initialize();
        }
    }

    private void Initialize()
    {
        if (_isInitialized) return;
        _baseMesh = CreateBaseTentacleMesh();
        int maxPoints = maxTentacles * pointsPerTentacle;
        _allPointsData = new List<TentaclePoint>(maxPoints);

        _pointsBuffer = new ComputeBuffer(maxPoints, Marshal.SizeOf(typeof(TentaclePoint)));
        _argsBuffer = new ComputeBuffer(1, _args.Length * sizeof(uint), ComputeBufferType.IndirectArguments);
        _isInitialized = true;
    }

    private Mesh CreateBaseTentacleMesh()
    {
        var mesh = new Mesh();
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var uv1s = new List<Vector2>(); // UV1.x will indicate mesh type (0 for ribbon, 1 for circular)
        var triangles = new List<int>();

        // --- Generate Mesh based on selected type (Ribbon or Circular) ---

        if (useCircularCrossSection)
        {
            // Circular Cross-Section Mesh Generation
            for (int i = 0; i <= totalMeshSegments; i++) // Iterate along the length of the tentacle (for each "slice")
            {
                float lengthT = (float)i / totalMeshSegments; // Normalized progress along length (0 to 1)

                for (int j = 0; j < radialSegments; j++) // Iterate around the circle (for each vertex in a slice)
                {
                    float radialT = (float)j / radialSegments; // Normalized progress around circle (0 to 1)

                    vertices.Add(Vector3.zero); // All base mesh vertices start at origin
                    uvs.Add(new Vector2(radialT, lengthT)); // UV.x for angle, UV.y for length progress
                    uv1s.Add(new Vector2(1, 0)); // UV1.x = 1 to indicate this is a CIRCULAR vertex
                }
            }

            // Generate triangles for the cylindrical mesh
            for (int i = 0; i < totalMeshSegments; i++) // Iterate along each segment of the length
            {
                int currentRingStartIdx = i * radialSegments;
                int nextRingStartIdx = (i + 1) * radialSegments;

                for (int j = 0; j < radialSegments; j++) // Iterate around each cross-section
                {
                    int currentVertexIdx = currentRingStartIdx + j;
                    int nextVertexAroundIdx = currentRingStartIdx + (j + 1) % radialSegments; // Wrap around for last vertex

                    int currentVertexNextRingIdx = nextRingStartIdx + j;
                    int nextVertexAroundNextRingIdx = nextRingStartIdx + (j + 1) % radialSegments;

                    // First triangle of the quad
                    triangles.Add(currentVertexIdx);
                    triangles.Add(currentVertexNextRingIdx);
                    triangles.Add(nextVertexAroundIdx);

                    // Second triangle of the quad
                    triangles.Add(nextVertexAroundIdx);
                    triangles.Add(currentVertexNextRingIdx);
                    triangles.Add(nextVertexAroundNextRingIdx);
                }
            }
        }
        else // useCircularCrossSection is FALSE, so generate Ribbon Mesh
        {
            // Ribbon Mesh Generation
            for (int i = 0; i <= totalMeshSegments; i++) // Iterate along the length
            {
                float lengthT = (float)i / totalMeshSegments; // Normalized progress along length (0 to 1)

                for (int j = 0; j < ribbonWidthSubdivisions; j++) // Iterate across width (typically 2 for a flat ribbon)
                {
                    float widthT = (float)j / (ribbonWidthSubdivisions - 1); // 0 to 1 for width subdivisions

                    vertices.Add(Vector3.zero); // Dummy position
                    uvs.Add(new Vector2(widthT, lengthT)); // UV.x for width position, UV.y for length progress
                    uv1s.Add(new Vector2(0, 0)); // UV1.x = 0 to indicate this is a RIBBON vertex
                }
            }

            // Generate triangles for the ribbon
            for (int i = 0; i < totalMeshSegments; i++)
            {
                int baseVertexIndex = i * ribbonWidthSubdivisions;
                // Connect current strip to next strip along length
                for (int j = 0; j < ribbonWidthSubdivisions - 1; j++)
                {
                    // Triangle 1: current point, next point in strip, current point in next strip
                    triangles.Add(baseVertexIndex + j);
                    triangles.Add(baseVertexIndex + j + 1);
                    triangles.Add(baseVertexIndex + j + ribbonWidthSubdivisions);

                    // Triangle 2: next point in strip, current point in next strip, next point in next strip
                    triangles.Add(baseVertexIndex + j + 1);
                    triangles.Add(baseVertexIndex + j + ribbonWidthSubdivisions + 1);
                    triangles.Add(baseVertexIndex + j + ribbonWidthSubdivisions);
                }
            }
        }

        // IMPORTANT: Point sprite generation remains commented out from here for debugging.
        // If you want point sprites again, you'll need to re-add this logic,
        // and carefully consider how UV1.x is used to differentiate between main mesh and point sprites.
        /*
        // Example if you combine different mesh types in a single base mesh
        // Be careful with UV1.x usage for differentiation.
        int pointsCount = 500; 
        int b = vertices.Count;
        for (int i = 0; i < pointsCount; i++) {
            float p = (float)(i + 1) / (pointsCount + 1);
            vertices.Add(Vector3.zero); uvs.Add(new Vector2(0, 0));
            vertices.Add(Vector3.zero); uvs.Add(new Vector2(1, 0));
            vertices.Add(Vector3.zero); uvs.Add(new Vector2(0, 1));
            vertices.Add(Vector3.zero); uvs.Add(new Vector2(1, 1));
            uv1s.Add(new Vector2(2, p)); // Use a new UV1.x value (e.g., 2) for point sprites
            uv1s.Add(new Vector2(2, p)); 
            uv1s.Add(new Vector2(2, p)); 
            uv1s.Add(new Vector2(2, p)); 
            triangles.Add(b + 0); tris.Add(b + 2); tris.Add(b + 1);
            tris.Add(b + 1); tris.Add(b + 2); tris.Add(b + 3);
            b += 4;
        }
        */

        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uv1s); // UV1s now carries the mesh type flag
        mesh.SetTriangles(triangles, 0);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 1000);
        return mesh;
    }

    private void LateUpdate()
    {
        // Early exit checks
        if (instancedTentacleMaterial == null)
        {
            Debug.LogError("ERROR: Instanced Tentacle Material is NOT ASSIGNED in the inspector!", this);
            return;
        }
        if (_activeTentacles.Count == 0)
        {
            return;
        }
        if (!_isInitialized) return;
        if (_activeTentacles.Count > maxTentacles)
        {
            Debug.LogError($"Active tentacle count ({_activeTentacles.Count}) has exceeded the pre-allocated maximum ({maxTentacles}). Max: {maxTentacles}", this);
            return;
        }

        _allPointsData.Clear();

        // Loop through all active logical tentacles to get their simulation points
        foreach (var tentacle in _activeTentacles)
        {
            tentacle.CompleteSimulation();
            var tentaclePoints = tentacle.Points;

            for (int i = 0; i < tentaclePoints.Length; i++)
            {
                TentaclePoint point = tentaclePoints[i];
                // ASSUMPTION: tentacle.Points are already in WORLD SPACE.
                // If they are in the LogicalTentacle's local space, uncomment the lines below:
                // point.position = tentacle.transform.localToWorldMatrix.MultiplyPoint3x4(point.position);
                // point.prevPosition = tentacle.transform.localToWorldMatrix.MultiplyPoint3x4(point.prevPosition);
                _allPointsData.Add(point);
            }
        }

        _pointsBuffer.SetData(_allPointsData, 0, 0, _allPointsData.Count);

        // Pass properties to the material/shader
        instancedTentacleMaterial.SetBuffer("_PointsBuffer", _pointsBuffer);
        instancedTentacleMaterial.SetFloat("_PointsPerTentacle", pointsPerTentacle);
        instancedTentacleMaterial.SetFloat("_RadialSegments", radialSegments); // Pass to shader for circular calculations
        instancedTentacleMaterial.SetFloat("_IsCircular", useCircularCrossSection ? 1.0f : 0.0f); // NEW: Pass boolean to shader

        // Prepare arguments for DrawMeshInstancedIndirect
        _args[0] = (uint)_baseMesh.GetIndexCount(0);
        _args[1] = (uint)_activeTentacles.Count;
        _argsBuffer.SetData(_args);

        // Perform the indirect instanced draw call
        Graphics.DrawMeshInstancedIndirect(
            _baseMesh,
            0,
            instancedTentacleMaterial,
            new Bounds(Vector3.zero, Vector3.one * 1000),
            _argsBuffer,
            0,
            null,
            ShadowCastingMode.Off,
            false);
    }

    public void Register(LogicalTentacle tentacle) { if (!_activeTentacles.Contains(tentacle)) _activeTentacles.Add(tentacle); }
    public void Unregister(LogicalTentacle tentacle) { _activeTentacles.Remove(tentacle); }

    private void OnDestroy() { _pointsBuffer?.Release(); _argsBuffer?.Release(); }

    // NOTE: The 'TentaclePoint' struct definition is expected to be in your 'LogicalTentacle' script
    // or a separate common file, as it is used by both LogicalTentacle and TentacleGpuRenderer.
    // Ensure that definition matches:
    // public struct TentaclePoint
    // {
    //     public Vector2 position;
    //     public Vector2 prevPosition;
    //     public TentaclePoint(Vector2 pos) { this.position = pos; this.prevPosition = pos; }
    // }
}