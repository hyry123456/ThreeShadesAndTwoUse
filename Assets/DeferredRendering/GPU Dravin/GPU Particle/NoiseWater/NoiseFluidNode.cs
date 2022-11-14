using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;
using UnityEngine.Rendering;

namespace DefferedRender
{

    //大水珠需要的数据
    struct FluidGroup
    {
        public Vector3 worldPos;
        public Vector3 nowSpeed;
        //0是未初始化，1是group，2是非组
        public int mode;
        public float dieTime;  //死亡时间
    };

    //液体粒子需要的数据
    struct FluidParticle
    {
        public Vector3 worldPos;
        public Vector3 nowSpeed;
        public Vector3 random;
        public float size;
        //0为使用，1:组阶段，2：自由粒子
        public int mode;
        public Vector4 uvTransData;     //uv动画需要的数据
        public float interpolation;    //插值需要的数据
    };

    enum FluidPass
    {
        Normal = 0,
        Width = 1,
        CopyDepth = 2,
        BlendTarget=3,
        Bilater=4,
        BilaterDepth=5,
        WriteDepth
    };


    public class NoiseFluidNode : MonoBehaviour, IFluidDraw
    {
        [SerializeField]
        /// <summary>  /// 液体组数量  /// </summary>
        private int GroupCount = 100;
        [SerializeField]
        private int PerReleaseCount = 10;

        ComputeBuffer particleBuffer;
        ComputeBuffer groupBuffer;
        ComputeBuffer collsionBuffer;
        int kernel_Perframe, kernel_PerFixframe;
        [SerializeField]
        ComputeShader compute;
        [SerializeField]
        Shader shader;
        Material material;


        FluidGroup[] groups;

        [SerializeField]
        List<IGetCollsion> collsions;
        [SerializeField]
        WaterSetting waterSetting;

        float time;
        int index = 0;
        bool isInsert;

        private int
            widthTexId = Shader.PropertyToID("FluidWidth"),
            normalTexId = Shader.PropertyToID("FluidNormalTex"),
            depthTexId = Shader.PropertyToID("FluidDepthTex"),
            tempWidthTexId = Shader.PropertyToID("TempWidthTex"),
            tempNormalTexId = Shader.PropertyToID("TempNormalTex"),
            tempDepthTexId = Shader.PropertyToID("TempDepthTex");


        #region MaterialSetting
        public Texture2D mainTex;
        public Texture2D normalTex;
        public int rowCount = 1;
        public int columnCount = 1;
        public bool particleFollowSpeed;
        public bool useParticleNormal;      //是否使用默认法线，不是就是用法线贴图
        public bool useNormalMap;      //是否使用默认法线，不是就是用法线贴图


        public bool useNearAlpha = false;
        public float nearFadeDistance = 1;
        public float nearFadeRange = 1;

        public bool useSoftParticle = false;
        public float softParticleDistance = 1;
        public float softParticleRange = 1;


        #endregion

        private void Start()
        {
            if (compute == null || shader == null)
                return;

            FluidDrawStack.Instance.InsertDraw(this);
            isInsert = true;

            kernel_Perframe = compute.FindKernel("Water_PerFrame");
            kernel_PerFixframe = compute.FindKernel("Water_PerFixFrame");
            ReadyMaterial();
            ReadyBuffer();
            index = 0;
            time = waterSetting.sustainTime - 0.1f;
        }

        private void OnDestroy()
        {
            if (isInsert)
            {
                FluidDrawStack.Instance.RemoveDraw(this);
                groupBuffer?.Release();
                particleBuffer?.Release();
                collsionBuffer?.Release();
                isInsert = false;
            }

        }

        private void Update()
        {
            if (!isInsert) return;

            time += Time.deltaTime;
            if (time > waterSetting.releaseTime)
            {
                time = 0;
                for (int i = 0; i < PerReleaseCount; i++)
                {
                    //到时间就拷贝数据
                    if (groups[index].dieTime < Time.time)
                    {
                        groups[index].dieTime = Time.time + waterSetting.sustainTime;
                        groupBuffer.SetData(groups, index, index, 1);
                        index++;
                        index %= GroupCount;
                    }
                    else
                        break;
                }

            }
            SetOnCompute();
            compute.Dispatch(kernel_Perframe, GroupCount, 1, 1);
        }

        private void FixedUpdate()
        {
            if (!isInsert) return;

            SetOnFixCompute();
            compute.Dispatch(kernel_PerFixframe, GroupCount, 1, 1);
        }



#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (waterSetting == null)
                return;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = Color.red;
            if (waterSetting.groupShapeMode == InitialShapeMode.Cube)
            {
                Gizmos.DrawWireCube(Vector3.zero, waterSetting.groupCubeRange);
            }
            else if (waterSetting.groupShapeMode == InitialShapeMode.Sphere)
            {
                Gizmos.DrawWireSphere(Vector3.zero, waterSetting.groupRadius);
            }
        }

        private void OnValidate()
        {
            if (!isInsert) return;
            ReadyMaterial();
            ReadyBuffer();
            index = 0;
            time = waterSetting.sustainTime - 0.1f;
        }
#endif


        private void ReadyBuffer()
        {
            particleBuffer?.Release();
            groupBuffer?.Release();
            collsionBuffer?.Release();

            FluidParticle[] particles = new FluidParticle[GroupCount * 64];
            particleBuffer = new ComputeBuffer(particles.Length, Marshal.SizeOf<FluidParticle>());
            for (int i = 0; i < particles.Length; i++)
            {
                particles[i] = new FluidParticle()
                {
                    random = new Vector3(Random.value, Random.value, Random.value),
                };
            }
            particleBuffer.SetData(particles, 0, 0, particles.Length);

            groups = new FluidGroup[GroupCount];
            groupBuffer = new ComputeBuffer(groups.Length, Marshal.SizeOf<FluidGroup>());
            for (int i = 0; i < groups.Length; i++)
            {
                groups[i] = new FluidGroup()
                {
                    dieTime = -1,
                };
            }
            groupBuffer.SetData(groups, 0, 0, GroupCount);

            if (this.collsions == null) this.collsions = new List<IGetCollsion>();
            List<CollsionStruct> collsions = new List<CollsionStruct>();
            for (int i = 0; i < this.collsions.Count; i++)
            {
                collsions.Add(this.collsions[i].GetCollsionStruct());
            }
            if(collsions.Count == 0)
            {
                collsions.Add(new CollsionStruct
                {
                    radius = 0,
                    center = Vector3.zero,
                    offset = Vector3.zero,
                    mode = 0
                });
            }
            collsionBuffer = new ComputeBuffer(collsions.Count,
                Marshal.SizeOf<CollsionStruct>());
            collsionBuffer.SetData(collsions, 0, 0, collsions.Count);
        }

        private void ReadyMaterial()
        {
            material = new Material(shader);
            if (useSoftParticle)
            {
                material.EnableKeyword("_SOFT_PARTICLE");
                material.SetFloat("_SoftParticlesDistance", softParticleDistance);
                material.SetFloat("_SoftParticlesRange", softParticleRange);
            }
            else
                material.DisableKeyword("_SOFT_PARTICLE");

            if (useNearAlpha)
            {
                material.EnableKeyword("_NEAR_ALPHA");
                material.SetFloat("_NearFadeDistance", nearFadeDistance);
                material.SetFloat("_NearFadeRange", nearFadeRange);
            }
            else
                material.DisableKeyword("_NEAR_ALPHA");

            if (particleFollowSpeed)
            {
                material.EnableKeyword("_FOLLOW_SPEED");
            }
            else material.DisableKeyword("_FOLLOW_SPEED");

            if (useParticleNormal)
                material.EnableKeyword("_PARTICLE_NORMAL");
            else material.DisableKeyword("_PARTICLE_NORMAL");

            if (useNormalMap)
                material.EnableKeyword("_NORMAL_MAP");
            else material.DisableKeyword("_NORMAL_MAP");
        }

        /// <summary>/// 设置逐帧的Compute shader的数据/// </summary>
        private void SetOnCompute()
        {
            //设置组数据
            compute.SetInts("_GroupMode", new int[]
                {(int)waterSetting.groupShapeMode, (int)waterSetting.groupSpeedMode,
                waterSetting.groupUseGravity? 1:0});
            compute.SetFloat("_GroupArc", waterSetting.groupArc);
            compute.SetFloat("_GroupRadius", waterSetting.groupRadius);
            compute.SetVector("_GroupCubeRange", waterSetting.groupCubeRange);

            //设置单个粒子数据
            compute.SetFloat("_Arc", waterSetting.particleArc);
            compute.SetFloat("_Radius", waterSetting.particleRadius);
            compute.SetVector("_CubeRange", waterSetting.particleCubeRange);
            compute.SetFloat("_ParticleBeginSpeed", waterSetting.particleVelocityBegin);
            compute.SetVector("_LifeTime", new Vector4(waterSetting.sustainTime,
                0, 0, 0));

            compute.SetInts("_Mode", new int[] {(int)waterSetting.particleShadpeMode,
                (int)waterSetting.particleSpeedMode, (int)waterSetting.sizeBySpeedMode,
                waterSetting.particleUseGravity? 1 : 0});
            compute.SetMatrix("_RotateMatrix", transform.localToWorldMatrix);
            Vector3 speed = waterSetting.groupVelocityBegin;
            compute.SetVector("_BeginSpeed", new Vector4(speed.x,
                speed.y, speed.z, waterSetting.groupVelocityBegin.magnitude));

            compute.SetVector("_SizeRange", new Vector4(
                waterSetting.sizeRange.x, waterSetting.sizeRange.y,
                waterSetting.speedRange.x, waterSetting.speedRange.y));
            compute.SetVector("_Time", new Vector4(Time.time, Time.deltaTime, Time.fixedDeltaTime));
            compute.SetInts("_UVCount", new int[] { rowCount, columnCount });

            compute.SetVectorArray("_Sizes", waterSetting.GetParticleSizes());

            compute.SetBuffer(kernel_Perframe, "_FluidGroup", groupBuffer);
            compute.SetBuffer(kernel_Perframe, "_FluidParticle", particleBuffer);
        }

        /// <summary>
        /// 设置逐固定帧的Compute shader的数据，因为compute shader是全部共用的，
        /// 所以要每次都设置一次
        /// </summary>
        private void SetOnFixCompute()
        {
            int kernel = kernel_PerFixframe;
            compute.SetBuffer(kernel, "_FluidGroup", groupBuffer);
            compute.SetBuffer(kernel, "_FluidParticle", particleBuffer);
            compute.SetBuffer(kernel, "_CollsionBuffer", collsionBuffer);
            compute.SetInt("_CollsionData", collsions.Count);
            compute.SetFloat("_CollsionScale", waterSetting.collsionScale);

            compute.SetFloat("_Frequency", waterSetting.frequency);
            compute.SetInt("_Octave", waterSetting.octave);
            compute.SetFloat("_Intensity", waterSetting.particleIntensity);
            compute.SetInts("_GroupMode", new int[]
                {(int)waterSetting.groupShapeMode, (int)waterSetting.groupSpeedMode,
                waterSetting.groupUseGravity? 1:0});
            compute.SetInts("_Mode", new int[] {(int)waterSetting.particleShadpeMode,
                (int)waterSetting.particleSpeedMode, (int)waterSetting.sizeBySpeedMode,
                waterSetting.particleUseGravity? 1 : 0});
        }

        public void IFluidDraw(ScriptableRenderContext context, CommandBuffer buffer,
            RenderTargetIdentifier[] gBuffers, int gBufferDepth, int width, int height)
        {
            buffer.GetTemporaryRT(widthTexId, width / 2, height / 2, 0,
                FilterMode.Bilinear, RenderTextureFormat.RFloat);
            buffer.GetTemporaryRT(normalTexId, width / 2, height / 2, 0,
                FilterMode.Bilinear, RenderTextureFormat.Default);
            buffer.GetTemporaryRT(depthTexId, width / 2, height / 2, 32,
                FilterMode.Point, RenderTextureFormat.Depth);

            buffer.GetTemporaryRT(tempWidthTexId, width / 2, height / 2, 0,
                FilterMode.Point, RenderTextureFormat.RFloat);
            buffer.GetTemporaryRT(tempNormalTexId, width / 2, height / 2, 0,
                FilterMode.Point, RenderTextureFormat.Default);
            buffer.GetTemporaryRT(tempDepthTexId, width / 2, height / 2, 32,
                FilterMode.Point, RenderTextureFormat.Depth);

            buffer.SetGlobalTexture("_MainTex", gBufferDepth);
            buffer.Blit(null, depthTexId, material, (int)FluidPass.CopyDepth);

            buffer.SetGlobalTexture("_MainTex", mainTex);
            buffer.SetGlobalTexture("_NormalMap", normalTex);
            buffer.SetGlobalInt("_RowCount", rowCount);
            buffer.SetGlobalInt("_ColCount", columnCount);
            buffer.SetGlobalFloat("_TexAspectRatio", (float)mainTex.width / mainTex.height);
            buffer.SetGlobalBuffer("_FluidParticle", particleBuffer);

            buffer.SetRenderTarget(
                widthTexId, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                depthTexId, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store
            );
            buffer.ClearRenderTarget(true, true, Color.clear);
            buffer.DrawProcedural(Matrix4x4.identity, material, (int)FluidPass.Width,
                MeshTopology.Points, 1, particleBuffer.count);

            buffer.SetRenderTarget(
                normalTexId, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
                depthTexId, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store
                );
            buffer.ClearRenderTarget(true, true, Color.clear);

            buffer.DrawProcedural(Matrix4x4.identity, material, (int)FluidPass.Normal,
                MeshTopology.Points, 1, particleBuffer.count);

            for(int i=0; i< waterSetting.circleBlur; i++)
            {
                buffer.SetGlobalFloat("_BilaterFilterFactor", waterSetting.bilaterFilterFactor);
                buffer.SetGlobalVector("_BlurRadius", new Vector4(waterSetting.blurRadius, 0));
                buffer.SetGlobalTexture("_MainTex", widthTexId);
                buffer.Blit(null, tempWidthTexId, material, (int)FluidPass.Bilater);
                buffer.SetGlobalTexture("_MainTex", normalTexId);
                buffer.Blit(null, tempNormalTexId, material, (int)FluidPass.Bilater);
                buffer.SetGlobalTexture("_WaterDepth", depthTexId);
                buffer.Blit(null, tempDepthTexId, material, (int)FluidPass.BilaterDepth);

                buffer.SetGlobalVector("_BlurRadius", new Vector4(0, waterSetting.blurRadius));
                buffer.SetGlobalTexture("_MainTex", tempNormalTexId);
                buffer.Blit(null, normalTexId, material, (int)FluidPass.Bilater);
                buffer.SetGlobalTexture("_MainTex", tempWidthTexId);
                buffer.Blit(null, widthTexId, material, (int)FluidPass.Bilater);
                buffer.SetGlobalTexture("_WaterDepth", tempDepthTexId);
                buffer.Blit(null, depthTexId, material, (int)FluidPass.BilaterDepth);
            }

            buffer.SetGlobalTexture("_MainTex", gBufferDepth);
            buffer.Blit(null, tempDepthTexId, material, (int)FluidPass.CopyDepth);

            //设置渲染目标，传递所有的渲染目标
            buffer.SetRenderTarget(
                gBuffers,
                gBufferDepth
            );

            buffer.SetGlobalTexture("_WaterDepth", depthTexId); 
            buffer.SetGlobalTexture("_NormalMap", normalTexId);
            buffer.SetGlobalTexture("_MainTex", widthTexId);
            buffer.SetGlobalTexture("_CameraDepth", tempDepthTexId);
            buffer.SetGlobalColor("_WaterColor", waterSetting.waterCol);
            buffer.SetGlobalFloat("_MaxFluidWidth", waterSetting.maxFluidWidth);
            buffer.SetGlobalFloat("_CullOff", waterSetting.cullOff);

            //buffer.Blit(null, sourceId, material, (int)FluidPass.BlendTarget);
            buffer.DrawProcedural(
                Matrix4x4.identity, material, (int)FluidPass.BlendTarget, 
                MeshTopology.Quads, 6
            );

            buffer.Blit(null, gBufferDepth, material, (int)FluidPass.WriteDepth);

            buffer.ReleaseTemporaryRT(widthTexId);
            buffer.ReleaseTemporaryRT(normalTexId);
            buffer.ReleaseTemporaryRT(depthTexId);

            buffer.ReleaseTemporaryRT(tempDepthTexId);
            buffer.ReleaseTemporaryRT(tempNormalTexId);
            buffer.ReleaseTemporaryRT(tempWidthTexId);
        }
    }
}