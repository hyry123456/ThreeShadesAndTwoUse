using UnityEngine;

namespace DefferedRender
{
    [ExecuteInEditMode]
    /// <summary>
    /// 角色阴影贴图类，用来传递灯光方向以及灯光坐标对阴影进行偏移
    /// </summary>
    public class CharacterShadowTexture : MonoBehaviour
    {
        public Shader shader;

        public Transform originPos;
        private Mesh mesh;
        
        private Material material;

        public Color shadowCol;
        public Texture2D mainTex;

        private void OnValidate()
        {
            if (shader == null) return;
            if (mesh == null)
            {
                mesh = new Mesh();
                mesh.vertices = new Vector3[]
                {
                    new Vector3(-1, 0, -1),
                    new Vector3(1, 0, -1),
                    new Vector3(-1, 0, 1),
                    new Vector3(1, 0, 1),
                };
                mesh.triangles = new int[]
                {
                    2, 1, 0, 2, 3, 1
                };
                mesh.normals = new Vector3[]
                {
                    Vector3.up,
                    Vector3.up,
                    Vector3.up,
                    Vector3.up,
                };
                mesh.uv = new Vector2[]
                {
                    Vector2.zero,
                    Vector2.right,
                    Vector2.up,
                    Vector2.one
                };
            }

            MeshFilter mf = GetComponent<MeshFilter>();
            if (mf == null)
            {
                mf = gameObject.AddComponent<MeshFilter>();
            }
            mf.mesh = mesh;

            MeshRenderer mr = GetComponent<MeshRenderer>();
            if (mr == null)
            {
                mr = gameObject.AddComponent<MeshRenderer>();
            }
            if (material == null)
            {
                material = new Material(shader);
                material.renderQueue = 2100;
            }
            mr.material = material;
        }

        private void Update()
        {
            if (originPos == null)
                return;
            material.SetVector("_OriginLightCenter", originPos.position);
            if (mainTex)
                material.SetTexture("_MainTex", mainTex);
            material.SetColor("_ShadowColor", shadowCol);
        }


    }
}