using UnityEngine;

namespace PirateGame.UI
{
    [ExecuteAlways, RequireComponent(typeof(RectTransform))]
    public sealed class ReferenceFrame : MonoBehaviour
    {
        private Canvas canvas;
        private RectTransform frame;

        void OnEnable()
        {
            frame = (RectTransform)transform;
            canvas = GetComponentInParent<Canvas>();
            Canvas.willRenderCanvases += Fit;
            Fit();
        }

        void OnDisable() => Canvas.willRenderCanvases -= Fit;

        // CanvasScaler recalculates during preWillRenderCanvases. Use its final scale for this display.
        void Fit()
        {
            if (!canvas || !frame || canvas.scaleFactor <= 0) return;
            Vector2 design = frame.rect.size;
            Vector2 pixels = canvas.pixelRect.size;
            if (design.x <= 0 || design.y <= 0 || pixels.x <= 0 || pixels.y <= 0) return;
            float fit = Mathf.Min(pixels.x / design.x, pixels.y / design.y) / canvas.scaleFactor;
            if (!Mathf.Approximately(frame.localScale.x, fit)) frame.localScale = new Vector3(fit, fit, 1);
        }
    }
}