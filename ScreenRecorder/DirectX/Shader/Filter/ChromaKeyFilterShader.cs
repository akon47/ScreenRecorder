using System;
using SharpDX;
using SharpDX.D3DCompiler;
using SharpDX.Direct3D11;
using SharpDX.DXGI;
using SharpDX.Mathematics.Interop;
using Buffer = SharpDX.Direct3D11.Buffer;
using Device = SharpDX.Direct3D11.Device;
using MapFlags = SharpDX.Direct3D11.MapFlags;

namespace ScreenRecorder.DirectX.Shader.Filter
{
    /// <summary>
    ///     ChromaKeyFilterShader
    ///     I used the following code ->
    ///     https://github.com/obsproject/obs-studio/blob/master/plugins/obs-filters/data/chroma_key_filter_v2.effect
    ///     I wrote the HLSL referring to the code above.
    /// </summary>
    public class ChromaKeyFilterShader : IDisposable
    {
        private readonly string shaderCode =
            @"
Texture2D Texture : register(t0);
SamplerState TextureSampler;
cbuffer Args
{
	float opacity;
	float contrast;
	float brightness;
	float gamma;
	float2 chroma_key;
	float2 pixel_size;
	float similarity;
	float smoothness;
	float spill;
};
struct VSInput
{
	float4 position : POSITION;
	float2 uv : TEXCOORD0;
};
struct PSInput
{
	float4 position : SV_POSITION;
	float2 uv : TEXCOORD0;
};
PSInput VShader(VSInput input)
{
	PSInput output;
	output.position = input.position;
	output.uv = input.uv;
	return output;
}
static const float4 cb_v4 = { -0.100644, -0.338572,  0.439216, 0.501961 };
static const float4 cr_v4 = {  0.439216, -0.398942, -0.040274, 0.501961 };
float4 CalcColor(float4 rgba)
{
	return float4(pow(rgba.rgb, float3(gamma, gamma, gamma)) * contrast + brightness, rgba.a);
}
float GetChromaDist(float3 rgb)
{
	float cb = dot(rgb.rgb, cb_v4.xyz) + cb_v4.w;
	float cr = dot(rgb.rgb, cr_v4.xyz) + cr_v4.w;
	return distance(chroma_key, float2(cr, cb));
}
float4 SampleTexture(float2 uv)
{
	return Texture.Sample(TextureSampler, uv);
}
float GetBoxFilteredChromaDist(float3 rgb, float2 texCoord)
{
	float2 h_pixel_size = pixel_size / 2.0;
	float2 point_0 = float2(pixel_size.x, h_pixel_size.y);
	float2 point_1 = float2(h_pixel_size.x, -pixel_size.y);
	float distVal = GetChromaDist(SampleTexture(texCoord-point_0).rgb);
	distVal += GetChromaDist(SampleTexture(texCoord+point_0).rgb);
	distVal += GetChromaDist(SampleTexture(texCoord-point_1).rgb);
	distVal += GetChromaDist(SampleTexture(texCoord+point_1).rgb);
	distVal *= 2.0;
	distVal += GetChromaDist(rgb);
	return distVal / 9.0;
}
float4 ProcessChromaKey(float4 rgba, PSInput p_in)
{
	float chromaDist = GetBoxFilteredChromaDist(rgba.rgb, p_in.uv);
	float baseMask = chromaDist - similarity;
	float fullMask = pow(saturate(baseMask / smoothness), 1.5);
	float spillVal = pow(saturate(baseMask / spill), 1.5);
	rgba.a *= opacity;
	rgba.a *= fullMask;
	float desat = dot(rgba.rgb, float3(0.2126, 0.7152, 0.0722));
	rgba.rgb = lerp(float3(desat, desat, desat), rgba.rgb, spillVal);
	return CalcColor(rgba);
}
float4 PShader(PSInput input) : SV_Target
{
	float4 rgba = Texture.Sample(TextureSampler, input.uv);
	return ProcessChromaKey(rgba, input);
}
";

        private Buffer argsBuffer;
        private InputLayout inputLayout;
        private ShaderSignature inputSignature;
        private int oldKeyRed = -1, oldKeyGreen = -1, oldKeyBlue = -1;

        private float oldOpacity = -1,
            oldContrast = -1,
            oldBrightness = -1,
            oldGamma = -1,
            oldSimilarity = -1,
            oldSmoothness = -1,
            oldSpill = -1;

        private PixelShader pixelShader;
        private SamplerState samplerState;
        private VertexShader vertexShader;

        public void Dispose()
        {
            if (inputLayout != null)
            {
                inputLayout.Dispose();
            }

            if (inputSignature != null)
            {
                inputSignature.Dispose();
            }

            if (vertexShader != null)
            {
                vertexShader.Dispose();
            }

            if (pixelShader != null)
            {
                pixelShader.Dispose();
            }

            if (samplerState != null)
            {
                samplerState.Dispose();
            }

            if (argsBuffer != null)
            {
                argsBuffer.Dispose();
            }
        }

        public void Initialize(Device device)
        {
            InitializeShader(device);
        }

        private void InitializeShader(Device device)
        {
            using (var bytecode = ShaderBytecode.Compile(shaderCode, "VShader", "vs_4_0"))
            {
                inputSignature = ShaderSignature.GetInputSignature(bytecode);
                vertexShader = new VertexShader(device, bytecode);
            }

            using (var bytecode = ShaderBytecode.Compile(shaderCode, "PShader", "ps_4_0"))
            {
                pixelShader = new PixelShader(device, bytecode);
            }

            var elements = new[]
            {
                new InputElement("POSITION", 0, Format.R32G32B32_Float, 0, 0, InputClassification.PerVertexData, 0),
                new InputElement("TEXCOORD", 0, Format.R32G32_Float, InputElement.AppendAligned, 0,
                    InputClassification.PerVertexData, 0)
            };

            inputLayout = new InputLayout(device, inputSignature, elements);

            argsBuffer = new Buffer(device, 64, ResourceUsage.Dynamic, BindFlags.ConstantBuffer, CpuAccessFlags.Write,
                ResourceOptionFlags.None, 0);

            samplerState = new SamplerState(device,
                new SamplerStateDescription
                {
                    Filter = SharpDX.Direct3D11.Filter.MinMagMipLinear,
                    AddressU = TextureAddressMode.Clamp,
                    AddressV = TextureAddressMode.Clamp,
                    AddressW = TextureAddressMode.Wrap,
                    MipLodBias = 0.0f,
                    MaximumAnisotropy = 2,
                    ComparisonFunction = Comparison.Always,
                    BorderColor = new RawColor4(0, 0, 0, 0),
                    MinimumLod = 0,
                    MaximumLod = float.MaxValue
                });
        }

        public void Render(DeviceContext deviceContext, ShaderResourceView shaderResourceView, float textureWidth,
            float textureHeight,
            float opacity, float contrast, float brightness, float gamma, int key_red, int key_green, int key_blue,
            float similarity, float smoothness, float spill)
        {
            SetShaderParameters(deviceContext, shaderResourceView, textureWidth, textureHeight, opacity, contrast,
                brightness, gamma, key_red, key_green, key_blue, similarity, smoothness, spill);
            RenderShader(deviceContext);
        }

        private static float srgb_nonlinear_to_linear(float u)
        {
            return (float)(u <= 0.04045d ? u / 12.92d : Math.Pow((u + 0.055d) / 1.055d, 2.4d));
        }

        private static void vec4_from_rgba_srgb(out Vector4 dst, uint rgba)
        {
            dst = new Vector4();
            dst.X = srgb_nonlinear_to_linear((rgba & 0xFF) / 255.0f);
            rgba >>= 8;
            dst.Y = srgb_nonlinear_to_linear((rgba & 0xFF) / 255.0f);
            rgba >>= 8;
            dst.Z = srgb_nonlinear_to_linear((rgba & 0xFF) / 255.0f);
            rgba >>= 8;
            dst.W = (rgba & 0xFF) / 255.0f;
        }

        private void SetShaderParameters(DeviceContext deviceContext, ShaderResourceView shaderResourceView,
            float textureWidth, float textureHeight,
            float opacity, float contrast, float brightness, float gamma, int key_red, int key_green, int key_blue,
            float similarity, float smoothness, float spill)
        {
            if (opacity != oldOpacity || contrast != oldContrast || brightness != oldBrightness || gamma != oldGamma ||
                key_red != oldKeyRed || key_green != oldKeyGreen || key_blue != oldKeyBlue ||
                similarity != oldSimilarity || smoothness != oldSmoothness || spill != oldSpill)
            {
                oldOpacity = opacity;
                oldContrast = contrast;
                oldBrightness = brightness;
                oldGamma = gamma;
                oldSimilarity = similarity;
                oldSmoothness = smoothness;
                oldSpill = spill;

                if (key_red < 0)
                {
                    key_red = 0;
                }
                else if (key_red > 255)
                {
                    key_red = 255;
                }

                if (key_green < 0)
                {
                    key_green = 0;
                }
                else if (key_green > 255)
                {
                    key_green = 255;
                }

                if (key_blue < 0)
                {
                    key_blue = 0;
                }
                else if (key_blue > 255)
                {
                    key_blue = 255;
                }

                oldKeyRed = key_red;
                oldKeyGreen = key_green;
                oldKeyBlue = key_blue;

                var key_color = ((uint)key_red << 16) | ((uint)key_green << 8) | ((uint)key_blue << 0);
                vec4_from_rgba_srgb(out var key_rgb, key_color | 0xFF000000);
                var cb = new Vector4(-0.100644f, -0.338572f, 0.439216f, 0.501961f);
                var cr = new Vector4(0.439216f, -0.398942f, -0.040274f, 0.501961f);
                deviceContext.MapSubresource(argsBuffer, MapMode.WriteDiscard, MapFlags.None, out var stream);
                stream.Write(opacity);
                stream.Write(contrast);
                stream.Write(brightness);
                stream.Write(gamma);
                stream.Write(new Vector2(Vector4.Dot(key_rgb, cb), Vector4.Dot(key_rgb, cr)));
                stream.Write(new Vector2(1.0f / textureWidth, 1.0f / textureHeight));
                stream.Write(similarity);
                stream.Write(smoothness);
                stream.Write(spill);
                deviceContext.UnmapSubresource(argsBuffer, 0);
            }

            deviceContext.PixelShader.SetConstantBuffer(0, argsBuffer);

            deviceContext.PixelShader.SetShaderResource(0, shaderResourceView);
        }

        private void RenderShader(DeviceContext deviceContext)
        {
            deviceContext.InputAssembler.InputLayout = inputLayout;
            deviceContext.VertexShader.Set(vertexShader);
            deviceContext.PixelShader.Set(pixelShader);
            deviceContext.PixelShader.SetSampler(0, samplerState);
            deviceContext.Draw(6, 0);
        }
    }
}
