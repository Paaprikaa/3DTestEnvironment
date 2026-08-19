using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

[CustomEditor(typeof(MeshCombiner))]
public class MeshCombinerEditor : Editor
{
    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        MeshCombiner combiner = (MeshCombiner)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Combine Meshes (Editor)"))
        {
            CombineMeshes(combiner);
        }
    }

    private void CombineMeshes(MeshCombiner combiner)
    {
        Transform root = combiner.transform;
        MeshFilter[] meshFilters = root.GetComponentsInChildren<MeshFilter>();

        List<CombineInstance> combineInstances = new List<CombineInstance>();
        Material sharedMaterial = null;
        int totalVertexCount = 0;

        foreach (MeshFilter mf in meshFilters)
        {
            if (mf.transform == root) continue;
            if (mf.sharedMesh == null) continue;

            MeshRenderer mr = mf.GetComponent<MeshRenderer>();
            if (mr == null) continue;

            if (sharedMaterial == null)
            {
                sharedMaterial = mr.sharedMaterial;
            }
            else if (mr.sharedMaterial != sharedMaterial)
            {
                Debug.LogWarning($"MeshCombiner: '{mf.name}' has a different material, omitted", mf);
                continue;
            }

            CombineInstance ci = new CombineInstance
            {
                mesh = mf.sharedMesh,
                transform = root.worldToLocalMatrix * mf.transform.localToWorldMatrix
            };
            combineInstances.Add(ci);
            totalVertexCount += mf.sharedMesh.vertexCount;
        }

        if (combineInstances.Count == 0)
        {
            Debug.LogWarning("MeshCombiner: valid meshes not found.", combiner);
            return;
        }

        Mesh combinedMesh = new Mesh();
        combinedMesh.name = root.name + "_Combined";

        if (combiner.Use32BitIndices || totalVertexCount > 65000)
        {
            combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
        }

        // true, true = combines in one submesh and uses matrices to transform each mesh
        combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true);
        combinedMesh.RecalculateBounds();

        // Saves the combined mesh as a new asset
        string folderPath = "Assets/CombinedMeshes";
        if (!AssetDatabase.IsValidFolder(folderPath))
        {
            AssetDatabase.CreateFolder("Assets", "CombinedMeshes");
        }

        string assetPath = AssetDatabase.GenerateUniqueAssetPath($"{folderPath}/{combinedMesh.name}.asset");
        AssetDatabase.CreateAsset(combinedMesh, assetPath);
        AssetDatabase.SaveAssets();

        Undo.RecordObject(combiner.GetComponent<MeshFilter>(), "Combine Meshes");
        combiner.GetComponent<MeshFilter>().sharedMesh = combinedMesh;

        Undo.RecordObject(combiner.GetComponent<MeshRenderer>(), "Combine Meshes");
        combiner.GetComponent<MeshRenderer>().sharedMaterial = sharedMaterial;

        if (combiner.DestroyOriginals)
        {
            // Reversed order to not breach hierarchy
            for (int i = meshFilters.Length - 1; i >= 0; i--)
            {
                if (meshFilters[i].transform == root) continue;
                Undo.DestroyObjectImmediate(meshFilters[i].gameObject);
            }
        }

        EditorUtility.SetDirty(combiner.gameObject); // updates gameobject state in editor
        Debug.Log($"MeshCombiner: combined mesh saved in '{assetPath}' ({totalVertexCount} vertices, {combineInstances.Count} objects merged).", combiner);
    }
}