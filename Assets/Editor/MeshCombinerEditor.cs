using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(MeshCombiner))]
public class MeshCombinerEditor : Editor
{
    private const int LOD_LEVELS = 4;

    private class SourceObject
    {
        public Transform root;
        public LODGroup lodGroup;
        public Renderer[] rendererPerLevel = new Renderer[LOD_LEVELS];
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        MeshCombiner combiner = (MeshCombiner)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Combine Meshes Same Material (Editor)"))
        {
            CombineMeshes(combiner);
        }
    }

    private void CombineMeshes(MeshCombiner combiner)
    {
        Transform root = combiner.transform;

        List<SourceObject> sources = new List<SourceObject>();
        Material sharedMaterial = null;

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            LODGroup lodGroup = child.GetComponent<LODGroup>();

            if (lodGroup == null)
            {
                Debug.LogWarning($"MeshCombiner: '{child.name}' doesn't have LODGroup, omitted.", child);
                continue;
            }

            LOD[] lods = lodGroup.GetLODs();
            if (lods.Length < 1)
            {
                Debug.LogWarning($"MeshCombiner: '{child.name}' has a LODGroup without levels, omitted.", child);
                continue;
            }

            SourceObject src = new SourceObject { root = child, lodGroup = lodGroup };

            for (int level = 0; level < LOD_LEVELS && level < lods.Length; level++)
            {
                if (lods[level].renderers.Length == 0) continue;
                Renderer rnd = lods[level].renderers[0];
                src.rendererPerLevel[level] = rnd;

                if (rnd != null)
                {
                    if (sharedMaterial == null)
                    {
                        sharedMaterial = rnd.sharedMaterial;
                    }
                    else if (rnd.sharedMaterial != sharedMaterial)
                    {
                        Debug.LogWarning($"MeshCombiner: '{child.name}' has a different material, omitted", child);
                    }
                }
            }

            sources.Add(src);
        }

        if (sources.Count == 0)
        {
            Debug.LogWarning("MeshCombiner: valid objects not found (LODGroup must be 4 levels).", combiner);
            return;
        }

        string folderPath = "Assets/CombinedMeshes";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets", "CombinedMeshes");
        }

        Renderer[] combinedRenderers = new Renderer[LOD_LEVELS];

        for (int level = 0; level < LOD_LEVELS; level++)
        {
            List<CombineInstance> combineInstances = new List<CombineInstance>();
            int totalVertexCount = 0;

            foreach (SourceObject src in sources)
            {
                Renderer levelRenderer = src.rendererPerLevel[level];
                if (levelRenderer == null) continue;

                MeshFilter mf = levelRenderer.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;

                CombineInstance ci = new CombineInstance
                {
                    mesh = mf.sharedMesh,
                    transform = root.worldToLocalMatrix * mf.transform.localToWorldMatrix
                };
                combineInstances.Add(ci);
                totalVertexCount += mf.sharedMesh.vertexCount;
            }

            if (combineInstances.Count == 0) continue;

            Mesh combinedMesh = new Mesh();
            combinedMesh.name = $"{root.name}_Combined_LOD{level}";

            if (combiner.Use32BitIndices || totalVertexCount > 65000)
            {
                combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true);
            combinedMesh.RecalculateBounds();

            string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{combinedMesh.name}.asset");
            AssetDatabase.CreateAsset(combinedMesh, assetPath);

            GameObject lodObject = new GameObject($"Combined_LOD{level}");
            lodObject.transform.SetParent(root, false);
            Undo.RegisterCreatedObjectUndo(lodObject, "Combine Meshes");

            MeshFilter newMf = lodObject.AddComponent<MeshFilter>();
            MeshRenderer newMr = lodObject.AddComponent<MeshRenderer>();
            newMf.sharedMesh = combinedMesh;
            newMr.sharedMaterial = sharedMaterial;

            combinedRenderers[level] = newMr;
        }

        // Build new LODGroup
        LOD[] sourceLods = sources[0].lodGroup.GetLODs();
        LOD[] newLods = new LOD[LOD_LEVELS];
        for (int level = 0; level < LOD_LEVELS; level++)
        {
            if (combinedRenderers[level] == null) continue;

            float threshold = level < sourceLods.Length
                ? sourceLods[level].screenRelativeTransitionHeight
                : 0.01f;

            newLods[level] = new LOD(threshold, new Renderer[] { combinedRenderers[level] });
        }

        Undo.RecordObject(combiner.gameObject, "Combine Meshes");

        LODGroup newLodGroup = combiner.GetComponent<LODGroup>();
        if (newLodGroup == null) newLodGroup = Undo.AddComponent<LODGroup>(combiner.gameObject);
        
        newLodGroup.SetLODs(newLods);
        newLodGroup.RecalculateBounds();

        // Optional: remove old MeshFilter and MeshRenderer if they still exist on root
        MeshFilter oldMf = combiner.GetComponent<MeshFilter>();
        if (oldMf != null) Undo.DestroyObjectImmediate(oldMf);
        MeshRenderer oldMr = combiner.GetComponent<MeshRenderer>();
        if (oldMr != null) Undo.DestroyObjectImmediate(oldMr);

        if (combiner.DestroyOriginals)
        {
            foreach (SourceObject src in sources)
            {
                Undo.DestroyObjectImmediate(src.root.gameObject);
            }
        }

        AssetDatabase.SaveAssets();
        EditorUtility.SetDirty(combiner.gameObject);
        Debug.Log($"MeshCombiner: combined mesh with LODs saved in '{folderPath}'.", combiner);
    }
}