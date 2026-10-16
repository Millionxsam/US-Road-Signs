using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using USRoadSigns.Geometry;

namespace SignForge.Preview
{
    /// <summary>
    /// Writes an <see cref="AssemblyMesh"/> as Wavefront OBJ/MTL for previews in Blender.
    /// Unity/CS2 space is left-handed; OBJ is right-handed, so X is mirrored and triangle winding reversed.
    /// </summary>
    public static class ObjExporter
    {
        public static void Export(AssemblyMesh mesh, string objPath, string contentFolder, float offsetX = 0f, bool append = false, StringBuilder mtl = null, HashSet<string> writtenMaterials = null)
        {
            var obj = new StringBuilder();
            CultureInfo ci = CultureInfo.InvariantCulture;
            string mtlName = Path.GetFileNameWithoutExtension(objPath) + ".mtl";
            obj.AppendLine("mtllib " + mtlName);

            int vertexBase = 1;
            int partIndex = 0;
            mtl = mtl ?? new StringBuilder();
            writtenMaterials = writtenMaterials ?? new HashSet<string>();
            foreach (MeshPart part in mesh.Parts)
            {
                string material = MaterialName(part);
                obj.AppendLine("o part" + partIndex++ + "_" + material);
                foreach (Vec3 p in part.Positions)
                    obj.AppendLine(string.Format(ci, "v {0:F5} {1:F5} {2:F5}", -(p.X + offsetX), p.Y, p.Z));
                foreach (Vec2 uv in part.UVs)
                    obj.AppendLine(string.Format(ci, "vt {0:F5} {1:F5}", uv.X, uv.Y));
                foreach (Vec3 n in part.Normals)
                    obj.AppendLine(string.Format(ci, "vn {0:F4} {1:F4} {2:F4}", -n.X, n.Y, n.Z));
                obj.AppendLine("usemtl " + material);
                for (int i = 0; i < part.Indices.Count; i += 3)
                {
                    int a = part.Indices[i] + vertexBase;
                    int b = part.Indices[i + 2] + vertexBase;
                    int c = part.Indices[i + 1] + vertexBase;
                    obj.AppendLine(string.Format(ci, "f {0}/{0}/{0} {1}/{1}/{1} {2}/{2}/{2}", a, b, c));
                }
                vertexBase += part.VertexCount;

                if (writtenMaterials.Add(material))
                {
                    mtl.AppendLine("newmtl " + material);
                    mtl.AppendLine("Kd 1 1 1");
                    string texture = TexturePath(part, contentFolder);
                    if (texture != null)
                        mtl.AppendLine("map_Kd " + texture.Replace('\\', '/'));
                    mtl.AppendLine();
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(objPath)));
            File.WriteAllText(objPath, obj.ToString());
            File.WriteAllText(Path.Combine(Path.GetDirectoryName(Path.GetFullPath(objPath)), mtlName), mtl.ToString());
        }

        public static string MaterialName(MeshPart part)
        {
            return part.Slot == MaterialSlot.Face ? "Face_" + part.FaceId : part.Slot.ToString();
        }

        private static string TexturePath(MeshPart part, string contentFolder)
        {
            switch (part.Slot)
            {
                case MaterialSlot.Face:
                    return Path.GetFullPath(Path.Combine(contentFolder, "Textures", "Faces", part.FaceId + ".png"));
                case MaterialSlot.Galvanized:
                    return Path.GetFullPath(Path.Combine(contentFolder, "Textures", "Materials", "Galvanized_BaseColor.png"));
                case MaterialSlot.Perforated:
                    return Path.GetFullPath(Path.Combine(contentFolder, "Textures", "Materials", "Perforated_BaseColor.png"));
                case MaterialSlot.Aluminum:
                    return Path.GetFullPath(Path.Combine(contentFolder, "Textures", "Materials", "Aluminum_BaseColor.png"));
                case MaterialSlot.DarkPlastic:
                    return Path.GetFullPath(Path.Combine(contentFolder, "Textures", "Materials", "DarkPlastic_BaseColor.png"));
                default:
                    return null;
            }
        }
    }
}
