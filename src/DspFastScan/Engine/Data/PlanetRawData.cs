using System;
using DspSeedFinder.MathLf;

namespace DspSeedFinder.Data
{
    /// <summary>行星原始地形数据（P1：球面顶点网格/高度场/QueryHeight/PositionHash 忠实移植）。</summary>
    public class PlanetRawData
    {
        public int precision;
        public ushort[] heightData = new ushort[0];
        public byte[] modData = new byte[0];
        public ushort[] vegeIds = new ushort[0];
        public byte[] biomoData = new byte[0];
        public short[] temprData = new short[0];
        public Vector3[] vertices = new Vector3[0];
        public Vector3[] normals = new Vector3[0];
        public int[] indexMap = new int[0];
        public int indexMapPrecision;
        public int indexMapDataLength;
        public int indexMapFaceStride;
        public int indexMapCornerStride;

        private static Vector3[] verts200;
        private static int[] indexMap200;
        private static Vector3[] verts80;
        private static int[] indexMap80;

        public static readonly Vector3[] poles = new Vector3[6]
        {
            Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back,
        };

        public int dataLength => (precision + 1) * (precision + 1) * 4;
        public int stride => (precision + 1) * 2;
        public int substride => precision + 1;

        public PlanetRawData(int _precision)
        {
            precision = _precision;
            int num = dataLength;
            heightData = new ushort[num];
            vegeIds = new ushort[num];
            biomoData = new byte[num];
            temprData = new short[num];
            vertices = new Vector3[num];
            normals = new Vector3[num];
            indexMapPrecision = precision >> 2;
            indexMapFaceStride = indexMapPrecision * indexMapPrecision;
            indexMapCornerStride = indexMapFaceStride * 3;
            indexMapDataLength = indexMapCornerStride * 8;
            indexMap = new int[indexMapDataLength];
            modData = new byte[num / 2];
        }

        /// <summary>球面顶点网格（六面体 Slerp 插值；precision 200/80 缓存）。</summary>
        public void CalcVerts()
        {
            if (precision == 200 && verts200 != null)
            {
                Array.Copy(verts200, vertices, verts200.Length);
                Array.Copy(indexMap200, indexMap, indexMap200.Length);
                return;
            }
            if (precision == 80 && verts80 != null)
            {
                Array.Copy(verts80, vertices, verts80.Length);
                Array.Copy(indexMap80, indexMap, indexMap80.Length);
                return;
            }
            for (int i = 0; i < indexMapDataLength; i++) indexMap[i] = -1;
            int num = (precision + 1) * 2;
            int num2 = precision + 1;
            for (int j = 0; j < dataLength; j++)
            {
                int num3 = j % num;
                int num4 = j / num;
                int num5 = num3 % num2;
                int num6 = num4 % num2;
                int num7 = (((num3 >= num2) ? 1 : 0) + ((num4 >= num2) ? 1 : 0) * 2) * 2 + ((num5 < num6) ? 1 : 0);
                float num8 = (num5 >= num6) ? (precision - num5) : num5;
                float num9 = (num5 >= num6) ? num6 : (precision - num6);
                float num10 = precision - num9;
                num9 /= precision;
                num8 = num10 > 0f ? num8 / num10 : 0f;
                int num11 = 0;
                Vector3 a, a2, b;
                switch (num7)
                {
                    case 0: a = poles[2]; a2 = poles[0]; b = poles[4]; num11 = 7; break;
                    case 1: a = poles[3]; a2 = poles[4]; b = poles[0]; num11 = 5; break;
                    case 2: a = poles[2]; a2 = poles[4]; b = poles[1]; num11 = 6; break;
                    case 3: a = poles[3]; a2 = poles[1]; b = poles[4]; num11 = 4; break;
                    case 4: a = poles[2]; a2 = poles[1]; b = poles[5]; num11 = 2; break;
                    case 5: a = poles[3]; a2 = poles[5]; b = poles[1]; num11 = 0; break;
                    case 6: a = poles[2]; a2 = poles[5]; b = poles[0]; num11 = 3; break;
                    case 7: a = poles[3]; a2 = poles[0]; b = poles[5]; num11 = 1; break;
                    default: a = poles[2]; a2 = poles[0]; b = poles[4]; num11 = 7; break;
                }
                vertices[j] = Vector3.Slerp(Vector3.Slerp(a, b, num9), Vector3.Slerp(a2, b, num9), num8);
                int num12 = PositionHash(vertices[j], num11);
                if (indexMap[num12] == -1) indexMap[num12] = j;
            }
            for (int k = 1; k < indexMapDataLength; k++)
            {
                if (indexMap[k] == -1) indexMap[k] = indexMap[k - 1];
            }
            if (precision == 200)
            {
                if (verts200 == null)
                {
                    verts200 = new Vector3[vertices.Length];
                    indexMap200 = new int[indexMap.Length];
                    Array.Copy(vertices, verts200, vertices.Length);
                    Array.Copy(indexMap, indexMap200, indexMap.Length);
                }
            }
            else if (precision == 80 && verts80 == null)
            {
                verts80 = new Vector3[vertices.Length];
                indexMap80 = new int[indexMap.Length];
                Array.Copy(vertices, verts80, vertices.Length);
                Array.Copy(indexMap, indexMap80, indexMap.Length);
            }
        }

        /// <summary>位置哈希索引（minimap/网格查询用）。</summary>
        public int PositionHash(Vector3 v, int corner = 0)
        {
            if (corner == 0)
            {
                corner = ((v.x > 0f) ? 1 : 0) + ((v.y > 0f) ? 2 : 0) + ((v.z > 0f) ? 4 : 0);
            }
            if (v.x < 0f) v.x = -v.x;
            if (v.y < 0f) v.y = -v.y;
            if (v.z < 0f) v.z = -v.z;
            if ((double)v.x < 1E-06 && (double)v.y < 1E-06 && (double)v.z < 1E-06) return 0;
            int num = 0, num2 = 0, num3 = 0;
            if (v.x >= v.y && v.x >= v.z)
            {
                num = 0;
                num2 = trans(v.z / v.x, indexMapPrecision);
                num3 = trans(v.y / v.x, indexMapPrecision);
            }
            else if (v.y >= v.x && v.y >= v.z)
            {
                num = 1;
                num2 = trans(v.x / v.y, indexMapPrecision);
                num3 = trans(v.z / v.y, indexMapPrecision);
            }
            else
            {
                num = 2;
                num2 = trans(v.x / v.z, indexMapPrecision);
                num3 = trans(v.y / v.z, indexMapPrecision);
            }
            int num4 = indexMapFaceStride * num;
            int num5 = indexMapCornerStride * corner;
            return num5 + num4 + num2 + num3 * indexMapPrecision;
        }

        private int trans(float x, int pr)
        {
            int num = (int)((Mathf.Sqrt(x + 0.23f) - 0.4795832f) / 0.6294705f * (float)pr);
            if (num >= pr) num = pr - 1;
            return num;
        }

        /// <summary>modData 高度带等级（CalcLandPercent 用）。</summary>
        public int GetModLevel(int index)
        {
            if (modData == null || index >= modData.Length) return 0;
            int num = index >> 1;
            if ((index & 1) == 0) return modData[num] & 0xF;
            return (modData[num] >> 4) & 0xF;
        }

        /// <summary>查询方向上的地表高度（反距离加权插值，返回世界单位高度）。</summary>
        public float QueryHeight(Vector3 vpos)
        {
            vpos.Normalize();
            int num = PositionHash(vpos);
            int num2 = indexMap[num];
            float num3 = MathF.PI / (float)(precision * 2) * 1.2f;
            float num4 = num3 * num3;
            float num5 = 0f;
            float num6 = 0f;
            int num7 = stride;
            for (int i = -1; i <= 3; i++)
            {
                for (int j = -1; j <= 3; j++)
                {
                    int num8 = num2 + i + j * num7;
                    if ((uint)num8 < (uint)dataLength)
                    {
                        float sqrMagnitude = (vertices[num8] - vpos).sqrMagnitude;
                        if (!(sqrMagnitude > num4))
                        {
                            float num9 = 1f - Mathf.Sqrt(sqrMagnitude) / num3;
                            float num10 = (int)heightData[num8];
                            num5 += num9;
                            num6 += num10 * num9;
                        }
                    }
                }
            }
            if (num5 == 0f) return (float)(int)heightData[0] * 0.01f;
            return num6 / num5 * 0.01f;
        }
    }
}
