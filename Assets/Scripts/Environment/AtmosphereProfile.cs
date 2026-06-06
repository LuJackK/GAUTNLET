using UnityEngine;
using UnityEngine.Rendering;

[CreateAssetMenu(fileName = "AtmosphereProfile", menuName = "GAUTNLET/Environment/Atmosphere Profile")]
public sealed class AtmosphereProfile : ScriptableObject
{
    [Header("Sky")]
    [SerializeField] private Material skyboxMaterial;
    [SerializeField] private Texture2D frontTexture;
    [SerializeField] private Texture2D backTexture;
    [SerializeField] private Texture2D leftTexture;
    [SerializeField] private Texture2D rightTexture;
    [SerializeField] private Texture2D upTexture;
    [SerializeField] private Texture2D downTexture;
    [SerializeField] private Color skyboxTint = new(0.68f, 0.78f, 1f, 1f);
    [SerializeField] private float skyboxExposure = 0.82f;
    [SerializeField] private float skyboxRotation;

    [Header("Fog")]
    [SerializeField] private bool fogEnabled = true;
    [SerializeField] private Color fogColor = new(0.045f, 0.07f, 0.16f, 1f);
    [SerializeField] private FogMode fogMode = FogMode.ExponentialSquared;
    [SerializeField] private float fogDensity = 0.0065f;
    [SerializeField] private float linearFogStart = 18f;
    [SerializeField] private float linearFogEnd = 280f;

    [Header("Ambient")]
    [SerializeField] private AmbientMode ambientMode = AmbientMode.Trilight;
    [SerializeField] private Color ambientSkyColor = new(0.24f, 0.34f, 0.62f, 1f);
    [SerializeField] private Color ambientEquatorColor = new(0.09f, 0.14f, 0.28f, 1f);
    [SerializeField] private Color ambientGroundColor = new(0.035f, 0.03f, 0.055f, 1f);
    [SerializeField] private float ambientIntensity = 0.7f;
    [SerializeField] private Color subtractiveShadowColor = new(0.08f, 0.1f, 0.22f, 1f);

    [Header("Reflections")]
    [SerializeField] private DefaultReflectionMode defaultReflectionMode = DefaultReflectionMode.Skybox;
    [SerializeField] private int defaultReflectionResolution = 128;
    [SerializeField] private float reflectionIntensity = 0.38f;
    [SerializeField] private int reflectionBounces = 1;

    private Material generatedSkyboxMaterial;

    public Material SkyboxMaterial => ResolveSkyboxMaterial();

    public void Apply()
    {
        RenderSettings.skybox = ResolveSkyboxMaterial();

        RenderSettings.fog = fogEnabled;
        RenderSettings.fogColor = fogColor;
        RenderSettings.fogMode = fogMode;
        RenderSettings.fogDensity = fogDensity;
        RenderSettings.fogStartDistance = linearFogStart;
        RenderSettings.fogEndDistance = linearFogEnd;

        RenderSettings.ambientMode = ambientMode;
        RenderSettings.ambientSkyColor = ambientSkyColor;
        RenderSettings.ambientEquatorColor = ambientEquatorColor;
        RenderSettings.ambientGroundColor = ambientGroundColor;
        RenderSettings.ambientIntensity = ambientIntensity;
        RenderSettings.subtractiveShadowColor = subtractiveShadowColor;

        RenderSettings.defaultReflectionMode = defaultReflectionMode;
        RenderSettings.defaultReflectionResolution = defaultReflectionResolution;
        RenderSettings.reflectionIntensity = reflectionIntensity;
        RenderSettings.reflectionBounces = reflectionBounces;

        DynamicGI.UpdateEnvironment();
    }

    private Material ResolveSkyboxMaterial()
    {
        if (skyboxMaterial != null)
        {
            return skyboxMaterial;
        }

        if (frontTexture == null || backTexture == null || leftTexture == null || rightTexture == null || upTexture == null || downTexture == null)
        {
            return null;
        }

        if (generatedSkyboxMaterial == null)
        {
            Shader skyboxShader = Shader.Find("Skybox/6 Sided");
            if (skyboxShader == null)
            {
                return null;
            }

            generatedSkyboxMaterial = new Material(skyboxShader)
            {
                name = $"{name}_GeneratedSkybox",
                hideFlags = HideFlags.DontSave
            };
        }

        generatedSkyboxMaterial.SetTexture("_FrontTex", frontTexture);
        generatedSkyboxMaterial.SetTexture("_BackTex", backTexture);
        generatedSkyboxMaterial.SetTexture("_LeftTex", leftTexture);
        generatedSkyboxMaterial.SetTexture("_RightTex", rightTexture);
        generatedSkyboxMaterial.SetTexture("_UpTex", upTexture);
        generatedSkyboxMaterial.SetTexture("_DownTex", downTexture);
        generatedSkyboxMaterial.SetColor("_Tint", skyboxTint);
        generatedSkyboxMaterial.SetFloat("_Exposure", skyboxExposure);
        generatedSkyboxMaterial.SetFloat("_Rotation", skyboxRotation);

        return generatedSkyboxMaterial;
    }
}
