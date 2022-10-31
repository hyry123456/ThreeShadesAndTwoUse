using UnityEngine;
using UnityEngine.Rendering;
using static DefferedRender.PostFXSetting;

namespace DefferedRender
{

    public class PostFXStack 
    {
        enum Pass
        {
			BloomAdd,
			BlurHorizontal,
			BloomPrefilter,
			BloomPrefilterFireflies,
			BloomScatter,
			BloomScatterFinal,
			BlurVertical,
			Copy,
			ColorGradingNone,
			ColorGradingACES,
			ColorGradingNeutral,
			ColorGradingReinhard,
			Final,
			SSS,
			GBufferFinal,
			BulkLight,
			BilateralFilter,
			BlendBulk,
			//Fog,
			CaculateGray,
			FXAA,
			CopyDepth,
		}

        const string bufferName = "PostFX";

        const int maxBloomPyramidLevels = 16;

		int
			sssTargetTex = Shader.PropertyToID("_SSSTargetTex"),
			maxRayMarchingStepId = Shader.PropertyToID("_MaxRayMarchingStep"),
			rayMarchingStepSizeId = Shader.PropertyToID("_RayMarchingStepSize"),
			maxRayMarchingDistance = Shader.PropertyToID("_MaxRayMarchingDistance"),
			depthThicknessId = Shader.PropertyToID("_DepthThickness");


		int
			bloomBicubicUpsamplingId = Shader.PropertyToID("_BloomBicubicUpsampling"),
            bloomIntensityId = Shader.PropertyToID("_BloomIntensity"),
            bloomPrefilterId = Shader.PropertyToID("_BloomPrefilter"),
            bloomResultId = Shader.PropertyToID("_BloomResult"),
            bloomThresholdId = Shader.PropertyToID("_BloomThreshold"),
            fxSourceId = Shader.PropertyToID("_PostFXSource"),
            fxSource2Id = Shader.PropertyToID("_PostFXSource2");

		int
			colorGradingLUTId = Shader.PropertyToID("_ColorGradingLUT"),
			colorGradingLUTParametersId = Shader.PropertyToID("_ColorGradingLUTParameters"),
			colorGradingLUTInLogId = Shader.PropertyToID("_ColorGradingLUTInLogC"),
			colorAdjustmentsId = Shader.PropertyToID("_ColorAdjustments"),
			colorFilterId = Shader.PropertyToID("_ColorFilter"),
			whiteBalanceId = Shader.PropertyToID("_WhiteBalance"),
			splitToningShadowsId = Shader.PropertyToID("_SplitToningShadows"),
			splitToningHighlightsId = Shader.PropertyToID("_SplitToningHighlights"),
			bulkLightTargetTexId = Shader.PropertyToID("_BulkLightTargetTex"),
			bulkLightTempTexId = Shader.PropertyToID("_BulkLightTempTex"),
			bulkLightTemp2TexId = Shader.PropertyToID("_BulkLightTemp2Tex"),
			bulkLightDepthTexId = Shader.PropertyToID("_BulkLightDepthTex"),
			bulkLightShrinkRadioId = Shader.PropertyToID("_BulkLightShrinkRadio"),
			bulkLightSampleCountId = Shader.PropertyToID("_BulkSampleCount"),
			bulkLightScatterRadioId = Shader.PropertyToID("_BulkLightScatterRadio"),
			bulkLightCheckMaxDistanceId = Shader.PropertyToID("_BulkLightCheckMaxDistance"),


			//fogColorsId = Shader.PropertyToID("_Colors"),

			finalTempTexId = Shader.PropertyToID("_FinalTempTexure"),
			fxaaTempTexId = Shader.PropertyToID("_FXAATempTexture"),
			contrastThresholdId = Shader.PropertyToID("_ContrastThreshold"),
			relativeThresholdId = Shader.PropertyToID("_RelativeThreshold"),
			subpixelBlending = Shader.PropertyToID("_SubpixelBlending"),

			detactiveViewTexId = Shader.PropertyToID("_DetactiveViewTexture");	//临时目标纹理




		CommandBuffer buffer = new CommandBuffer
        {
            name = bufferName
        };

        ScriptableRenderContext context;

        Camera camera;

        PostFXSetting settings;
        int bloomPyramidId;
		bool useHDR;
		CullingResults cullingResults; int depthId;
		public bool IsActive => settings != null;
		int width, height;

		public PostFXStack()
        {
            bloomPyramidId = Shader.PropertyToID("_BloomPyramid0");
			for (int i = 1; i < maxBloomPyramidLevels * 2; i++)
            {
                Shader.PropertyToID("_BloomPyramid" + i);
            }
        }

        /// <summary>	/// 准备后处理前的数据准备	/// </summary>
        public void Setup(
            ScriptableRenderContext context, Camera camera, PostFXSetting settings,
            bool useHDR, CullingResults cullingResults, int depthId,
			int width, int height
        )
        {
            this.useHDR = useHDR;
            this.context = context;
            this.camera = camera;
            this.settings = settings;
			this.cullingResults = cullingResults;
			this.depthId = depthId;
			this.width = width;
			this.height = height;
		}

		/// <summary>
		/// 对传入的纹理进行后处理
		/// </summary>
		/// <param name="sourceId">影响的纹理</param>
		public void Render(int sourceId)
        {

            if (settings.BulkLighting.useBulkLight)
            {
                DrawBulkLight(sourceId);
            }

            if (DoBloom(sourceId))
			{
                DoColorGradingAndToneMapping(bloomResultId);
                buffer.ReleaseTemporaryRT(bloomResultId);
			}
			else
			{
                DoColorGradingAndToneMapping(sourceId);
            }

			context.ExecuteCommandBuffer(buffer);
			buffer.Clear();
		}


		/// <summary>
		/// 准备SSS贴图，如果没有开启SSS就是仅有一张全黑图
		/// </summary>
		/// <param name="preFrameRenderFinal">上一帧渲染出来的纹理</param>
		/// <param name="reflectTex">采集到的反射贴图</param>
		public void DrawSSS(RenderTexture preFrameRenderFinal, int reflectTex)
        {
			buffer.GetTemporaryRT(sssTargetTex, this.width, this.height,
				0, FilterMode.Bilinear, useHDR ?
					RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default);

			//把静态反射保存起来
			Draw(reflectTex, sssTargetTex, Pass.Copy);
			if (settings.ssr.useSSR && camera.cameraType == CameraType.Game)
            {
				SSR ssr = settings.ssr;
				buffer.SetGlobalInt(maxRayMarchingStepId, ssr.rayMarchingSetp);
				buffer.SetGlobalFloat(rayMarchingStepSizeId, ssr.marchSetpSize);
				buffer.SetGlobalFloat(maxRayMarchingDistance, ssr.maxMarchDistance);
				buffer.SetGlobalFloat(depthThicknessId, ssr.depthThickness);
				//绘制实时反射
				Draw(preFrameRenderFinal, sssTargetTex, Pass.SSS);

                buffer.GetTemporaryRT(bulkLightTempTexId, this.width, this.height,
                0, FilterMode.Bilinear, useHDR ?
                    RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default);

                Draw(sssTargetTex, bulkLightTempTexId, Pass.BlurHorizontal);
                Draw(bulkLightTempTexId, sssTargetTex, Pass.BlurVertical);
                buffer.ReleaseTemporaryRT(bulkLightTempTexId);
            }
			//进行模糊处理
			//buffer.GetTemporaryRT(bulkLightTempTexId, this.width, this.height,
			//0, FilterMode.Bilinear, useHDR ?
			//	RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default);

			//Draw(sssTargetTex, bulkLightTempTexId, Pass.BlurHorizontal);
			//Draw(bulkLightTempTexId, sssTargetTex, Pass.BlurVertical);
			//buffer.ReleaseTemporaryRT(bulkLightTempTexId);
		}

		/// <summary>
		/// 绘制最终纹理，也就是根据SSS和GBuffer数据渲染出来的纹理
		/// </summary>
		public void DrawGBufferFinal(int targetTexId)
		{


            Draw(0, targetTexId, Pass.GBufferFinal);
			buffer.ReleaseTemporaryRT(sssTargetTex);
			ExecuteBuffer();
		}

		/// <summary>		/// 渲染体积光		/// </summary>
		public void DrawBulkLight(int source)
        {
			buffer.BeginSample("BulkLight");

            buffer.SetGlobalFloat(bulkLightShrinkRadioId, settings.BulkLighting.shrinkRadio / 100000f);
            buffer.SetGlobalInt(bulkLightSampleCountId, settings.BulkLighting.circleCount);
            buffer.SetGlobalFloat(bulkLightScatterRadioId, settings.BulkLighting.scatterRadio);
            buffer.SetGlobalFloat(bulkLightCheckMaxDistanceId, settings.BulkLighting.checkDistance);

            RenderTextureFormat format = useHDR ?
                RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
            int width = this.width / 2, height = this.height / 2;
            buffer.GetTemporaryRT(bulkLightTargetTexId, width, height, 0, FilterMode.Bilinear, format);
            buffer.GetTemporaryRT(bulkLightTempTexId, width, height, 0, FilterMode.Bilinear, format);
			buffer.GetTemporaryRT(bulkLightDepthTexId, width, height, 32, 
				FilterMode.Point, RenderTextureFormat.Depth);
			//清除Target的颜色
			Draw(Texture2D.blackTexture, bulkLightTargetTexId, Pass.Copy);
			Draw(depthId, bulkLightDepthTexId, Pass.CopyDepth);
            //Draw(0, bulkLightTargetTexId, Pass.BulkLight);      //获得Bulk Light强度图
            buffer.SetRenderTarget(
				bulkLightTargetTexId, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
				bulkLightDepthTexId, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            BulkLight.Instance.DrawBulkLight(buffer);

			buffer.ReleaseTemporaryRT(bulkLightDepthTexId);

            //高斯模糊体积光贴图
            Draw(bulkLightTargetTexId, bulkLightTempTexId, Pass.BlurHorizontal);
			Draw(bulkLightTempTexId, bulkLightTargetTexId, Pass.BlurVertical);

            buffer.SetGlobalTexture(fxSource2Id, bulkLightTargetTexId);	//模糊后的强度图

			buffer.GetTemporaryRT(bulkLightTemp2TexId, this.width, this.height,
				0, FilterMode.Bilinear, format);
			Draw(source, bulkLightTemp2TexId, Pass.BlendBulk);         //最终目标纹理
			Draw(bulkLightTemp2TexId, source, Pass.Copy);         //拷贝到目标纹理原纹理

			buffer.ReleaseTemporaryRT(bulkLightTemp2TexId);
			buffer.ReleaseTemporaryRT(bulkLightTempTexId);
			buffer.ReleaseTemporaryRT(bulkLightTargetTexId);

			buffer.EndSample("BulkLight");
			ExecuteBuffer();
		}

		/// <summary>		/// 渲染雾效		/// </summary>
		//public void DrawFog(int soure)
  //      {
		//	buffer.BeginSample("Fog");
		//	FogSetting fog = settings.Fog;
		//	//settings.Fog
		//	GradientColorKey[] gradientColorKeys = fog.colors.colorKeys;
		//	Vector4[] colors = new Vector4[gradientColorKeys.Length];
		//	for (int i = 0; i < gradientColorKeys.Length; i++)
		//	{
		//		colors[i] = gradientColorKeys[i].color;
		//		colors[i].w = gradientColorKeys[i].time;
		//	}
		//	buffer.SetGlobalVectorArray(fogColorsId, colors);

		//	buffer.SetGlobalFloat(fogMaxDepth, fog.fogMaxDepth);
		//	buffer.SetGlobalFloat(fogMinDepth, fog.fogMinDepth);
		//	buffer.SetGlobalFloat(fogDepthFallOff, fog.fogDepthFallOff);

		//	buffer.SetGlobalFloat(fogMaxHight, fog.fogMaxHeight);
		//	buffer.SetGlobalFloat(fogMinHight, fog.fogMinHeight);
		//	buffer.SetGlobalFloat(fogPosYFallOff, fog.fogPosYFallOff);

		//	RenderTextureFormat format = (useHDR) ? RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;

		//	buffer.GetTemporaryRT(bulkLightTempTexId, this.width, this.height,
		//		0, FilterMode.Bilinear, format);

		//	Draw(soure, bulkLightTempTexId, Pass.Fog);

		//	Draw(bulkLightTempTexId, soure, Pass.Copy);

		//	buffer.ReleaseTemporaryRT(bulkLightTempTexId);

		//	buffer.EndSample("Fog");
		//	ExecuteBuffer();
		//}

		/// <summary>		/// 对输出到最终图像的位置进行抗锯齿		/// </summary>
		public void DrawFXAAInFinal(int soure)
        {
			buffer.BeginSample("FXAA");
			FXAASetting fXAA = settings.FXAA;

			buffer.SetGlobalFloat(contrastThresholdId, fXAA.contrastThreshold);
			buffer.SetGlobalFloat(relativeThresholdId, fXAA.relativeThreshold);
			buffer.SetGlobalFloat(subpixelBlending, fXAA.subpixelBlending);

			if (fXAA.lowQuality)
				buffer.EnableShaderKeyword("LOW_QUALITY");
			else
				buffer.DisableShaderKeyword("LOW_QUALITY");
			
			if(fXAA.luminanceMode == LuminanceMode.Green)
            {
				buffer.DisableShaderKeyword("LUMINANCE_GREEN");
				Draw(soure, BuiltinRenderTextureType.CameraTarget, Pass.FXAA);
			}
			else
            {
				buffer.EnableShaderKeyword("LUMINANCE_GREEN");
				buffer.GetTemporaryRT(fxaaTempTexId, this.width, this.height,
					0, FilterMode.Bilinear, RenderTextureFormat.Default);
				Draw(soure, fxaaTempTexId, Pass.CaculateGray);
				Draw(fxaaTempTexId, BuiltinRenderTextureType.CameraTarget, Pass.FXAA);
				buffer.ReleaseTemporaryRT(fxaaTempTexId);
			}
			buffer.EndSample("FXAA");

		}

		/// <summary>	/// 进行Bloom操作	/// </summary>
		/// <returns>是否成功进行Bloom</returns>
		bool DoBloom(int sourceId)
		{
			BloomSettings bloom = settings.Bloom;
            int width = this.width / 2, height = this.height / 2;
            if (
				bloom.maxIterations == 0 || bloom.intensity <= 0f ||
				height < bloom.downscaleLimit * 2 || width < bloom.downscaleLimit * 2
			)
			{
				return false;
			}

			buffer.BeginSample("Bloom");
			Vector4 threshold;
			threshold.x = Mathf.GammaToLinearSpace(bloom.threshold);
			threshold.y = threshold.x * bloom.thresholdKnee;
			threshold.z = 2f * threshold.y;
			threshold.w = 0.25f / (threshold.y + 0.00001f);
			threshold.y -= threshold.x;
			buffer.SetGlobalVector(bloomThresholdId, threshold);        //Bloom参数准备

			RenderTextureFormat format = useHDR ?
				RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
			buffer.GetTemporaryRT(
				bloomPrefilterId, width, height, 0, FilterMode.Bilinear, format
			);

			Draw(
				sourceId, bloomPrefilterId, bloom.fadeFireflies ?
					Pass.BloomPrefilterFireflies : Pass.BloomPrefilter
			);
			width /= 2;
			height /= 2;

			int fromId = bloomPrefilterId, toId = bloomPyramidId + 1;
			int i;
			//逐个循环，进行逐级高斯模糊
			for (i = 0; i < bloom.maxIterations; i++)
			{

				if (height < bloom.downscaleLimit || width < bloom.downscaleLimit)
				{
					break;
				}
				int midId = toId - 1;
				buffer.GetTemporaryRT(
					midId, width, height, 0, FilterMode.Bilinear, format
				);
				buffer.GetTemporaryRT(
					toId, width, height, 0, FilterMode.Bilinear, format
				);
				//水平以及垂直高斯模糊
				Draw(fromId, midId, Pass.BlurHorizontal);
				Draw(midId, toId, Pass.BlurVertical);
				//模糊后减少纹理像素大小
				fromId = toId;
				toId += 2;
				width /= 2;
				height /= 2;
			}

			buffer.ReleaseTemporaryRT(bloomPrefilterId);
			buffer.SetGlobalFloat(
				bloomBicubicUpsamplingId, bloom.bicubicUpsampling ? 1f : 0f
			);

			//控制纹理的混合模式，是使用渐变混合还是直接相加
			Pass combinePass, finalPass;
			float finalIntensity;
			if (bloom.mode == BloomSettings.Mode.Additive)
			{
				combinePass = finalPass = Pass.BloomAdd;
				buffer.SetGlobalFloat(bloomIntensityId, 1f);
				finalIntensity = bloom.intensity;
			}
			else
			{
				combinePass = Pass.BloomScatter;
				finalPass = Pass.BloomScatterFinal;
				buffer.SetGlobalFloat(bloomIntensityId, bloom.scatter);
				finalIntensity = Mathf.Min(bloom.intensity, 1f);
			}

			//循环混合到最上级纹理上
			if (i > 1)
			{
				buffer.ReleaseTemporaryRT(fromId - 1);
				toId -= 5;
				for (i -= 1; i > 0; i--)
				{
					buffer.SetGlobalTexture(fxSource2Id, toId + 1);
					Draw(fromId, toId, combinePass);
					buffer.ReleaseTemporaryRT(fromId);
					buffer.ReleaseTemporaryRT(toId + 1);
					fromId = toId;
					toId -= 2;
				}
			}
			else
			{
				buffer.ReleaseTemporaryRT(bloomPyramidId);
			}
			buffer.SetGlobalFloat(bloomIntensityId, finalIntensity);
			buffer.SetGlobalTexture(fxSource2Id, sourceId);
			buffer.GetTemporaryRT(
				bloomResultId, this.width, this.height, 0,
				FilterMode.Bilinear, format
			);
			//绘制到目标纹理上
			Draw(fromId, bloomResultId, finalPass);
			buffer.ReleaseTemporaryRT(fromId);
			buffer.EndSample("Bloom");
			return true;
		}

		/// <summary>	/// 进行色彩分级以及渲染到摄像机上	/// </summary>
		/// <param name="sourceId">源纹理</param>
		void DoColorGradingAndToneMapping(int sourceId)
		{
			ConfigureColorAdjustments();
			ConfigureWhiteBalance();
			ConfigureSplitToning();

			int lutHeight = (int)settings.LUTResolution;
			int lutWidth = lutHeight * lutHeight;
			buffer.GetTemporaryRT(
				colorGradingLUTId, lutWidth, lutHeight, 0,
				FilterMode.Bilinear, RenderTextureFormat.DefaultHDR
			);
			buffer.SetGlobalVector(colorGradingLUTParametersId, new Vector4(
				lutHeight, 0.5f / lutWidth, 0.5f / lutHeight, lutHeight / (lutHeight - 1f)
			));

			ToneMappingSettings.Mode mode = settings.ToneMapping.mode;
			Pass pass = Pass.ColorGradingNone + (int)mode;
			buffer.SetGlobalFloat(
				colorGradingLUTInLogId, useHDR && pass != Pass.ColorGradingNone ? 1f : 0f
			);
			//绘制查找集纹理
			Draw(sourceId, colorGradingLUTId, pass);

			buffer.SetGlobalVector(colorGradingLUTParametersId,
				new Vector4(1f / lutWidth, 1f / lutHeight, lutHeight - 1f)
			);

			if(settings.FXAA.luminanceMode == LuminanceMode.None)
            {
				//绘制最终图像
				DrawFinal(sourceId);
            }
            else
            {
				buffer.GetTemporaryRT(finalTempTexId, this.width, this.height, 
					0, FilterMode.Bilinear, RenderTextureFormat.Default);
				Draw(sourceId, finalTempTexId, Pass.Final);
				DrawFXAAInFinal(finalTempTexId);
				buffer.ReleaseTemporaryRT(finalTempTexId);
			}
			buffer.ReleaseTemporaryRT(colorGradingLUTId);
		}


		//static ShaderTagId
		//	detactiveShaderTagId = new ShaderTagId("DetavtiveView");

		///// <summary>/// 绘制侦探视觉，对特殊物体进行绘制/// </summary>
		//void DrawDetectiveView(int sourceId)
  //      {
		//	buffer.BeginSample("Draw DetectiveView");
		//	RenderTextureFormat format = useHDR ?
		//		RenderTextureFormat.DefaultHDR : RenderTextureFormat.Default;
		//	buffer.GetTemporaryRT(detactiveViewTexId, this.width, this.height,
		//		0, FilterMode.Bilinear, format);

		//	//设置该摄像机的物体排序模式，目前是渲染普通物体，因此用一般排序方式
		//	var sortingSettings = new SortingSettings(camera)
		//	{
		//		criteria = SortingCriteria.CommonOpaque
		//	};
		//	//第一次渲染只绘制GBuffer，且GBuffer仅渲染非透明
		//	var drawingSettings = new DrawingSettings(
		//		detactiveShaderTagId, sortingSettings
		//	)
		//	{
		//		enableDynamicBatching = true,
		//		enableInstancing = true,
		//	};
		//	var filteringSettings = new FilteringSettings(RenderQueueRange.opaque);

		//	Draw(sourceId, detactiveViewTexId, Pass.Copy);

		//	buffer.SetRenderTarget(detactiveViewTexId, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store,
		//		depthId, RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);

		//	//float blendRadio = 1.0f - Mathf.Clamp01(settings.Fog.fogMaxDepth / 0.1f);

		//	//buffer.SetGlobalFloat("_BlendRadio", blendRadio);

		//	ExecuteBuffer();

		//	//进行渲染的执行方法
		//	context.DrawRenderers(
		//		cullingResults, ref drawingSettings, ref filteringSettings
		//	);

		//	//复制到真实纹理中
		//	Draw(detactiveViewTexId, sourceId, Pass.Copy);
		//	buffer.ReleaseTemporaryRT(detactiveViewTexId);

		//	buffer.EndSample("Draw DetectiveView");

		//}


		/// <summary>	/// 颜色调整参数赋值	/// </summary>
		void ConfigureColorAdjustments()
		{
			ColorAdjustmentsSettings colorAdjustments = settings.ColorAdjustments;
			buffer.SetGlobalVector(colorAdjustmentsId, new Vector4(
				Mathf.Pow(2f, colorAdjustments.postExposure),
				colorAdjustments.contrast * 0.01f + 1f,
				colorAdjustments.hueShift * (1f / 360f),
				colorAdjustments.saturation * 0.01f + 1f
			));
			buffer.SetGlobalColor(colorFilterId, colorAdjustments.colorFilter.linear);
		}

		/// <summary>	/// 白平衡	/// </summary>
		void ConfigureWhiteBalance()
		{
			WhiteBalanceSettings whiteBalance = settings.WhiteBalance;
			buffer.SetGlobalVector(whiteBalanceId, ColorUtils.ColorBalanceToLMSCoeffs(
				whiteBalance.temperature, whiteBalance.tint
			));
		}

		/// <summary>	/// 色调分离	/// </summary>
		void ConfigureSplitToning()
		{
			SplitToningSettings splitToning = settings.SplitToning;
			Color splitColor = splitToning.shadows;
			splitColor.a = splitToning.balance * 0.01f;
			buffer.SetGlobalColor(splitToningShadowsId, splitColor);
			buffer.SetGlobalColor(splitToningHighlightsId, splitToning.highlights);
		}

		/// <summary>        /// 进行纹理绘制        /// </summary>
		/// <param name="from">根据纹理</param>
		/// <param name="to">目标纹理</param>
		/// <param name="isDepth">是否为深度</param>
		void Draw(
			RenderTargetIdentifier from, RenderTargetIdentifier to, Pass mode
		)
		{
			buffer.SetGlobalTexture(fxSourceId, from);
			buffer.Blit(null, to, settings.Material, (int)mode);
		}

		/// <summary>	/// 绘制最终纹理，渲染至摄像机的实际看到的纹理中	/// </summary>
		/// <param name="from">根据的纹理</param>
		void DrawFinal(RenderTargetIdentifier from)
		{
			buffer.SetGlobalTexture(fxSourceId, from);
			buffer.Blit(null, BuiltinRenderTextureType.CameraTarget,
				settings.Material, (int)Pass.Final);
		}

		void ExecuteBuffer()
        {
			context.ExecuteCommandBuffer(buffer);
			buffer.Clear();
		}
	}
}