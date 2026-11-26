using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PirateGame.UI
{
    // Editable uGUI geometry for wireframe silhouettes, route lines and placeholder icons.
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class WireframeShape : MaskableGraphic
    {
        public Vector2[] points;
        public bool closed;
        public Color edge = Color.clear;
        public float edgeWidth = 2;
        public bool dashed;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            if (points == null || points.Length < 2) return;
            Rect r = rectTransform.rect;
            var vertices = new Vector2[points.Length];
            for (int i = 0; i < points.Length; i++)
                vertices[i] = r.min + Vector2.Scale(points[i], r.size);
            if (closed && color.a > 0) Fill(mesh, vertices);
            if (edge.a <= 0 || edgeWidth <= 0) return;
            for (int i = 0; i < vertices.Length - (closed ? 0 : 1); i++)
            {
                var start = vertices[i];
                var end = vertices[(i + 1) % vertices.Length];
                if (!dashed) Stroke(mesh, start, end);
                else
                {
                    float length = Vector2.Distance(start, end);
                    for (float d = 0; d < length; d += 28f)
                        Stroke(mesh, Vector2.Lerp(start, end, d / length), Vector2.Lerp(start, end, Mathf.Min(d + 16, length) / length));
                }
            }
        }

        private void Stroke(VertexHelper mesh, Vector2 start, Vector2 end)
        {
            var side = Vector2.Perpendicular((end - start).normalized) * (edgeWidth / 2);
            int index = mesh.currentVertCount;
            foreach (var p in new[] { start + side, end + side, end - side, start - side })
                mesh.AddVert(p, edge, Vector2.zero);
            mesh.AddTriangle(index, index + 1, index + 2);
            mesh.AddTriangle(index, index + 2, index + 3);
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private void Fill(VertexHelper mesh, Vector2[] v)
        {
            var polygon = new List<int>(v.Length);
            float area = 0;
            for (int i = 0; i < v.Length; i++)
            {
                mesh.AddVert(v[i], color, Vector2.zero);
                polygon.Add(i);
                area += Cross(v[i], v[(i + 1) % v.Length]);
            }
            if (area < 0) polygon.Reverse();
            while (polygon.Count >= 3)
            {
                bool clipped = false;
                for (int i = 0; i < polygon.Count; i++)
                {
                    int a = polygon[(i + polygon.Count - 1) % polygon.Count], b = polygon[i], c = polygon[(i + 1) % polygon.Count];
                    if (Cross(v[b] - v[a], v[c] - v[b]) <= 0) continue;
                    bool inside = false;
                    foreach (int p in polygon)
                    {
                        if (p == a || p == b || p == c) continue;
                        if (Cross(v[b]-v[a], v[p]-v[a]) >= 0 && Cross(v[c]-v[b], v[p]-v[b]) >= 0 && Cross(v[a]-v[c], v[p]-v[c]) >= 0)
                        { inside = true; break; }
                    }
                    if (inside) continue;
                    mesh.AddTriangle(a, b, c);
                    polygon.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
        }
    }
}