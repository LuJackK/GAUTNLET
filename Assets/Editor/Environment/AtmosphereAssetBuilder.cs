using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class AtmosphereAssetBuilder
{
    private const string TextureRoot = "Assets/Textures/Space Skybox Pack/shine";
    private const string SkyboxMaterialPath = "Assets/Materials/Skyboxes/M_SpaceSkybox_UT99.mat";
    private const string ProfilePath = "Assets/Settings/Environment/UT99SpaceAtmosphere.asset";
    private const string PrefabPath = "Assets/Prefabs/Environment/PF_UT99SpaceAtmosphere.prefab";

    [MenuItem("GAUTNLET/Environment/Build UT99 Space Atmosphere Assets")]
    public static void BuildAssets()
    {
        EnsureAssetFolder("Assets/Materials/Skyboxes");
        EnsureAssetFolder("Assets/Settings/Environment");
        EnsureAssetFolder("Assets/Prefabs/Environment");

        Material skyboxMaterial = BuildSkyboxMaterial();
        AtmosphereProfile profile = BuildProfile(skyboxMaterial);
        BuildPrefab(profile);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"Built reusable atmosphere assets: {SkyboxMaterialPath}, {ProfilePath}, {PrefabPath}");
    }

    [MenuItem("GAUTNLET/Environment/Apply UT99 Space Atmosphere To Open Scene")]
    public static void ApplyToOpenScene()
    {
        BuildAssets();

        AtmosphereProfile profile = AssetDatabase.LoadAssetAtPath<AtmosphereProfile>(ProfilePath);
        profile.Apply();
        ApplyEditorLightingMood();

        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Applied UT99 space atmosphere to the open scene. Re-bake lighting when you are happy with the look.");
    }

    [MenuItem("GAUTNLET/Environment/Apply UT99 Space Atmosphere To GAUTNLET Scenes")]
    public static void ApplyToProjectScenes()
    {
        BuildAssets();

        string[] scenePaths =
        {
            "Assets/Scenes/Level1.unity",
            "Assets/Scenes/Map1.unity",
            "Assets/Scenes/Map2.unity",
            "Assets/Scenes/Map3.unity",
            "Assets/Scenes/MenuScreen.unity",
            "Assets/Scenes/NetworkTest.unity",
            "Assets/Scenes/TestLevel1.unity",
            "Assets/Scenes/Tutorial.unity",
            "Assets/Scenes/UnrealImportLava.unity",
        };

        AtmosphereProfile profile = AssetDatabase.LoadAssetAtPath<AtmosphereProfile>(ProfilePath);
        foreach (string scenePath in scenePaths)
        {
            if (!File.Exists(scenePath))
            {
                continue;
            }

            EditorSceneManager.OpenScene(scenePath);
            profile.Apply();
            ApplyEditorLightingMood();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }

        Debug.Log("Applied UT99 space atmosphere to GAUTNLET scenes. Re-bake lighting in the scenes that use baked GI.");
    }

    private static Material BuildSkyboxMaterial()
    {
        Shader skyboxShader = Shader.Find("Skybox/6 Sided");
        if (skyboxShader == null)
        {
            throw new MissingReferenceException("Could not find built-in shader: Skybox/6 Sided");
        }

        Material material = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath);
        if (material == null)
        {
            material = new Material(skyboxShader);
            AssetDatabase.CreateAsset(material, SkyboxMaterialPath);
        }
        else
        {
            material.shader = skyboxShader;
        }

        material.name = "M_SpaceSkybox_UT99";
        material.SetTexture("_FrontTex", LoadTexture("jettelly_space_shine_blue_FRONT.png"));
        material.SetTexture("_BackTex", LoadTexture("jettelly_space_shine_blue_BACK.png"));
        material.SetTexture("_LeftTex", LoadTexture("jettelly_space_shine_blue_LEFT.png"));
        material.SetTexture("_RightTex", LoadTexture("jettelly_space_shine_blue_RIGHT.png"));
        material.SetTexture("_UpTex", LoadTexture("jettelly_space_shine_blue_UP.png"));
        material.SetTexture("_DownTex", LoadTexture("jettelly_space_shine_blue_DOWN.png"));
        material.SetColor("_Tint", new Color(0.68f, 0.78f, 1f, 1f));
        material.SetFloat("_Exposure", 0.82f);
        material.SetFloat("_Rotation", 0f);
        EditorUtility.SetDirty(material);

        return material;
    }

    private static AtmosphereProfile BuildProfile(Material skyboxMaterial)
    {
        AtmosphereProfile profile = AssetDatabase.LoadAssetAtPath<AtmosphereProfile>(ProfilePath);
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<AtmosphereProfile>();
            AssetDatabase.CreateAsset(profile, ProfilePath);
        }

        SerializedObject serializedProfile = new(profile);
        serializedProfile.FindProperty("skyboxMaterial").objectReferenceValue = skyboxMaterial;
        serializedProfile.FindProperty("frontTexture").objectReferenceValue = LoadTexture("jettelly_space_shine_blue_FRONT.png");
        serializedProfile.FindProperty("backTexture").objectReferenceValue = LoadTexture("jettelly_space_shine_blue_BACK.png");
        serializedProfile.FindProperty("leftTexture").objectReferenceValue = LoadTexture("jettelly_space_shine_blue_LEFT.png");
        serializedProfile.FindProperty("rightTexture").objectReferenceValue = LoadTexture("jettelly_space_shine_blue_RIGHT.png");
        serializedProfile.FindProperty("upTexture").objectReferenceValue = LoadTexture("jettelly_space_shine_blue_UP.png");
        serializedProfile.FindProperty("downTexture").objectReferenceValue = LoadTexture("jettelly_space_shine_blue_DOWN.png");
        serializedProfile.FindProperty("skyboxTint").colorValue = new Color(0.68f, 0.78f, 1f, 1f);
        serializedProfile.FindProperty("skyboxExposure").floatValue = 0.82f;
        serializedProfile.FindProperty("skyboxRotation").floatValue = 0f;
        serializedProfile.FindProperty("fogEnabled").boolValue = true;
        serializedProfile.FindProperty("fogColor").colorValue = new Color(0.045f, 0.07f, 0.16f, 1f);
        serializedProfile.FindProperty("fogMode").intValue = (int)FogMode.ExponentialSquared;
        serializedProfile.FindProperty("fogDensity").floatValue = 0.0065f;
        serializedProfile.FindProperty("linearFogStart").floatValue = 18f;
        serializedProfile.FindProperty("linearFogEnd").floatValue = 280f;
        serializedProfile.FindProperty("ambientMode").intValue = 1;
        serializedProfile.FindProperty("ambientSkyColor").colorValue = new Color(0.24f, 0.34f, 0.62f, 1f);
        serializedProfile.FindProperty("ambientEquatorColor").colorValue = new Color(0.09f, 0.14f, 0.28f, 1f);
        serializedProfile.FindProperty("ambientGroundColor").colorValue = new Color(0.035f, 0.03f, 0.055f, 1f);
        serializedProfile.FindProperty("ambientIntensity").floatValue = 0.7f;
        serializedProfile.FindProperty("subtractiveShadowColor").colorValue = new Color(0.08f, 0.1f, 0.22f, 1f);
        serializedProfile.FindProperty("defaultReflectionMode").intValue = 0;
        serializedProfile.FindProperty("defaultReflectionResolution").intValue = 128;
        serializedProfile.FindProperty("reflectionIntensity").floatValue = 0.38f;
        serializedProfile.FindProperty("reflectionBounces").intValue = 1;
        serializedProfile.ApplyModifiedPropertiesWithoutUndo();

        return profile;
    }

    private static void BuildPrefab(AtmosphereProfile profile)
    {
        bool loadedExistingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null;
        GameObject root = loadedExistingPrefab
            ? PrefabUtility.LoadPrefabContents(PrefabPath)
            : new GameObject("PF_UT99SpaceAtmosphere");

        root.name = "PF_UT99SpaceAtmosphere";
        AtmosphereProfileApplier applier = root.GetComponent<AtmosphereProfileApplier>();
        if (applier == null)
        {
            applier = root.AddComponent<AtmosphereProfileApplier>();
        }

        SerializedObject serializedApplier = new(applier);
        serializedApplier.FindProperty("profile").objectReferenceValue = profile;
        serializedApplier.FindProperty("applyOnAwake").boolValue = true;
        serializedApplier.FindProperty("reapplyOnSceneLoaded").boolValue = true;
        serializedApplier.FindProperty("keepAcrossScenes").boolValue = true;
        serializedApplier.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        if (loadedExistingPrefab)
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
        else
        {
            Object.DestroyImmediate(root);
        }
    }

    private static Texture2D LoadTexture(string fileName)
    {
        string path = $"{TextureRoot}/{fileName}";
        Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (texture == null)
        {
            throw new FileNotFoundException($"Missing skybox texture at {path}");
        }

        return texture;
    }

    private static void ApplyEditorLightingMood()
    {
        if (!Lightmapping.TryGetLightingSettings(out LightingSettings lightingSettings) || lightingSettings == null)
        {
            lightingSettings = new LightingSettings
            {
                name = "UT99 Space Lighting Settings"
            };
            Lightmapping.SetLightingSettingsForScene(SceneManager.GetActiveScene(), lightingSettings);
        }

        lightingSettings.autoGenerate = false;
        lightingSettings.bakedGI = true;
        lightingSettings.realtimeGI = false;
        lightingSettings.lightmapper = LightingSettings.Lightmapper.ProgressiveCPU;
        lightingSettings.lightmapResolution = 24f;
        lightingSettings.indirectResolution = 1f;
        lightingSettings.lightmapMaxSize = 1024;
        lightingSettings.lightmapPadding = 2;
        lightingSettings.directionalityMode = LightmapsMode.NonDirectional;
        lightingSettings.mixedBakeMode = MixedLightingMode.Subtractive;
        lightingSettings.maxBounces = 1;
        lightingSettings.indirectScale = 0.7f;
        lightingSettings.albedoBoost = 1.2f;
    }

    private static void EnsureAssetFolder(string folderPath)
    {
        if (AssetDatabase.IsValidFolder(folderPath))
        {
            return;
        }

        string parent = Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
        string folderName = Path.GetFileName(folderPath);
        if (!string.IsNullOrEmpty(parent))
        {
            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
