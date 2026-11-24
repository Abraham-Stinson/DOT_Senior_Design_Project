using UnityEngine;
using UnityEngine.Rendering;

namespace Game.Gameplay
{
    /// <summary>
    /// Renders a smooth circular territory disc and crisp perimeter ring matching a territory zone's world radius.
    /// Uses a unit-radius procedural mesh scaled to radius plus a local-space LineRenderer border for crisp edges.
    /// </summary>
    public sealed class TerritoryCircleVisual : MonoBehaviour
    {
        public const int DefaultSegmentCount = 64;

        private MeshFilter _discMeshFilter;
        private MeshRenderer _discRenderer;
        private LineRenderer _borderLineRenderer;
        private Mesh _unitDiscMesh;
        private Material _discMaterial;
        private Material _borderMaterial;
        private int _segmentCount = DefaultSegmentCount;
        private float _currentRadius = -1f;

        public float CurrentRadius => Mathf.Max(0f, _currentRadius);
        public int SegmentCount => _segmentCount;
        public MeshFilter DiscMeshFilter => _discMeshFilter;
        public LineRenderer BorderLineRenderer => _borderLineRenderer;

        private void OnDestroy()
        {
            DestroyUnityAsset(_unitDiscMesh);
            DestroyUnityAsset(_discMaterial);
            DestroyUnityAsset(_borderMaterial);
            _unitDiscMesh = null;
            _discMaterial = null;
            _borderMaterial = null;
        }

        public void Configure(
            Vector2 center,
            float radius,
            Color baseColor,
            float fillAlpha = 0.12f,
            float borderAlpha = 0.72f,
            float borderWidth = 0.07f)
        {
            EnsureRenderers();

            float clampedRadius = Mathf.Max(0f, radius);
            bool visible = clampedRadius > 0.001f;
            gameObject.SetActive(visible);
            if (!visible)
            {
                return;
            }

            transform.position = new Vector3(center.x, center.y, 0f);
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;

            _discMeshFilter.transform.localPosition = Vector3.zero;
            _discMeshFilter.transform.localScale = new Vector3(clampedRadius, clampedRadius, 1f);

            if (_discMaterial != null)
            {
                _discMaterial.color = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Clamp01(fillAlpha));
            }

            if (!Mathf.Approximately(_currentRadius, clampedRadius) || _borderLineRenderer.positionCount != _segmentCount)
            {
                RebuildBorderRingPoints(clampedRadius);
                _currentRadius = clampedRadius;
            }

            float width = Mathf.Max(0.01f, borderWidth);
            _borderLineRenderer.startWidth = width;
            _borderLineRenderer.endWidth = width;
            Color borderColor = new Color(baseColor.r, baseColor.g, baseColor.b, Mathf.Clamp01(borderAlpha));
            _borderLineRenderer.startColor = borderColor;
            _borderLineRenderer.endColor = borderColor;
        }

        private void EnsureRenderers()
        {
            if (_discMeshFilter == null)
            {
                var discGo = new GameObject("DiscMesh");
                discGo.transform.SetParent(transform, false);
                _discMeshFilter = discGo.AddComponent<MeshFilter>();
                _discRenderer = discGo.AddComponent<MeshRenderer>();
                _discRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _discRenderer.receiveShadows = false;
                _discRenderer.sortingOrder = -2;

                _unitDiscMesh = BuildUnitCircleMesh(_segmentCount);
                _discMeshFilter.sharedMesh = _unitDiscMesh;

                _discMaterial = CreateSpriteTransparentMaterial();
                _discRenderer.sharedMaterial = _discMaterial;
            }

            if (_borderLineRenderer == null)
            {
                var ringGo = new GameObject("BorderRing");
                ringGo.transform.SetParent(transform, false);
                _borderLineRenderer = ringGo.AddComponent<LineRenderer>();
                _borderLineRenderer.useWorldSpace = false;
                _borderLineRenderer.loop = true;
                _borderLineRenderer.shadowCastingMode = ShadowCastingMode.Off;
                _borderLineRenderer.receiveShadows = false;
                _borderLineRenderer.sortingOrder = -1;
                _borderLineRenderer.alignment = LineAlignment.TransformZ;
                _borderLineRenderer.numCornerVertices = 2;
                _borderLineRenderer.positionCount = _segmentCount;

                _borderMaterial = CreateSpriteTransparentMaterial();
                if (_borderMaterial != null)
                {
                    _borderMaterial.color = Color.white;
                }
                _borderLineRenderer.sharedMaterial = _borderMaterial;
            }
        }

        private void RebuildBorderRingPoints(float radius)
        {
            _borderLineRenderer.positionCount = _segmentCount;
            float angleStep = (Mathf.PI * 2f) / _segmentCount;
            for (int i = 0; i < _segmentCount; i++)
            {
                float angle = i * angleStep;
                _borderLineRenderer.SetPosition(i, new Vector3(Mathf.Cos(angle) * radius, Mathf.Sin(angle) * radius, 0f));
            }
        }

        private static Mesh BuildUnitCircleMesh(int segments)
        {
            int clampedSegments = Mathf.Max(12, segments);
            var mesh = new Mesh
            {
                name = "UnitTerritoryDisc"
            };

            var vertices = new Vector3[clampedSegments + 1];
            var colors = new Color[clampedSegments + 1];
            var uvs = new Vector2[clampedSegments + 1];
            var triangles = new int[clampedSegments * 3];

            vertices[0] = Vector3.zero;
            colors[0] = Color.white;
            uvs[0] = new Vector2(0.5f, 0.5f);

            float angleStep = (Mathf.PI * 2f) / clampedSegments;
            for (int i = 0; i < clampedSegments; i++)
            {
                float angle = i * angleStep;
                float cos = Mathf.Cos(angle);
                float sin = Mathf.Sin(angle);
                vertices[i + 1] = new Vector3(cos, sin, 0f);
                colors[i + 1] = Color.white;
                uvs[i + 1] = new Vector2(cos * 0.5f + 0.5f, sin * 0.5f + 0.5f);

                int nextVertex = i == clampedSegments - 1 ? 1 : i + 2;
                int triOffset = i * 3;
                // Clockwise winding in XY so normal faces -Z toward the orthographic 2D camera.
                triangles[triOffset] = 0;
                triangles[triOffset + 1] = nextVertex;
                triangles[triOffset + 2] = i + 1;
            }

            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.uv = uvs;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Material CreateSpriteTransparentMaterial()
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            }
            if (shader == null)
            {
                shader = Shader.Find("Universal Render Pipeline/Unlit");
            }

            return shader != null ? new Material(shader) : null;
        }

        private static void DestroyUnityAsset(Object asset)
        {
            if (asset == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(asset);
            }
            else
            {
                DestroyImmediate(asset);
            }
        }
    }
}
