using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DMI.Xenoarch.EditorTools.UE4Convert
{
    /// <summary>
    /// Converts UModel's static-mesh ActorX files (.pskx / .psk without a skeleton) into plain OBJ + MTL so Unity can import
    /// them with no extra package. Output is already in Unity space and metres, with a first line marker
    /// "# xenoarch-psk metres" that the planner uses to skip its centimetre guess.
    /// Skeletal PSK (bones/weights) and PSA animations are NOT converted.
    /// </summary>
    public static class PskToObj
    {
        public const string Marker = "# xenoarch-psk metres";

        public sealed class Mesh
        {
            public float[] points;                 // xyz per point, UE space (cm)
            public int[] wedgePoint; public float[] wedgeU, wedgeV;
            public int[] faceW0, faceW1, faceW2, faceMat; public uint[] faceSmooth;
            public List<string> materials = new List<string>();
            public int bones, weights;
            public bool IsSkeletal => bones > 0 || weights > 0;
        }

        public sealed class Result { public int converted, skippedSkeletal, failed, tris; public List<string> errors = new List<string>(), skeletal = new List<string>(); }

        public static Mesh Parse(string path)
        {
            using (var br = new BinaryReader(File.OpenRead(path)))
            {
                var m = new Mesh();
                long len = br.BaseStream.Length;
                while (br.BaseStream.Position + 32 <= len)
                {
                    string id = Encoding.ASCII.GetString(br.ReadBytes(20)).TrimEnd('\0');
                    br.ReadInt32(); int size = br.ReadInt32(), count = br.ReadInt32();
                    long start = br.BaseStream.Position, end = start + (long)size * count;
                    if (size < 0 || count < 0 || end > len) throw new InvalidDataException("bad chunk " + id);
                    if (id == "PNTS0000")
                    {
                        m.points = new float[count * 3];
                        for (int i = 0; i < m.points.Length; i++) m.points[i] = br.ReadSingle();
                    }
                    else if (id == "VTXW0000" || id == "VTXW3200")
                    {
                        m.wedgePoint = new int[count]; m.wedgeU = new float[count]; m.wedgeV = new float[count];
                        for (int i = 0; i < count; i++)
                        {
                            br.BaseStream.Position = start + (long)i * size;
                            if (id == "VTXW0000") { m.wedgePoint[i] = br.ReadUInt16(); br.ReadUInt16(); } else m.wedgePoint[i] = br.ReadInt32();
                            m.wedgeU[i] = br.ReadSingle(); m.wedgeV[i] = br.ReadSingle();
                        }
                    }
                    else if (id == "FACE0000" || id == "FACE3200")
                    {
                        m.faceW0 = new int[count]; m.faceW1 = new int[count]; m.faceW2 = new int[count]; m.faceMat = new int[count]; m.faceSmooth = new uint[count];
                        for (int i = 0; i < count; i++)
                        {
                            br.BaseStream.Position = start + (long)i * size;
                            if (id == "FACE0000") { m.faceW0[i] = br.ReadUInt16(); m.faceW1[i] = br.ReadUInt16(); m.faceW2[i] = br.ReadUInt16(); }
                            else { m.faceW0[i] = br.ReadInt32(); m.faceW1[i] = br.ReadInt32(); m.faceW2[i] = br.ReadInt32(); }
                            m.faceMat[i] = br.ReadByte(); br.ReadByte(); m.faceSmooth[i] = br.ReadUInt32();
                        }
                    }
                    else if (id == "MATT0000")
                    {
                        for (int i = 0; i < count; i++)
                        {
                            br.BaseStream.Position = start + (long)i * size;
                            m.materials.Add(Encoding.ASCII.GetString(br.ReadBytes(64)).TrimEnd('\0'));
                        }
                    }
                    else if (id == "REFSKELT" || id == "REFSKEL0") m.bones = count;
                    else if (id == "RAWWEIGHTS") m.weights = count;
                    br.BaseStream.Position = end;
                }
                if (m.points == null || m.wedgePoint == null || m.faceW0 == null) throw new InvalidDataException("missing geometry chunks");
                return m;
            }
        }

        static float Inv(float v) => v;

        /// <summary>Writes OBJ/MTL text. UE (X fwd, Y right, Z up, cm, left-handed) -> Unity (x=Y, y=Z, z=X, metres); OBJ x is negated because Unity's importer mirrors it back.</summary>
        public static string ToObj(Mesh m, string mtlName, out int tris)
        {
            int np = m.points.Length / 3, nf = m.faceW0.Length;
            var P = new float[np * 3];
            for (int i = 0; i < np; i++) { float x = m.points[i * 3], y = m.points[i * 3 + 1], z = m.points[i * 3 + 2]; P[i * 3] = y * 0.01f; P[i * 3 + 1] = z * 0.01f; P[i * 3 + 2] = x * 0.01f; }

            int[][] tri = new int[nf][];
            for (int f = 0; f < nf; f++) tri[f] = new[] { m.wedgePoint[m.faceW0[f]], m.wedgePoint[m.faceW1[f]], m.wedgePoint[m.faceW2[f]] };

            // winding vote: front faces (clockwise in Unity) must point away from the mesh centre
            double cx = 0, cy = 0, cz = 0; for (int i = 0; i < np; i++) { cx += P[i * 3]; cy += P[i * 3 + 1]; cz += P[i * 3 + 2]; }
            cx /= Math.Max(1, np); cy /= Math.Max(1, np); cz /= Math.Max(1, np);
            double vote = 0;
            for (int f = 0; f < nf; f++)
            {
                Cross(P, tri[f][0], tri[f][1], tri[f][2], out double nx, out double ny, out double nz);
                double mx = (P[tri[f][0] * 3] + P[tri[f][1] * 3] + P[tri[f][2] * 3]) / 3 - cx, my = (P[tri[f][0] * 3 + 1] + P[tri[f][1] * 3 + 1] + P[tri[f][2] * 3 + 1]) / 3 - cy, mz = (P[tri[f][0] * 3 + 2] + P[tri[f][1] * 3 + 2] + P[tri[f][2] * 3 + 2]) / 3 - cz;
                vote += nx * mx + ny * my + nz * mz;
            }
            bool flip = vote < 0;
            var wd = new int[nf][];
            for (int f = 0; f < nf; f++)
            {
                if (flip) { tri[f] = new[] { tri[f][0], tri[f][2], tri[f][1] }; wd[f] = new[] { m.faceW0[f], m.faceW2[f], m.faceW1[f] }; }
                else wd[f] = new[] { m.faceW0[f], m.faceW1[f], m.faceW2[f] };
            }

            // smoothing-group normals (area weighted), per face corner
            var fn = new double[nf * 3];
            var byPoint = new List<int>[np];
            for (int f = 0; f < nf; f++)
            {
                Cross(P, tri[f][0], tri[f][1], tri[f][2], out fn[f * 3], out fn[f * 3 + 1], out fn[f * 3 + 2]);
                for (int c = 0; c < 3; c++) { int pi = tri[f][c]; (byPoint[pi] ?? (byPoint[pi] = new List<int>())).Add(f); }
            }
            var ci = CultureInfo.InvariantCulture;
            var sb = new StringBuilder(nf * 120);
            sb.AppendLine(Marker); sb.AppendLine("mtllib " + mtlName);
            for (int i = 0; i < np; i++) sb.Append("v ").Append((-P[i * 3]).ToString("0.#####", ci)).Append(' ').Append(P[i * 3 + 1].ToString("0.#####", ci)).Append(' ').Append(P[i * 3 + 2].ToString("0.#####", ci)).Append('\n');
            for (int i = 0; i < m.wedgeU.Length; i++) sb.Append("vt ").Append(m.wedgeU[i].ToString("0.#####", ci)).Append(' ').Append((1f - m.wedgeV[i]).ToString("0.#####", ci)).Append('\n');
            var vnIndex = new int[nf * 3]; int vnCount = 0;
            for (int f = 0; f < nf; f++)
                for (int c = 0; c < 3; c++)
                {
                    double ax = fn[f * 3], ay = fn[f * 3 + 1], az = fn[f * 3 + 2];
                    uint sg = m.faceSmooth[f];
                    if (sg != 0)
                    {
                        ax = ay = az = 0;
                        foreach (int g in byPoint[tri[f][c]]) if ((m.faceSmooth[g] & sg) != 0) { ax += fn[g * 3]; ay += fn[g * 3 + 1]; az += fn[g * 3 + 2]; }
                    }
                    double l = Math.Sqrt(ax * ax + ay * ay + az * az); if (l < 1e-12) { ax = 0; ay = 1; az = 0; l = 1; }
                    sb.Append("vn ").Append((-ax / l).ToString("0.####", ci)).Append(' ').Append((ay / l).ToString("0.####", ci)).Append(' ').Append((az / l).ToString("0.####", ci)).Append('\n');
                    vnIndex[f * 3 + c] = ++vnCount;
                }
            int nMat = Math.Max(1, m.materials.Count);
            for (int mi = 0; mi < nMat; mi++)
            {
                bool any = false;
                for (int f = 0; f < nf; f++)
                {
                    if (Math.Min(m.faceMat[f], nMat - 1) != mi) continue;
                    if (!any) { sb.Append("usemtl ").Append(MatName(m, mi)).Append('\n'); any = true; }
                    // written reversed (a,c,b) with x negated: Unity's OBJ importer mirrors x and reverses winding back to (a,b,c)
                    sb.Append("f ");
                    foreach (int c in new[] { 0, 2, 1 })
                        sb.Append(tri[f][c] + 1).Append('/').Append(wd[f][c] + 1).Append('/').Append(vnIndex[f * 3 + c]).Append(' ');
                    sb.Append('\n');
                }
            }
            tris = nf;
            return sb.ToString();
        }

        static string MatName(Mesh m, int i) => i < m.materials.Count && !string.IsNullOrWhiteSpace(m.materials[i]) ? m.materials[i].Replace(' ', '_') : "Material" + i;

        static void Cross(float[] P, int a, int b, int c, out double x, out double y, out double z)
        {
            double ux = P[b * 3] - P[a * 3], uy = P[b * 3 + 1] - P[a * 3 + 1], uz = P[b * 3 + 2] - P[a * 3 + 2];
            double vx = P[c * 3] - P[a * 3], vy = P[c * 3 + 1] - P[a * 3 + 1], vz = P[c * 3 + 2] - P[a * 3 + 2];
            // Unity (left-handed) cross product
            x = uy * vz - uz * vy; y = uz * vx - ux * vz; z = ux * vy - uy * vx;
        }

        public static string ToMtl(Mesh m)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < Math.Max(1, m.materials.Count); i++) sb.Append("newmtl ").Append(MatName(m, i)).Append("\nKd 1 1 1\n");
            return sb.ToString();
        }

        /// <summary>Converts every static .pskx/.psk under root next to itself (skips ones already converted).</summary>
        public static Result ConvertTree(string root, Func<string, bool> progress = null)
        {
            var r = new Result();
            var files = new List<string>();
            foreach (var ext in new[] { "*.pskx", "*.psk" }) files.AddRange(Directory.GetFiles(root, ext, SearchOption.AllDirectories));
            int n = 0;
            foreach (var f in files)
            {
                if (progress != null && !progress(Path.GetFileName(f))) break;
                n++;
                string obj = Path.ChangeExtension(f, ".obj");
                if (File.Exists(obj) && File.GetLastWriteTimeUtc(obj) >= File.GetLastWriteTimeUtc(f)) { r.converted++; continue; }
                try
                {
                    var m = Parse(f);
                    if (m.IsSkeletal) { r.skippedSkeletal++; r.skeletal.Add(f); continue; }
                    string mtl = Path.ChangeExtension(f, ".mtl");
                    File.WriteAllText(obj, ToObj(m, Path.GetFileName(mtl), out int t));
                    File.WriteAllText(mtl, ToMtl(m));
                    r.converted++; r.tris += t;
                }
                catch (Exception e) { r.failed++; if (r.errors.Count < 50) r.errors.Add(Path.GetFileName(f) + ": " + e.Message); }
            }
            return r;
        }
    }
}