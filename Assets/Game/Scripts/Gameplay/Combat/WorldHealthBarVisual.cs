using UnityEngine;

namespace Game.Gameplay
{
    /// <summary>
    /// Lightweight procedural world-space health bar rendered cleanly without extra assets.
    /// </summary>
    public sealed class WorldHealthBarVisual : MonoBehaviour
    {
        private static Texture2D s_whiteTexture;
        private static Sprite s_whiteSprite;

        private SpriteRenderer _bgRenderer;
        private SpriteRenderer _fillRenderer;
        private float _barWidth = 1.6f;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticCache()
        {
            s_whiteSprite = null;
            s_whiteTexture = null;
        }

        public void SetHealth(float current, float max, float yOffset, float width, bool alwaysShow = false)
        {
            bool visible = max > 0.01f && current > 0.001f && (alwaysShow || current < max - 0.01f);
            if (!visible)
            {
                if (_bgRenderer != null) _bgRenderer.enabled = false;
                if (_fillRenderer != null) _fillRenderer.enabled = false;
                return;
            }

            EnsureRenderers();
            _barWidth = Mathf.Max(0.8f, width);
            const float barHeight = 0.14f;

            transform.localPosition = new Vector3(0f, yOffset, 0f);
            transform.localRotation = Quaternion.identity;

            _bgRenderer.enabled = true;
            _bgRenderer.transform.localScale = new Vector3(_barWidth + 0.08f, barHeight + 0.06f, 1f);

            float ratio = Mathf.Clamp01(current / max);
            float fillWidth = _barWidth * ratio;
            _fillRenderer.enabled = fillWidth > 0.005f;
            _fillRenderer.transform.localScale = new Vector3(fillWidth, barHeight, 1f);
            _fillRenderer.transform.localPosition = new Vector3((fillWidth - _barWidth) * 0.5f, 0f, -0.01f);
            _fillRenderer.color = ratio > 0.55f
                ? new Color(0.24f, 0.9f, 0.38f, 0.95f)
                : (ratio > 0.25f ? new Color(1f, 0.78f, 0.18f, 0.95f) : new Color(0.95f, 0.24f, 0.22f, 0.95f));
        }

        private void EnsureRenderers()
        {
            Sprite sprite = GetOrCreateWhiteSprite();

            if (_bgRenderer == null)
            {
                var bgGo = new GameObject("HealthBarBg");
                bgGo.transform.SetParent(transform, false);
                _bgRenderer = bgGo.AddComponent<SpriteRenderer>();
                _bgRenderer.sprite = sprite;
                _bgRenderer.color = new Color(0.06f, 0.08f, 0.12f, 0.85f);
                _bgRenderer.sortingOrder = 10;
            }

            if (_fillRenderer == null)
            {
                var fillGo = new GameObject("HealthBarFill");
                fillGo.transform.SetParent(transform, false);
                _fillRenderer = fillGo.AddComponent<SpriteRenderer>();
                _fillRenderer.sprite = sprite;
                _fillRenderer.sortingOrder = 11;
            }
        }

        private static Sprite GetOrCreateWhiteSprite()
        {
            if (s_whiteSprite != null)
            {
                return s_whiteSprite;
            }

            s_whiteTexture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Point,
                hideFlags = HideFlags.DontSave
            };

            var pixels = new[] { Color.white, Color.white, Color.white, Color.white };
            s_whiteTexture.SetPixels(pixels);
            s_whiteTexture.Apply();

            s_whiteSprite = Sprite.Create(s_whiteTexture, new Rect(0f, 0f, 2f, 2f), new Vector2(0.5f, 0.5f), 2f);
            s_whiteSprite.hideFlags = HideFlags.DontSave;
            return s_whiteSprite;
        }
    }
}
