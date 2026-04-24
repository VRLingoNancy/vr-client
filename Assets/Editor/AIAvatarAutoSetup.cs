using System.Linq;
using UnityEditor;
using UnityEngine;

public static class AIAvatarAutoSetup
{
    const string AI_AVATAR_PATH = "Assets/Prefabs/AI avatar.prefab";
    const string COWBOY_PATH = "Assets/VertexModeler/CowboyRIO/Prefab/CowboyRIO_Normal.prefab";
    const string COWBOY_FBX = "Assets/VertexModeler/CowboyRIO/Mesh/CowboyRio_Unity.fbx";
    const string IDLE_FBX = "Assets/Animations/StopOneHand1.fbx";
    const string LISTENING_FBX = "Assets/Animations/NodYes1.fbx";

    [MenuItem("VRLingo/Setup AI Character Animation")]
    public static void Setup()
    {
        EnsureHumanoid(IDLE_FBX);
        EnsureHumanoid(LISTENING_FBX);

        var idle = AssetDatabase.LoadAssetAtPath<AnimationClip>(IDLE_FBX);
        var listening = AssetDatabase.LoadAssetAtPath<AnimationClip>(LISTENING_FBX);

        if (idle == null || listening == null)
        {
            Debug.LogError($"[AISetup] Missing clip(s): idle={idle}, listening={listening}. Check Assets/Animations/.");
            return;
        }

        var cowboyAvatar = AssetDatabase.LoadAllAssetsAtPath(COWBOY_FBX)
            .OfType<Avatar>().FirstOrDefault();
        if (cowboyAvatar == null)
        {
            Debug.LogError($"[AISetup] No humanoid Avatar on {COWBOY_FBX}. Select the FBX → Rig → Animation Type = Humanoid → Apply.");
            return;
        }

        var root = PrefabUtility.LoadPrefabContents(AI_AVATAR_PATH);
        try
        {
            var cowboyPrefabAsset = AssetDatabase.LoadAssetAtPath<GameObject>(COWBOY_PATH);

            // Find all direct children that are instances of CowboyRIO_Normal (by prefab source, not name).
            var existingInstances = new System.Collections.Generic.List<GameObject>();
            foreach (Transform child in root.transform)
            {
                var source = PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject);
                if (source == cowboyPrefabAsset) existingInstances.Add(child.gameObject);
            }

            GameObject character;
            if (existingInstances.Count == 0)
            {
                character = (GameObject)PrefabUtility.InstantiatePrefab(cowboyPrefabAsset, root.transform);
                character.transform.localPosition = Vector3.zero;
                character.transform.localRotation = Quaternion.identity;
            }
            else
            {
                character = existingInstances[0];
                for (int i = 1; i < existingInstances.Count; i++)
                    Object.DestroyImmediate(existingInstances[i], true);
            }
            character.name = "Character";

            // The Animator ships inside the nested FBX — find it, don't add a new one on the root.
            var animator = character.GetComponentInChildren<Animator>(true);
            if (animator == null)
            {
                Debug.LogError($"[AISetup] No Animator found in Character. Check that {COWBOY_FBX} imports as Humanoid.");
                return;
            }
            animator.avatar = cowboyAvatar;
            animator.applyRootMotion = false;
            animator.runtimeAnimatorController = null;

            // Put AICharacterAnimator on the SAME GameObject as the Animator.
            var targetGo = animator.gameObject;
            var anim = targetGo.GetComponent<AICharacterAnimator>() ?? targetGo.AddComponent<AICharacterAnimator>();
            var so = new SerializedObject(anim);
            so.FindProperty("idleClip").objectReferenceValue = idle;
            so.FindProperty("listeningClip").objectReferenceValue = listening;
            so.ApplyModifiedProperties();

            // Clean up: if a stray Animator was added earlier on the root, remove it.
            if (character != targetGo)
            {
                var strayAnimator = character.GetComponent<Animator>();
                if (strayAnimator != null) Object.DestroyImmediate(strayAnimator, true);
                var strayAnim = character.GetComponent<AICharacterAnimator>();
                if (strayAnim != null) Object.DestroyImmediate(strayAnim, true);
            }

            PrefabUtility.SaveAsPrefabAsset(root, AI_AVATAR_PATH);
            Debug.Log("[AISetup] AI avatar wired with Character + Animator + clips.");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static void EnsureHumanoid(string fbxPath)
    {
        var importer = AssetImporter.GetAtPath(fbxPath) as ModelImporter;
        if (importer == null) { Debug.LogWarning($"[AISetup] No ModelImporter for {fbxPath}"); return; }
        bool changed = false;
        if (importer.animationType != ModelImporterAnimationType.Human)
        {
            importer.animationType = ModelImporterAnimationType.Human;
            changed = true;
        }
        if (!importer.autoGenerateAvatarMappingIfUnspecified)
        {
            importer.autoGenerateAvatarMappingIfUnspecified = true;
            changed = true;
        }
        if (changed)
        {
            importer.SaveAndReimport();
            Debug.Log($"[AISetup] Re-imported {fbxPath} as Humanoid.");
        }
    }
}
