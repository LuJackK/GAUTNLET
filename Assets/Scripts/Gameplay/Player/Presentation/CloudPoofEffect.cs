using UnityEngine;

namespace Fragsurf.Movement {

    public class CloudPoofEffect : MonoBehaviour {

        [SerializeField] private float lifetime = 0.35f;
        [SerializeField] private Vector3 startScale = Vector3.one;
        [SerializeField] private Vector3 endScale = new Vector3(2.5f, 1.4f, 2.5f);
        [SerializeField] private AnimationCurve scaleCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);
        [SerializeField] private AnimationCurve alphaCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);
        [SerializeField] private bool randomizeYaw = true;

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SurfaceId = Shader.PropertyToID("_Surface");
        private static readonly int ModeId = Shader.PropertyToID("_Mode");
        private static readonly int SrcBlendId = Shader.PropertyToID("_SrcBlend");
        private static readonly int DstBlendId = Shader.PropertyToID("_DstBlend");
        private static readonly int ZWriteId = Shader.PropertyToID("_ZWrite");

        private MaterialPropertyBlock _propertyBlock;
        private Renderer[] _renderers;
        private Color[] _baseColors;
        private float _age;
        private bool _playing;

        public bool IsAvailable => !_playing;

        private void Awake() {
            _propertyBlock = new MaterialPropertyBlock();
            CacheRenderers();
        }

        public void Play(Vector3 position, Quaternion rotation) {
            if (_propertyBlock == null)
                _propertyBlock = new MaterialPropertyBlock();

            CacheRenderers();

            transform.position = position;
            transform.rotation = randomizeYaw ? Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * rotation : rotation;
            transform.localScale = startScale;

            _age = 0f;
            _playing = true;
            gameObject.SetActive(true);
            SetAlpha(1f);
        }

        private void Update() {
            if (!_playing)
                return;

            _age += Time.deltaTime;
            float t = lifetime > 0f ? Mathf.Clamp01(_age / lifetime) : 1f;

            transform.localScale = Vector3.LerpUnclamped(startScale, endScale, scaleCurve.Evaluate(t));
            SetAlpha(alphaCurve.Evaluate(t));

            if (t >= 1f) {
                _playing = false;
                gameObject.SetActive(false);
            }
        }

        private void CacheRenderers() {
            if (_renderers != null)
                return;

            _renderers = GetComponentsInChildren<Renderer>(true);
            _baseColors = new Color[_renderers.Length];

            for (int i = 0; i < _renderers.Length; i++) {
                Renderer renderer = _renderers[i];
                Material material = renderer != null && renderer.material != null ? renderer.material : null;
                ConfigureTransparentMaterial(material);
                _baseColors[i] = material != null ? GetMaterialColor(material) : Color.white;
            }
        }

        private void SetAlpha(float alpha) {
            if (_renderers == null)
                return;

            if (_propertyBlock == null)
                _propertyBlock = new MaterialPropertyBlock();

            for (int i = 0; i < _renderers.Length; i++) {
                Renderer renderer = _renderers[i];
                if (renderer == null)
                    continue;

                Color color = _baseColors[i];
                color.a *= Mathf.Clamp01(alpha);

                renderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(ColorId, color);
                _propertyBlock.SetColor(BaseColorId, color);
                renderer.SetPropertyBlock(_propertyBlock);
            }
        }

        private static Color GetMaterialColor(Material material) {
            if (material.HasProperty(BaseColorId))
                return material.GetColor(BaseColorId);

            if (material.HasProperty(ColorId))
                return material.GetColor(ColorId);

            return Color.white;
        }

        private static void ConfigureTransparentMaterial(Material material) {
            if (material == null)
                return;

            if (material.HasProperty(SurfaceId))
                material.SetFloat(SurfaceId, 1f);

            if (material.HasProperty(ModeId))
                material.SetFloat(ModeId, 3f);

            if (material.HasProperty(SrcBlendId))
                material.SetFloat(SrcBlendId, (float)UnityEngine.Rendering.BlendMode.SrcAlpha);

            if (material.HasProperty(DstBlendId))
                material.SetFloat(DstBlendId, (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);

            if (material.HasProperty(ZWriteId))
                material.SetFloat(ZWriteId, 0f);

            material.EnableKeyword("_ALPHABLEND_ON");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }
    }
}
