using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(MeshAtlasCombiner))]
public class MeshAtlasCombinerEditor : Editor
{
    private const int LOD_LEVELS = 4;

    // Childen object data
    private class SourceObject
    {
        public Transform root;
        public LODGroup lodGroup;
        public Renderer[] rendererPerLevel = new Renderer[LOD_LEVELS];
        public Material material;
        public Texture2D baseColor;
        public Texture2D normalMap;
        public Texture2D maskMap;

        // UV Rectangle assigned on atlas (after packing)
        public Vector2 uvScale;
        public Vector2 uvOffset;

        // Key identifying the unique texture set (for deduplication)
        public string textureKey;
    }

    public override void OnInspectorGUI()
    {
        DrawDefaultInspector();

        MeshAtlasCombiner combiner = (MeshAtlasCombiner)target;

        EditorGUILayout.Space();
        if (GUILayout.Button("Combine Meshes (Atlas + LOD)"))
        {
            Combine(combiner);
        }
    }

    private void Combine(MeshAtlasCombiner combiner)
    {
        Transform root = combiner.transform;

        List<SourceObject> sources = CollectSources(root);
        if (sources.Count == 0)
        {
            Debug.LogWarning("MeshAtlasCombiner: valid objects not found (LODGroup must be 4 levels).", combiner);
            return;
        }

        if (!Mathf.IsPowerOfTwo(combiner.atlasSize))
        {
            Debug.LogWarning($"MeshAtlasCombiner: atlasSize ({combiner.atlasSize}) is not power of 2.", combiner);
        }

        EnsureFolder(combiner.outputFolder);

        // Deduplicate texture sets: group sources that share the same 3 textures
        // so they occupy a single cell in the atlas instead of duplicating pixels.
        List<SourceObject> uniqueTextureSources = new List<SourceObject>();
        Dictionary<string, int> textureKeyToSlot = new Dictionary<string, int>();

        foreach (SourceObject src in sources)
        {
            if (!textureKeyToSlot.ContainsKey(src.textureKey))
            {
                textureKeyToSlot[src.textureKey] = uniqueTextureSources.Count;
                uniqueTextureSources.Add(src);
            }
        }

        int uniqueCount = uniqueTextureSources.Count;
        int deduplicatedCount = sources.Count - uniqueCount;
        if (deduplicatedCount > 0)
        {
            Debug.Log($"MeshAtlasCombiner: {sources.Count} sources share {uniqueCount} unique texture sets ({deduplicatedCount} deduplicated).", combiner);
        }

        // Calculate atlas grid based on unique texture count (not total source count)
        int gridDim = Mathf.CeilToInt(Mathf.Sqrt(uniqueCount));
        int cellPixel = combiner.atlasSize / gridDim;
        int contentPixel = cellPixel - combiner.paddingPixels * 2;

        if (contentPixel <= 0)
        {
            Debug.LogError("MeshAtlasCombiner: padding is too big. Reduce paddingPixels or increase atlasSize.", combiner);
            return;
        }

        // Assign UV rects to unique texture slots
        Vector2[] slotUvScale = new Vector2[uniqueCount];
        Vector2[] slotUvOffset = new Vector2[uniqueCount];

        for (int i = 0; i < uniqueCount; i++)
        {
            int col = i % gridDim;
            int row = i / gridDim;

            float uvCellSize = 1f / gridDim;
            float paddingUv = (float)combiner.paddingPixels / combiner.atlasSize;

            slotUvScale[i] = new Vector2(uvCellSize - paddingUv * 2f, uvCellSize - paddingUv * 2f);
            slotUvOffset[i] = new Vector2(col * uvCellSize + paddingUv, row * uvCellSize + paddingUv);
        }

        // Map every source to its slot's UV rect (duplicates share the same slot)
        foreach (SourceObject src in sources)
        {
            int slot = textureKeyToSlot[src.textureKey];
            src.uvScale = slotUvScale[slot];
            src.uvOffset = slotUvOffset[slot];
        }

        // Build atlas using only the unique texture set list, and save them
        Texture2D baseColorAtlas = BuildAtlas(uniqueTextureSources, s => s.baseColor, Color.white, combiner.atlasSize, gridDim, cellPixel, combiner.paddingPixels, linear: false, isNormalMap: false);
        Texture2D normalMapAtlas = BuildAtlas(uniqueTextureSources, s => s.normalMap, new Color(0.5f, 0.5f, 1f, 1f), combiner.atlasSize, gridDim, cellPixel, combiner.paddingPixels, linear: true, isNormalMap: true);
        Texture2D maskMapAtlas = BuildAtlas(uniqueTextureSources, s => s.maskMap, Color.white, combiner.atlasSize, gridDim, cellPixel, combiner.paddingPixels, linear: true, isNormalMap: false);

        Texture2D savedBaseColor = SaveTextureAsset(baseColorAtlas, combiner.outputFolder, root.name + "_Atlas_BaseColor", isNormalMap: false, isLinear: false);
        Texture2D savedNormalMap = SaveTextureAsset(normalMapAtlas, combiner.outputFolder, root.name + "_Atlas_Normal", isNormalMap: true, isLinear: true);
        Texture2D savedMaskMap = SaveTextureAsset(maskMapAtlas, combiner.outputFolder, root.name + "_Atlas_Mask", isNormalMap: false, isLinear: true);

        // Create new material
        Material combinedMaterial = new Material(sources[0].material.shader);
        combinedMaterial.name = root.name + "_CombinedMat";
        if (combinedMaterial.HasProperty(combiner.baseColorProperty)) combinedMaterial.SetTexture(combiner.baseColorProperty, savedBaseColor);
        if (combinedMaterial.HasProperty(combiner.normalMapProperty)) combinedMaterial.SetTexture(combiner.normalMapProperty, savedNormalMap);
        if (combinedMaterial.HasProperty(combiner.maskMapProperty)) combinedMaterial.SetTexture(combiner.maskMapProperty, savedMaskMap);

        string matPath = AssetDatabase.GenerateUniqueAssetPath($"{combiner.outputFolder}/{combinedMaterial.name}.mat");
        AssetDatabase.CreateAsset(combinedMaterial, matPath);

        // Combine geometry by LOD level
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

                Mesh meshCopy = Object.Instantiate(mf.sharedMesh);
                RemapUVs(meshCopy, src.uvScale, src.uvOffset);

                CombineInstance ci = new CombineInstance
                {
                    mesh = meshCopy,
                    transform = root.worldToLocalMatrix * mf.transform.localToWorldMatrix
                };
                combineInstances.Add(ci);
                totalVertexCount += meshCopy.vertexCount;
            }

            if (combineInstances.Count == 0) continue;

            Mesh combinedMesh = new Mesh();
            combinedMesh.name = $"{root.name}_Combined_LOD{level}";

            if (combiner.use32BitIndices || totalVertexCount > 65000)
            {
                combinedMesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            }

            combinedMesh.CombineMeshes(combineInstances.ToArray(), true, true);
            combinedMesh.RecalculateBounds();

            string meshPath = AssetDatabase.GenerateUniqueAssetPath($"{combiner.outputFolder}/{combinedMesh.name}.asset");
            AssetDatabase.CreateAsset(combinedMesh, meshPath);

            GameObject lodObject = new GameObject($"Combined_LOD{level}");
            lodObject.transform.SetParent(root, false);
            MeshFilter newMf = lodObject.AddComponent<MeshFilter>();
            MeshRenderer newMr = lodObject.AddComponent<MeshRenderer>();
            newMf.sharedMesh = combinedMesh;
            newMr.sharedMaterial = combinedMaterial;

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

        LODGroup newLodGroup = combiner.GetComponent<LODGroup>();
        if (newLodGroup == null) newLodGroup = combiner.gameObject.AddComponent<LODGroup>();
        newLodGroup.SetLODs(newLods);
        newLodGroup.RecalculateBounds();

        // Destroy originals
        if (combiner.destroyOriginals)
        {
            foreach (SourceObject src in sources)
            {
                Object.DestroyImmediate(src.root.gameObject);
            }
        }

        AssetDatabase.SaveAssets();
        EditorUtility.SetDirty(combiner.gameObject);

        Debug.Log($"MeshAtlasCombiner: {sources.Count} objects combined and saved in {combiner.outputFolder}. Atlas {combiner.atlasSize}x{combiner.atlasSize}, grid {gridDim}x{gridDim} ({uniqueCount} unique texture sets).", combiner);
    }

    private List<SourceObject> CollectSources(Transform root)
    {
        List<SourceObject> result = new List<SourceObject>();

        for (int i = 0; i < root.childCount; i++)
        {
            Transform child = root.GetChild(i);
            LODGroup lodGroup = child.GetComponent<LODGroup>();

            if (lodGroup == null)
            {
                Debug.LogWarning($"MeshAtlasCombiner: '{child.name}' doesn't have LODGroup, omitted.", child);
                continue;
            }

            LOD[] lods = lodGroup.GetLODs();
            if (lods.Length < 1)
            {
                Debug.LogWarning($"MeshAtlasCombiner: '{child.name}' has a LODGroup without levels, omitted.", child);
                continue;
            }

            SourceObject src = new SourceObject { root = child, lodGroup = lodGroup };

            for (int level = 0; level < LOD_LEVELS && level < lods.Length; level++)
            {
                if (lods[level].renderers.Length == 0) continue;
                src.rendererPerLevel[level] = lods[level].renderers[0];
            }

            
            Renderer representative = System.Array.Find(src.rendererPerLevel, r => r != null);
            if (representative == null)
            {
                Debug.LogWarning($"MeshAtlasCombiner: '{child.name}' invalid renderers in LODGroup, omitted.", child);
                continue;
            }

            src.material = representative.sharedMaterial;
            if (src.material == null)
            {
                Debug.LogWarning($"MeshAtlasCombiner: '{child.name}' doesn't have a material assigned, omitted.", child);
                continue;
            }

            MeshAtlasCombiner combiner = root.GetComponent<MeshAtlasCombiner>();
            src.baseColor = src.material.GetTexture(combiner.baseColorProperty) as Texture2D;
            src.normalMap = src.material.GetTexture(combiner.normalMapProperty) as Texture2D;
            src.maskMap = src.material.GetTexture(combiner.maskMapProperty) as Texture2D;

            // Build a key from the instance IDs of all 3 textures for deduplication
            int baseId = src.baseColor != null ? src.baseColor.GetInstanceID() : 0;
            int normalId = src.normalMap != null ? src.normalMap.GetInstanceID() : 0;
            int maskId = src.maskMap != null ? src.maskMap.GetInstanceID() : 0;
            src.textureKey = $"{baseId}_{normalId}_{maskId}";

            result.Add(src);
        }

        return result;
    }

   
    private Texture2D BuildAtlas(List<SourceObject> sources, System.Func<SourceObject, Texture2D> selector, Color fallback, int atlasSize, int gridDim, int cellPixel, int paddingPixels, bool linear, bool isNormalMap)
    {
        Texture2D atlas = new Texture2D(atlasSize, atlasSize, TextureFormat.RGBA32, true);

        Color[] fill = new Color[atlasSize * atlasSize];
        for (int i = 0; i < fill.Length; i++) fill[i] = fallback;
        atlas.SetPixels(fill);

        int contentPixel = cellPixel - paddingPixels * 2;

        for (int i = 0; i < sources.Count; i++)
        {
            Texture2D sourceTex = selector(sources[i]);
            int col = i % gridDim;
            int row = i / gridDim;
            int x = col * cellPixel + paddingPixels;
            int y = row * cellPixel + paddingPixels;

            if (sourceTex == null)
            {
                Color[] cell = new Color[contentPixel * contentPixel];
                for (int p = 0; p < cell.Length; p++) cell[p] = fallback;
                atlas.SetPixels(x, y, contentPixel, contentPixel, cell);
                continue;
            }

            Texture2D resized = ResizeTextureGPU(sourceTex, contentPixel, contentPixel, linear, isNormalMap);
            atlas.SetPixels(x, y, contentPixel, contentPixel, resized.GetPixels());
            Object.DestroyImmediate(resized);
        }

        atlas.Apply();
        return atlas;
    }

    // Resize texture using GPU (Blit + ReadPixels).
    // For normal maps, Unity stores textures internally as DXT5nm/BC5 with swizzled
    // channels (X in Alpha, Y in Green, R and B are filler ~1.0). After blitting,
    // we reconstruct standard tangent-space RGB normals from the packed AG channels.
    private Texture2D ResizeTextureGPU(Texture2D source, int width, int height, bool linear, bool isNormalMap)
    {
        RenderTextureReadWrite readWrite = linear ? RenderTextureReadWrite.Linear : RenderTextureReadWrite.sRGB;
        RenderTexture rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, readWrite);
        Graphics.Blit(source, rt);

        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D result = new Texture2D(width, height, TextureFormat.RGBA32, false);
        result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        result.Apply();

        RenderTexture.active = previous;
        RenderTexture.ReleaseTemporary(rt);

        // Reconstruct standard RGB normals from DXT5nm packed channels.
        // DXT5nm stores: Alpha = X component, Green = Y component.
        // We derive Z = sqrt(1 - x² - y²) and re-encode to RGB [0,1] range.
        // The saved atlas PNG will be re-imported as NormalMap, so Unity will
        // re-encode it to DXT5nm and the shader's UnpackNormal will decode it.
        if (isNormalMap)
        {
            Color[] pixels = result.GetPixels();
            for (int p = 0; p < pixels.Length; p++)
            {
                float nx = pixels[p].a * 2f - 1f;
                float ny = pixels[p].g * 2f - 1f;
                float nz = Mathf.Sqrt(Mathf.Max(0f, 1f - nx * nx - ny * ny));

                pixels[p] = new Color(
                    nx * 0.5f + 0.5f,
                    ny * 0.5f + 0.5f,
                    nz * 0.5f + 0.5f,
                    1f
                );
            }
            result.SetPixels(pixels);
            result.Apply();
        }

        return result;
    }

    private void RemapUVs(Mesh mesh, Vector2 uvScale, Vector2 uvOffset)
    {
        Vector2[] uvs = mesh.uv;
        for (int i = 0; i < uvs.Length; i++)
        {
            // Wrap UVs into [0,1] range to handle tiling/wrapping textures.
            // Without this, a UV of e.g. 1.5 would be remapped outside the
            // atlas cell and sample from a neighboring texture's region.
            float u = uvs[i].x % 1f;
            float v = uvs[i].y % 1f;
            if (u < 0f) u += 1f;
            if (v < 0f) v += 1f;

            uvs[i] = new Vector2(
                u * uvScale.x + uvOffset.x,
                v * uvScale.y + uvOffset.y
            );
        }
        mesh.uv = uvs;
    }

    private Texture2D SaveTextureAsset(Texture2D texture, string folder, string name, bool isNormalMap, bool isLinear)
    {
        byte[] pngData = texture.EncodeToPNG();
        string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{name}.png");
        File.WriteAllBytes(path, pngData);
        AssetDatabase.ImportAsset(path);

        TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
        if (importer != null)
        {
            importer.textureType = isNormalMap ? TextureImporterType.NormalMap : TextureImporterType.Default;
            importer.sRGBTexture = !isLinear;
            importer.SaveAndReimport();
        }

        return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
    }

    private void EnsureFolder(string folder)
    {
        if (AssetDatabase.IsValidFolder(folder)) return;

        string[] parts = folder.Split('/');
        string current = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = current + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next))
            {
                AssetDatabase.CreateFolder(current, parts[i]);
            }
            current = next;
        }
    }
}

