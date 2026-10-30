namespace GUI.Linux.Types.Exporter;

/// <summary>What a single-file glTF export should include.</summary>
/// <param name="Animations">Whether the model's animations are exported.</param>
/// <param name="Materials">Whether materials, and with them textures, are exported.</param>
/// <param name="ConvertTextures">Whether textures are converted to image files instead of copied as-is.</param>
internal readonly record struct GltfExportOptions(bool Animations, bool Materials, bool ConvertTextures);
