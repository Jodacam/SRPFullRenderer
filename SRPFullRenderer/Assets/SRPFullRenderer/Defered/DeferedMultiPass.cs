using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Unity.Collections;
using System;

public class DeferedMultiPass : RenderPipeline
{


    #region Constants
    public const string BUFFER_CAMERA = "Render Camera";
    public const string DEFERED_SHADER_PASS = "Defered Shader Pass";
    const int depthIndex = 0, albedoIndex = 1, specRoughIndex = 2, normalIndex = 3, emissionIndex = 4;
    #endregion


    ScriptableCullingParameters cullingParameters;

    protected override void Render(ScriptableRenderContext context, Camera[] cameras)
    {
        foreach (var camera in cameras)
        {
            RenderCamera(context, camera);
        }   
    }



    public void RenderCamera(ScriptableRenderContext context, Camera camera)
    {
        CommandBuffer cameraBuffer = CommandBufferPool.Get(BUFFER_CAMERA);
#if UNITY_EDITOR
        if(camera.cameraType == CameraType.SceneView)
        {
            ScriptableRenderContext.EmitWorldGeometryForSceneView(camera);
        }
#endif
        context.SetupCameraProperties(camera);


        CameraClearFlags clearFlags = camera.clearFlags;
        cameraBuffer.ClearRenderTarget(
            (clearFlags & CameraClearFlags.Depth) != 0,
            (clearFlags & CameraClearFlags.Color) != 0,
            camera.backgroundColor
        );

        context.ExecuteCommandBuffer(cameraBuffer);


        CommandBufferPool.Release(cameraBuffer);

        cullingParameters = new ScriptableCullingParameters();

        if (camera.TryGetCullingParameters(out cullingParameters))
        {
            CullingResults cullingResult = context.Cull(ref cullingParameters);
            //RenderGBuffer(context, camera, ref cullingResult);
            DeferedRender(context, camera, ref cullingResult);
        }

        context.DrawSkybox(camera);
        context.Submit();
    }



    private void DeferedRender(ScriptableRenderContext context, Camera camera, ref CullingResults cullingResult)
    {
        var albedo = new AttachmentDescriptor(RenderTextureFormat.ARGB32);
        var specRough = new AttachmentDescriptor(RenderTextureFormat.ARGB32);
        var normal = new AttachmentDescriptor(RenderTextureFormat.ARGB2101010);
        var emission = new AttachmentDescriptor(RenderTextureFormat.ARGBHalf);
        var depth = new AttachmentDescriptor(RenderTextureFormat.Depth);

        emission.ConfigureClear(new Color(0.0f, 0.0f, 0.0f, 0.0f), 1.0f, 0);
        depth.ConfigureClear(new Color(), 1.0f, 0);

        NativeArray<AttachmentDescriptor> colorAttachments = new NativeArray<AttachmentDescriptor>(5, Allocator.Temp);
        colorAttachments[depthIndex] = depth;
        colorAttachments[specRoughIndex] = specRough;
        colorAttachments[normalIndex] = normal;
        colorAttachments[emissionIndex] = emission;
        colorAttachments[albedoIndex] = albedo;
        
        
        albedo.ConfigureTarget(BuiltinRenderTextureType.CameraTarget, false, true);

        context.BeginRenderPass(camera.pixelWidth, camera.pixelHeight, 1, colorAttachments, depthIndex);
       
        colorAttachments.Dispose();

        var gbufferColors = new NativeArray<int>(4, Allocator.Temp);
        gbufferColors[0] = albedoIndex;
        gbufferColors[1] = specRoughIndex;
        gbufferColors[2] = normalIndex;
        gbufferColors[3] = emissionIndex;
        

        //G-Buffer Pass
        context.BeginSubPass(gbufferColors);
        gbufferColors.Dispose();

        RenderGBuffer(context, camera, ref cullingResult);

        context.EndSubPass();

        var lightingColors = new NativeArray<int>(1, Allocator.Temp);
        lightingColors[0] = albedoIndex;
        var lightingInputs = new NativeArray<int>(4, Allocator.Temp);
        lightingInputs[0] = emissionIndex;
        lightingInputs[1] = specRoughIndex;
        lightingInputs[2] = normalIndex;
        lightingInputs[3] = depthIndex;
        //Lightning Pass.
        context.BeginSubPass(lightingColors,lightingInputs,true);

        lightingInputs.Dispose();
        lightingColors.Dispose();

        RenderLights(context, camera, ref cullingResult);
        

        context.EndSubPass();


        context.EndRenderPass();
    }

    private void RenderGBuffer(ScriptableRenderContext context, Camera camera, ref CullingResults cullingResult)
    {
        CommandBuffer renderBuffer = CommandBufferPool.Get("RenderBuffer");

        DrawingSettings d_settings = new DrawingSettings(new ShaderTagId("SRPDefaultUnlit"), new SortingSettings(camera) {criteria = SortingCriteria.CommonOpaque });

        // d_settings.SetShaderPassName(1, new ShaderTagId("SRPDefaultUnlit"));

        FilteringSettings f_settings = FilteringSettings.defaultValue;
        f_settings.renderQueueRange = RenderQueueRange.opaque;
        
        context.DrawRenderers(cullingResult, ref d_settings,ref f_settings);
        CommandBufferPool.Release(renderBuffer);
    }

    private void RenderLights(ScriptableRenderContext context, Camera camera, ref CullingResults cullingResult)
    {
        
    }

    
}
